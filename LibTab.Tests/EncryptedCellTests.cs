// <copyright file="EncryptedCellTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibTab;
using Xunit;

/// <summary>
/// Unit tests for Monocypher-compatible ENCRYPTED cells.
/// </summary>
public sealed class EncryptedCellTests
{
    private const string PinnedEncryptedCell = "encrypted:ASAhIiMkJSYnKCkqKywtLi8wMTIzNDU2N7u-J3SJJxAJNYA3iTWMpxJrPC6_FEU=";

    /// <summary>
    /// Verifies the pinned encrypted cell vector observed from C libtab.
    /// </summary>
    [Fact]
    public void EncryptedCellMatchesPinnedMonocypherVector()
    {
        byte[] key = Key();
        byte[] nonce = Nonce();
        byte[] plaintext = Encoding.ASCII.GetBytes("vector");

        string cell = TabCrypto.CreateEncryptedCell(plaintext, key, nonce);

        Assert.Equal(PinnedEncryptedCell, cell);
        Assert.Equal(plaintext, TabCrypto.DecryptEncryptedCell(cell, key));
    }

    /// <summary>
    /// Verifies ENCRYPTED cells survive table serialization and parsing.
    /// </summary>
    [Fact]
    public void EncryptedCellsRoundTripThroughTableSerialization()
    {
        byte[] key = Key();
        byte[] plaintext = Encoding.UTF8.GetBytes("line one\nline two");
        TabTable table = NewEncryptedTable();
        TabRow row = table.AddRow("id", "a");

        table.SetEncrypted(row, "secret", plaintext, key);
        string? cell = row.Get("secret");

        Assert.StartsWith("encrypted:", cell, StringComparison.Ordinal);
        TabTable parsed = TabTable.Parse("/tmp/not-written.tab", table.Serialize());
        TabRow parsedRow = Assert.Single(parsed.Search("id", "a"));
        Assert.Equal(plaintext, parsed.Decrypt(parsedRow, "secret", key));
    }

    /// <summary>
    /// Verifies empty plaintext is legal.
    /// </summary>
    [Fact]
    public void EmptyEncryptedPlaintextRoundTrips()
    {
        byte[] key = Key();
        string cell = TabCrypto.CreateEncryptedCell(Array.Empty<byte>(), key, Nonce());

        Assert.Equal(Array.Empty<byte>(), TabCrypto.DecryptEncryptedCell(cell, key));
    }

    /// <summary>
    /// Verifies multi-block plaintext uses consecutive ChaCha20 counters.
    /// </summary>
    [Fact]
    public void LongEncryptedPlaintextRoundTrips()
    {
        byte[] key = Key();
        byte[] plaintext = Enumerable.Range(0, 150).Select(value => (byte)value).ToArray();
        string cell = TabCrypto.CreateEncryptedCell(plaintext, key, Nonce());

        Assert.Equal(plaintext, TabCrypto.DecryptEncryptedCell(cell, key));
    }

    /// <summary>
    /// Verifies encrypting equal plaintext twice creates distinct ciphertext rows.
    /// </summary>
    [Fact]
    public void EncryptedCellsUseFreshNonceAndDistinctRowIdentity()
    {
        byte[] key = Key();
        byte[] plaintext = Encoding.UTF8.GetBytes("same secret");
        TabTable table = NewEncryptedTable();
        TabRow first = table.AddRow("id", "same");
        table.SetEncrypted(first, "secret", plaintext, key);
        TabRow second = table.AddRow("id", "same");

        table.SetEncrypted(second, "secret", plaintext, key);

        Assert.NotEqual(first.Get("secret"), second.Get("secret"));
        Assert.Equal(2, table.Search("id", "same").Count());
        Assert.Equal(plaintext, table.Decrypt(first, "secret", key));
        Assert.Equal(plaintext, table.Decrypt(second, "secret", key));
    }

