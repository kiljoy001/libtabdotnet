// <copyright file="TabTableTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibTab;
using Xunit;

/// <summary>
/// Unit tests for table parsing, mutation, serialization, and typed cells.
/// </summary>
public sealed class TabTableTests
{
    /// <summary>
    /// Verifies that committed cells can be reopened from disk.
    /// </summary>
    [Fact]
    public void CommitRoundTripsCells()
    {
        string path = TestData.TempPath(nameof(this.CommitRoundTripsCells));
        try
        {
            TabTable table = TabTable.Create(path, "test", TestData.PlainColumns("k", "v"));
            TabRow row = table.AddRow("k", "alpha");
            table.Set(row, "v", "one");
            table.Commit();

            TabTable reopened = TabTable.Open(path);
            TabRow found = Assert.Single(reopened.Search("k", "alpha"));
            Assert.Equal("one", found["v"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies that setting one row leaves sibling rows unchanged.
    /// </summary>
    [Fact]
    public void SetReplacesOneCellWithoutTouchingSiblingRows()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow first = table.AddRow("k", "first");
        TabRow second = table.AddRow("k", "second");

        table.Set(first, "v", "A");
        table.Set(second, "v", "B");
        table.Set(first, "v", "CHANGED");

        Assert.Equal("CHANGED", first["v"]);
        Assert.Equal("B", second["v"]);
    }

    /// <summary>
    /// Verifies that missing cells and literal "nil" remain distinct.
    /// </summary>
    [Fact]
    public void MissingNilAndLiteralNilStayDistinct()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v", "w"));
        TabRow row = table.AddRow("k", "row");
        table.Set(row, "v", "nil");

        Assert.Null(row["w"]);
        Assert.Equal("nil", row["v"]);
        Assert.Single(table.Search("v", "nil"));
        Assert.Single(table.Search("w", null));

        table.Set(row, "v", null);
        Assert.Null(row["v"]);
    }

    /// <summary>
    /// Verifies plain-text encoding and readable serialization.
    /// </summary>
    [Fact]
    public void TextValuesRoundTripAndSerializeReadably()
    {
        var cases = new Dictionary<string, string>
        {
            ["space"] = "Divine Comedy",
            ["quote"] = "The Number \"e\"",
            ["newline"] = "line one\nline two",
            ["literal-nil"] = "nil",
            ["prefix"] = "__libtab_text64_v1:not-user-visible",
            ["amp"] = "AT&T",
            ["hash"] = "#not-a-comment",
        };
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));

        foreach (KeyValuePair<string, string> pair in cases)
        {
            TabRow row = table.AddRow("k", pair.Key);
            table.Set(row, "v", pair.Value);
        }

        string serialized = table.Serialize();
        TabTable parsed = TabTable.Parse("/tmp/not-written.tab", serialized);

        foreach (KeyValuePair<string, string> pair in cases)
        {
            Assert.Equal(pair.Value, Assert.Single(parsed.Search("k", pair.Key))["v"]);
        }

        Assert.Contains("The Number &quot;e&quot;", serialized);
        Assert.Contains("line one&#10;line two", serialized);
        Assert.Contains("n&#105;l", serialized);
        Assert.Contains("AT&amp;T", serialized);
        Assert.Contains("__libtab_text64_v1&#58;not-user-visible", serialized);
        Assert.Contains("v=\"#not-a-comment\"", serialized);
    }

    /// <summary>
    /// Verifies that raw ndb nil loads as semantic nil.
    /// </summary>
    [Fact]
    public void LegacyRawNilLoadsAsSemanticNil()
    {
        const string Text = "schema=test\n"
            + "\tcol=k\n"
            + "\tcol=v\n"
            + "\n"
            + "k=row\n"
            + "\tv=nil\n"
            + "\n";

        TabTable table = TabTable.Parse("/tmp/not-written.tab", Text);

        Assert.Null(Assert.Single(table.Search("k", "row"))["v"]);
    }

