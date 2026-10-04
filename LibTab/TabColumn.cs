// <copyright file="TabColumn.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>A schema column declaration.</summary>
public sealed class TabColumn
{
    private static readonly IReadOnlyList<KeyValuePair<string, string>> Empty =
        Array.Empty<KeyValuePair<string, string>>();

    /// <summary>
    /// Initializes a new instance of the <see cref="TabColumn"/> class.
    /// </summary>
    /// <param name="name">The ndb attribute name for this column.</param>
    /// <param name="type">The optional libtab column type.</param>
    /// <param name="attributes">Additional schema attributes from the column line.</param>
    public TabColumn(string name, string? type = null, IReadOnlyDictionary<string, string>? attributes = null)
        : this(
            name,
            type,
            attributes?.Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabColumn"/> class with ordered attributes.
    /// </summary>
    /// <param name="name">The ndb attribute name for this column.</param>
    /// <param name="type">The optional libtab column type.</param>
    /// <param name="attributes">Additional schema attributes in source order.</param>
    internal TabColumn(string name, string? type, IEnumerable<KeyValuePair<string, string>>? attributes)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        this.Name = name;
        this.Type = string.IsNullOrEmpty(type) ? null : type;
        this.OrderedAttributes = (attributes ?? Empty)
            .Select(pair =>
            {
                ArgumentException.ThrowIfNullOrEmpty(pair.Key);
                ArgumentNullException.ThrowIfNull(pair.Value);
                return new KeyValuePair<string, string>(pair.Key, pair.Value);
            })
            .ToArray();

        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in this.OrderedAttributes)
        {
            byName.TryAdd(pair.Key, pair.Value);
        }

        this.Attributes = new ReadOnlyDictionary<string, string>(byName);
    }

    /// <summary>
    /// Gets the ndb attribute name for this column.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the schema type, for example <c>HASHED</c> or <c>SIGNED</c>; null means plain text.
    /// </summary>
    public string? Type { get; }

    /// <summary>
    /// Gets additional schema attributes from the same ndb line as this column.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; }

    /// <summary>
    /// Gets additional schema attributes in source order.
    /// </summary>
    internal IReadOnlyList<KeyValuePair<string, string>> OrderedAttributes { get; }

    /// <summary>
    /// Gets a schema attribute by name.
    /// </summary>
    /// <param name="key">The attribute name to find.</param>
    /// <returns>The attribute value, or null when it is absent.</returns>
    public string? GetAttribute(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return this.Attributes.TryGetValue(key, out string? value) ? value : null;
    }
}
