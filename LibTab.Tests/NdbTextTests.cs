// <copyright file="NdbTextTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using System.Text;
using LibTab;
using Xunit;

/// <summary>
/// Unit tests for ndb text encoding.
/// </summary>
public sealed class NdbTextTests
{
    /// <summary>
    /// Verifies the text encoding matrix against libtab rules.
    /// </summary>
    /// <param name="value">The semantic value.</param>
    /// <param name="needsEncoding">The expected encoding requirement.</param>
    /// <param name="needsQuote">The expected quoting requirement.</param>
    /// <param name="wire">The expected wire value.</param>
    [Theory]
    [InlineData("Divine Comedy", false, true, "Divine Comedy")]
    [InlineData("The Number \"e\"", true, true, "The Number &quot;e&quot;")]
    [InlineData("line one\nline two", true, true, "line one&#10;line two")]
    [InlineData("nil", true, false, "n&#105;l")]
    [InlineData("AT&T", true, false, "AT&amp;T")]
    [InlineData("#not-a-comment", false, true, "#not-a-comment")]
    [InlineData("__libtab_text64_v1:not-user-visible", true, false, "__libtab_text64_v1&#58;not-user-visible")]
    public void EncodeMatchesLibtabRules(string value, bool needsEncoding, bool needsQuote, string wire)
    {
        Assert.Equal(needsEncoding, NdbText.NeedsEncoding(value));
        Assert.Equal(needsQuote, NdbText.NeedsQuote(wire));
        Assert.Equal(wire, NdbText.Encode(value));
        Assert.Equal(value, NdbText.Decode(wire));
    }

    /// <summary>
    /// Verifies legacy text64 values still decode.
    /// </summary>
    [Fact]
    public void LegacyText64StillDecodes()
    {
        string payload = "libtab-text-v1:The Number \"e\"";
        string cell = NdbText.LegacyPrefix + TabCodec.B64Encode(Encoding.UTF8.GetBytes(payload));

        Assert.Equal("The Number \"e\"", NdbText.Decode(cell));
    }

    /// <summary>
    /// Verifies encoded NUL values are rejected.
    /// </summary>
    [Fact]
    public void EncodedNulIsRejected()
    {
        TabException ex = Assert.Throws<TabException>(() => NdbText.Decode("a&#0;b"));
        Assert.Contains("NUL", ex.Message);
    }
}
