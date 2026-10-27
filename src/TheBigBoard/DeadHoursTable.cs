using System;
using System.Collections.Generic;
using UnityEngine;
using static TheBigBoard.DataGrid;

namespace TheBigBoard;

/// <summary>Dead-hours table (businesses popup). One row per hour 0-23:
/// HOUR | CUSTOMERS (total) | AVG/DAY | OPEN (plan days) | VERDICT.
/// Derives from TablePanel, so headers sort and columns resize for free.
/// Rows are hour-ordered by default (SortCol = -1 = query order).</summary>
public sealed class DeadHoursTable : TablePanel<DeadHoursTable.Row>
{
    public sealed class Row
    {
        public int hour;
        public string hourLabel;
        public long customers;
        public float avg;
        public int openDays; // 0-7 per current weekly plan
        public string verdict;
        public int verdictRank; // 0 = dead, 1 = weak, 2 = ok, 3 = closed
    }

    static readonly string[] HeadNames = { "HOUR", "CUSTOMERS", "AVG/DAY", "OPEN", "VERDICT" };

    static readonly TMPro.HorizontalAlignmentOptions[] ColAlign =
    {
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Right,
        TMPro.HorizontalAlignmentOptions.Center,
        TMPro.HorizontalAlignmentOptions.Left,
    };

    // CUSTOMERS, AVG/DAY, OPEN fixed; HOUR flexes into the rest.
    static readonly float[] DefaultWidths = { 150f, 150f, 110f, 220f };

    public DeadHoursTable()
        : base("deadHours", HeadNames, DefaultWidths, ColAlign, 40f, 40f, 20f,
            26, null, -1, true)
    {
    }

    protected override int CompareRows(Row a, Row b, int col)
    {
        int c = 0;
        switch (col)
        {
            case 0: c = a.hour.CompareTo(b.hour); break;
            case 1: c = a.customers.CompareTo(b.customers); break;
            case 2: c = a.avg.CompareTo(b.avg); break;
            case 3: c = a.openDays.CompareTo(b.openDays); break;
            case 4: c = a.verdictRank.CompareTo(b.verdictRank); break;
        }
        if (c == 0) c = a.hour.CompareTo(b.hour);
        return c;
    }

    protected override void RenderRow(GameObject rowGO, Row r)
    {
        Color verdictColor = InkLight;
        switch (r.verdictRank)
        {
            case 0: verdictColor = LossRed; break;
            case 1: verdictColor = new Color(1f, 0.75f, 0.3f, 1f); break;
            case 2: verdictColor = ProfitGreen; break;
            default: verdictColor = InkGrey; break;
        }
        NameCell(rowGO, "RowHour", r.hourLabel, InkLight,
            TMPro.HorizontalAlignmentOptions.Center);
        Fixed(rowGO, "RowCust", r.customers.ToString("N0"), 0, InkLight);
        Fixed(rowGO, "RowAvg", r.avg.ToString("F1"), 1,
            r.avg < 1f ? LossRed : InkLight);
        Fixed(rowGO, "RowOpen", r.openDays + "/7", 2, InkGrey);
        VerdictCell(rowGO, r.verdict, verdictColor);
    }

    protected override string EmptyText
    {
        get { return "No hourly data."; }
    }

    TMPro.TextMeshProUGUI NameCell(GameObject parent, string goName, string value,
        Color color, TMPro.HorizontalAlignmentOptions align)
    {
        return DataGrid.AddNameCell(parent, goName, value, Widths,
            PadL, PadR, Gap, 30, color, align);
    }

    // Hides TablePanel.Fixed on purpose: first two columns right, rest centered.
    new TMPro.TextMeshProUGUI Fixed(GameObject parent, string goName, string value,
        int index, Color color)
    {
        return DataGrid.AddFixedCell(parent, goName, value, Widths, index,
            PadR, Gap, 30, color, index == 0 || index == 1
                ? TMPro.HorizontalAlignmentOptions.Right
                : TMPro.HorizontalAlignmentOptions.Center);
    }

    TMPro.TextMeshProUGUI VerdictCell(GameObject parent, string value, Color color)
    {
        return DataGrid.AddFixedCell(parent, "RowVerdict", value, Widths, 3,
            PadR, Gap, 30, color, TMPro.HorizontalAlignmentOptions.Left);
    }
}
