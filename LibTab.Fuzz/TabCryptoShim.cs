// <copyright file="TabCryptoShim.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Fuzz;

using System;
using LibTab;

/// <summary>
/// Exposes typed-cell verification through table APIs for fuzzing.
/// </summary>
internal static class TabCryptoShim
{
    private const string FuzzPath = "libtab-fuzz.tab";

    /// <summary>
    /// Verifies a candidate HASHED cell through the table API.
    /// </summary>
    /// <param name="cell">The candidate HASHED cell.</param>
    /// <param name="preimage">The preimage bytes.</param>
    /// <returns>True when the cell verifies; otherwise false.</returns>
    public static bool VerifyHashCell(string cell, ReadOnlySpan<byte> preimage)
    {
        TabTable table = TabTable.Create(
            FuzzPath,
            "fuzz",
            new[] { new TabColumn("name"), new TabColumn("secret", "HASHED") });
        TabRow row = table.AddRow("name", "row");
        SetRawTypedCell(row, "secret", cell);
        return table.VerifyHash(row, "secret", preimage);
    }

    /// <summary>
    /// Verifies a candidate SIGNED cell through the table API.
    /// </summary>
    /// <param name="cell">The candidate SIGNED cell.</param>
    /// <param name="publicKey">The signer public key bytes.</param>
    /// <returns>The verified signed body.</returns>
    public static byte[] VerifySignedCell(string cell, ReadOnlySpan<byte> publicKey)
    {
        TabTable table = TabTable.Create(
            FuzzPath,
            "fuzz",
            new[] { new TabColumn("name"), new TabColumn("payload", "SIGNED") });
        TabRow row = table.AddRow("name", "row");
        SetRawTypedCell(row, "payload", cell);
        return table.VerifySigned(row, "payload", publicKey);
    }

    private static void SetRawTypedCell(TabRow row, string column, string cell)
    {
        row.Entry.Add(new NdbTuple(column, cell));
    }
}
