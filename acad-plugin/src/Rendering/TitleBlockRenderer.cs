// TitleBlockRenderer.cs
//
// Populates the house title-block (rajzpecset) block attributes by tag from
// titleblock.config.json when an instance of that block exists in the drawing.
// When no house block is present it draws a simple framed fallback title block
// at the configured paper-millimeter positions, listing the project and summary
// fields (Targy, Rajzszam, Datum, Kivitelezo, Tervezo, Muszaki Rajzolo,
// Meretarany 1:500, Erintett kozterulet, Kabel teljes hossza).
//
// The title_block object in the spec maps UPPERCASE attribute tags
// (TARGY, RAJZSZAM, DATUM, ...) to values; this renderer populates by tag.

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Config;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Fills the title block from spec values, or draws a fallback frame.</summary>
    public sealed class TitleBlockRenderer
    {
        public const string FallbackLayer = "RAJZPECSET";

        private readonly RenderContext _ctx;

        public TitleBlockRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>
        /// Populate the house block if present; otherwise draw the fallback frame.
        /// <paramref name="fallbackOrigin"/> is the model-space anchor (meters) for
        /// the fallback block when there is no house template.
        /// </summary>
        public void Render(DrawingSpec spec, Point3d fallbackOrigin)
        {
            IReadOnlyDictionary<string, string> values = BuildValues(spec);

            if (TryPopulateHouseBlock(values))
            {
                _ctx.Log($"Fejlec: a(z) '{_ctx.TitleBlock.BlockName}' blokk attributumai kitoltve.");
                return;
            }

            _ctx.Log($"Fejlec: a(z) '{_ctx.TitleBlock.BlockName}' blokk nem talalhato, helyettesito rajzpecset keszul.");
            DrawFallback(values, fallbackOrigin);
        }

        /// <summary>
        /// Merge spec.title_block (authoritative, keyed by attribute tag) with
        /// derived project/summary values so the fallback always has data even if
        /// title_block is sparse.
        /// </summary>
        private Dictionary<string, string> BuildValues(DrawingSpec spec)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Derive from project + summary via the config field mapping.
            foreach (TitleBlockField f in _ctx.TitleBlock.AllFields())
            {
                string? derived = DeriveValue(spec, f.Key);
                if (!string.IsNullOrEmpty(derived))
                {
                    map[f.AttributeTag] = derived!;
                }
            }

            // spec.title_block wins (it is the explicit, AI/human-authored value).
            if (spec.TitleBlock != null)
            {
                foreach (KeyValuePair<string, string> kv in spec.TitleBlock)
                {
                    map[kv.Key] = kv.Value;
                }
            }

            return map;
        }

        private static string? DeriveValue(DrawingSpec spec, string key) => key switch
        {
            "subject" => spec.Project.Subject,
            "rajzszam" => spec.Project.Rajzszam,
            "datum" => spec.Project.Datum,
            "kivitelezo" => spec.Project.Kivitelezo,
            "tervezo" => spec.Project.Tervezo,
            "muszaki_rajzolo" => spec.Project.MuszakiRajzolo,
            "meretarany" => spec.Summary.Meretarany,
            "erintett_kozterulet" => spec.Summary.ErintettKozterulet,
            "kabel_teljes_hossza_m" => $"{spec.Summary.KabelTeljesHosszaM:0.##} m",
            _ => null,
        };

        /// <summary>Find a block reference of the configured block and set its attributes by tag.</summary>
        private bool TryPopulateHouseBlock(IReadOnlyDictionary<string, string> values)
        {
            string blockName = _ctx.TitleBlock.BlockName;
            var bt = (BlockTable)_ctx.Tr.GetObject(_ctx.Db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
            {
                return false;
            }

            // Scan model space for an existing reference to the title-block definition.
            ObjectId btrId = bt[blockName];
            bool populatedAny = false;

            foreach (ObjectId entId in _ctx.ModelSpace)
            {
                if (_ctx.Tr.GetObject(entId, OpenMode.ForRead) is not BlockReference bref)
                {
                    continue;
                }

                if (bref.DynamicBlockTableRecord != btrId && bref.BlockTableRecord != btrId)
                {
                    continue;
                }

                bref.UpgradeOpen();
                foreach (ObjectId attId in bref.AttributeCollection)
                {
                    var att = (AttributeReference)_ctx.Tr.GetObject(attId, OpenMode.ForWrite);
                    if (values.TryGetValue(att.Tag, out string? v))
                    {
                        att.TextString = v;
                        populatedAny = true;
                    }
                }
            }

            return populatedAny;
        }

        /// <summary>
        /// Draw a labeled, framed fallback title block.
        ///
        /// PLACEHOLDER (deliberate): this path runs only when the house .dwt and
        /// its real rajzpecset block are absent. It is a schematic stand-in whose
        /// geometry is not drawn to any real sheet layout. The config's
        /// fallback_xy_mm positions (documented as paper millimeters) are used
        /// directly as model-space offsets from <paramref name="origin"/>, i.e.
        /// 1 model unit stands in for 1 paper mm inside this frame; this mixes
        /// units on purpose to keep the stub readable without a sheet. The frame
        /// is sized from the actual field extents plus a uniform text-height pad.
        /// Once the house .dwt arrives, TryPopulateHouseBlock fills the real block
        /// by attribute tag and this fallback is not used. See acad-plugin/README.
        /// </summary>
        private void DrawFallback(IReadOnlyDictionary<string, string> values, Point3d origin)
        {
            _ctx.Layers.EnsureLayer(FallbackLayer, 7, "CONTINUOUS");

            double h = _ctx.TitleBlock.FallbackTextHeightMm;
            if (h <= 0)
            {
                h = 2.5;
            }

            // Approximate text width per character for a monospace-ish estimate;
            // only used to size the schematic frame so it encloses the text.
            const double CharWidthFactor = 0.6;

            // Frame bounds derived from the actual field text extents (position
            // plus estimated text width/height), so the frame encloses the labels.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

            foreach (TitleBlockField f in _ctx.TitleBlock.AllFields())
            {
                double fx = origin.X + f.FallbackXyMm.X;
                double fy = origin.Y + f.FallbackXyMm.Y;

                string value = values.TryGetValue(f.AttributeTag, out string? v) ? v : "";
                string line = $"{f.LabelFullHu}: {value}";

                double textWidth = line.Length * h * CharWidthFactor;
                minX = Math.Min(minX, fx);
                minY = Math.Min(minY, fy);
                maxX = Math.Max(maxX, fx + textWidth);
                maxY = Math.Max(maxY, fy + h);

                var text = new DBText
                {
                    TextString = line,
                    Position = new Point3d(fx, fy, 0),
                    Height = h,
                    Layer = FallbackLayer,
                };
                _ctx.Append(text);
            }

            if (minX <= maxX && minY <= maxY)
            {
                double pad = h * 2.0;
                var frame = new Polyline { Closed = true, Layer = FallbackLayer };
                frame.AddVertexAt(0, new Point2d(minX - pad, minY - pad), 0, 0, 0);
                frame.AddVertexAt(1, new Point2d(maxX + pad, minY - pad), 0, 0, 0);
                frame.AddVertexAt(2, new Point2d(maxX + pad, maxY + pad), 0, 0, 0);
                frame.AddVertexAt(3, new Point2d(minX - pad, maxY + pad), 0, 0, 0);
                _ctx.Append(frame);
            }
        }
    }
}
