// <copyright file="TabKeyPair.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Security.Cryptography;

/// <summary>A Monocypher-compatible EdDSA key pair.</summary>
public sealed class TabKeyPair
{
    /// <summary>
    /// The required seed length in bytes.
    /// </summary>
    public const int SeedLength = 32;

    /// <summary>
    /// The public key length in bytes.
    /// </summary>
    public const int PublicKeyLength = 32;

    /// <summary>
    /// The secret key length in bytes.
    /// </summary>
    public const int SecretKeyLength = 64;

    private TabKeyPair(byte[] secretKey, byte[] publicKey)
    {
        this.SecretKey = secretKey;
        this.PublicKey = publicKey;
    }

    /// <summary>
    /// Gets Monocypher's 64-byte secret key form: 32-byte seed followed by 32-byte public key.
    /// </summary>
    public byte[] SecretKey { get; }

    /// <summary>
    /// Gets the 32-byte Ed25519 public key point.
    /// </summary>
    public byte[] PublicKey { get; }

    /// <summary>
    /// Builds a deterministic key pair from a 32-byte seed.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <returns>The generated key pair.</returns>
    public static TabKeyPair FromSeed(ReadOnlySpan<byte> seed)
    {
        if (seed.Length != SeedLength)
        {
            throw new TabException($"seed must be {SeedLength} bytes");
        }

        MonocypherEdDsa.KeyPair(seed, out byte[] secretKey, out byte[] publicKey);
        return new TabKeyPair(secretKey, publicKey);
    }

    /// <summary>
    /// Generates a random key pair.
    /// </summary>
    /// <returns>The generated key pair.</returns>
    public static TabKeyPair Generate()
    {
        Span<byte> seed = stackalloc byte[SeedLength];
        RandomNumberGenerator.Fill(seed);
        return FromSeed(seed);
    }
}
