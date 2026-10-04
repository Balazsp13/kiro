// LegendRenderer.cs
//
// Renders the jelmagyarazat (legend) as a stacked column near a configured
// corner of the drawing. Each entry gets a small symbol swatch (the same
// fallback marker the SymbolRenderer draws, or a short line for linear symbols)
// plus its Hungarian explanatory text. Placed in model units; the legend is a
// drawing-space annotation block anchored at a fixed model offset.

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Config;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Draws the legend column.</summary>
    public sealed class LegendRenderer
    {
        public const string LegendLayer = "JELMAGYARAZAT";

        // Legend layout constants in model meters.
        private const double RowHeight = 1.6;
        private const double SwatchWidth = 2.0;
        private const double TextHeight = 0.6;
        private const double TextGap = 0.8;

        private readonly RenderContext _ctx;

        public LegendRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>
        /// Render the legend starting at <paramref name="topLeft"/> (model meters),
        /// stacking entries downward. Caller chooses a free corner of the sheet.
        /// </summary>
        public void Render(IEnumerable<LegendEntry> entries, Point3d topLeft)
        {
            if (entries == null)
            {
                return;
            }

            _ctx.Layers.EnsureLayer(LegendLayer, 7, "CONTINUOUS");

            // Title row.
            var title = new DBText
            {
                TextString = "JELMAGYARAZAT",
                Position = topLeft,
                Height = TextHeight * 1.2,
                Layer = LegendLayer,
            };
            _ctx.Append(title);

            double y = topLeft.Y - (RowHeight * 1.4);
            foreach (LegendEntry entry in entries)
            {
                RenderRow(entry, topLeft.X, y);
                y -= RowHeight;
            }
        }

        private void RenderRow(LegendEntry entry, double x, double y)
        {
            SymbolDef? def = _ctx.Symbols.Get(entry.SymbolKey);
            string layer = def?.Layer ?? LegendLayer;
            string linetype = def?.Linetype ?? "CONTINUOUS";
            short color = def?.Color ?? 7;
            _ctx.Layers.EnsureLayer(layer, color, linetype);

            bool isLinear = def?.FallbackGeometry != null &&
                            def.FallbackGeometry.Equals("polyline", StringComparison.OrdinalIgnoreCase);

            if (isLinear || string.IsNullOrWhiteSpace(def?.BlockName))
            {
                DrawSwatch(def, x, y, layer, isLinear);
            }
            else
            {
                // Point symbol with a real block: a small marker swatch stands in.
                DrawSwatch(def, x, y, layer, false);
            }

            // Explanatory text (prefer the entry text, fall back to display name).
            string text = !string.IsNullOrWhiteSpace(entry.Text)
                ? entry.Text
                : (def?.DisplayNameHu ?? entry.SymbolKey);

            var dbText = new DBText
            {
                TextString = text,
                Position = new Point3d(x + SwatchWidth + TextGap, y - (TextHeight / 2.0), 0),
                Height = TextHeight,
                Layer = LegendLayer,
            };
            _ctx.Append(dbText);
        }

        private void DrawSwatch(SymbolDef? def, double x, double y, string layer, bool isLinear)
        {
            if (isLinear)
            {
                var line = new Line(new Point3d(x, y, 0), new Point3d(x + SwatchWidth, y, 0)) { Layer = layer };
                if (def != null && !string.IsNullOrWhiteSpace(def.Linetype))
                {
                    line.Linetype = def.Linetype;
                }

                _ctx.Append(line);
            }
            else
            {
                double r = SwatchWidth / 4.0;
                double cx = x + (SwatchWidth / 2.0);
                var circle = new Circle(new Point3d(cx, y, 0), Vector3d.ZAxis, r) { Layer = layer };
                var h = new Line(new Point3d(cx - r, y, 0), new Point3d(cx + r, y, 0)) { Layer = layer };
                var v = new Line(new Point3d(cx, y - r, 0), new Point3d(cx, y + r, 0)) { Layer = layer };
                _ctx.Append(circle);
                _ctx.Append(h);
                _ctx.Append(v);
            }
        }
    }
}
