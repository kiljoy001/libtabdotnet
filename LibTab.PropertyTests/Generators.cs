// <copyright file="Generators.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.PropertyTests;

using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck;

/// <summary>
/// FsCheck generators used by the property tests.
/// </summary>
public static class Generators
{
    /// <summary>
    /// Builds arbitrary ndb-safe text values.
    /// </summary>
    /// <returns>The arbitrary safe text generator.</returns>
    public static Arbitrary<SafeText> SafeTexts()
    {
        Gen<char> chars = Gen.Elements(SafeCharacters().ToArray());
        Gen<SafeText> texts = Gen.ListOf(chars).Select(list => new SafeText(new string(list.ToArray())));
        return Arb.From(texts);
    }

    /// <summary>
    /// Builds arbitrary table operation sequences.
    /// </summary>
    /// <returns>The arbitrary operation sequence generator.</returns>
    public static Arbitrary<TableOperation[]> TableOperations()
    {
        Gen<TableOperation> op =
            from kind in Gen.Choose(0, 3)
            from key in Gen.Choose(0, 15)
            from value in Gen.Choose(0, 7)
            select new TableOperation((OperationKind)kind, key, value);

        Gen<TableOperation[]> ops =
            from length in Gen.Choose(1, 64)
            from list in Gen.ListOf(length, op)
            select list.ToArray();

        return Arb.From(ops);
    }

    private static IEnumerable<char> SafeCharacters()
    {
        yield return ' ';
        yield return '\t';
        yield return '\n';
        yield return '\r';
        yield return '"';
        yield return '&';
        yield return '#';

        for (char c = '!'; c <= '~'; c++)
        {
            yield return c;
        }
    }
}
