// <copyright file="TestData.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Tests;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shared fixtures for unit tests.
/// </summary>
internal static class TestData
{
    /// <summary>
    /// The pinned Monocypher-compatible public key observed from C libtab.
    /// </summary>
    public const string PinnedPublicKey = "f65333fa6303b6a23defd7de2af8aa461cb047ccbf12d4edd29ef3b1eba6706b";

    /// <summary>
    /// The pinned Monocypher-compatible signature observed from C libtab.
    /// </summary>
    public const string PinnedSignature = "e83009fa8880c1af370a90223796f87126697ead9176619c9f7a2d7bccd50b0fc167bb32a20e1061d80dd719b3f870470c6a452d1bc96505ed775e1885b72200";

    /// <summary>
    /// Builds plain schema columns.
    /// </summary>
    /// <param name="names">The column names.</param>
    /// <returns>The plain columns.</returns>
    public static IReadOnlyList<TabColumn> PlainColumns(params string[] names)
    {
        return names.Select(name => new TabColumn(name)).ToArray();
    }

    /// <summary>
    /// Builds a unique temporary table path.
    /// </summary>
    /// <param name="name">The logical test name.</param>
    /// <returns>The temporary table path.</returns>
    public static string TempPath(string name)
    {
        return System.IO.Path.Combine(System.IO.Path.GetTempPath(), string.Concat("libtabdotnet-", name, "-", Guid.NewGuid().ToString("n"), ".tab"));
    }

    /// <summary>
    /// Formats bytes as lowercase hexadecimal.
    /// </summary>
    /// <param name="bytes">The bytes to format.</param>
    /// <returns>The lowercase hexadecimal text.</returns>
    public static string Hex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
