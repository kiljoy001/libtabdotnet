// <copyright file="TabCrypto.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;

/// <summary>
/// Implements libtab typed-cell cryptography.
/// </summary>
internal static class TabCrypto
{
    /// <summary>
    /// The fixed HASHED digest length in bytes.
    /// </summary>
    public const int HashDigestLength = 32;

    /// <summary>
    /// The fixed ENCRYPTED key length in bytes.
    /// </summary>
    public const int EncryptedKeyLength = 32;

    private const byte HashedAlgoBlake2b = 0x01;
    private const byte HashedAlgoArgon2id = 0x02;
    private const byte Argon2idDefaultMLog2 = 16;
    private const byte Argon2idDefaultPasses = 3;
    private const byte Argon2idDefaultParallelism = 1;
    private const int Argon2idDefaultSaltLength = 16;
    private const int Argon2idMaxSaltLength = 64;
    private const byte EncryptedVersion = 0x01;
    private const int EncryptedNonceLength = 24;
    private const int EncryptedMacLength = 16;
    private const int EncryptedHeaderLength = 1 + EncryptedNonceLength + EncryptedMacLength;
    private const int ChaChaBlockLength = 64;
    private const uint ChaChaConstant0 = 0x61707865;
    private const uint ChaChaConstant1 = 0x3320646e;
    private const uint ChaChaConstant2 = 0x79622d32;
    private const uint ChaChaConstant3 = 0x6b206574;

    private static readonly byte[] ZeroBlock = new byte[16];

    /// <summary>
    /// Creates a HASHED cell using the requested schema algorithm.
    /// </summary>
    /// <param name="preimage">The preimage bytes to hash.</param>
    /// <param name="algorithm">The optional schema algorithm name.</param>
    /// <returns>The encoded HASHED cell.</returns>
    public static string CreateHashedCell(ReadOnlySpan<byte> preimage, string? algorithm)
    {
        return algorithm switch
        {
            null or "" or "blake2b" => CreateBlake2bCell(preimage),
            "argon2id" => CreateArgon2idCell(preimage),
            _ => throw new TabException($"HASHED algorithm '{algorithm}' is not supported"),
        };
    }

    /// <summary>
    /// Creates an argon2id HASHED cell using default libtab parameters.
    /// </summary>
    /// <param name="preimage">The preimage bytes to hash.</param>
    /// <returns>The encoded HASHED cell.</returns>
    public static string CreateArgon2idCell(ReadOnlySpan<byte> preimage)
    {
        Span<byte> salt = stackalloc byte[Argon2idDefaultSaltLength];
        RandomNumberGenerator.Fill(salt);
        return CreateArgon2idCell(preimage, salt);
    }

    /// <summary>
    /// Verifies a HASHED cell against a preimage.
    /// </summary>
    /// <param name="cell">The encoded HASHED cell.</param>
    /// <param name="preimage">The preimage bytes to verify.</param>
    /// <returns>True when the preimage matches; otherwise false.</returns>
    public static bool VerifyHashCell(string? cell, ReadOnlySpan<byte> preimage)
    {
        if (string.IsNullOrEmpty(cell))
        {
            return false;
        }

        byte[] wire = TabCodec.CellDecode(cell, "HASHED");
        if (wire.Length < 1)
        {
            throw new TabException("HASHED cell is too short");
        }

        switch (wire[0])
        {
            case HashedAlgoBlake2b:
                if (wire.Length != 1 + HashDigestLength)
                {
                    throw new TabException($"BLAKE2b HASHED cell is {wire.Length} bytes, expected {1 + HashDigestLength}");
                }

                byte[] blake = Blake2b(preimage, HashDigestLength);
                return CryptographicOperations.FixedTimeEquals(wire.AsSpan(1), blake);

            case HashedAlgoArgon2id:
                return VerifyArgon2idCell(wire, preimage);

            default:
                throw new TabException($"unknown HASHED algorithm id 0x{wire[0]:x2}");
        }
    }

