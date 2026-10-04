// <copyright file="LibtabCompatibilitySteps.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Specs.Steps;

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LibTab;
using Reqnroll;
using Xunit;

/// <summary>
/// Step definitions for libtab compatibility behavior specs.
/// </summary>
[Binding]
public sealed class LibtabCompatibilitySteps : IDisposable
{
    private const string PinnedPublicKeyHex = "f65333fa6303b6a23defd7de2af8aa461cb047ccbf12d4edd29ef3b1eba6706b";

    private readonly string directory;
    private readonly string path;

    private TabTable? table;
    private TabRow? row;
    private TabKeyPair? keyPair;
    private string value = string.Empty;
    private Exception? parsingException;
    private Exception? verificationException;
    private byte[]? verifiedBody;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibtabCompatibilitySteps"/> class.
    /// </summary>
    public LibtabCompatibilitySteps()
    {
        this.directory = Path.Combine(Path.GetTempPath(), "libtab-specs-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        this.path = Path.Combine(this.directory, "table.tab");
        Directory.CreateDirectory(this.directory);
    }

    /// <summary>
    /// Parses a table fixture.
    /// </summary>
    /// <param name="text">The table text from the scenario.</param>
    [Given(@"the table")]
    public void GivenTheTable(string text)
    {
        this.table = TabTable.Parse(this.path, CompleteNdbText(text));
    }

    /// <summary>
    /// Attempts to parse a table fixture.
    /// </summary>
    /// <param name="text">The table text from the scenario.</param>
    [When(@"parsing the table")]
    public void WhenParsingTheTable(string text)
    {
        try
        {
            this.table = TabTable.Parse(this.path, CompleteNdbText(text));
        }
        catch (Exception ex) when (ex is TabException or ArgumentException)
        {
            this.parsingException = ex;
        }
    }

    /// <summary>
    /// Stores a scenario text value.
    /// </summary>
    /// <param name="text">The scenario value.</param>
    [Given(@"a value of ""(.*)""")]
    public void GivenAValue(string text)
    {
        this.value = text;
    }

    /// <summary>
    /// Creates a table with a HASHED column.
    /// </summary>
    [Given(@"a table with a HASHED column")]
    public void GivenATableWithAHashedColumn()
    {
        this.table = TabTable.Create(
            this.path,
            "accounts",
            new[] { new TabColumn("name"), new TabColumn("password", "HASHED") });
        this.row = this.table.AddRow("name", "scott");
    }

    /// <summary>
    /// Creates the pinned Monocypher-compatible key pair.
    /// </summary>
    [Given(@"the pinned Monocypher key")]
    public void GivenThePinnedMonocypherKey()
    {
        this.keyPair = TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    }

    /// <summary>
    /// Creates a table with a SIGNED column.
    /// </summary>
    [Given(@"a table with a SIGNED column")]
    public void GivenATableWithASignedColumn()
    {
        this.table = TabTable.Create(
            this.path,
            "content",
            new[] { new TabColumn("name"), new TabColumn("payload", "SIGNED") });
        this.row = this.table.AddRow("name", "record");
    }

    /// <summary>
    /// Stores a HASHED cell for a scenario value.
    /// </summary>
    /// <param name="text">The preimage text to hash.</param>
    [When(@"I store the hash of ""(.*)""")]
    public void WhenIStoreTheHashOf(string text)
    {
        this.RequireTable().SetHashed(this.RequireRow(), "password", Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Signs a scenario value and commits it to disk.
    /// </summary>
    /// <param name="text">The body text to sign.</param>
    [When(@"I sign ""(.*)""")]
    public void WhenISign(string text)
    {
        this.RequireTable().SetSigned(this.RequireRow(), "payload", Encoding.UTF8.GetBytes(text), this.RequireKey().SecretKey);
        this.RequireTable().Commit();
    }

    /// <summary>
    /// Tamper-edits the signed cell body in the committed file.
    /// </summary>
    [When(@"I edit the signed cell body on disk")]
    public void WhenIEditTheSignedCellBodyOnDisk()
    {
        string text = File.ReadAllText(this.path);
        int at = text.IndexOf("payload=signed:", StringComparison.Ordinal);
        Assert.True(at >= 0);

        int bodyAt = at + "payload=signed:".Length;
        char replacement = text[bodyAt] == 'A' ? 'B' : 'A';
        text = text[..bodyAt] + replacement + text[(bodyAt + 1)..];
        File.WriteAllText(this.path, text);
        this.table = TabTable.Open(this.path);
        this.row = this.table.Search("name", "record").Single();
    }

    /// <summary>
    /// Verifies the parsed schema name.
    /// </summary>
    /// <param name="expected">The expected schema name.</param>
    [Then(@"the schema should be ""(.*)""")]
    public void ThenTheSchemaShouldBe(string expected)
    {
        Assert.Equal(expected, this.RequireTable().SchemaName);
    }

    /// <summary>
    /// Verifies the parsed schema columns.
    /// </summary>
    /// <param name="expected">The expected comma-separated columns.</param>
    [Then(@"it should declare the columns ""(.*)""")]
    public void ThenItShouldDeclareTheColumns(string expected)
    {
        string[] columns = expected.Split(',').Select(item => item.Trim()).ToArray();
        Assert.Equal(columns, this.RequireTable().Columns.Select(column => column.Name));
    }

    /// <summary>
    /// Verifies the visible row count.
    /// </summary>
    /// <param name="expected">The expected row count.</param>
    [Then(@"there should be (.*) rows")]
    public void ThenThereShouldBeRows(int expected)
    {
        Assert.Equal(expected, this.RequireTable().Rows.Count());
    }

    /// <summary>
    /// Verifies a named row has no semantic value in a column.
    /// </summary>
    /// <param name="name">The row name.</param>
    /// <param name="column">The column to inspect.</param>
    [Then(@"the row named ""(.*)"" should have no ""(.*)""")]
    public void ThenTheRowNamedShouldHaveNo(string name, string column)
    {
        Assert.Null(this.RowNamed(name).Get(column));
    }

    /// <summary>
    /// Verifies costs for all rows with a given name.
    /// </summary>
    /// <param name="name">The row name.</param>
    /// <param name="expected">The expected comma-separated costs.</param>
    [Then(@"the rows named ""(.*)"" should have costs ""(.*)""")]
    public void ThenTheRowsNamedShouldHaveCosts(string name, string expected)
    {
        string[] costs = expected.Split(',').Select(item => item.Trim()).ToArray();
        Assert.Equal(costs, this.RequireTable().Search("name", name).Select(found => found.Get("cost")));
    }

    /// <summary>
    /// Verifies text encoding output.
    /// </summary>
    /// <param name="expected">The expected encoded text.</param>
    [Then(@"encoding it should give ""(.*)""")]
    public void ThenEncodingItShouldGive(string expected)
    {
        Assert.Equal(expected, NdbText.Encode(this.value));
    }

    /// <summary>
    /// Verifies encoded text decodes to the original value.
    /// </summary>
    [Then(@"decoding that should give it back")]
    public void ThenDecodingThatShouldGiveItBack()
    {
        Assert.Equal(this.value, NdbText.Decode(NdbText.Encode(this.value)));
    }

    /// <summary>
    /// Verifies parsing was refused.
    /// </summary>
    [Then(@"parsing should be refused")]
    public void ThenParsingShouldBeRefused()
    {
        Assert.NotNull(this.parsingException);
    }

    /// <summary>
    /// Verifies a stored HASHED cell matches a preimage.
    /// </summary>
    /// <param name="text">The preimage text to verify.</param>
    [Then(@"the hash should verify ""(.*)""")]
    public void ThenTheHashShouldVerify(string text)
    {
        Assert.True(this.RequireTable().VerifyHash(this.RequireRow(), "password", Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Verifies a stored HASHED cell rejects a preimage.
    /// </summary>
    /// <param name="text">The preimage text to reject.</param>
    [Then(@"the hash should not verify ""(.*)""")]
    public void ThenTheHashShouldNotVerify(string text)
    {
        Assert.False(this.RequireTable().VerifyHash(this.RequireRow(), "password", Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Verifies the pinned public key.
    /// </summary>
    [Then(@"the public key should be the pinned public key")]
    public void ThenThePublicKeyShouldBeThePinnedPublicKey()
    {
        Assert.Equal(PinnedPublicKeyHex, Convert.ToHexString(this.RequireKey().PublicKey).ToLowerInvariant());
    }

    /// <summary>
    /// Verifies a SIGNED cell body.
    /// </summary>
    /// <param name="expected">The expected signed body text.</param>
    [Then(@"the signed body should verify as ""(.*)""")]
    public void ThenTheSignedBodyShouldVerifyAs(string expected)
    {
        this.verifiedBody = this.RequireTable().VerifySigned(this.RequireRow(), "payload", this.RequireKey().PublicKey);
        Assert.Equal(expected, Encoding.UTF8.GetString(this.verifiedBody));
    }

    /// <summary>
    /// Verifies SIGNED cell verification is refused.
    /// </summary>
    [Then(@"signed verification should be refused")]
    public void ThenSignedVerificationShouldBeRefused()
    {
        try
        {
            this.verifiedBody = this.RequireTable().VerifySigned(this.RequireRow(), "payload", this.RequireKey().PublicKey);
        }
        catch (Exception ex) when (ex is TabException or ArgumentException)
        {
            this.verificationException = ex;
        }

        Assert.Null(this.verifiedBody);
        Assert.NotNull(this.verificationException);
    }

    /// <summary>
    /// Deletes the scenario temporary directory.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    private static string CompleteNdbText(string text)
    {
        return text.EndsWith('\n') ? text : text + "\n";
    }

    private TabTable RequireTable()
    {
        return this.table ?? throw new InvalidOperationException("table has not been created");
    }

    private TabRow RequireRow()
    {
        return this.row ?? throw new InvalidOperationException("row has not been created");
    }

    private TabKeyPair RequireKey()
    {
        return this.keyPair ?? throw new InvalidOperationException("key has not been created");
    }

    private TabRow RowNamed(string name)
    {
        return this.RequireTable().Search("name", name).Single();
    }
}
