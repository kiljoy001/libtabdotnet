// <copyright file="RowIdentity.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Stable byte identity for duplicate row detection.
/// </summary>
internal readonly struct RowIdentity : IEquatable<RowIdentity>
{
    private readonly byte[] bytes;
    private readonly int hashCode;

    /// <summary>
    /// Initializes a new instance of the <see cref="RowIdentity"/> struct.
    /// </summary>
    /// <param name="bytes">The identity bytes.</param>
    public RowIdentity(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        this.bytes = bytes;
        this.hashCode = Fnv1a(bytes);
    }

    /// <summary>
    /// Builds row identity bytes for a table entry.
    /// </summary>
    /// <param name="columns">The schema columns in identity order.</param>
    /// <param name="entry">The ndb entry to identify.</param>
    /// <returns>The row identity.</returns>
    public static RowIdentity From(IReadOnlyList<TabColumn> columns, NdbEntry entry)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(entry);

        using var stream = new MemoryStream();

        for (int i = 0; i < columns.Count; i++)
        {
            NdbTuple? tuple = entry.First(columns[i].Name);
            if (tuple is null || tuple.IsNil)
            {
                stream.WriteByte(0);
            }
            else
            {
                byte[] bytes = TabCodec.Utf8(tuple.Value);
                stream.Write(bytes);
            }

            if (i + 1 < columns.Count)
            {
                stream.WriteByte((byte)'\t');
            }
        }

        return new RowIdentity(stream.ToArray());
    }

    /// <summary>
    /// Returns a value indicating whether this identity equals another identity.
    /// </summary>
    /// <param name="other">The identity to compare.</param>
    /// <returns>True when the identity bytes match; otherwise false.</returns>
    public bool Equals(RowIdentity other)
    {
        return this.bytes.AsSpan().SequenceEqual(other.bytes);
    }

    /// <summary>
    /// Returns a value indicating whether this identity equals another object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>True when the object is the same identity; otherwise false.</returns>
    public override bool Equals(object? obj)
    {
        return obj is RowIdentity other && this.Equals(other);
    }

    /// <summary>
    /// Gets the cached hash code for this identity.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        return this.hashCode;
    }

    /// <summary>
    /// Copies the identity bytes.
    /// </summary>
    /// <returns>A copy of the identity bytes.</returns>
    public byte[] ToArray()
    {
        return (byte[])this.bytes.Clone();
    }

    private static int Fnv1a(ReadOnlySpan<byte> input)
    {
        uint hash = 0x811c9dc5u;
        foreach (byte b in input)
        {
            hash ^= b;
            hash *= 0x01000193u;
        }

        return unchecked((int)hash);
    }
}
