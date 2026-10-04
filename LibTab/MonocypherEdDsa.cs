// <copyright file="MonocypherEdDsa.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Numerics;

/// <summary>
/// Monocypher-compatible EdDSA over Curve25519 using BLAKE2b expansion.
/// </summary>
internal static class MonocypherEdDsa
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

    /// <summary>
    /// The signature length in bytes.
    /// </summary>
    public const int SignatureLength = 64;

    private static readonly BigInteger P = (BigInteger.One << 255) - 19;
    private static readonly BigInteger L = (BigInteger.One << 252) + BigInteger.Parse("27742317777372353535851937790883648493");
    private static readonly BigInteger D = Mod(-121665 * ModInverse(121666));
    private static readonly BigInteger SqrtMinusOne = BigInteger.ModPow(2, (P - 1) / 4, P);
    private static readonly Point BasePoint = new(
        BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202"),
        BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960"));

    private static readonly Point Neutral = new(BigInteger.Zero, BigInteger.One);

    /// <summary>
    /// Builds a Monocypher-compatible key pair from a seed.
    /// </summary>
    /// <param name="seed">The 32-byte seed.</param>
    /// <param name="secretKey">The generated 64-byte secret key.</param>
    /// <param name="publicKey">The generated 32-byte public key.</param>
    public static void KeyPair(ReadOnlySpan<byte> seed, out byte[] secretKey, out byte[] publicKey)
    {
        if (seed.Length != SeedLength)
        {
            throw new TabException($"seed must be {SeedLength} bytes");
        }

        byte[] expanded = TabCrypto.Blake2b(seed, 64);
        BigInteger scalar = DecodeLittleEndian(PruneScalar(expanded.AsSpan(0, 32)));
        publicKey = EncodePoint(ScalarMult(scalar, BasePoint));

        secretKey = new byte[SecretKeyLength];
        seed.CopyTo(secretKey);
        publicKey.CopyTo(secretKey.AsSpan(32));
    }

    /// <summary>
    /// Signs a message using Monocypher-compatible EdDSA.
    /// </summary>
    /// <param name="secretKey">The 64-byte secret key.</param>
    /// <param name="message">The message bytes.</param>
    /// <returns>The 64-byte signature.</returns>
    public static byte[] Sign(ReadOnlySpan<byte> secretKey, ReadOnlySpan<byte> message)
    {
        if (secretKey.Length != SecretKeyLength)
        {
            throw new TabException($"signer secret key must be {SecretKeyLength} bytes");
        }

        ReadOnlySpan<byte> seed = secretKey[..32];
        ReadOnlySpan<byte> publicKey = secretKey[32..64];
        byte[] expanded = TabCrypto.Blake2b(seed, 64);
        BigInteger secretScalar = DecodeLittleEndian(PruneScalar(expanded.AsSpan(0, 32)));

        byte[] nonceHash = TabCrypto.Blake2b(Concat(expanded.AsSpan(32, 32), message), 64);
        BigInteger nonce = Reduce(nonceHash);
        byte[] encodedNoncePoint = EncodePoint(ScalarMult(nonce, BasePoint));

        byte[] challengeHash = TabCrypto.Blake2b(Concat(encodedNoncePoint, publicKey, message), 64);
        BigInteger challenge = Reduce(challengeHash);
        BigInteger s = ModL((challenge * secretScalar) + nonce);

        var signature = new byte[SignatureLength];
        encodedNoncePoint.CopyTo(signature.AsSpan(0, 32));
        EncodeLittleEndian(s).CopyTo(signature.AsSpan(32, 32));
        return signature;
    }

    /// <summary>
    /// Verifies a Monocypher-compatible EdDSA signature.
    /// </summary>
    /// <param name="signature">The 64-byte signature.</param>
    /// <param name="publicKey">The 32-byte public key.</param>
    /// <param name="message">The message bytes.</param>
    /// <returns>True when the signature verifies; otherwise false.</returns>
    public static bool Verify(ReadOnlySpan<byte> signature, ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> message)
    {
        if (signature.Length != SignatureLength)
        {
            throw new TabException($"signature must be {SignatureLength} bytes");
        }

        if (publicKey.Length != PublicKeyLength)
        {
            throw new TabException($"signer public key must be {PublicKeyLength} bytes");
        }

        BigInteger s = DecodeLittleEndian(signature[32..64]);
        if (s >= L)
        {
            return false;
        }

        if (!TryDecodePoint(publicKey, out Point a) || !TryDecodePoint(signature[..32], out Point r))
        {
            return false;
        }

        BigInteger challenge = Reduce(TabCrypto.Blake2b(Concat(signature[..32], publicKey, message), 64));
        Point check = Add(Add(ScalarMult(s, BasePoint), Negate(ScalarMult(challenge, a))), Negate(r));
        check = ScalarMult(8, check);

        return check.X.IsZero && check.Y.IsOne;
    }

    private static byte[] PruneScalar(ReadOnlySpan<byte> input)
    {
        var scalar = input.ToArray();
        scalar[0] &= 248;
        scalar[31] &= 127;
        scalar[31] |= 64;
        return scalar;
    }

    private static BigInteger Reduce(ReadOnlySpan<byte> expanded)
    {
        return ModL(DecodeLittleEndian(expanded));
    }

    private static Point Add(Point left, Point right)
    {
        BigInteger x1x2 = Mod(left.X * right.X);
        BigInteger y1y2 = Mod(left.Y * right.Y);
        BigInteger dxxyy = Mod(D * x1x2 * y1y2);
        BigInteger x = Mod((left.X * right.Y) + (left.Y * right.X)) * ModInverse(1 + dxxyy);
        BigInteger y = Mod(y1y2 + x1x2) * ModInverse(1 - dxxyy);
        return new Point(Mod(x), Mod(y));
    }

    private static Point Negate(Point point)
    {
        return new Point(Mod(-point.X), point.Y);
    }

    private static Point ScalarMult(BigInteger scalar, Point point)
    {
        Point result = Neutral;
        Point addend = point;

        while (scalar > BigInteger.Zero)
        {
            if (!scalar.IsEven)
            {
                result = Add(result, addend);
            }

            addend = Add(addend, addend);
            scalar >>= 1;
        }

        return result;
    }

    private static byte[] EncodePoint(Point point)
    {
        byte[] encoded = EncodeLittleEndian(point.Y);
        if (!point.X.IsEven)
        {
            encoded[31] |= 0x80;
        }

        return encoded;
    }

    private static bool TryDecodePoint(ReadOnlySpan<byte> encoded, out Point point)
    {
        point = Neutral;

        if (encoded.Length != 32)
        {
            return false;
        }

        Span<byte> yBytes = stackalloc byte[32];
        encoded.CopyTo(yBytes);
        int sign = yBytes[31] >> 7;
        yBytes[31] &= 0x7f;

        BigInteger y = Mod(DecodeLittleEndian(yBytes));
        BigInteger y2 = Mod(y * y);
        BigInteger x2 = Mod((y2 - 1) * ModInverse(Mod((D * y2) + 1)));
        BigInteger x = BigInteger.ModPow(x2, (P + 3) / 8, P);

        if (Mod((x * x) - x2) != 0)
        {
            x = Mod(x * SqrtMinusOne);
        }

        if (Mod((x * x) - x2) != 0)
        {
            return false;
        }

        if ((x.IsEven ? 0 : 1) != sign)
        {
            x = Mod(-x);
        }

        point = new Point(x, y);
        return true;
    }

    private static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        var output = new byte[first.Length + second.Length];
        first.CopyTo(output);
        second.CopyTo(output.AsSpan(first.Length));
        return output;
    }

    private static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third)
    {
        var output = new byte[first.Length + second.Length + third.Length];
        first.CopyTo(output);
        second.CopyTo(output.AsSpan(first.Length));
        third.CopyTo(output.AsSpan(first.Length + second.Length));
        return output;
    }

    private static BigInteger DecodeLittleEndian(ReadOnlySpan<byte> bytes)
    {
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
    }

    private static byte[] EncodeLittleEndian(BigInteger value)
    {
        var output = new byte[32];
        value.TryWriteBytes(output, out _, isUnsigned: true, isBigEndian: false);
        return output;
    }

    private static BigInteger ModInverse(BigInteger value)
    {
        return BigInteger.ModPow(Mod(value), P - 2, P);
    }

    private static BigInteger Mod(BigInteger value)
    {
        BigInteger result = value % P;
        return result.Sign < 0 ? result + P : result;
    }

    private static BigInteger ModL(BigInteger value)
    {
        BigInteger result = value % L;
        return result.Sign < 0 ? result + L : result;
    }

    private readonly record struct Point(BigInteger X, BigInteger Y);
}
