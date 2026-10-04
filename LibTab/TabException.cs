// <copyright file="TabException.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;

/// <summary>A libtab table could not be read, written, mutated, or verified.</summary>
public sealed class TabException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TabException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public TabException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TabException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public TabException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
