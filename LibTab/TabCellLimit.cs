// <copyright file="TabCellLimit.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Text;

/// <summary>
/// Enforces the libtab encoded cell line limit.
/// </summary>
internal static class TabCellLimit
{
    /// <summary>
    /// Maximum encoded cell line length accepted by libtab.
    /// </summary>
    public const int MaxCellLineBytes = 7168;

    private static readonly Encoding Utf8 = Encoding.UTF8;

    /// <summary>
    /// Rejects a cell value whose emitted ndb line would exceed the libtab limit.
    /// </summary>
    /// <param name="column">The column name.</param>
    /// <param name="value">The semantic cell value, or null for semantic nil.</param>
    /// <param name="operation">The operation name for the error message.</param>
    public static void Validate(string column, string? value, string operation)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);
        ArgumentException.ThrowIfNullOrEmpty(operation);

        if (value is null)
        {
            return;
        }

        string wire = NdbText.NeedsEncoding(value) ? NdbText.Encode(value) : value;
        int encodedBytes = Utf8.GetByteCount(wire);
        if (NdbText.NeedsQuote(wire))
        {
            encodedBytes += 2;
        }

        int framingBytes = 1 + Utf8.GetByteCount(column) + 1 + 1;
        int lineBytes = encodedBytes + framingBytes;
        if (lineBytes > MaxCellLineBytes)
        {
            throw new TabException(
                $"{operation}: cell '{column}' is {lineBytes} bytes encoded, over the {MaxCellLineBytes}-byte limit; store a value this large as a file and keep its path or digest in the cell");
        }
    }
}
