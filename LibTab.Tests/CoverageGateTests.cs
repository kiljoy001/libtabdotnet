// <copyright file="CoverageGateTests.cs" company="PlaceholderCompany">
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
/// Tests edge paths that protect the configured coverage and CRAP gates.
/// </summary>
public sealed class CoverageGateTests
{
    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("secret");
    private static readonly byte[] Wrong = Encoding.UTF8.GetBytes("wrong");

    /// <summary>
    /// Covers uncommon entity and legacy plain-text paths.
    /// </summary>
    [Fact]
    public void TextCodecCoversEntityAndLegacyEdges()
    {
        Assert.False(NdbText.NeedsEncoding(null));
        Assert.False(NdbText.NeedsEncoding(string.Empty));
        Assert.True(NdbText.NeedsEncoding("a\u0001b"));
        Assert.True(NdbText.NeedsEncoding("a\u007fb"));
        Assert.Equal("a&#1;b", NdbText.Encode("a\u0001b"));
        Assert.Equal("a&#127;b", NdbText.Encode("a\u007fb"));
        Assert.Equal(string.Empty, NdbText.Decode(null));
        Assert.Equal("a&missing;b", NdbText.Decode("a&missing;b"));
        Assert.Equal("a&;b", NdbText.Decode("a&;b"));
        Assert.Equal("a&#;b", NdbText.Decode("a&#;b"));
        Assert.Equal("a&#x;b", NdbText.Decode("a&#x;b"));
        Assert.Equal("a&#xg;b", NdbText.Decode("a&#xg;b"));
        Assert.Equal("a&#1114112;b", NdbText.Decode("a&#1114112;b"));
        Assert.Equal("a&#999999999999999999999;b", NdbText.Decode("a&#999999999999999999999;b"));
        Assert.Equal("<>'AA", NdbText.Decode("&lt;&gt;&apos;&#x41;&#65;"));
        Assert.Throws<TabException>(() => NdbText.Encode("a\0b"));
        Assert.Throws<TabException>(() => NdbText.Decode("a\0b"));
        Assert.Throws<TabException>(() => NdbText.Decode("a&#0;b"));
        Assert.Throws<TabException>(() => NdbText.Decode("a&#55296;b"));
        Assert.Throws<TabException>(() => NdbText.Decode(NdbText.LegacyPrefix + "abc"));
        Assert.Throws<TabException>(() => NdbText.Decode(NdbText.LegacyPrefix + TabCodec.B64Encode(Encoding.UTF8.GetBytes("bad"))));
        Assert.Throws<TabException>(() => NdbText.Decode(NdbText.LegacyPrefix + TabCodec.B64Encode(Encoding.UTF8.GetBytes("libtab-text-v1:\0"))));
    }

    /// <summary>
    /// Covers Plan 9 ndb parser edge behavior.
    /// </summary>
    [Fact]
    public void NdbParserCoversPlan9ParsingEdges()
    {
        Assert.Empty(NdbParser.ParseEntries("schema=test"));
        Assert.Empty(NdbParser.ParseEntries("   \n# comment\n"));

        NdbTuple emptyFromComment = NdbParser.ParseEntries("a = #comment\n").Single().Tuples.Single();
        Assert.Equal("a", emptyFromComment.Attribute);
        Assert.Equal(string.Empty, emptyFromComment.Value);

        NdbTuple unterminatedQuote = NdbParser.ParseEntries("a=\"unterminated\n").Single().Tuples.Single();
        Assert.Equal("unterminated", unterminatedQuote.Value);

        NdbTuple longAttr = NdbParser.ParseEntries("abcdefghijklmnopqrstuvwxyz0123456789=value\n").Single().Tuples.Single();
        Assert.Equal("abcdefghijklmnopqrstuvwxyz01234", longAttr.Attribute);

        TabTable table = TabTable.Parse(
            "not-written.tab",
            "schema=test\r\n\tcol=password type=HASHED algo=argon2id\r\n\r\n");
        TabColumn column = Assert.Single(table.Columns);
        Assert.Equal("argon2id", column.GetAttribute("algo"));
    }

