// <copyright file="NdbTuple.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;

/// <summary>
/// One parsed ndb attribute/value pair.
/// </summary>
internal sealed class NdbTuple
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NdbTuple"/> class.
    /// </summary>
    /// <param name="attribute">The tuple attribute name.</param>
    /// <param name="value">The tuple value text.</param>
    /// <param name="isNil">A value indicating whether the tuple is semantic nil.</param>
    public NdbTuple(string attribute, string value, bool isNil = false)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        ArgumentNullException.ThrowIfNull(value);

        this.Attribute = attribute;
        this.Value = value;
        this.IsNil = isNil;
    }

    /// <summary>
    /// Gets or sets the tuple attribute name.
    /// </summary>
    public string Attribute { get; set; }

    /// <summary>
    /// Gets or sets the tuple value text.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tuple is semantic nil.
    /// </summary>
    public bool IsNil { get; set; }

    /// <summary>
    /// Gets or sets the parsed ndb line that contains this tuple.
    /// </summary>
    public IReadOnlyList<NdbTuple> Line { get; set; } = Array.Empty<NdbTuple>();

    /// <summary>
    /// Creates a detached copy of this tuple.
    /// </summary>
    /// <returns>The cloned tuple.</returns>
    public NdbTuple Clone()
    {
        return new NdbTuple(this.Attribute, this.Value, this.IsNil);
    }
}
