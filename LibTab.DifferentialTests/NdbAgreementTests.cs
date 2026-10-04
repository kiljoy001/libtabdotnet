// <copyright file="NdbAgreementTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.DifferentialTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibTab;
using Xunit;

/// <summary>
/// Differential tests for managed output against C-observed libtab bytes.
/// </summary>
public sealed class NdbAgreementTests
{
    private const string PinnedPublicKey = "f65333fa6303b6a23defd7de2af8aa461cb047ccbf12d4edd29ef3b1eba6706b";
    private const string PinnedSignedCell = "signed:dmVjdG9y:6DAJ-oiAwa83CpAiN5b4cSZpfq2RdmGcn3ote8zVCw_BZ7syog4QYdgN1xmz-HBHDGpFLRvJZQXtd14YhbciAA==";
    private const string PinnedBlake2bSecretCell = "hashed:Aa5Kr7PH2GFrc58Fow0GtOIjb96nEhaBWfNG6KPv1X2e";
    private const string PinnedEncryptedCell = "encrypted:ASAhIiMkJSYnKCkqKywtLi8wMTIzNDU2N7u-J3SJJxAJNYA3iTWMpxJrPC6_FEU=";

    /// <summary>
    /// Gets C-observed compatibility cases.
    /// </summary>
    /// <returns>The named expected and actual values.</returns>
    public static IEnumerable<object[]> CObservedCases()
    {
        yield return Case("base64 empty", string.Empty, TabCodec.B64Encode(Array.Empty<byte>()));
        yield return Case("base64 f", "Zg==", TabCodec.B64Encode(Encoding.ASCII.GetBytes("f")));
        yield return Case("base64 fo", "Zm8=", TabCodec.B64Encode(Encoding.ASCII.GetBytes("fo")));
        yield return Case("base64 foo", "Zm9v", TabCodec.B64Encode(Encoding.ASCII.GetBytes("foo")));
        yield return Case("base64 url alphabet", "_-7d", TabCodec.B64Encode(Encoding.Latin1.GetBytes("\xff\xee\xdd")));
        yield return Case("HASHED cell tag is lowercase", "hashed:AQID", TabCodec.CellEncode("HASHED", new byte[] { 1, 2, 3 }));
        yield return Case("SIGNED cell tag is lowercase", "signed:AQI=", TabCodec.CellEncode("SIGNED", new byte[] { 1, 2 }));
        yield return Case("space text serializes like C", ExpectedSingleValue("space", "\"Divine Comedy\""), SerializedSingleValue("space", "Divine Comedy"));
        yield return Case("quote text serializes like C", ExpectedSingleValue("quote", "\"The Number &quot;e&quot;\""), SerializedSingleValue("quote", "The Number \"e\""));
        yield return Case("newline text serializes like C", ExpectedSingleValue("newline", "\"line one&#10;line two\""), SerializedSingleValue("newline", "line one\nline two"));
        yield return Case("literal nil serializes like C", ExpectedSingleValue("literal-nil", "n&#105;l"), SerializedSingleValue("literal-nil", "nil"));
        yield return Case("ampersand text serializes like C", ExpectedSingleValue("amp", "AT&amp;T"), SerializedSingleValue("amp", "AT&T"));
        yield return Case("leading hash text serializes like C", ExpectedSingleValue("hash", "\"#not-a-comment\""), SerializedSingleValue("hash", "#not-a-comment"));
        yield return Case("legacy prefix serializes like C", ExpectedSingleValue("prefix", "__libtab_text64_v1&#58;not-user-visible"), SerializedSingleValue("prefix", "__libtab_text64_v1:not-user-visible"));
        yield return Case("empty text serializes like C", ExpectedSingleValue("empty", string.Empty), SerializedSingleValue("empty", string.Empty));
        yield return Case("tab text serializes like C", ExpectedSingleValue("tab", "\"left\tright\""), SerializedSingleValue("tab", "left\tright"));
        yield return Case("legacy raw nil parses as semantic nil", "null", ParsedCell(LegacyRawNilTable(), "k", "row", "v") ?? "null");
        yield return Case("legacy text64 parses like C", "The Number \"e\"", ParsedCell(LegacyText64Table(), "k", "row", "v") ?? "null");
        yield return Case("quoted ndb value parses like C", "beam rifle", ParsedCell(QuotedValueTable(), "name", "beam rifle", "name") ?? "null");
        yield return Case("comments and blanks are skipped", "1", CountRows(CommentedTable()).ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return Case("duplicate whole rows are deduped", "2", CountSearch(DuplicateRowsTable(), "k", "same").ToString(System.Globalization.CultureInfo.InvariantCulture));
        yield return Case("duplicate schema is refused", "TabException", ExceptionName(() => TabTable.Parse("/tmp/libtab-diff.tab", DuplicateSchemaTable())));
        yield return Case("undeclared row column is refused", "TabException", ExceptionName(() => TabTable.Parse("/tmp/libtab-diff.tab", UndeclaredColumnTable())));
        yield return Case("BLAKE2b HASHED wire bytes match C", PinnedBlake2bSecretCell, HashedCell("secret"));
        yield return Case("Monocypher public key matches C", PinnedPublicKey, Convert.ToHexString(PinnedKeyPair().PublicKey).ToLowerInvariant());
        yield return Case("Monocypher SIGNED cell matches C", PinnedSignedCell, SignedCell("vector"));
        yield return Case("Monocypher ENCRYPTED cell matches C", PinnedEncryptedCell, EncryptedCell("vector"));
    }

    /// <summary>
    /// Verifies managed output matches the corresponding C-observed value.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="expected">The C-observed value.</param>
    /// <param name="actual">The managed value.</param>
    [Theory]
    [MemberData(nameof(CObservedCases))]
    public void ManagedOutputMatchesCObservedBytes(string name, string expected, string actual)
    {
        _ = name;
        Assert.Equal(expected, actual);
    }

    private static object[] Case(string name, string expected, string actual)
    {
        return new object[] { name, expected, actual };
    }

    private static string SerializedSingleValue(string key, string value)
    {
        TabTable table = TabTable.Create("/tmp/libtab-diff.tab", "test", PlainColumns("k", "v"));
        TabRow row = table.AddRow("k", key);
        table.Set(row, "v", value);
        return table.Serialize();
    }

    private static string ExpectedSingleValue(string key, string wire)
    {
        return $"schema=test\n\tcol=k\n\tcol=v\n\nk={key}\n\tv={wire}\n\n";
    }

    private static string? ParsedCell(string text, string searchColumn, string searchValue, string resultColumn)
    {
        TabTable table = TabTable.Parse("/tmp/libtab-diff.tab", text);
        return table.Search(searchColumn, searchValue).Single().Get(resultColumn);
    }

    private static int CountRows(string text)
    {
        return TabTable.Parse("/tmp/libtab-diff.tab", text).Rows.Count();
    }

    private static int CountSearch(string text, string column, string value)
    {
        return TabTable.Parse("/tmp/libtab-diff.tab", text).Search(column, value).Count();
    }

    private static string ExceptionName(Action action)
    {
        try
        {
            action();
            return "ok";
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static string HashedCell(string preimage)
    {
        TabTable table = TabTable.Create(
            "/tmp/libtab-diff.tab",
            "accounts",
            new[] { new TabColumn("name"), new TabColumn("password", "HASHED") });
        TabRow row = table.AddRow("name", "scott");
        table.SetHashed(row, "password", Encoding.UTF8.GetBytes(preimage));
        return row.Get("password") ?? string.Empty;
    }

    private static string SignedCell(string body)
    {
        TabTable table = TabTable.Create(
            "/tmp/libtab-diff.tab",
            "content",
            new[] { new TabColumn("name"), new TabColumn("payload", "SIGNED") });
        TabRow row = table.AddRow("name", "record");
        table.SetSigned(row, "payload", Encoding.ASCII.GetBytes(body), PinnedKeyPair().SecretKey);
        return row.Get("payload") ?? string.Empty;
    }

    private static string EncryptedCell(string plaintext)
    {
        byte[] key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] nonce = Enumerable.Range(32, 24).Select(i => (byte)i).ToArray();
        return TabCrypto.CreateEncryptedCell(Encoding.ASCII.GetBytes(plaintext), key, nonce);
    }

    private static TabKeyPair PinnedKeyPair()
    {
        return TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    }

    private static IReadOnlyList<TabColumn> PlainColumns(params string[] names)
    {
        return names.Select(name => new TabColumn(name)).ToArray();
    }

    private static string LegacyRawNilTable()
    {
        return "schema=test\n\tcol=k\n\tcol=v\n\nk=row\n\tv=nil\n\n";
    }

    private static string LegacyText64Table()
    {
        return "schema=test\n\tcol=k\n\tcol=v\n\nk=row\n\tv=__libtab_text64_v1:bGlidGFiLXRleHQtdjE6VGhlIE51bWJlciAiZSI=\n\n";
    }

    private static string QuotedValueTable()
    {
        return "schema=test\n\tcol=name\n\nname=\"beam rifle\"\n\n";
    }

    private static string CommentedTable()
    {
        return "# comment\n\nschema=test\n\tcol=k\n\n# row\nk=row\n\n";
    }

    private static string DuplicateRowsTable()
    {
        return "schema=test\n\tcol=k\n\tcol=v\n\nk=same\n\tv=one\n\nk=same\n\tv=one\n\nk=same\n\tv=two\n\n";
    }

    private static string DuplicateSchemaTable()
    {
        return "schema=test\n\tcol=k\n\tcol=k\n\n";
    }

    private static string UndeclaredColumnTable()
    {
        return "schema=test\n\tcol=k\n\nk=row\n\tv=one\n\n";
    }
}
