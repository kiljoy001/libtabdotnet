// <copyright file="TabCodec.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Text;

/// <summary>Base64url and typed-cell helpers used by libtab cells.</summary>
public static class TabCodec
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    private static readonly byte[] DecodeTable = BuildDecodeTable();

    /// <summary>
    /// Encodes bytes with RFC 4648 section 5 base64url, including padding.
    /// </summary>
    /// <param name="input">The bytes to encode.</param>
    /// <returns>The base64url text.</returns>
    public static string B64Encode(ReadOnlySpan<byte> input)
    {
        if (input.IsEmpty)
        {
            return string.Empty;
        }

        string standard = Convert.ToBase64String(input);
        return standard.Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Decodes libtab base64url text.
    /// </summary>
    /// <param name="text">The base64url text to decode.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] B64Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if ((text.Length & 3) != 0)
        {
            throw new TabException($"base64url length {text.Length} is not a multiple of 4");
        }

        if (text.Length == 0)
        {
            return Array.Empty<byte>();
        }

        int pad = 0;
        if (text[^1] == '=')
        {
            pad++;
        }

        if (text[^2] == '=')
        {
            pad++;
        }

        if (pad > 2)
        {
            throw new TabException("base64url has too much padding");
        }

        var output = new byte[(text.Length / 4 * 3) - pad];
        int outIndex = 0;

        for (int i = 0; i < text.Length; i += 4)
        {
            byte v0 = Decode(text[i], i);
            byte v1 = Decode(text[i + 1], i);
            byte v2 = text[i + 2] == '=' ? (byte)0 : Decode(text[i + 2], i);
            byte v3 = text[i + 3] == '=' ? (byte)0 : Decode(text[i + 3], i);
            uint quad = ((uint)v0 << 18) | ((uint)v1 << 12) | ((uint)v2 << 6) | v3;

            if (outIndex < output.Length)
            {
                output[outIndex++] = (byte)((quad >> 16) & 0xff);
            }

            if (outIndex < output.Length)
            {
                output[outIndex++] = (byte)((quad >> 8) & 0xff);
            }

            if (outIndex < output.Length)
            {
                output[outIndex++] = (byte)(quad & 0xff);
            }
        }

        return output;
    }

    /// <summary>
    /// Returns a value indicating whether <paramref name="cell"/> starts with the lowercase form of <paramref name="columnType"/> and a colon.
    /// </summary>
    /// <param name="cell">The candidate typed cell.</param>
    /// <param name="columnType">The schema column type.</param>
    /// <returns>True when the tag is present; otherwise false.</returns>
    public static bool CellHasTag(string? cell, string? columnType)
    {
        if (cell is null || columnType is null || cell.Length <= columnType.Length)
        {
            return false;
        }

        for (int i = 0; i < columnType.Length; i++)
        {
            if (AsciiLower(cell[i]) != AsciiLower(columnType[i]))
            {
                return false;
            }
        }

        return cell[columnType.Length] == ':';
    }

    /// <summary>
    /// Decodes a typed cell payload after validating its tag.
    /// </summary>
    /// <param name="cell">The typed cell text.</param>
    /// <param name="columnType">The expected schema column type.</param>
    /// <returns>The decoded payload bytes.</returns>
    public static byte[] CellDecode(string cell, string columnType)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(columnType);

        if (!CellHasTag(cell, columnType))
        {
            throw new TabException($"cell is missing {columnType}: tag");
        }

        return B64Decode(cell[(columnType.Length + 1)..]);
    }

    /// <summary>
    /// Builds a typed cell as <c>lowercase-type:base64url</c>.
    /// </summary>
    /// <param name="columnType">The schema column type.</param>
    /// <param name="payload">The binary payload to encode.</param>
    /// <returns>The encoded typed cell.</returns>
    public static string CellEncode(string columnType, ReadOnlySpan<byte> payload)
    {
        ArgumentException.ThrowIfNullOrEmpty(columnType);

        return string.Concat(AsciiLower(columnType), ":", B64Encode(payload));
    }

    /// <summary>
    /// Encodes text as UTF-8 bytes.
    /// </summary>
    /// <param name="value">The text to encode.</param>
    /// <returns>The UTF-8 bytes.</returns>
    internal static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    private static byte Decode(char c, int offset)
    {
        if (c > 255 || DecodeTable[c] == 0xff)
        {
            throw new TabException($"base64url has an invalid character at offset {offset}");
        }

        return DecodeTable[c];
    }

    private static byte[] BuildDecodeTable()
    {
        var table = new byte[256];
        Array.Fill(table, (byte)0xff);

        for (int i = 0; i < Alphabet.Length; i++)
        {
            table[Alphabet[i]] = (byte)i;
        }

        return table;
    }

    private static string AsciiLower(string value)
    {
        Span<char> chars = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];

        for (int i = 0; i < value.Length; i++)
        {
            chars[i] = AsciiLower(value[i]);
        }

        return new string(chars);
    }

    private static char AsciiLower(char c)
    {
        return c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
    }
}
