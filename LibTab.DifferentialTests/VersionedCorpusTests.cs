// <copyright file="VersionedCorpusTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.DifferentialTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibTab;

/// <summary>
/// Tests checked-in compatibility corpus files generated from C libtab.
/// </summary>
public sealed class VersionedCorpusTests
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Gets valid versioned corpus files.
    /// </summary>
    /// <returns>Fixture file names.</returns>
    public static IEnumerable<object[]> ValidCorpusFiles()
    {
        yield return new object[] { "basic.tab" };
        yield return new object[] { "text.tab" };
        yield return new object[] { "mixed.tab" };
        yield return new object[] { "writer.tab" };
    }

    /// <summary>
    /// Verifies valid corpus files parse and reserialize without byte drift.
    /// </summary>
    /// <param name="fileName">The corpus file name.</param>
    [Theory]
    [MemberData(nameof(ValidCorpusFiles))]
    public void ValidCorpusFilesReserializeExactly(string fileName)
    {
        string path = CorpusPath(fileName);
        string expected = File.ReadAllText(path, StrictUtf8);
        TabTable table = TabTable.Open(path);

        Assert.Equal(expected, table.Serialize());
    }

    /// <summary>
    /// Verifies typed cells in the mixed corpus still validate.
    /// </summary>
    [Fact]
    public void MixedCorpusVerifiesTypedCells()
    {
        TabTable table = TabTable.Open(CorpusPath("mixed.tab"));
        TabRow row = Assert.Single(table.Search("k", "plain"));
        TabKeyPair keyPair = PinnedKeyPair();

        Assert.True(table.VerifyHash(row, "digest", Encoding.UTF8.GetBytes("secret")));
        Assert.Equal("vector", Encoding.UTF8.GetString(table.VerifySigned(row, "proof", keyPair.PublicKey)));
    }

    /// <summary>
    /// Verifies malformed corpus files are rejected.
    /// </summary>
    /// <param name="fileName">The malformed corpus file name.</param>
    [Theory]
    [InlineData("duplicate-column.tab")]
    [InlineData("missing-schema.tab")]
    [InlineData("missing-typed-tag.tab")]
    [InlineData("undeclared-column.tab")]
    public void MalformedCorpusFilesAreRejected(string fileName)
    {
        Assert.Throws<TabException>(() => TabTable.Open(CorpusPath("Malformed", fileName)));
    }

    /// <summary>
    /// Verifies a tagged but malformed HASHED payload can be read but not verified.
    /// </summary>
    [Fact]
    public void TaggedBadPayloadOpensButHashVerificationFails()
    {
        TabTable table = TabTable.Open(CorpusPath("Malformed", "tagged-bad-payload.tab"));
        TabRow row = Assert.Single(table.Search("k", "row"));

        Assert.Throws<TabException>(() => table.VerifyHash(row, "digest", Encoding.UTF8.GetBytes("secret")));
    }

    private static string CorpusPath(params string[] parts)
    {
        return Path.Combine(new[] { AppContext.BaseDirectory, "Corpus" }.Concat(parts).ToArray());
    }

    private static TabKeyPair PinnedKeyPair()
    {
        return TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
    }
}
