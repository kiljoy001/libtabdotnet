// <copyright file="MonocypherEdDsaTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using System.Linq;
using System.Text;
using LibTab;
using Xunit;

/// <summary>
/// Unit tests for Monocypher-compatible EdDSA vectors.
/// </summary>
public sealed class MonocypherEdDsaTests
{
    /// <summary>
    /// Verifies the pinned public key and signature vector observed from C libtab.
    /// </summary>
    [Fact]
    public void KeypairAndSignatureMatchPinnedMonocypherVector()
    {
        byte[] seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        TabKeyPair pair = TabKeyPair.FromSeed(seed);
        byte[] message = Encoding.ASCII.GetBytes("vector");
        byte[] signature = MonocypherEdDsa.Sign(pair.SecretKey, message);

        Assert.Equal(TestData.PinnedPublicKey, TestData.Hex(pair.PublicKey));
        Assert.Equal(TestData.PinnedSignature, TestData.Hex(signature));
        Assert.True(MonocypherEdDsa.Verify(signature, pair.PublicKey, message));
    }

    /// <summary>
    /// Verifies signature checks fail when the message changes.
    /// </summary>
    [Fact]
    public void SignatureRejectsTamperedMessage()
    {
        byte[] seed = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        TabKeyPair pair = TabKeyPair.FromSeed(seed);
        byte[] signature = MonocypherEdDsa.Sign(pair.SecretKey, Encoding.ASCII.GetBytes("original"));

        Assert.False(MonocypherEdDsa.Verify(signature, pair.PublicKey, Encoding.ASCII.GetBytes("tampered")));
    }
}
