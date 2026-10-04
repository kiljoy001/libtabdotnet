// <copyright file="TabTable.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>A pure C# libtab table backed by Plan 9 ndb-shaped text.</summary>
public sealed class TabTable
{
    /// <summary>
    /// Maximum encoded cell line length accepted by libtab.
    /// </summary>
    public const int MaxCellLineBytes = TabCellLimit.MaxCellLineBytes;

    /// <summary>
    /// Required ENCRYPTED key length in bytes.
    /// </summary>
    public const int EncryptedKeyLength = TabCrypto.EncryptedKeyLength;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    private readonly List<TabColumn> columns;
    private readonly Dictionary<string, TabColumn> columnsByName;
    private readonly List<TabRow> rows;
    private readonly Dictionary<RowIdentity, TabRow> rowIndex;

    private TabRow? nilRow;

    private TabTable(string path, string schemaName, IEnumerable<TabColumn> columns)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(schemaName);

        this.Path = path;
        this.SchemaName = schemaName;
        this.columns = ValidateColumns(columns);
        this.columnsByName = this.columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        this.rows = new List<TabRow>();
        this.rowIndex = new Dictionary<RowIdentity, TabRow>();
    }

    /// <summary>
    /// Gets the local path used by <see cref="Open"/> and <see cref="Commit"/>.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the schema name from the mandatory <c>schema=</c> tuple.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Gets the declared schema columns in order.
    /// </summary>
    public IReadOnlyList<TabColumn> Columns => new ReadOnlyCollection<TabColumn>(this.columns);

    /// <summary>
    /// Gets rows visible to callers, in source order. The internal all-nil row is skipped.
    /// </summary>
    public IEnumerable<TabRow> Rows => this.rows.Where(row => !this.IsNilRow(row));

    /// <summary>
    /// Gets a value indicating whether the table has pending writes.
    /// </summary>
    internal bool IsDirty { get; private set; }

    /// <summary>
    /// Creates a fresh table. Call <see cref="Commit"/> to write it.
    /// </summary>
    /// <param name="path">The table file path to write during commit.</param>
    /// <param name="schemaName">The libtab schema name.</param>
    /// <param name="columns">The schema columns.</param>
    /// <returns>The newly created table.</returns>
    public static TabTable Create(string path, string schemaName, IEnumerable<TabColumn> columns)
    {
        var table = new TabTable(path, schemaName, columns);
        table.EnsureNilRow();
        table.IsDirty = true;
        return table;
    }

    /// <summary>
    /// Opens and parses a libtab file.
    /// </summary>
    /// <param name="path">The table file path to open.</param>
    /// <returns>The parsed table.</returns>
    public static TabTable Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string text;
        try
        {
            text = StrictUtf8.GetString(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new TabException($"failed to read '{path}'", ex);
        }

        return Parse(path, text);
    }

    /// <summary>
    /// Parses table text already in memory.
    /// </summary>
    /// <param name="path">The logical source path for the parsed table.</param>
    /// <param name="text">The ndb-shaped table text.</param>
    /// <returns>The parsed table.</returns>
    public static TabTable Parse(string path, string text)
    {
        IReadOnlyList<NdbEntry> entries = NdbParser.ParseEntries(text);
        if (entries.Count == 0)
        {
            throw new TabException("missing schema tuple");
        }

        NdbEntry schemaEntry = entries[0];
        if (schemaEntry.Count == 0 || schemaEntry[0].Attribute != "schema")
        {
            string got = schemaEntry.Count == 0 ? "(empty)" : schemaEntry[0].Attribute;
            throw new TabException($"first tuple must be schema=, got '{got}'");
        }

        var table = new TabTable(path, schemaEntry[0].Value, ExtractColumns(schemaEntry));

        for (int i = 1; i < entries.Count; i++)
        {
            NdbEntry rowEntry = entries[i];
            table.ValidateAndDecodeRow(rowEntry, i - 1);
            table.InsertRow(new TabRow(rowEntry), allowDuplicate: true);
        }

        table.EnsureNilRow();
        table.IsDirty = false;
        return table;
    }

    /// <summary>
    /// Adds a bare row or returns the existing identical row.
    /// </summary>
    /// <param name="headColumn">The identity column for the new row.</param>
    /// <param name="headValue">The identity value for the new row.</param>
    /// <returns>The added row, or an existing identical row.</returns>
    public TabRow AddRow(string headColumn, string headValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(headColumn);
        ArgumentException.ThrowIfNullOrEmpty(headValue);
        NdbText.ValidateNoNul(headValue, nameof(headValue));

        TabColumn column = this.RequireColumn(headColumn);
        if (column.Type is not null)
        {
            throw new TabException($"head column '{headColumn}' is typed {column.Type}");
        }

        TabCellLimit.Validate(headColumn, headValue, "tab_add_row");

        var entry = new NdbEntry();
        entry.Add(new NdbTuple(headColumn, headValue));
        var row = new TabRow(entry);

        if (this.rowIndex.TryGetValue(this.Identity(row), out TabRow? existing))
        {
            return existing;
        }

        this.InsertRow(row, allowDuplicate: false);
        this.IsDirty = true;
        return row;
    }

    /// <summary>
    /// Sets or creates a plain-text cell. A null value stores semantic nil.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The plain column to update.</param>
    /// <param name="value">The semantic cell value, or null for semantic nil.</param>
    public void Set(TabRow row, string column, string? value)
    {
        ArgumentNullException.ThrowIfNull(row);
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

        TabCellLimit.Validate(column, value, "tab_set");
        this.Mutate(row, entry => SetTuple(entry, column, value ?? "nil", value is null));
    }

    /// <summary>
    /// Removes a non-identity cell from a row. Missing cells are already semantic nil.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The plain column to clear.</param>
    public void Clear(TabRow row, string column)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentException.ThrowIfNullOrEmpty(column);
        this.RequireColumn(column);

        if (column == this.columns[0].Name)
        {
            throw new TabException($"cannot clear row identity column '{column}'");
        }

        this.Mutate(row, entry =>
        {
            int index = entry.IndexOfFirst(column);
            if (index >= 0)
            {
                entry.RemoveAt(index);
            }
        });
    }

    /// <summary>
    /// Removes a row by deleting it from the in-memory row set.
    /// </summary>
    /// <param name="row">The row to remove.</param>
    public void RemoveRow(TabRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row == this.nilRow || !this.rows.Contains(row))
        {
            throw new TabException("invalid row");
        }

        this.rowIndex.Remove(this.Identity(row));
        this.rows.Remove(row);
        this.IsDirty = true;
    }

    /// <summary>
    /// Sets a HASHED cell according to the column's schema algorithm.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The HASHED column to update.</param>
    /// <param name="preimage">The preimage bytes to hash.</param>
    public void SetHashed(TabRow row, string column, ReadOnlySpan<byte> preimage)
    {
        TabColumn schemaColumn = this.RequireTypedColumn(column, "HASHED");
        string cell = TabCrypto.CreateHashedCell(preimage, schemaColumn.GetAttribute("algo"));
        this.Mutate(row, entry => SetTuple(entry, column, cell, isNil: false));
    }

    /// <summary>
    /// Sets a HASHED cell using argon2id default parameters regardless of schema algorithm.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The HASHED column to update.</param>
    /// <param name="preimage">The preimage bytes to hash.</param>
    public void SetHashedArgon2id(TabRow row, string column, ReadOnlySpan<byte> preimage)
    {
        this.RequireTypedColumn(column, "HASHED");
        string cell = TabCrypto.CreateArgon2idCell(preimage);
        this.Mutate(row, entry => SetTuple(entry, column, cell, isNil: false));
    }

    /// <summary>
    /// Verifies a HASHED cell against a preimage.
    /// </summary>
    /// <param name="row">The row containing the HASHED cell.</param>
    /// <param name="column">The HASHED column to verify.</param>
    /// <param name="preimage">The preimage bytes to verify.</param>
    /// <returns>True when the preimage matches; otherwise false.</returns>
    public bool VerifyHash(TabRow row, string column, ReadOnlySpan<byte> preimage)
    {
        ArgumentNullException.ThrowIfNull(row);
        this.RequireTypedColumn(column, "HASHED");
        return TabCrypto.VerifyHashCell(row.Get(column), preimage);
    }

    /// <summary>
    /// Sets a SIGNED cell using Monocypher-compatible EdDSA.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The SIGNED column to update.</param>
    /// <param name="body">The body bytes to sign.</param>
    /// <param name="signerSecretKey">The 64-byte signer secret key.</param>
    public void SetSigned(TabRow row, string column, ReadOnlySpan<byte> body, ReadOnlySpan<byte> signerSecretKey)
    {
        this.RequireTypedColumn(column, "SIGNED");
        string cell = TabCrypto.CreateSignedCell(body, signerSecretKey);
        TabCellLimit.Validate(column, cell, "tab_set_signed");
        this.Mutate(row, entry => SetTuple(entry, column, cell, isNil: false));
    }

    /// <summary>
    /// Verifies a SIGNED cell and returns its signed body.
    /// </summary>
    /// <param name="row">The row containing the SIGNED cell.</param>
    /// <param name="column">The SIGNED column to verify.</param>
    /// <param name="signerPublicKey">The 32-byte signer public key.</param>
    /// <returns>The signed body bytes.</returns>
    public byte[] VerifySigned(TabRow row, string column, ReadOnlySpan<byte> signerPublicKey)
    {
        ArgumentNullException.ThrowIfNull(row);
        this.RequireTypedColumn(column, "SIGNED");
        return TabCrypto.VerifySignedCell(row.Get(column), signerPublicKey);
    }

    /// <summary>
    /// Sets an ENCRYPTED cell using Monocypher-compatible XChaCha20-Poly1305.
    /// </summary>
    /// <param name="row">The row to update.</param>
    /// <param name="column">The ENCRYPTED column to update.</param>
    /// <param name="plaintext">The plaintext bytes to encrypt.</param>
    /// <param name="key">The 32-byte encryption key.</param>
    public void SetEncrypted(TabRow row, string column, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key)
    {
        this.RequireTypedColumn(column, "ENCRYPTED");
        string cell = TabCrypto.CreateEncryptedCell(plaintext, key);
        TabCellLimit.Validate(column, cell, "tab_set_encrypted");
        this.Mutate(row, entry => SetTuple(entry, column, cell, isNil: false));
    }

    /// <summary>
    /// Decrypts an ENCRYPTED cell using Monocypher-compatible XChaCha20-Poly1305.
    /// </summary>
    /// <param name="row">The row containing the ENCRYPTED cell.</param>
    /// <param name="column">The ENCRYPTED column to decrypt.</param>
    /// <param name="key">The 32-byte encryption key.</param>
    /// <returns>The authenticated plaintext bytes.</returns>
    public byte[] Decrypt(TabRow row, string column, ReadOnlySpan<byte> key)
    {
        ArgumentNullException.ThrowIfNull(row);
        this.RequireTypedColumn(column, "ENCRYPTED");
        return TabCrypto.DecryptEncryptedCell(row.Get(column), key);
    }

    /// <summary>
    /// Searches rows by column value. A null value searches for semantic nil.
    /// </summary>
    /// <param name="column">The column to search.</param>
    /// <param name="value">The value to match, or null for semantic nil.</param>
    /// <returns>The matching rows.</returns>
    public IEnumerable<TabRow> Search(string column, string? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);
        this.RequireColumn(column);

        foreach (TabRow row in this.Rows)
        {
            string? cell = row.Get(column);
            if (value is null ? cell is null : string.Equals(cell, value, StringComparison.Ordinal))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// Serializes this table to ndb-shaped text.
    /// </summary>
    /// <returns>The serialized table text.</returns>
    public string Serialize()
    {
        var builder = new StringBuilder();
        builder.Append(NdbEmitter.EmitSchema(this.SchemaName, this.columns));
        builder.Append('\n');

        foreach (TabRow row in this.rows)
        {
            if (this.IsNilRow(row))
            {
                continue;
            }

            builder.Append(NdbEmitter.EmitRow(row.Entry));
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Atomically writes this table to <see cref="Path"/>.
    /// </summary>
    public void Commit()
    {
        string text = this.Serialize();
        string? directory = System.IO.Path.GetDirectoryName(this.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = string.Concat(this.Path, ".tmp.", Environment.ProcessId);
        File.WriteAllText(temp, text, StrictUtf8);
        File.Move(temp, this.Path, overwrite: true);
        this.IsDirty = false;
    }

    /// <summary>
    /// Builds a row identity for tests.
    /// </summary>
    /// <param name="table">The table that owns the row.</param>
    /// <param name="row">The row to identify.</param>
    /// <returns>The row identity.</returns>
    internal static RowIdentity IdentityForTest(TabTable table, TabRow row)
    {
        return table.Identity(row);
    }

    /// <summary>
    /// Validates schema columns and normalizes them into a list.
    /// </summary>
    /// <param name="source">The columns to validate.</param>
    /// <returns>The validated columns.</returns>
    internal static List<TabColumn> ValidateColumns(IEnumerable<TabColumn> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new List<TabColumn>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (TabColumn column in source)
        {
            if (!SupportedType(column.Type))
            {
                throw new TabException($"column '{column.Name}' has unsupported type '{column.Type}'");
            }

            if (!names.Add(column.Name))
            {
                throw new TabException($"duplicate column '{column.Name}'");
            }

            result.Add(column);
        }

        if (result.Count == 0)
        {
            throw new TabException("schema declares no columns");
        }

        return result;
    }

    private static IReadOnlyList<TabColumn> ExtractColumns(NdbEntry schemaEntry)
    {
        var columns = new List<TabColumn>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (NdbTuple tuple in schemaEntry.Tuples)
        {
            if (tuple.Attribute != "col")
            {
                continue;
            }

            string name = tuple.Value;
            if (string.IsNullOrEmpty(name))
            {
                throw new TabException("schema has empty column name");
            }

            string? type = null;
            var attrs = new List<KeyValuePair<string, string>>();

            foreach (NdbTuple sibling in tuple.Line)
            {
                if (sibling == tuple || sibling.Attribute == "col")
                {
                    continue;
                }

                if (sibling.Attribute == "type")
                {
                    type = sibling.Value;
                }
                else
                {
                    attrs.Add(new KeyValuePair<string, string>(sibling.Attribute, sibling.Value));
                }
            }

            if (!SupportedType(type))
            {
                throw new TabException($"column '{name}' has unsupported type '{type}'");
            }

            if (!names.Add(name))
            {
                throw new TabException($"duplicate column '{name}'");
            }

            columns.Add(new TabColumn(name, type, attrs));
        }

        if (columns.Count == 0)
        {
            throw new TabException($"schema '{schemaEntry[0].Value}' declares no columns");
        }

        return columns;
    }

    private static bool SupportedType(string? type)
    {
        return type is null or "HASHED" or "SIGNED" or "ENCRYPTED";
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

    private void ValidateAndDecodeRow(NdbEntry entry, int rowIndex)
    {
        foreach (NdbTuple tuple in entry.Tuples)
        {
            if (!this.columnsByName.TryGetValue(tuple.Attribute, out TabColumn? column))
            {
                throw new TabException($"row {rowIndex} has undeclared column '{tuple.Attribute}'");
            }

            if (column.Type is null)
            {
                if (tuple.Value == "nil")
                {
                    tuple.IsNil = true;
                    continue;
                }

                tuple.Value = NdbText.Decode(tuple.Value);
                tuple.IsNil = false;
                continue;
            }

            if (tuple.Value == "nil")
            {
                tuple.IsNil = true;
                continue;
            }

            tuple.IsNil = false;
            if (tuple.Value.Length > 0 && !TabCodec.CellHasTag(tuple.Value, column.Type))
            {
                throw new TabException($"row {rowIndex} column '{column.Name}' is missing {column.Type}: tag");
            }
        }
    }

    private void InsertRow(TabRow row, bool allowDuplicate)
    {
        RowIdentity identity = this.Identity(row);
        if (this.rowIndex.ContainsKey(identity))
        {
            if (allowDuplicate)
            {
                return;
            }

            throw new TabException("duplicate row");
        }

        this.rows.Add(row);
        this.rowIndex.Add(identity, row);

        if (this.IsNilRow(row))
        {
            this.nilRow = row;
        }
    }

    private void Mutate(TabRow row, Action<NdbEntry> mutation)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(mutation);

        if (!this.rows.Contains(row))
        {
            throw new TabException("row does not belong to this table");
        }

        RowIdentity oldIdentity = this.Identity(row);
        NdbEntry saved = row.Entry.Clone();
        this.rowIndex.Remove(oldIdentity);

        try
        {
            mutation(row.Entry);
            RowIdentity newIdentity = this.Identity(row);

            if (this.rowIndex.TryGetValue(newIdentity, out TabRow? existing) && existing != row)
            {
                throw new TabException("mutation would create a duplicate row");
            }

            this.rowIndex[newIdentity] = row;
            if (this.IsNilRow(row))
            {
                this.nilRow = row;
            }

            this.IsDirty = true;
        }
        catch
        {
            row.Entry = saved;
            this.rowIndex[oldIdentity] = row;
            throw;
        }
    }

    private void EnsureNilRow()
    {
        if (this.nilRow is not null)
        {
            return;
        }

        TabRow? existingNilRow = this.rows.FirstOrDefault(this.IsNilRow);
        if (existingNilRow is not null)
        {
            this.nilRow = existingNilRow;
            return;
        }

        var entry = new NdbEntry();
        foreach (TabColumn column in this.columns)
        {
            entry.Add(new NdbTuple(column.Name, "nil", isNil: true));
        }

        this.InsertRow(new TabRow(entry), allowDuplicate: true);
    }

    private TabColumn RequireTypedColumn(string column, string type)
    {
        ArgumentException.ThrowIfNullOrEmpty(column);

        TabColumn schemaColumn = this.RequireColumn(column);
        if (schemaColumn.Type != type)
        {
            string actual = schemaColumn.Type ?? "(plain)";
            throw new TabException($"column '{column}' is not {type} (type={actual})");
        }

        return schemaColumn;
    }

    private TabColumn RequireColumn(string column)
    {
        if (!this.columnsByName.TryGetValue(column, out TabColumn? schemaColumn))
        {
            throw new TabException($"column '{column}' is not in schema");
        }

        return schemaColumn;
    }

    private bool IsNilRow(TabRow row)
    {
        foreach (TabColumn column in this.columns)
        {
            NdbTuple? tuple = row.Entry.First(column.Name);
            if (tuple is not null && !tuple.IsNil)
            {
                return false;
            }
        }

        return true;
    }

    private RowIdentity Identity(TabRow row)
    {
        return RowIdentity.From(this.columns, row.Entry);
    }
}
