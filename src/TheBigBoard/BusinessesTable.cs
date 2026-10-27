using System;
using System.Collections.Generic;
using UnityEngine;
using BAModAPI;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>TAB 6 table: one row per business as listed in the vanilla BizMan
/// app (same population + same columns as the game's own Businesses list).
/// Columns: NAME | TYPE | AVG DAILY INCOME | ALERTS | SATISFACTION | STATUS.
/// All headers sort. Click a row to select the business: the panel shows its
/// requirement checklist (open, staffing, marketing, stock, uniforms) on
/// the right.</summary>
public sealed class BusinessesTable : TablePanel<BusinessesTable.BRow>
{
    public sealed class BRow
    {
        public BuildingRegistration reg;
        public bool isRealEstate;
        public bool hasBusiness;
        public string name;
        public string typeName;
        public float income;
        public int alerts;
        public string alertsText;
        public int satisfaction;
        public string satText;
        public string statusText;
        public int statusRank; // 0 = open/occupied-high, higher = worse (sort key)
        public float occupancy;
    }

    public Action<BRow> OnSelect;

    static readonly string[] HeadNames = { "NAME", "TYPE", "AVG DAILY INCOME",
        "ALERTS", "SATISFACTION", "STATUS" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Left, TMPro.HorizontalAlignmentOptions.Left,
        TMPro.HorizontalAlignmentOptions.Right, TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Center, TMPro.HorizontalAlignmentOptions.Center,
    };

    static readonly float[] DefaultWidths = { 220f, 150f, 150f, 90f, 110f };

    public BusinessesTable()
        : base("businesses", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 10f,
            22, null, 0, true)
    {
    }

    protected override void OnSortChanged(int col, bool asc)
    {
        Context?.Logger.Info("The Big Board businesses sort: " + Headers[col] +
            (asc ? " asc." : " desc."));
    }

    protected override int CompareRows(BRow a, BRow b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = string.Compare(a.name, b.name, StringComparison.Ordinal); break;
            case 1: c = string.Compare(a.typeName, b.typeName, StringComparison.Ordinal); break;
            case 2: c = a.income.CompareTo(b.income); break;
            case 3: c = a.alerts.CompareTo(b.alerts); break;
            case 4: c = a.satisfaction.CompareTo(b.satisfaction); break;
            case 5: c = a.statusRank.CompareTo(b.statusRank); break;
        }
        if (c == 0) c = string.Compare(a.name, b.name, StringComparison.Ordinal);
        return c;
    }

    protected override void RenderTotalRow(List<BRow> rows)
    {
        float sum = 0f;
        foreach (var r in rows) sum += r.income;
        var rowGO = DataGrid.AddRowRoot(RowsRoot, "BizTotalRow", DataGrid.TotalBg);
        Cell(rowGO, "BizTotalName", "TOTAL (" + rows.Count + ")", 26, DataGrid.ProfitGreen);
        Fixed(rowGO, "BizTotalType", "-", 0, 26, DataGrid.ProfitGreen, ColAlign[1]);
        Fixed(rowGO, "BizTotalInc", BusinessesPanel.FormatMoney(sum), 1, 26, DataGrid.ProfitGreen, ColAlign[2]);
        Fixed(rowGO, "BizTotalAl", "-", 2, 26, DataGrid.ProfitGreen, ColAlign[3]);
        Fixed(rowGO, "BizTotalSat", "-", 3, 26, DataGrid.ProfitGreen, ColAlign[4]);
        Fixed(rowGO, "BizTotalSt", "-", 4, 26, DataGrid.ProfitGreen, ColAlign[5]);
    }

    protected override void RenderRow(GameObject rowGO, BRow r)
    {
        bool sel = BusinessesPanel.IsSelected(r);
        var bg = sel ? DataGrid.Accent : (rowGO.GetComponent<UnityEngine.UI.Image>() != null
            ? rowGO.GetComponent<UnityEngine.UI.Image>().color : DataGrid.RowBg);
        if (sel)
        {
            var img = rowGO.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(0.13f, 0.35f, 0.38f, 1f);
        }
        Cell(rowGO, "BizRowName", r.name, 24, sel ? Color.white : DataGrid.InkLight);
        Fixed(rowGO, "BizRowType", r.typeName, 0, 24, DataGrid.InkGrey, ColAlign[1]);
        Fixed(rowGO, "BizRowInc", BusinessesPanel.FormatMoney(r.income), 1, 24,
            r.income < 0 ? DataGrid.LossRed : DataGrid.InkLight, ColAlign[2]);
        Fixed(rowGO, "BizRowAl", r.alertsText, 2, 24,
            r.alerts > 0 ? DataGrid.LossRed : DataGrid.InkGrey, ColAlign[3]);
        Fixed(rowGO, "BizRowSat", r.satText, 3, 24,
            (!r.hasBusiness || r.satisfaction >= 50) ? DataGrid.InkLight : DataGrid.LossRed, ColAlign[4]);
        Fixed(rowGO, "BizRowSt", r.statusText, 4, 24,
            r.statusRank == 0 ? DataGrid.ProfitGreen : (r.statusRank >= 3 ? DataGrid.LossRed : DataGrid.InkLight),
            ColAlign[5]);
    }

    TMPro.TextMeshProUGUI Cell(GameObject parent, string goName, string value,
        int fontSize, Color color)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, fontSize, color);
    }

    TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value, int index,
        int fontSize, Color color, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, fontSize, color, align);
    }

    protected override string EmptyText
    {
        get
        {
            return "No businesses yet. Rent a building and set up a business first.";
        }
    }

    protected override bool RowClickable(BRow row)
    {
        return true;
    }

    protected override void OnRowClicked(BRow r)
    {
        if (OnSelect != null) OnSelect(r);
    }
}
