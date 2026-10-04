// <copyright file="NdbText.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Globalization;
using System.Text;

/// <summary>Encoding rules for plain libtab cells inside ndb text.</summary>
public static class NdbText
{
    /// <summary>
    /// The legacy prefix used by early text64-encoded plain cells.
    /// </summary>
    internal const string LegacyPrefix = "__libtab_text64_v1:";

    private const string EncodedNil = "n&#105;l";
    private const string LegacyMagic = "libtab-text-v1:";

    /// <summary>
    /// Returns a value indicating whether native ndb text would lose or corrupt this value.
    /// </summary>
    /// <param name="value">The plain cell value.</param>
    /// <returns>True when entity encoding is required; otherwise false.</returns>
    public static bool NeedsEncoding(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value == "nil" || value.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (char c in value)
        {
            if (c is '&' or '"' or '\n' or '\r' || (c < ' ' && c != '\t') || c == '\x7f')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a value indicating whether an ndb value must be quoted to remain one token.
    /// </summary>
    /// <param name="value">The ndb value text.</param>
    /// <returns>True when the value needs double quotes; otherwise false.</returns>
    public static bool NeedsQuote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value[0] == '#' || value.Contains(' ', StringComparison.Ordinal) || value.Contains('\t', StringComparison.Ordinal);
    }

    /// <summary>
    /// Encodes a plain-text cell for writing into an ndb tuple.
    /// </summary>
    /// <param name="value">The semantic cell value.</param>
    /// <returns>The ndb-safe cell text.</returns>
    public static string Encode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        ValidateNoNul(value, nameof(value));

        if (value == "nil")
        {
            return EncodedNil;
        }

        var built = new StringBuilder(value.Length);
        int legacyColon = value.StartsWith(LegacyPrefix, StringComparison.Ordinal)
            ? LegacyPrefix.Length - 1
            : -1;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (i == legacyColon)
            {
                built.Append("&#58;");
                continue;
            }

            switch (c)
            {
                case '&':
                    built.Append("&amp;");
                    break;

                case '"':
                    built.Append("&quot;");
                    break;

                case '\n':
                    built.Append("&#10;");
                    break;

                case '\r':
                    built.Append("&#13;");
                    break;

                default:
                    if ((c < ' ' && c != '\t') || c == '\x7f')
                    {
                        built.Append(CultureInfo.InvariantCulture, $"&#{(int)c};");
                    }
                    else
                    {
                        built.Append(c);
                    }

                    break;
            }
        }

        return built.ToString();
    }

    /// <summary>
    /// Decodes text entities written by libtab.
    /// </summary>
    /// <param name="wire">The encoded ndb value text.</param>
    /// <returns>The semantic cell value.</returns>
    public static string Decode(string? wire)
    {
        if (string.IsNullOrEmpty(wire))
        {
            return string.Empty;
        }

        if (wire.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            return DecodeLegacyText64(wire);
        }

        if (wire.IndexOf('&', StringComparison.Ordinal) < 0)
        {
            ValidateNoNul(wire, nameof(wire));
            return wire;
        }

        var built = new StringBuilder(wire.Length);

        int i = 0;
        while (i < wire.Length)
        {
            if (wire[i] != '&')
            {
                built.Append(wire[i]);
                i++;
                continue;
            }

            int end = wire.IndexOf(';', i + 1);
            if (end < 0 || !TryEntity(wire.AsSpan(i + 1, end - i - 1), out Rune decoded))
            {
                built.Append(wire[i]);
                i++;
                continue;
            }

            built.Append(decoded);
            i = end + 1;
        }

        string result = built.ToString();
        ValidateNoNul(result, nameof(wire));
        return result;
    }

    /// <summary>
    /// Rejects strings containing NUL because ndb/libtab cannot represent them.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="name">The logical argument name used in the error message.</param>
    internal static void ValidateNoNul(string value, string name)
    {
        if (value.IndexOf('\0', StringComparison.Ordinal) >= 0)
        {
            throw new TabException($"{name} contains NUL, which ndb/libtab cannot represent");
        }
    }

    private static string DecodeLegacyText64(string wire)
    {
        byte[] raw = TabCodec.B64Decode(wire[LegacyPrefix.Length..]);
        byte[] magic = Encoding.UTF8.GetBytes(LegacyMagic);

        if (raw.Length < magic.Length || !raw.AsSpan(0, magic.Length).SequenceEqual(magic))
        {
            throw new TabException("legacy text64 cell has bad magic");
        }

        ReadOnlySpan<byte> text = raw.AsSpan(magic.Length);
        if (text.IndexOf((byte)0) >= 0)
        {
            throw new TabException("legacy text64 cell contains NUL");
        }

        return Encoding.UTF8.GetString(text);
    }

    private static bool TryEntity(ReadOnlySpan<char> body, out Rune decoded)
    {
        if (TryNamedEntity(body, out decoded))
        {
            return true;
        }

        if (!TryGetEntityDigits(body, out bool hex, out ReadOnlySpan<char> digits))
        {
            return false;
        }

        return TryNumericEntity(digits, hex, out decoded);
    }

    private static bool TryNamedEntity(ReadOnlySpan<char> body, out Rune decoded)
    {
        decoded = default;

        if (body.SequenceEqual("amp"))
        {
            decoded = new Rune('&');
            return true;
        }

        if (body.SequenceEqual("quot"))
        {
            decoded = new Rune('"');
            return true;
        }

        if (body.SequenceEqual("apos"))
        {
            decoded = new Rune('\'');
            return true;
        }

        if (body.SequenceEqual("lt"))
        {
            decoded = new Rune('<');
            return true;
        }

        if (body.SequenceEqual("gt"))
        {
            decoded = new Rune('>');
            return true;
        }

        return false;
    }

    private static bool TryGetEntityDigits(ReadOnlySpan<char> body, out bool hex, out ReadOnlySpan<char> digits)
    {
        hex = body.Length > 2 && (body[1] == 'x' || body[1] == 'X');
        if (body.Length < 2 || body[0] != '#')
        {
            digits = default;
            return false;
        }

        digits = hex ? body[2..] : body[1..];
        return !digits.IsEmpty;
    }

    private static bool TryNumericEntity(ReadOnlySpan<char> digits, bool hex, out Rune decoded)
    {
        decoded = default;
        int value = 0;
        foreach (char c in digits)
        {
            int digit = Digit(c, hex);
            if (digit < 0)
            {
                return false;
            }

            try
            {
                value = checked((value * (hex ? 16 : 10)) + digit);
            }
            catch (OverflowException)
            {
                return false;
            }

            if (value > 0x10ffff)
            {
                return false;
            }
        }

        if (value == 0 || !Rune.TryCreate(value, out decoded))
        {
            throw new TabException("encoded text cell contains NUL or invalid Unicode");
        }

        return true;
    }

    private static int Digit(char c, bool hex)
    {
        if (c is >= '0' and <= '9')
        {
            return c - '0';
        }

        if (!hex)
        {
            return -1;
        }

        if (c is >= 'a' and <= 'f')
        {
            return c - 'a' + 10;
        }

        if (c is >= 'A' and <= 'F')
        {
            return c - 'A' + 10;
        }

        return -1;
    }
}
