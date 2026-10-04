// <copyright file="TabRow.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;

/// <summary>One libtab row.</summary>
public sealed class TabRow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabRow"/> class.
    /// </summary>
    /// <param name="entry">The backing ndb entry.</param>
    internal TabRow(NdbEntry entry)
    {
        this.Entry = entry;
    }

    /// <summary>
    /// Gets the row's tuple attributes in source order.
    /// </summary>
    public IEnumerable<string> Columns
    {
        get
        {
            foreach (NdbTuple tuple in this.Entry.Tuples)
            {
                yield return tuple.Attribute;
            }
        }
    }

    /// <summary>
    /// Gets or sets the backing ndb entry.
    /// </summary>
    internal NdbEntry Entry { get; set; }

    /// <summary>
    /// Gets a cell value, or null when the cell is absent or semantic nil.
    /// </summary>
    /// <param name="column">The column name to read.</param>
    /// <returns>The semantic cell value, or null.</returns>
    public string? this[string column] => this.Get(column);

    /// <summary>
    /// Gets a cell value, or null when the cell is absent or semantic nil.
    /// </summary>
    /// <param name="column">The column name to read.</param>
    /// <returns>The semantic cell value, or null.</returns>
    public string? Get(string column)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);

        NdbTuple? tuple = this.Entry.First(column);
        return tuple is null || tuple.IsNil ? null : tuple.Value;
    }
}
