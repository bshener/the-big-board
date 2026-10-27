using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>FACTORY supply table: per-ingredient NEED/WK, FACTORY stock,
/// INCOMING (importer deliveries due in 7 days), DAYS of coverage and
/// SHORTFALL. Warehouse stock counts toward DAYS behind the scenes.
/// All headers sort.</summary>
public sealed class SupplyTable : TablePanel<FactoryPanel.SupplyRow>
{
    static readonly string[] HeadNames = { "INGREDIENT", "NEED/WK", "FACTORY",
        "INCOMING", "DAYS", "SHORTFALL" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
    };

    static readonly float[] DefaultWidths = { 150f, 150f, 160f, 100f, 170f };

    public SupplyTable()
        : base("factorySupply", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 12f,
            26, null, -1, true)
    {
    }

    protected override int CompareRows(FactoryPanel.SupplyRow a, FactoryPanel.SupplyRow b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.displayName, b.displayName, StringComparison.Ordinal); break;
            case 1: c = a.needWk.CompareTo(b.needWk); break;
            case 2: c = a.inFactory.CompareTo(b.inFactory); break;
            case 3: c = a.incoming.CompareTo(b.incoming); break;
            case 4: c = a.days.CompareTo(b.days); break;
            case 5: c = a.shortfall.CompareTo(b.shortfall); break;
        }
        if (c == 0) c = string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderRow(GameObject rowGO, FactoryPanel.SupplyRow s)
    {
        string days = s.days < 0 ? "--" : s.days.ToString("F1");
        NameCell(rowGO, "RowName", s.displayName, DataGrid.InkLight);
        Fixed(rowGO, "RowNeed", s.needWk.ToString("N0"), 0, DataGrid.InkLight);
        Fixed(rowGO, "RowFactory", s.inFactory.ToString("N0"), 1, DataGrid.InkLight);
        Fixed(rowGO, "RowIncoming", s.incoming.ToString("N0"), 2, DataGrid.InkLight);
        Fixed(rowGO, "RowDays", days, 3, DataGrid.InkLight);
        Fixed(rowGO, "RowShortfall", s.shortfall.ToString("N0"), 4,
            s.shortfall > 0 ? DataGrid.WarnRed : DataGrid.InkLight);
    }

    // Row cells come from the TablePanel base (shared NameCell/Fixed).

    protected override string EmptyText
    {
        get { return "No ingredients scheduled - assign recipes and staff first."; }
    }
}