    /// <summary>
    /// Creates a SIGNED cell using Monocypher-compatible EdDSA.
    /// </summary>
    /// <param name="body">The body bytes to sign.</param>
    /// <param name="signerSecretKey">The 64-byte signer secret key.</param>
    /// <returns>The encoded SIGNED cell.</returns>
    public static string CreateSignedCell(ReadOnlySpan<byte> body, ReadOnlySpan<byte> signerSecretKey)
    {
        byte[] signature = MonocypherEdDsa.Sign(signerSecretKey, body);
        return string.Concat("signed:", TabCodec.B64Encode(body), ":", TabCodec.B64Encode(signature));
    }

    /// <summary>
    /// Verifies a SIGNED cell and returns its body.
    /// </summary>
    /// <param name="cell">The encoded SIGNED cell.</param>
    /// <param name="signerPublicKey">The 32-byte signer public key.</param>
    /// <returns>The signed body bytes.</returns>
    public static byte[] VerifySignedCell(string? cell, ReadOnlySpan<byte> signerPublicKey)
    {
        if (string.IsNullOrEmpty(cell) || !cell.StartsWith("signed:", StringComparison.Ordinal))
        {
            throw new TabException("cell is missing signed: prefix");
        }

        ReadOnlySpan<char> rest = cell.AsSpan("signed:".Length);
        int separator = rest.IndexOf(':');
        if (separator < 0)
        {
            throw new TabException("SIGNED cell is missing body/signature separator");
        }

        byte[] body = TabCodec.B64Decode(new string(rest[..separator]));
        byte[] signature = TabCodec.B64Decode(new string(rest[(separator + 1)..]));

        if (signature.Length != MonocypherEdDsa.SignatureLength)
        {
            throw new TabException($"SIGNED signature is {signature.Length} bytes, expected {MonocypherEdDsa.SignatureLength}");
        }

        if (!MonocypherEdDsa.Verify(signature, signerPublicKey, body))
        {
            throw new TabException("SIGNED signature check failed");
        }

        return body;
    }

    /// <summary>
    /// Creates an ENCRYPTED cell using Monocypher-compatible XChaCha20-Poly1305.
    /// </summary>
    /// <param name="plaintext">The plaintext bytes to encrypt.</param>
    /// <param name="key">The 32-byte encryption key.</param>
    /// <returns>The encoded ENCRYPTED cell.</returns>
    public static string CreateEncryptedCell(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        Span<byte> nonce = stackalloc byte[EncryptedNonceLength];
        RandomNumberGenerator.Fill(nonce);
        return CreateEncryptedCell(plaintext, key, nonce);
    }

    /// <summary>
    /// Decrypts an ENCRYPTED cell using Monocypher-compatible XChaCha20-Poly1305.
    /// </summary>
    /// <param name="cell">The encoded ENCRYPTED cell.</param>
    /// <param name="key">The 32-byte encryption key.</param>
    /// <returns>The authenticated plaintext bytes.</returns>
    public static byte[] DecryptEncryptedCell(string? cell, ReadOnlySpan<byte> key)
    {
        ValidateEncryptedKey(key);
        if (string.IsNullOrEmpty(cell))
        {
            throw new TabException("ENCRYPTED cell is empty");
        }

        byte[] wire = TabCodec.CellDecode(cell, "ENCRYPTED");
        if (wire.Length < EncryptedHeaderLength)
        {
            throw new TabException(
                $"ENCRYPTED cell is {wire.Length} bytes, too short for a {EncryptedHeaderLength}-byte header");
        }

        if (wire[0] != EncryptedVersion)
        {
            throw new TabException($"unknown ENCRYPTED version 0x{wire[0]:x2}");
        }

        return DecryptXChaCha20Poly1305(
            wire.AsSpan(EncryptedHeaderLength),
            key,
            wire.AsSpan(1, EncryptedNonceLength),
            wire.AsSpan(1 + EncryptedNonceLength, EncryptedMacLength));
    }

    /// <summary>
    /// Creates a BLAKE2b HASHED cell.
    /// </summary>
    /// <param name="preimage">The preimage bytes to hash.</param>
    /// <returns>The encoded HASHED cell.</returns>
    internal static string CreateBlake2bCell(ReadOnlySpan<byte> preimage)
    {
        Span<byte> wire = stackalloc byte[1 + HashDigestLength];
        wire[0] = HashedAlgoBlake2b;
        Blake2b(preimage, HashDigestLength).CopyTo(wire[1..]);
        return TabCodec.CellEncode("HASHED", wire);
    }

