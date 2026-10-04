// <copyright file="NdbEmitter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Emits the Plan 9 ndb subset used by libtab.
/// </summary>
internal static class NdbEmitter
{
    /// <summary>
    /// Emits the schema entry for a table.
    /// </summary>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="columns">The schema columns.</param>
    /// <returns>The serialized schema entry.</returns>
    public static string EmitSchema(string schemaName, IReadOnlyList<TabColumn> columns)
    {
        ArgumentException.ThrowIfNullOrEmpty(schemaName);
        ArgumentNullException.ThrowIfNull(columns);

        var builder = new StringBuilder();
        AppendRawKeyValue(builder, "schema", schemaName);
        builder.Append('\n');

        foreach (TabColumn column in columns)
        {
            builder.Append('\t');
            AppendRawKeyValue(builder, "col", column.Name);

            if (column.Type is not null)
            {
                builder.Append(' ');
                AppendRawKeyValue(builder, "type", column.Type);
            }

            foreach (var attr in column.OrderedAttributes)
            {
                builder.Append(' ');
                AppendRawKeyValue(builder, attr.Key, attr.Value);
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Emits a row entry.
    /// </summary>
    /// <param name="entry">The row entry to serialize.</param>
    /// <returns>The serialized row entry.</returns>
    public static string EmitRow(NdbEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var builder = new StringBuilder();
        bool first = true;

        foreach (NdbTuple tuple in entry.Tuples)
        {
            if (!first)
            {
                builder.Append('\t');
            }

            AppendCell(builder, tuple);
            builder.Append('\n');
            first = false;
        }

        return builder.ToString();
    }

    private static void AppendRawKeyValue(StringBuilder builder, string attribute, string value)
    {
        builder.Append(attribute);
        builder.Append('=');
        builder.Append(value);
    }

    private static void AppendCell(StringBuilder builder, NdbTuple tuple)
    {
        builder.Append(tuple.Attribute);
        builder.Append('=');

        if (tuple.IsNil)
        {
            builder.Append("nil");
            return;
        }

        if (string.IsNullOrEmpty(tuple.Value))
        {
            return;
        }

        string wire = NdbText.NeedsEncoding(tuple.Value) ? NdbText.Encode(tuple.Value) : tuple.Value;
        if (NdbText.NeedsQuote(wire))
        {
            builder.Append('"');
            builder.Append(wire);
            builder.Append('"');
        }
        else
        {
            builder.Append(wire);
        }
    }
}