    /// <summary>
    /// Verifies wrong keys and tampering fail authentication.
    /// </summary>
    [Fact]
    public void EncryptedCellsRejectWrongKeyAndTampering()
    {
        byte[] key = Key();
        byte[] wrongKey = Key();
        wrongKey[0] ^= 0xff;
        byte[] wire = TabCodec.CellDecode(PinnedEncryptedCell, "ENCRYPTED");
        wire[^1] ^= 0x01;
        string tampered = TabCodec.CellEncode("ENCRYPTED", wire);

        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell(PinnedEncryptedCell, wrongKey));
        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell(tampered, key));
    }

    /// <summary>
    /// Verifies malformed ENCRYPTED cell payloads are rejected during decrypt.
    /// </summary>
    [Fact]
    public void EncryptedCellsRejectMalformedPayloads()
    {
        byte[] unknownVersion = new byte[41];
        unknownVersion[0] = 0x02;

        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell("encrypted:", Key()));
        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell("hashed:AQID", Key()));
        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell(TabCodec.CellEncode("ENCRYPTED", unknownVersion), Key()));
    }

    /// <summary>
    /// Verifies key and nonce lengths are checked before encryption.
    /// </summary>
    [Fact]
    public void EncryptedCellsRequireFixedKeyAndNonceLengths()
    {
        byte[] plaintext = Encoding.ASCII.GetBytes("vector");

        Assert.Throws<TabException>(() => TabCrypto.CreateEncryptedCell(plaintext, new byte[31], Nonce()));
        Assert.Throws<TabException>(() => TabCrypto.CreateEncryptedCell(plaintext, Key(), new byte[23]));
        Assert.Throws<TabException>(() => TabCrypto.DecryptEncryptedCell(PinnedEncryptedCell, new byte[31]));
    }

    /// <summary>
    /// Verifies ENCRYPTED APIs reject non-ENCRYPTED columns.
    /// </summary>
    [Fact]
    public void EncryptedApiRequiresEncryptedColumns()
    {
        TabTable table = TabTable.Create(
            "/tmp/not-written.tab",
            "test",
            new[] { new TabColumn("id"), new TabColumn("secret") });
        TabRow row = table.AddRow("id", "a");

        Assert.Throws<TabException>(() => table.SetEncrypted(row, "secret", Array.Empty<byte>(), Key()));
        Assert.Throws<TabException>(() => table.Decrypt(row, "secret", Key()));
    }

    /// <summary>
    /// Verifies ENCRYPTED columns require tagged cells when files are opened.
    /// </summary>
    [Fact]
    public void UntaggedEncryptedCellsAreRejectedAtOpen()
    {
        const string Text = "schema=test\n"
            + "\tcol=id\n"
            + "\tcol=secret type=ENCRYPTED\n"
            + "\n"
            + "id=a\n"
            + "\tsecret=plain\n"
            + "\n";

        Assert.Throws<TabException>(() => TabTable.Parse("/tmp/not-written.tab", Text));
    }

    /// <summary>
    /// Verifies tagged but invalid ENCRYPTED cells open and then fail decrypt.
    /// </summary>
    [Fact]
    public void TaggedInvalidEncryptedCellsFailOnDecrypt()
    {
        const string Text = "schema=test\n"
            + "\tcol=id\n"
            + "\tcol=secret type=ENCRYPTED\n"
            + "\n"
            + "id=a\n"
            + "\tsecret=encrypted:AQID\n"
            + "\n";

        TabTable table = TabTable.Parse("/tmp/not-written.tab", Text);
        TabRow row = Assert.Single(table.Search("id", "a"));

        Assert.Throws<TabException>(() => table.Decrypt(row, "secret", Key()));
    }

    private static TabTable NewEncryptedTable()
    {
        var columns = new[]
        {
            new TabColumn("id"),
            new TabColumn("secret", "ENCRYPTED", new Dictionary<string, string> { ["key"] = "local" }),
        };

        return TabTable.Create("/tmp/not-written.tab", "secrets", columns);
    }

    private static byte[] Key()
    {
        return Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    }

    private static byte[] Nonce()
    {
        return Enumerable.Range(32, 24).Select(value => (byte)value).ToArray();
    }
}