    /// <summary>
    /// Creates an ENCRYPTED cell using a supplied nonce.
    /// </summary>
    /// <param name="plaintext">The plaintext bytes to encrypt.</param>
    /// <param name="key">The 32-byte encryption key.</param>
    /// <param name="nonce">The 24-byte XChaCha20 nonce.</param>
    /// <returns>The encoded ENCRYPTED cell.</returns>
    internal static string CreateEncryptedCell(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce)
    {
        ValidateEncryptedKey(key);
        ValidateEncryptedNonce(nonce);

        byte[] ciphertext = EncryptXChaCha20Poly1305(plaintext, key, nonce, out byte[] mac);
        var wire = new byte[EncryptedHeaderLength + ciphertext.Length];
        wire[0] = EncryptedVersion;
        nonce.CopyTo(wire.AsSpan(1, EncryptedNonceLength));
        mac.CopyTo(wire.AsSpan(1 + EncryptedNonceLength, EncryptedMacLength));
        ciphertext.CopyTo(wire.AsSpan(EncryptedHeaderLength));
        CryptographicOperations.ZeroMemory(mac);
        return TabCodec.CellEncode("ENCRYPTED", wire);
    }

    /// <summary>
    /// Creates an argon2id HASHED cell using default work parameters and a supplied salt.
    /// </summary>
    /// <param name="preimage">The preimage bytes to hash.</param>
    /// <param name="salt">The argon2id salt bytes.</param>
    /// <returns>The encoded HASHED cell.</returns>
    internal static string CreateArgon2idCell(ReadOnlySpan<byte> preimage, ReadOnlySpan<byte> salt)
    {
        return CreateArgon2idCell(
            preimage,
            salt,
            Argon2idDefaultMLog2,
            Argon2idDefaultPasses,
            Argon2idDefaultParallelism);
    }

    /// <summary>
    /// Creates an argon2id HASHED cell using explicit work parameters.
    /// </summary>
    /// <param name="preimage">The preimage bytes to hash.</param>
    /// <param name="salt">The argon2id salt bytes.</param>
    /// <param name="mLog2">The log2 memory cost in KiB.</param>
    /// <param name="passes">The iteration count.</param>
    /// <param name="parallelism">The parallelism degree.</param>
    /// <returns>The encoded HASHED cell.</returns>
    internal static string CreateArgon2idCell(
        ReadOnlySpan<byte> preimage,
        ReadOnlySpan<byte> salt,
        byte mLog2,
        byte passes,
        byte parallelism)
    {
        if (salt.Length is < 1 or > Argon2idMaxSaltLength)
        {
            throw new TabException($"argon2id salt length must be between 1 and {Argon2idMaxSaltLength} bytes");
        }

        byte[] digest = Argon2id(
            preimage,
            salt,
            mLog2,
            passes,
            parallelism);
        var wire = new byte[1 + 4 + salt.Length + HashDigestLength];
        wire[0] = HashedAlgoArgon2id;
        wire[1] = mLog2;
        wire[2] = passes;
        wire[3] = parallelism;
        wire[4] = (byte)salt.Length;
        salt.CopyTo(wire.AsSpan(5));
        digest.CopyTo(wire.AsSpan(5 + salt.Length));
        return TabCodec.CellEncode("HASHED", wire);
    }

    /// <summary>
    /// Computes a BLAKE2b digest.
    /// </summary>
    /// <param name="input">The input bytes.</param>
    /// <param name="digestLength">The digest length in bytes.</param>
    /// <returns>The digest bytes.</returns>
    internal static byte[] Blake2b(ReadOnlySpan<byte> input, int digestLength)
    {
        var digest = new Blake2bDigest(digestLength * 8);
        byte[] output = new byte[digestLength];

        foreach (byte b in input)
        {
            digest.Update(b);
        }

        digest.DoFinal(output, 0);
        return output;
    }

