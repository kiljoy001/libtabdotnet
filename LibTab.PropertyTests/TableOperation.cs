// <copyright file="TableOperation.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.PropertyTests;

/// <summary>
/// One generated table operation.
/// </summary>
public readonly struct TableOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableOperation"/> struct.
    /// </summary>
    /// <param name="kind">The operation kind.</param>
    /// <param name="key">The generated key identifier.</param>
    /// <param name="value">The generated value identifier.</param>
    public TableOperation(OperationKind kind, int key, int value)
    {
        this.Kind = kind;
        this.Key = key;
        this.Value = value;
    }

    /// <summary>
    /// Gets the operation kind.
    /// </summary>
    public OperationKind Kind { get; }

    /// <summary>
    /// Gets the generated key identifier.
    /// </summary>
    public int Key { get; }

    /// <summary>
    /// Gets the generated value identifier.
    /// </summary>
    public int Value { get; }
}
