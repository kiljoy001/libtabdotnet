// <copyright file="NdbEntry.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One ndb entry, including all continuation-line tuples.
/// </summary>
internal sealed class NdbEntry
{
    private readonly List<NdbTuple> tuples;

    /// <summary>
    /// Initializes a new instance of the <see cref="NdbEntry"/> class.
    /// </summary>
    public NdbEntry()
    {
        this.tuples = new List<NdbTuple>();
    }

    /// <summary>
    /// Gets the tuples in source order.
    /// </summary>
    public IReadOnlyList<NdbTuple> Tuples => this.tuples;

    /// <summary>
    /// Gets the tuple count.
    /// </summary>
    public int Count => this.tuples.Count;

    /// <summary>
    /// Gets a tuple by source-order index.
    /// </summary>
    /// <param name="index">The tuple index.</param>
    /// <returns>The tuple at the requested index.</returns>
    public NdbTuple this[int index] => this.tuples[index];

    /// <summary>
    /// Appends a parsed ndb line to this entry.
    /// </summary>
    /// <param name="line">The parsed line tuples.</param>
    public void AddLine(IReadOnlyList<NdbTuple> line)
    {
        ArgumentNullException.ThrowIfNull(line);

        foreach (NdbTuple tuple in line)
        {
            this.tuples.Add(tuple);
        }
    }

    /// <summary>
    /// Finds the first tuple with the requested attribute.
    /// </summary>
    /// <param name="attribute">The attribute to find.</param>
    /// <returns>The first matching tuple, or null when no tuple matches.</returns>
    public NdbTuple? First(string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        return this.tuples.FirstOrDefault(tuple => string.Equals(tuple.Attribute, attribute, StringComparison.Ordinal));
    }

    /// <summary>
    /// Finds the source-order index of the first tuple with the requested attribute.
    /// </summary>
    /// <param name="attribute">The attribute to find.</param>
    /// <returns>The first matching tuple index, or -1 when no tuple matches.</returns>
    public int IndexOfFirst(string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        for (int i = 0; i < this.tuples.Count; i++)
        {
            if (string.Equals(this.tuples[i].Attribute, attribute, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Appends one tuple as a single-line tuple.
    /// </summary>
    /// <param name="tuple">The tuple to append.</param>
    public void Add(NdbTuple tuple)
    {
        ArgumentNullException.ThrowIfNull(tuple);
        tuple.Line = new[] { tuple };
        this.tuples.Add(tuple);
    }

    /// <summary>
    /// Inserts one tuple as a single-line tuple.
    /// </summary>
    /// <param name="index">The insertion index.</param>
    /// <param name="tuple">The tuple to insert.</param>
    public void Insert(int index, NdbTuple tuple)
    {
        ArgumentNullException.ThrowIfNull(tuple);
        tuple.Line = new[] { tuple };
        this.tuples.Insert(index, tuple);
    }

    /// <summary>
    /// Removes a tuple by source-order index.
    /// </summary>
    /// <param name="index">The tuple index to remove.</param>
    public void RemoveAt(int index)
    {
        this.tuples.RemoveAt(index);
    }

    /// <summary>
    /// Creates a detached copy of this entry.
    /// </summary>
    /// <returns>The cloned entry.</returns>
    public NdbEntry Clone()
    {
        var clone = new NdbEntry();

        foreach (NdbTuple tuple in this.tuples)
        {
            clone.Add(tuple.Clone());
        }

        return clone;
    }
}