    private static bool VerifyArgon2idCell(ReadOnlySpan<byte> wire, ReadOnlySpan<byte> preimage)
    {
        if (wire.Length < 1 + 4 + HashDigestLength)
        {
            throw new TabException("argon2id HASHED cell is too short");
        }

        byte mLog2 = wire[1];
        byte passes = wire[2];
        byte parallelism = wire[3];
        byte saltLength = wire[4];

        if (saltLength is < 1 or > Argon2idMaxSaltLength)
        {
            throw new TabException($"argon2id salt length {saltLength} is out of range");
        }

        if (wire.Length != 1 + 4 + saltLength + HashDigestLength)
        {
            throw new TabException("argon2id HASHED cell length does not match its header");
        }

        if (mLog2 is < 3 or > 24)
        {
            throw new TabException($"argon2id m_log2={mLog2} is out of range");
        }

        if (passes < 1 || parallelism < 1)
        {
            throw new TabException("argon2id passes and parallelism must both be at least 1");
        }

        byte[] computed = Argon2id(preimage, wire.Slice(5, saltLength), mLog2, passes, parallelism);
        return CryptographicOperations.FixedTimeEquals(
            wire.Slice(5 + saltLength, HashDigestLength),
            computed);
    }

    private static byte[] Argon2id(
        ReadOnlySpan<byte> preimage,
        ReadOnlySpan<byte> salt,
        byte mLog2,
        byte passes,
        byte parallelism)
    {
        var parameters = new Argon2Parameters.Builder(Argon2Parameters.Argon2id)
            .WithVersion(Argon2Parameters.Version13)
            .WithMemoryAsKB(1 << mLog2)
            .WithIterations(passes)
            .WithParallelism(parallelism)
            .WithSalt(salt.ToArray())
            .Build();

        var generator = new Argon2BytesGenerator();
        var output = new byte[HashDigestLength];
        generator.Init(parameters);
        generator.GenerateBytes(preimage.ToArray(), output, 0, output.Length);
        return output;
    }

    private static byte[] EncryptXChaCha20Poly1305(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        out byte[] mac)
    {
        byte[] subKey = HChaCha20(key, nonce[..16]);
        byte[] macKey = ChaCha20KeyStream(subKey, nonce[16..], 0, EncryptedKeyLength);
        byte[] ciphertext = ChaCha20Xor(plaintext, subKey, nonce[16..], 1);
        mac = Poly1305Aead(macKey, ciphertext);
        CryptographicOperations.ZeroMemory(subKey);
        CryptographicOperations.ZeroMemory(macKey);
        return ciphertext;
    }

    private static byte[] DecryptXChaCha20Poly1305(
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> mac)
    {
        byte[] subKey = HChaCha20(key, nonce[..16]);
        byte[] macKey = ChaCha20KeyStream(subKey, nonce[16..], 0, EncryptedKeyLength);
        byte[] actualMac = Poly1305Aead(macKey, ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(mac, actualMac))
        {
            CryptographicOperations.ZeroMemory(subKey);
            CryptographicOperations.ZeroMemory(macKey);
            CryptographicOperations.ZeroMemory(actualMac);
            throw new TabException("ENCRYPTED authentication failed");
        }

        byte[] plaintext = ChaCha20Xor(ciphertext, subKey, nonce[16..], 1);
        CryptographicOperations.ZeroMemory(subKey);
        CryptographicOperations.ZeroMemory(macKey);
        CryptographicOperations.ZeroMemory(actualMac);
        return plaintext;
    }

