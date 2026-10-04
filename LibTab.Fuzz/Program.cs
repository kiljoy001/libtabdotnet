// <copyright file="Program.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.Fuzz;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibTab;
using SharpFuzz;

/// <summary>
/// SharpFuzz entry point for libtab parsers and typed-cell verification.
/// </summary>
public static class Program
{
    private const string FuzzPath = "libtab-fuzz.tab";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    /// <summary>
    /// Runs the selected fuzz target or the smoke corpus.
    /// </summary>
    /// <param name="args">The fuzz target arguments.</param>
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "smoke")
        {
            RunSmoke(args.Length > 1 ? args[1] : "fuzz/corpus");
            return;
        }

        string target = args.Length > 0 ? args[0] : "ndb";
        Fuzzer.Run(stream => RunTarget(target, ReadAll(stream)));
    }

    private static void RunSmoke(string corpusRoot)
    {
        string[] targets = new[] { "ndb", "text", "base64", "hashed", "signed" };
        var missing = new List<string>();
        int ran = 0;

        foreach (string target in targets)
        {
            string directory = Path.Combine(corpusRoot, target);
            string[] files = Directory.Exists(directory)
                ? Directory.GetFiles(directory).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                : Array.Empty<string>();

            if (files.Length == 0)
            {
                missing.Add(target);
                continue;
            }

            foreach (string file in files)
            {
                RunTarget(target, File.ReadAllBytes(file));
                ran++;
            }
        }

        // Refuses to pass on nothing, and says what it did.
        //
        // Silence on success is right for a fuzz run - no crash is the result. It also
        // meant an empty or moved corpus passed exactly as quietly as a real one: the
        // directory check skipped, the loop ran zero times, and the gate reported
        // success having tested nothing.
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"no corpus under '{corpusRoot}' for: {string.Join(", ", missing)}");
        }

        Console.WriteLine(
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"fuzz smoke: {ran} corpus inputs across {targets.Length} targets"));
    }

    private static void RunTarget(string target, byte[] data)
    {
        switch (target)
        {
            case "text":
                AcceptRefusal(() => FuzzText(data));
                break;

            case "base64":
                AcceptRefusal(() => FuzzBase64(data));
                break;

            case "hashed":
                AcceptRefusal(() => FuzzHashed(data));
                break;

            case "signed":
                AcceptRefusal(() => FuzzSigned(data));
                break;

            default:
                AcceptRefusal(() => FuzzNdb(data));
                break;
        }
    }

    private static void FuzzNdb(byte[] data)
    {
        string text = StrictUtf8.GetString(data);
        TabTable table = TabTable.Parse(FuzzPath, text);
        string serialized = table.Serialize();
        TabTable reparsed = TabTable.Parse(FuzzPath, serialized);

        int originalRows = table.Rows.Count();
        int reparsedRows = reparsed.Rows.Count();
        if (originalRows != reparsedRows)
        {
            throw new InvalidOperationException($"row count changed from {originalRows} to {reparsedRows}");
        }
    }

    private static void FuzzText(byte[] data)
    {
        string text = StrictUtf8.GetString(data);
        string decoded = NdbText.Decode(text);
        _ = NdbText.Encode(decoded);
    }

    private static void FuzzBase64(byte[] data)
    {
        string text = StrictUtf8.GetString(data);
        byte[] decoded = TabCodec.B64Decode(text);
        string encoded = TabCodec.B64Encode(decoded);
        _ = TabCodec.B64Decode(encoded);
    }

    private static void FuzzHashed(byte[] data)
    {
        string text = StrictUtf8.GetString(data);
        _ = TabCodec.CellHasTag(text, "HASHED");
        _ = TabCryptoShim.VerifyHashCell(text, data);
    }

    private static void FuzzSigned(byte[] data)
    {
        string text = StrictUtf8.GetString(data);
        TabKeyPair keyPair = TabKeyPair.FromSeed(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());
        _ = TabCryptoShim.VerifySignedCell(text, keyPair.PublicKey);
    }

    private static void AcceptRefusal(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is TabException or DecoderFallbackException or ArgumentException)
        {
            _ = ex;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
