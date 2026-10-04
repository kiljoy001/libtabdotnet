// <copyright file="NdbParser.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;

/// <summary>
/// Parses the Plan 9 ndb subset used by libtab.
/// </summary>
internal static class NdbParser
{
    private const int MaxAttributeLength = 31;

    /// <summary>
    /// Parses complete ndb entries from text.
    /// </summary>
    /// <param name="text">The ndb text to parse.</param>
    /// <returns>The parsed entries.</returns>
    public static IReadOnlyList<NdbEntry> ParseEntries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var entries = new List<NdbEntry>();
        NdbEntry? current = null;
        int lineStart = 0;

        while (lineStart < text.Length)
        {
            int newline = text.IndexOf('\n', lineStart);
            if (newline < 0)
            {
                break;
            }

            ReadOnlySpan<char> line = text.AsSpan(lineStart, newline - lineStart + 1);
            lineStart = newline + 1;

            if (current is not null && !IsWhite(line[0]) && line[0] != '#')
            {
                current = null;
            }

            IReadOnlyList<NdbTuple> parsed = ParseLine(line);
            if (parsed.Count == 0)
            {
                continue;
            }

            if (current is null)
            {
                current = new NdbEntry();
                entries.Add(current);
            }

            current.AddLine(parsed);
        }

        return entries;
    }

    private static IReadOnlyList<NdbTuple> ParseLine(ReadOnlySpan<char> line)
    {
        var tuples = new List<NdbTuple>();
        int at = 0;

        while (at < line.Length && line[at] != '#' && line[at] != '\n')
        {
            NdbTuple? tuple;
            at = ParseTuple(line, at, out tuple);

            if (tuple is null)
            {
                break;
            }

            tuples.Add(tuple);
        }

        foreach (NdbTuple tuple in tuples)
        {
            tuple.Line = tuples;
        }

        return tuples;
    }

    private static int ParseTuple(ReadOnlySpan<char> line, int at, out NdbTuple? tuple)
    {
        tuple = null;

        at = SkipWhite(line, at);
        if (AtCommentOrLineEnd(line, at))
        {
            return line.Length;
        }

        string attr = ReadAttribute(line, ref at);
        at = SkipWhite(line, at);

        string value = at < line.Length && line[at] == '='
            ? ReadValue(line, ref at)
            : string.Empty;

        tuple = new NdbTuple(attr, value);
        return at;
    }

    private static int SkipWhite(ReadOnlySpan<char> line, int at)
    {
        while (at < line.Length && IsWhite(line[at]))
        {
            at++;
        }

        return at;
    }

    private static bool AtCommentOrLineEnd(ReadOnlySpan<char> line, int at)
    {
        return at >= line.Length || line[at] is '#' or '\n';
    }

    private static string ReadAttribute(ReadOnlySpan<char> line, ref int at)
    {
        int attrStart = at;
        while (at < line.Length && line[at] != '=' && !IsWhite(line[at]) && line[at] != '\n')
        {
            at++;
        }

        int attrLength = Math.Min(at - attrStart, MaxAttributeLength);
        return new string(line.Slice(attrStart, attrLength));
    }

    private static string ReadValue(ReadOnlySpan<char> line, ref int at)
    {
        at++;
        if (at >= line.Length || line[at] == '#')
        {
            return string.Empty;
        }

        return line[at] == '"'
            ? ReadQuotedValue(line, ref at)
            : ReadBareValue(line, ref at);
    }

    private static string ReadQuotedValue(ReadOnlySpan<char> line, ref int at)
    {
        at++;
        int valueStart = at;
        while (at < line.Length && line[at] != '\n' && line[at] != '"')
        {
            at++;
        }

        string value = new(line[valueStart..at]);
        if (at < line.Length && line[at] == '"')
        {
            at++;
        }

        return value;
    }

    private static string ReadBareValue(ReadOnlySpan<char> line, ref int at)
    {
        int valueStart = at;
        while (at < line.Length && !IsWhite(line[at]) && line[at] != '\n')
        {
            at++;
        }

        return new string(line[valueStart..at]);
    }

    private static bool IsWhite(char c)
    {
        return c is ' ' or '\t' or '\r';
    }
}
