// <copyright file="TextProperties.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.PropertyTests;

using System;
using FsCheck.Xunit;
using LibTab;

/// <summary>
/// Property tests for text and base64 round trips.
/// </summary>
public sealed class TextProperties
{
    /// <summary>
    /// Verifies encoded text round trips for generated safe text.
    /// </summary>
    /// <param name="text">The generated safe text.</param>
    /// <returns>True when the value round trips.</returns>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(Generators) })]
    public bool EncodedTextRoundTrips(SafeText text)
    {
        return NdbText.Decode(NdbText.Encode(text.Value)) == text.Value;
    }

    /// <summary>
    /// Verifies base64url text round trips for generated safe text bytes.
    /// </summary>
    /// <param name="text">The generated safe text.</param>
    /// <returns>True when the bytes round trip.</returns>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(Generators) })]
    public bool Base64RoundTrips(SafeText text)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text.Value);
        return TabCodec.B64Decode(TabCodec.B64Encode(bytes)).AsSpan().SequenceEqual(bytes);
    }
}
