// <copyright file="TabCodecTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using LibTab;
using Xunit;

/// <summary>
/// Unit tests for base64url and typed-cell helpers.
/// </summary>
public sealed class TabCodecTests
{
    /// <summary>
    /// Verifies base64url encoding matches libtab padding behavior.
    /// </summary>
    /// <param name="text">The Latin-1 fixture text.</param>
    /// <param name="encoded">The expected base64url text.</param>
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg==")]
    [InlineData("fo", "Zm8=")]
    [InlineData("foo", "Zm9v")]
    [InlineData("\xff\xee\xdd", "_-7d")]
    public void Base64UrlEncodesWithPadding(string text, string encoded)
    {
        byte[] bytes = System.Text.Encoding.Latin1.GetBytes(text);

        Assert.Equal(encoded, TabCodec.B64Encode(bytes));
        Assert.Equal(bytes, TabCodec.B64Decode(encoded));
    }

    /// <summary>
    /// Verifies invalid base64url lengths are rejected.
    /// </summary>
    [Fact]
    public void Base64UrlRejectsWrongLength()
    {
        Assert.Throws<TabException>(() => TabCodec.B64Decode("abc"));
    }

    /// <summary>
    /// Verifies typed-cell tags are emitted lowercase and read case-insensitively.
    /// </summary>
    [Fact]
    public void CellTagsAreAsciiCaseInsensitive()
    {
        string cell = TabCodec.CellEncode("HASHED", new byte[] { 1, 2, 3 });

        Assert.Equal("hashed:AQID", cell);
        Assert.True(TabCodec.CellHasTag(cell, "HASHED"));
        Assert.Equal(new byte[] { 1, 2, 3 }, TabCodec.CellDecode(cell, "hashed"));
    }
}
