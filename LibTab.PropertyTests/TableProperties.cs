// <copyright file="TableProperties.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace LibTab.PropertyTests;

using System;
using System.Collections.Generic;
using System.Linq;
using FsCheck.Xunit;
using LibTab;

/// <summary>
/// Property tests for table mutation sequences.
/// </summary>
public sealed class TableProperties
{
    /// <summary>
    /// Verifies generated operation sequences preserve the reference model through serialization.
    /// </summary>
    /// <param name="operations">The generated operation sequence.</param>
    /// <returns>True when the table agrees with the model.</returns>
    [Property(MaxTest = 200, Arbitrary = new[] { typeof(Generators) })]
    public bool OperationSequencesMatchModel(TableOperation[] operations)
    {
        var model = new Dictionary<int, List<int?>>();
        TabTable table = TabTable.Create("/tmp/not-written.tab", "test", new[] { new TabColumn("k"), new TabColumn("v") });

        foreach (TableOperation operation in operations)
        {
            string key = Key(operation.Key);
            string value = Value(operation.Value);

            switch (operation.Kind)
            {
                case OperationKind.Add:
                    table.AddRow("k", key);
                    AddModelRow(Get(model, operation.Key), null);
                    break;

                case OperationKind.Set:
                    ApplySet(table, model, operation.Key, key, operation.Value, value);
                    break;

                case OperationKind.Clear:
                    ApplyClear(table, model, operation.Key, key);
                    break;

                case OperationKind.Remove:
                    ApplyRemove(table, model, operation.Key, key);
                    break;
            }
        }

        foreach (KeyValuePair<int, List<int?>> pair in model)
        {
            if (table.Search("k", Key(pair.Key)).Count() != pair.Value.Count)
            {
                return false;
            }
        }

        string serialized = table.Serialize();
        TabTable parsed = TabTable.Parse("/tmp/not-written.tab", serialized);

        foreach (KeyValuePair<int, List<int?>> pair in model)
        {
            if (parsed.Search("k", Key(pair.Key)).Count() != pair.Value.Count)
            {
                return false;
            }
        }

        return true;
    }

    private static void ApplySet(TabTable table, Dictionary<int, List<int?>> model, int keyId, string key, int valueId, string value)
    {
        List<int?> rows = Get(model, keyId);
        if (rows.Count == 0)
        {
            return;
        }

        if (rows.Skip(1).Contains(valueId))
        {
            return;
        }

        TabRow target = table.Search("k", key).First();
        table.Set(target, "v", value);
        rows[0] = valueId;
    }

    private static void ApplyClear(TabTable table, Dictionary<int, List<int?>> model, int keyId, string key)
    {
        List<int?> rows = Get(model, keyId);
        if (rows.Count == 0)
        {
            return;
        }

        if (rows[0] is null)
        {
            table.Clear(table.Search("k", key).First(), "v");
            return;
        }

        if (rows.Skip(1).Any(value => value is null))
        {
            return;
        }

        TabRow target = table.Search("k", key).First();
        table.Clear(target, "v");
        rows[0] = null;
    }

    private static void ApplyRemove(TabTable table, Dictionary<int, List<int?>> model, int keyId, string key)
    {
        List<int?> rows = Get(model, keyId);
        if (rows.Count == 0)
        {
            return;
        }

        TabRow target = table.Search("k", key).First();
        table.RemoveRow(target);
        rows.RemoveAt(0);
    }

    private static List<int?> Get(Dictionary<int, List<int?>> model, int key)
    {
        if (!model.TryGetValue(key, out List<int?>? row))
        {
            row = new List<int?>();
            model[key] = row;
        }

        return row;
    }

    private static void AddModelRow(List<int?> rows, int? value)
    {
        if (!rows.Contains(value))
        {
            rows.Add(value);
        }
    }

    private static string Key(int value)
    {
        return "key" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Value(int value)
    {
        return "val" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
