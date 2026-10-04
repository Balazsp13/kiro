// BomRenderer.cs
//
// Builds an AutoCAD Table (Autodesk.AutoCAD.DatabaseServices.Table) holding the
// anyagjegyzek (BOM) with columns: Tetel / Tipus / Mennyiseg / Egyseg /
// Megjegyzes. The table is placed at a configured model location.

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Renders the BOM as an AutoCAD Table.</summary>
    public sealed class BomRenderer
    {
        public const string BomLayer = "ANYAGJEGYZEK";

        private static readonly string[] Headers = { "Tetel", "Tipus", "Mennyiseg", "Egyseg", "Megjegyzes" };

        private const double RowHeight = 1.2;
        private const double TextHeight = 0.5;

        private readonly RenderContext _ctx;

        public BomRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>Build and place the BOM table with its upper-left at <paramref name="position"/>.</summary>
        public void Render(IReadOnlyList<BomRow> rows, Point3d position)
        {
            if (rows == null || rows.Count == 0)
            {
                _ctx.Log("Anyagjegyzek: nincs BOM sor, tablazat kihagyva.");
                return;
            }

            _ctx.Layers.EnsureLayer(BomLayer, 7, "CONTINUOUS");

            var table = new Table
            {
                TableStyle = _ctx.Db.Tablestyle,
                Position = position,
                Layer = BomLayer,
            };

            int numRows = rows.Count + 2; // title + header + data
            table.SetSize(numRows, Headers.Length);

            // Column widths (model meters) and uniform row heights.
            double[] widths = { 12.0, 12.0, 7.0, 5.0, 16.0 };
            for (int c = 0; c < Headers.Length; c++)
            {
                table.Columns[c].Width = widths[c];
            }

            for (int r = 0; r < numRows; r++)
            {
                table.Rows[r].Height = RowHeight;
            }

            // Title row (merged).
            table.Cells[0, 0].TextString = "ANYAGJEGYZEK (BOM)";
            table.Cells[0, 0].TextHeight = TextHeight * 1.2;
            try
            {
                table.MergeCells(CellRange.Create(table, 0, 0, 0, Headers.Length - 1));
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Merging is cosmetic; ignore if the table style forbids it.
            }

            // Header row.
            const int headerRow = 1;
            for (int c = 0; c < Headers.Length; c++)
            {
                Cell cell = table.Cells[headerRow, c];
                cell.TextString = Headers[c];
                cell.TextHeight = TextHeight;
            }

            // Data rows.
            for (int i = 0; i < rows.Count; i++)
            {
                BomRow row = rows[i];
                int r = i + 2;
                SetCell(table, r, 0, row.Item);
                SetCell(table, r, 1, row.Type ?? "");
                SetCell(table, r, 2, FormatQty(row.Qty));
                SetCell(table, r, 3, row.Unit);
                SetCell(table, r, 4, row.Note ?? "");
            }

            table.GenerateLayout();
            _ctx.Append(table);
            _ctx.Log($"Anyagjegyzek: {rows.Count} sor beillesztve.");
        }

        private static void SetCell(Table table, int r, int c, string value)
        {
            Cell cell = table.Cells[r, c];
            cell.TextString = value;
            cell.TextHeight = TextHeight;
        }

        private static string FormatQty(double qty)
        {
            // Integer quantities print without a decimal point.
            return Math.Abs(qty - Math.Round(qty)) < 1e-9
                ? ((long)Math.Round(qty)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : qty.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