    private static byte[] HChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        Span<uint> state = stackalloc uint[16];
        SetChaChaKeyState(state, key);
        state[12] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[..4]);
        state[13] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[4..8]);
        state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[8..12]);
        state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[12..16]);

        ChaChaRounds(state);

        var output = new byte[EncryptedKeyLength];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(0, 4), state[0]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4, 4), state[1]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), state[2]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), state[3]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(16, 4), state[12]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(20, 4), state[13]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(24, 4), state[14]);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(28, 4), state[15]);
        return output;
    }

    private static byte[] ChaCha20KeyStream(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ulong counter,
        int length)
    {
        var output = new byte[length];
        FillChaCha20(output, ReadOnlySpan<byte>.Empty, key, nonce, counter);
        return output;
    }

    private static byte[] ChaCha20Xor(
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ulong counter)
    {
        var output = new byte[input.Length];
        FillChaCha20(output, input, key, nonce, counter);
        return output;
    }

    private static void FillChaCha20(
        Span<byte> output,
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ulong counter)
    {
        Span<byte> block = stackalloc byte[ChaChaBlockLength];
        int offset = 0;

        while (offset < output.Length)
        {
            ChaChaBlock(block, key, nonce, counter++);
            int count = Math.Min(ChaChaBlockLength, output.Length - offset);
            for (int i = 0; i < count; i++)
            {
                byte value = input.IsEmpty ? (byte)0 : input[offset + i];
                output[offset + i] = (byte)(value ^ block[i]);
            }

            offset += count;
        }

        CryptographicOperations.ZeroMemory(block);
    }

    private static void ChaChaBlock(Span<byte> output, ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ulong counter)
    {
        Span<uint> state = stackalloc uint[16];
        Span<uint> working = stackalloc uint[16];
        SetChaChaKeyState(state, key);
        state[12] = (uint)counter;
        state[13] = (uint)(counter >> 32);
        state[14] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[..4]);
        state[15] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[4..8]);
        state.CopyTo(working);

        ChaChaRounds(working);
        for (int i = 0; i < working.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(i * 4, 4), unchecked(working[i] + state[i]));
        }
    }

    private static void SetChaChaKeyState(Span<uint> state, ReadOnlySpan<byte> key)
    {
        state[0] = ChaChaConstant0;
        state[1] = ChaChaConstant1;
        state[2] = ChaChaConstant2;
        state[3] = ChaChaConstant3;
        for (int i = 0; i < 8; i++)
        {
            state[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key.Slice(i * 4, 4));
        }
    }

    private static void ChaChaRounds(Span<uint> state)
    {
        for (int i = 0; i < 10; i++)
        {
            QuarterRound(ref state[0], ref state[4], ref state[8], ref state[12]);
            QuarterRound(ref state[1], ref state[5], ref state[9], ref state[13]);
            QuarterRound(ref state[2], ref state[6], ref state[10], ref state[14]);
            QuarterRound(ref state[3], ref state[7], ref state[11], ref state[15]);
            QuarterRound(ref state[0], ref state[5], ref state[10], ref state[15]);
            QuarterRound(ref state[1], ref state[6], ref state[11], ref state[12]);
            QuarterRound(ref state[2], ref state[7], ref state[8], ref state[13]);
            QuarterRound(ref state[3], ref state[4], ref state[9], ref state[14]);
        }
    }

    private static void QuarterRound(ref uint a, ref uint b, ref uint c, ref uint d)
    {
        a = unchecked(a + b);
        d = BitOperations.RotateLeft(d ^ a, 16);
        c = unchecked(c + d);
        b = BitOperations.RotateLeft(b ^ c, 12);
        a = unchecked(a + b);
        d = BitOperations.RotateLeft(d ^ a, 8);
        c = unchecked(c + d);
        b = BitOperations.RotateLeft(b ^ c, 7);
    }

    private static byte[] Poly1305Aead(ReadOnlySpan<byte> macKey, ReadOnlySpan<byte> ciphertext)
    {
        var poly1305 = new Poly1305();
        poly1305.Init(new KeyParameter(macKey.ToArray()));
        UpdatePadded(poly1305, ciphertext);

        Span<byte> sizes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(sizes[..8], 0);
        BinaryPrimitives.WriteUInt64LittleEndian(sizes[8..], (ulong)ciphertext.Length);
        byte[] sizeBytes = sizes.ToArray();
        poly1305.BlockUpdate(sizeBytes, 0, sizeBytes.Length);

        var tag = new byte[EncryptedMacLength];
        poly1305.DoFinal(tag, 0);
        return tag;
    }

    private static void UpdatePadded(Poly1305 poly1305, ReadOnlySpan<byte> input)
    {
        if (!input.IsEmpty)
        {
            byte[] bytes = input.ToArray();
            poly1305.BlockUpdate(bytes, 0, bytes.Length);
        }

        int padding = (16 - (input.Length & 15)) & 15;
        if (padding > 0)
        {
            poly1305.BlockUpdate(ZeroBlock, 0, padding);
        }
    }

    private static void ValidateEncryptedKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != EncryptedKeyLength)
        {
            throw new TabException($"ENCRYPTED key is {key.Length} bytes, expected {EncryptedKeyLength}");
        }
    }

    private static void ValidateEncryptedNonce(ReadOnlySpan<byte> nonce)
    {
        if (nonce.Length != EncryptedNonceLength)
        {
            throw new TabException($"ENCRYPTED nonce is {nonce.Length} bytes, expected {EncryptedNonceLength}");
        }
    }
}