    /// <summary>
    /// Verifies duplicate row handling during parsing.
    /// </summary>
    [Fact]
    public void DuplicateWholeRowsAreDedupedButSharedHeadsCanCoexist()
    {
        const string Text = "schema=test\n"
            + "\tcol=k\n"
            + "\tcol=v\n"
            + "\n"
            + "k=same\n"
            + "\tv=one\n"
            + "\n"
            + "k=same\n"
            + "\tv=one\n"
            + "\n"
            + "k=same\n"
            + "\tv=two\n"
            + "\n";

        TabTable table = TabTable.Parse("/tmp/not-written.tab", Text);

        Assert.Equal(2, table.Search("k", "same").Count());
        Assert.Equal("one", Assert.Single(table.Search("v", "one"))["v"]);
        Assert.Equal("two", Assert.Single(table.Search("v", "two"))["v"]);
    }

    /// <summary>
    /// Verifies that clearing a cell cannot create a duplicate row.
    /// </summary>
    [Fact]
    public void ClearingCellFailsWhenItWouldMergeRows()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow valued = table.AddRow("k", "same");
        table.Set(valued, "v", "x");
        TabRow bare = table.AddRow("k", "same");

        Assert.Throws<TabException>(() => table.Clear(valued, "v"));
        Assert.Equal("x", valued["v"]);
        Assert.Same(bare, table.AddRow("k", "same"));
    }

    /// <summary>
    /// Verifies malformed schemas are rejected.
    /// </summary>
    [Fact]
    public void RejectsBadSchema()
    {
        const string Text = "schema=test\n"
            + "\tcol=k\n"
            + "\tcol=k\n"
            + "\n";

        Assert.Throws<TabException>(() => TabTable.Parse("/tmp/not-written.tab", Text));
    }

    /// <summary>
    /// Verifies the streaming writer emits the same text as table serialization.
    /// </summary>
    [Fact]
    public void StreamingWriterMatchesTableCommitOutput()
    {
        string path = TestData.TempPath(nameof(this.StreamingWriterMatchesTableCommitOutput));
        try
        {
            var columns = TestData.PlainColumns("k", "v", "w");
            TabTable table = TabTable.Create(path, "test", columns);
            TabRow row = table.AddRow("k", "space");
            table.Set(row, "v", "Divine Comedy");
            table.Set(row, "w", "nil");
            row = table.AddRow("k", "quote");
            table.Set(row, "v", "The Number \"e\"");
            table.Set(row, "w", "line one\nline two");
            row = table.AddRow("k", "clear");
            table.Set(row, "v", "gone");
            table.Clear(row, "v");
            string tableText = table.Serialize();

            using (TabWriter writer = TabWriter.Create(path, "test", columns))
            {
                writer.AddRow("k", "space");
                writer.Set("v", "Divine Comedy");
                writer.Set("w", "nil");
                writer.AddRow("k", "quote");
                writer.Set("v", "The Number \"e\"");
                writer.Set("w", "line one\nline two");
                writer.AddRow("k", "clear");
                writer.Set("v", "gone");
                writer.Clear("v");
                writer.Commit();
            }

            Assert.Equal(tableText, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies HASHED and SIGNED cells round trip through table serialization.
    /// </summary>
    [Fact]
    public void HashAndSignedCellsRoundTrip()
    {
        byte[] seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        TabKeyPair keyPair = TabKeyPair.FromSeed(seed);
        var columns = new[]
        {
            new TabColumn("id"),
            new TabColumn("digest", "HASHED"),
            new TabColumn("proof", "SIGNED", new Dictionary<string, string> { ["signer"] = "test" }),
        };
        TabTable table = TabTable.Create("/tmp/not-written.tab", "typed", columns);
        TabRow row = table.AddRow("id", "a");

        table.SetHashed(row, "digest", Encoding.UTF8.GetBytes("secret"));
        table.SetSigned(row, "proof", Encoding.UTF8.GetBytes("body"), keyPair.SecretKey);

        Assert.True(table.VerifyHash(row, "digest", Encoding.UTF8.GetBytes("secret")));
        Assert.False(table.VerifyHash(row, "digest", Encoding.UTF8.GetBytes("wrong")));
        Assert.Equal("body", Encoding.UTF8.GetString(table.VerifySigned(row, "proof", keyPair.PublicKey)));

        TabTable parsed = TabTable.Parse("/tmp/not-written.tab", table.Serialize());
        TabRow parsedRow = Assert.Single(parsed.Search("id", "a"));
        Assert.True(parsed.VerifyHash(parsedRow, "digest", Encoding.UTF8.GetBytes("secret")));
        Assert.Equal("body", Encoding.UTF8.GetString(parsed.VerifySigned(parsedRow, "proof", keyPair.PublicKey)));
    }

    /// <summary>
    /// Verifies oversized plain cells are refused before mutation.
    /// </summary>
    [Fact]
    public void OversizedPlainCellsAreRefused()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow row = table.AddRow("k", "a");
        string accepted = new('x', TabTable.MaxCellLineBytes - 64);
        string oversized = new('x', TabTable.MaxCellLineBytes + 1);

        table.Set(row, "v", accepted);

        Assert.Throws<TabException>(() => table.Set(row, "v", oversized));
        Assert.Equal(accepted, row.Get("v"));
    }

    /// <summary>
    /// Verifies encoded expansion counts toward the cell size cap.
    /// </summary>
    [Fact]
    public void CellLimitCountsEncodedLength()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow row = table.AddRow("k", "a");
        string ampersands = new('&', TabTable.MaxCellLineBytes / 2);

        Assert.Throws<TabException>(() => table.Set(row, "v", ampersands));
    }

    /// <summary>
    /// Verifies accepted large cells survive serialization and parsing.
    /// </summary>
    [Fact]
    public void AcceptedLargeCellSurvivesReload()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow row = table.AddRow("k", "a");
        string accepted = new('x', TabTable.MaxCellLineBytes - 64);

        table.Set(row, "v", accepted);
        TabTable parsed = TabTable.Parse("/tmp/not-written.tab", table.Serialize());

        Assert.Equal(accepted, Assert.Single(parsed.Search("k", "a")).Get("v"));
    }

    /// <summary>
    /// Verifies oversized row head values are refused.
    /// </summary>
    [Fact]
    public void OversizedRowHeadsAreRefused()
    {
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", TestData.PlainColumns("k", "v"));
        string oversized = new('x', TabTable.MaxCellLineBytes + 1);

        Assert.Throws<TabException>(() => table.AddRow("k", oversized));
    }

    /// <summary>
    /// Verifies the streaming writer shares the cell size cap.
    /// </summary>
    [Fact]
    public void StreamingWriterRefusesOversizedCellsAndHeads()
    {
        string path = TestData.TempPath(nameof(this.StreamingWriterRefusesOversizedCellsAndHeads));
        string oversized = new('x', TabTable.MaxCellLineBytes + 1);

        try
        {
            using TabWriter writer = TabWriter.Create(path, "test", TestData.PlainColumns("k", "v"));
            writer.AddRow("k", "a");

            Assert.Throws<TabException>(() => writer.Set("v", oversized));
            Assert.Throws<TabException>(() => writer.AddRow("k", oversized));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Verifies the cell size cap applies to SIGNED and ENCRYPTED but not HASHED preimages.
    /// </summary>
    [Fact]
    public void CellLimitAppliesToLargeTypedPayloads()
    {
        byte[] body = Encoding.ASCII.GetBytes(new string('x', TabTable.MaxCellLineBytes));
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        TabKeyPair keyPair = TabKeyPair.FromSeed(seed);
        var columns = new[]
        {
            new TabColumn("k"),
            new TabColumn("encrypted", "ENCRYPTED"),
            new TabColumn("signed", "SIGNED"),
            new TabColumn("hashed", "HASHED"),
        };
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", columns);
        TabRow row = table.AddRow("k", "a");

        Assert.Throws<TabException>(() => table.SetEncrypted(row, "encrypted", body, key));
        Assert.Throws<TabException>(() => table.SetSigned(row, "signed", body, keyPair.SecretKey));
        table.SetHashed(row, "hashed", body);

        Assert.True(table.VerifyHash(row, "hashed", body));
    }
}
