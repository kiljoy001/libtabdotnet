// <copyright file="TabWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>Streaming writer for bulk libtab table creation.</summary>
public sealed class TabWriter : IDisposable
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly string path;
    private readonly string tempPath;
    private readonly List<TabColumn> columns;
    private readonly Dictionary<string, TabColumn> columnsByName;
    private readonly HashSet<RowIdentity> seenRows;
    private readonly FileStream stream;
    private readonly StreamWriter writer;

    private NdbEntry? currentRow;
    private bool committed;
    private bool disposed;

    private TabWriter(string path, string schemaName, IEnumerable<TabColumn> columns)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(schemaName);

        this.path = path;
        this.columns = ValidateColumns(columns);
        this.columnsByName = this.columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        this.seenRows = new HashSet<RowIdentity>();
        this.tempPath = string.Concat(path, ".tmp.", Environment.ProcessId);

        string? directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        this.stream = new FileStream(this.tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        this.writer = new StreamWriter(this.stream, StrictUtf8);
        this.writer.Write(NdbEmitter.EmitSchema(schemaName, this.columns));
        this.writer.Write('\n');
    }

    /// <summary>
    /// Creates a streaming writer.
    /// </summary>
    /// <param name="path">The table file path to publish on commit.</param>
    /// <param name="schemaName">The libtab schema name.</param>
    /// <param name="columns">The schema columns.</param>
    /// <returns>The streaming writer.</returns>
    public static TabWriter Create(string path, string schemaName, IEnumerable<TabColumn> columns)
    {
        return new TabWriter(path, schemaName, columns);
    }

    /// <summary>
    /// Finalizes the current row, then starts a new row.
    /// </summary>
    /// <param name="headColumn">The identity column for the new row.</param>
    /// <param name="headValue">The identity value for the new row.</param>
    public void AddRow(string headColumn, string headValue)
    {
        this.ThrowIfClosed();
        ArgumentException.ThrowIfNullOrEmpty(headColumn);
        ArgumentException.ThrowIfNullOrEmpty(headValue);
        NdbText.ValidateNoNul(headValue, nameof(headValue));

        this.FlushRow();

        TabColumn column = this.RequireColumn(headColumn);
        if (column.Type is not null)
        {
            throw new TabException($"head column '{headColumn}' is typed {column.Type}");
        }

        TabCellLimit.Validate(headColumn, headValue, "tab_writer_add_row");

        this.currentRow = new NdbEntry();
        this.currentRow.Add(new NdbTuple(headColumn, headValue));
    }

    /// <summary>
    /// Sets or creates a plain cell on the current row.
    /// </summary>
    /// <param name="column">The plain column to update.</param>
    /// <param name="value">The semantic cell value, or null for semantic nil.</param>
    public void Set(string column, string? value)
    {
        this.ThrowIfClosed();
        this.EnsureCurrentRow();
        ArgumentException.ThrowIfNullOrEmpty(column);

        TabColumn schemaColumn = this.RequireColumn(column);
        if (schemaColumn.Type is not null)
        {
            throw new TabException($"column '{column}' is typed {schemaColumn.Type}; use a typed setter");
        }

        if (column == this.columns[0].Name && string.IsNullOrEmpty(value))
        {
            throw new TabException("empty row name");
        }

        if (value is not null)
        {
            NdbText.ValidateNoNul(value, nameof(value));
        }

        TabCellLimit.Validate(column, value, "tab_writer_set");
        SetTuple(this.currentRow!, column, value ?? "nil", value is null);
    }

    /// <summary>
    /// Clears a non-identity cell on the current row.
    /// </summary>
    /// <param name="column">The plain column to clear.</param>
    public void Clear(string column)
    {
        this.ThrowIfClosed();
        this.EnsureCurrentRow();
        ArgumentException.ThrowIfNullOrEmpty(column);
        this.RequireColumn(column);

        if (column == this.columns[0].Name)
        {
            throw new TabException($"cannot clear row identity column '{column}'");
        }

        int index = this.currentRow!.IndexOfFirst(column);
        if (index >= 0)
        {
            this.currentRow.RemoveAt(index);
        }
    }

    /// <summary>
    /// Flushes any current row and atomically publishes the file.
    /// </summary>
    public void Commit()
    {
        if (this.committed)
        {
            return;
        }

        this.ThrowIfClosed();
        this.FlushRow();
        this.writer.Flush();
        this.stream.Flush(flushToDisk: true);
        this.writer.Dispose();
        File.Move(this.tempPath, this.path, overwrite: true);
        this.committed = true;
        this.disposed = true;
    }

    /// <summary>
    /// Releases the temporary file and deletes it unless it was committed.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.writer.Dispose();

        if (!this.committed && File.Exists(this.tempPath))
        {
            File.Delete(this.tempPath);
        }
    }

    private static List<TabColumn> ValidateColumns(IEnumerable<TabColumn> source)
    {
        return TabTable.ValidateColumns(source);
    }

    private static void SetTuple(NdbEntry entry, string column, string value, bool isNil)
    {
        NdbTuple? tuple = entry.First(column);
        if (tuple is null)
        {
            entry.Add(new NdbTuple(column, value, isNil));
            return;
        }

        tuple.Value = value;
        tuple.IsNil = isNil;
    }

    private void FlushRow()
    {
        if (this.currentRow is null)
        {
            return;
        }

        RowIdentity identity = RowIdentity.From(this.columns, this.currentRow);
        if (this.seenRows.Add(identity))
        {
            this.writer.Write(NdbEmitter.EmitRow(this.currentRow));
            this.writer.Write('\n');
        }

        this.currentRow = null;
    }

    private void EnsureCurrentRow()
    {
        if (this.currentRow is null)
        {
            throw new TabException("no current writable row");
        }
    }

    private TabColumn RequireColumn(string column)
    {
        if (!this.columnsByName.TryGetValue(column, out TabColumn? schemaColumn))
        {
            throw new TabException($"column '{column}' is not in schema");
        }

        return schemaColumn;
    }

    private void ThrowIfClosed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(TabWriter));
        }
    }
}