    /// <summary>
    /// Covers table API validation paths.
    /// </summary>
    [Fact]
    public void TableApiGuardRailsAreCovered()
    {
        string missingPath = TestData.TempPath(nameof(this.TableApiGuardRailsAreCovered));
        Assert.Throws<TabException>(() => TabTable.Open(missingPath));
        Assert.Throws<TabException>(() => TabTable.Parse("not-written.tab", string.Empty));
        Assert.Throws<TabException>(() => TabTable.Parse("not-written.tab", "name=row\n"));
        Assert.Throws<TabException>(() => TabTable.Parse("not-written.tab", "schema=test\n\n"));
        Assert.Throws<TabException>(() => TabTable.Parse("not-written.tab", "schema=test\n\tcol=\n\n"));
        Assert.Throws<TabException>(() => TabTable.Parse("not-written.tab", "schema=test\n\tcol=k type=BOGUS\n\n"));
        Assert.Throws<TabException>(() => TabTable.Create("not-written.tab", "test", Array.Empty<TabColumn>()));
        Assert.Throws<TabException>(() => TabTable.Create("not-written.tab", "test", new[] { new TabColumn("k", "BOGUS") }));
        Assert.Throws<TabException>(() => TabTable.Create("not-written.tab", "test", TestData.PlainColumns("k", "k")));

        TabTable table = TabTable.Create("not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow row = table.AddRow("k", "row");
        Assert.Throws<TabException>(() => table.AddRow("missing", "row"));
        Assert.Throws<TabException>(() => table.AddRow("k", "bad\0row"));
        Assert.Throws<TabException>(() => table.Set(row, "k", string.Empty));
        Assert.Throws<TabException>(() => table.Set(row, "missing", "value"));
        Assert.Throws<TabException>(() => table.Set(row, "v", "bad\0value"));
        Assert.Throws<TabException>(() => table.Clear(row, "k"));
        Assert.Throws<TabException>(() => table.Clear(row, "missing"));
        Assert.Throws<TabException>(() => table.Search("missing", "value").ToArray());
        table.Clear(row, "v");
        Assert.Null(row["v"]);

        TabTable other = TabTable.Create("not-written.tab", "test", TestData.PlainColumns("k", "v"));
        TabRow otherRow = other.AddRow("k", "other");
        Assert.Throws<TabException>(() => table.Set(otherRow, "v", "value"));
        Assert.Throws<TabException>(() => table.RemoveRow(otherRow));

        TabTable typedHead = TabTable.Create("not-written.tab", "typed", new[] { new TabColumn("id", "HASHED") });
        Assert.Throws<TabException>(() => typedHead.AddRow("id", "row"));

        TabTable typed = TabTable.Create("not-written.tab", "typed", new[] { new TabColumn("id"), new TabColumn("digest", "HASHED"), new TabColumn("proof", "SIGNED") });
        TabRow typedRow = typed.AddRow("id", "row");
        Assert.Throws<TabException>(() => typed.Set(typedRow, "digest", "plain"));
        Assert.Throws<TabException>(() => typed.VerifyHash(typedRow, "id", Secret));
        Assert.Throws<TabException>(() => typed.VerifySigned(typedRow, "id", Array.Empty<byte>()));
        Assert.Throws<TabException>(() => typed.VerifyHash(typedRow, "missing", Secret));

        TabTable typedNil = TabTable.Parse("not-written.tab", "schema=test\n\tcol=id\n\tcol=digest type=HASHED\n\nid=row\n\tdigest=nil\n\n");
        Assert.Null(Assert.Single(typedNil.Search("id", "row")).Get("digest"));
        TabTable hiddenNil = TabTable.Parse("not-written.tab", "schema=test\n\tcol=k\n\tcol=v\n\nk=nil\n\tv=nil\n\n");
        Assert.Empty(hiddenNil.Rows);
    }

    /// <summary>
    /// Covers commit behavior when parent directories are missing.
    /// </summary>
    [Fact]
    public void TableCommitCreatesMissingDirectories()
    {
        string directory = TestData.TempPath(nameof(this.TableCommitCreatesMissingDirectories)) + ".d";
        string path = Path.Combine(directory, "table.tab");

        try
        {
            TabTable table = TabTable.Create(path, "test", TestData.PlainColumns("k"));
            table.AddRow("k", "row");
            table.Commit();
            Assert.True(File.Exists(path));
            Assert.False(table.IsDirty);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>
    /// Covers crypto validation and low-cost argon2id verification paths.
    /// </summary>
    [Fact]
    public void CryptoFailurePathsAndLowCostArgon2AreCovered()
    {
        TabKeyPair keyPair = PinnedKeyPair();
        string argon2id = TabCrypto.CreateArgon2idCell(Secret, new byte[] { 1 }, 3, 1, 1);

        Assert.True(TabCrypto.VerifyHashCell(argon2id, Secret));
        Assert.False(TabCrypto.VerifyHashCell(argon2id, Wrong));
        Assert.Throws<TabException>(() => TabCrypto.CreateHashedCell(Secret, "sha256"));
        Assert.False(TabCrypto.VerifyHashCell(null, Secret));
        Assert.False(TabCrypto.VerifyHashCell(string.Empty, Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell("hashed:", Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(HashedCell(0x01, 0x02), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(HashedCell(0xff), Secret));
        Assert.Throws<TabException>(() => TabCrypto.CreateArgon2idCell(Secret, Array.Empty<byte>(), 3, 1, 1));
        Assert.Throws<TabException>(() => TabCrypto.CreateArgon2idCell(Secret, Enumerable.Repeat((byte)1, 65).ToArray(), 3, 1, 1));

        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(HashedCell(0x02, 0x10, 0x03), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(16, 3, 1, 0, 32), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(16, 3, 1, 65, 32), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(16, 3, 1, 2, 32), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(2, 3, 1, 1, 33), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(3, 0, 1, 1, 33), Secret));
        Assert.Throws<TabException>(() => TabCrypto.VerifyHashCell(ArgonWire(3, 1, 0, 1, 33), Secret));

        Assert.Throws<TabException>(() => TabCrypto.VerifySignedCell(null, keyPair.PublicKey));
        Assert.Throws<TabException>(() => TabCrypto.VerifySignedCell("plain", keyPair.PublicKey));
        Assert.Throws<TabException>(() => TabCrypto.VerifySignedCell("signed:dmVjdG9y", keyPair.PublicKey));
        Assert.Throws<TabException>(() => TabCrypto.VerifySignedCell("signed:dmVjdG9y:AQID", keyPair.PublicKey));

        string signed = TabCrypto.CreateSignedCell(Encoding.ASCII.GetBytes("vector"), keyPair.SecretKey);
        Assert.Equal("vector", Encoding.ASCII.GetString(TabCrypto.VerifySignedCell(signed, keyPair.PublicKey)));
        Assert.Throws<TabException>(() => TabCrypto.VerifySignedCell(TamperSignedBody(signed), keyPair.PublicKey));
    }

    /// <summary>
    /// Covers Monocypher-compatible EdDSA guard rails.
    /// </summary>
    [Fact]
    public void MonocypherGuardRailsAreCovered()
    {
        TabKeyPair keyPair = PinnedKeyPair();
        byte[] message = Encoding.ASCII.GetBytes("vector");
        byte[] signature = MonocypherEdDsa.Sign(keyPair.SecretKey, message);

        Assert.Throws<TabException>(() => MonocypherEdDsa.KeyPair(new byte[31], out _, out _));
        Assert.Throws<TabException>(() => MonocypherEdDsa.Sign(new byte[63], message));
        Assert.Throws<TabException>(() => MonocypherEdDsa.Verify(new byte[63], keyPair.PublicKey, message));
        Assert.Throws<TabException>(() => MonocypherEdDsa.Verify(signature, new byte[31], message));

        byte[] highS = new byte[64];
        Array.Copy(signature, highS, signature.Length);
        for (int i = 32; i < highS.Length; i++)
        {
            highS[i] = 0xff;
        }

        Assert.False(MonocypherEdDsa.Verify(highS, keyPair.PublicKey, message));

        byte[] badPublic = Enumerable.Repeat((byte)0xff, 32).ToArray();
        Assert.False(MonocypherEdDsa.Verify(signature, badPublic, message));

        byte[] badNonce = signature.ToArray();
        for (int i = 0; i < 32; i++)
        {
            badNonce[i] = 0xff;
        }

        Assert.False(MonocypherEdDsa.Verify(badNonce, keyPair.PublicKey, message));
        Assert.NotEqual(keyPair.PublicKey, TabKeyPair.Generate().PublicKey);
    }

    /// <summary>
    /// Covers streaming writer validation and disposal paths.
    /// </summary>
    [Fact]
    public void WriterGuardRailsAreCovered()
    {
        string path = TestData.TempPath(nameof(this.WriterGuardRailsAreCovered));
        string tempPath = string.Concat(path, ".tmp.", Environment.ProcessId);

        try
        {
            using (TabWriter writer = TabWriter.Create(path, "test", TestData.PlainColumns("k", "v")))
            {
                Assert.Throws<TabException>(() => writer.Set("v", "value"));
                Assert.Throws<TabException>(() => writer.Clear("v"));
                Assert.Throws<ArgumentException>(() => writer.AddRow("k", string.Empty));
                Assert.Throws<TabException>(() => writer.AddRow("k", "bad\0row"));

                writer.AddRow("k", "row");
                Assert.Throws<TabException>(() => writer.Set("missing", "value"));
                Assert.Throws<TabException>(() => writer.Set("k", string.Empty));
                Assert.Throws<TabException>(() => writer.Set("v", "bad\0value"));
                Assert.Throws<TabException>(() => writer.Clear("missing"));
                Assert.Throws<TabException>(() => writer.Clear("k"));
                writer.Clear("v");
                writer.Commit();
                writer.Commit();
            }

            Assert.True(File.Exists(path));

            var disposed = TabWriter.Create(path, "test", TestData.PlainColumns("k"));
            disposed.Dispose();
            disposed.Dispose();
            Assert.Throws<ObjectDisposedException>(() => disposed.AddRow("k", "row"));
            Assert.False(File.Exists(tempPath));

            using TabWriter typed = TabWriter.Create(path, "typed", new[] { new TabColumn("id"), new TabColumn("secret", "HASHED") });
            Assert.Throws<TabException>(() => typed.AddRow("secret", "row"));
            typed.AddRow("id", "row");
            Assert.Throws<TabException>(() => typed.Set("secret", "plain"));
        }
        finally
        {
            File.Delete(path);
            File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Covers internal value object behavior.
    /// </summary>
    [Fact]
    public void InternalValueObjectsAreCovered()
    {
        Assert.Throws<ArgumentException>(() => new TabColumn(string.Empty));
        Assert.Throws<ArgumentException>(() => new TabColumn("k", attributes: new Dictionary<string, string> { [string.Empty] = "v" }));
        Assert.Throws<ArgumentNullException>(() => new TabColumn("k", null, new[] { new KeyValuePair<string, string>("a", null!) }));

        var entry = new NdbEntry();
        NdbTuple tuple = new("k", "v");
        entry.Insert(0, tuple);
        Assert.Same(tuple, entry.First("k"));

        NdbTuple clone = tuple.Clone();
        Assert.Equal("k", clone.Attribute);
        Assert.Equal("v", clone.Value);

        RowIdentity identity = RowIdentity.From(TestData.PlainColumns("k", "v"), entry);
        Assert.True(identity.Equals((object)identity));
        Assert.False(identity.Equals("not an identity"));
        Assert.Equal(identity.GetHashCode(), new RowIdentity(identity.ToArray()).GetHashCode());

        TabRow row = new(entry);
        Assert.Equal(new[] { "k" }, row.Columns.ToArray());
    }

    private static TabKeyPair PinnedKeyPair()
    {
        return TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
    }

    private static string HashedCell(params byte[] wire)
    {
        return TabCodec.CellEncode("HASHED", wire);
    }

    private static string ArgonWire(byte mLog2, byte passes, byte parallelism, byte saltLength, int tailLength)
    {
        var wire = new byte[1 + 4 + tailLength];
        wire[0] = 0x02;
        wire[1] = mLog2;
        wire[2] = passes;
        wire[3] = parallelism;
        wire[4] = saltLength;
        return HashedCell(wire);
    }

    private static string TamperSignedBody(string signed)
    {
        int first = signed.IndexOf(':', StringComparison.Ordinal);
        int second = signed.IndexOf(':', first + 1);
        return string.Concat(signed[..(first + 1)], "emVjdG9y", signed[second..]);
    }
}
