// <copyright file="SafeText.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.PropertyTests;

/// <summary>
/// Generated text that can be represented by libtab.
/// </summary>
public readonly struct SafeText
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SafeText"/> struct.
    /// </summary>
    /// <param name="value">The generated text value.</param>
    public SafeText(string value)
    {
        this.Value = value;
    }

    /// <summary>
    /// Gets the generated text value.
    /// </summary>
    public string Value { get; }
}
