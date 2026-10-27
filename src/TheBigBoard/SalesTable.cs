using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>SALES table: per-store sold quantity for a product search,
/// aggregated over the selected time range. Was the last FLEX table;
/// converted to absolute so sorting + resize come from the base.</summary>
public sealed class SalesTable : TablePanel<SalesTable.Row>
{
    public sealed class Row
    {
        public string name;
        public long sold;
    }

    static readonly string[] HeadNames = { "STORE", "SOLD" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
    };

    static readonly float[] DefaultWidths = { 420f };

    string _totalLabel = "";
    long _totalSold;

    public SalesTable()
        : base("sales", HeadNames, DefaultWidths, ColAlign, 80f, 80f, 10f,
            28, null, -1, true)
    {
    }

    /// <summary>Rebuilds rows from the aggregated store map (owned by the
    /// panel), then sorts + renders via the base.</summary>
    public void ShowResults(Dictionary<string, Dictionary<string, long>> stores,
        string selectedKey, Dictionary<string, KeyValuePair<string, long>> products,
        string raw)
    {
        if (stores == null || stores.Count == 0)
        {
            ShowInfo("No sales found for '" + raw + "'.", 32);
            return;
        }
        var rows = new List<Row>();
        long total = 0;
        foreach (var kv in stores)
        {
            long sum = 0;
            if (selectedKey == null)
            {
                foreach (var p in kv.Value) sum += p.Value;
            }
            else if (!kv.Value.TryGetValue(selectedKey, out sum))
            {
                sum = 0;
            }
            if (sum == 0) continue;
            rows.Add(new Row { name = kv.Key, sold = sum });
            total += sum;
        }
        rows.Sort((a, b) => b.sold.CompareTo(a.sold));
        string totalLabel;
        if (selectedKey == null)
        {
            totalLabel = "TOTAL (" + rows.Count + " stores)";
        }
        else
        {
            string rawKey = selectedKey;
            if (products != null)
            {
                KeyValuePair<string, long> prod;
                if (products.TryGetValue(selectedKey, out prod)) rawKey = prod.Key;
            }
            totalLabel = ModPanel.GetProductDisplayName(rawKey) + " (" + rows.Count + " stores)";
        }
        _totalLabel = totalLabel;
        _totalSold = total;
        SetRows(rows);
    }

    protected override int CompareRows(Row a, Row b, int col)
    {
        int c = col == 0
            ? string.Compare(a.name, b.name, StringComparison.Ordinal)
            : a.sold.CompareTo(b.sold);
        if (c == 0) c = string.Compare(a.name, b.name, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<Row> rows)
    {
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "TotalRow", DataGrid.TotalBg);
        DataGrid.AddNameCell(rowGO, "TotalName", _totalLabel, Widths,
            PadL, PadR, Gap, 32, DataGrid.ProfitGreen,
            TMPro.HorizontalAlignmentOptions.Left);
        DataGrid.AddFixedCell(rowGO, "TotalQty", _totalSold.ToString("N0"), Widths, 0,
            PadR, Gap, 32, DataGrid.ProfitGreen,
            TMPro.HorizontalAlignmentOptions.Right);
    }

    protected override void RenderRow(GameObject rowGO, Row r)
    {
        DataGrid.AddNameCell(rowGO, "RowName", r.name, Widths,
            PadL, PadR, Gap, 32, DataGrid.InkLight,
            TMPro.HorizontalAlignmentOptions.Left);
        DataGrid.AddFixedCell(rowGO, "RowQty", r.sold.ToString("N0"), Widths, 0,
            PadR, Gap, 32, DataGrid.InkLight,
            TMPro.HorizontalAlignmentOptions.Right);
    }

    protected override string EmptyText
    {
        get { return "No sales found."; }
    }

    protected override int InfoFontSize
    {
        get { return 32; }
    }
}
