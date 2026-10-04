// SymbolRenderer.cs
//
// Places symbol instances. When the symbol's configured block_name exists in
// the current drawing (supplied by the house .dwt/.dwg) it is inserted as a
// BlockReference at the model position with rotation and scale. Otherwise a
// documented fallback marker (a circle with an inscribed cross) is drawn on the
// symbol's configured layer and color, so the drawing is never silently empty.

using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Config;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Inserts symbol blocks or draws fallback markers.</summary>
    public sealed class SymbolRenderer
    {
        /// <summary>Fallback marker radius in model meters.</summary>
        public const double FallbackMarkerRadius = 0.5;

        private readonly RenderContext _ctx;

        public SymbolRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Render(System.Collections.Generic.IEnumerable<SymbolInstance> symbols)
        {
            if (symbols == null)
            {
                return;
            }

            foreach (SymbolInstance s in symbols)
            {
                RenderOne(s);
            }
        }

        private void RenderOne(SymbolInstance s)
        {
            SymbolDef? def = _ctx.Symbols.Get(s.SymbolKey);
            string layer = def?.Layer ?? "0";
            short color = def?.Color ?? 7;
            string linetype = def?.Linetype ?? "CONTINUOUS";

            // Make sure the symbol's layer exists (from config when known).
            _ctx.Layers.EnsureLayer(layer, color, linetype);

            // Symbol positions are authored in model meters (schema: "position:
            // placement point in model meters"), so they are placed directly.
            // The georeference transform maps the raster base map only; it must
            // NOT be applied to this already-in-meters geometry.
            Point3d pos = RenderContext.ToPoint3d(s.Position);
            double rotationRad = s.RotationDeg * Math.PI / 180.0;
            double scale = s.Scale <= 0 ? 1.0 : s.Scale;

            bool inserted = false;
            if (def != null && !string.IsNullOrWhiteSpace(def.BlockName))
            {
                inserted = TryInsertBlock(def.BlockName!, pos, rotationRad, scale, layer);
            }

            if (!inserted)
            {
                DrawFallbackMarker(pos, scale, layer, def?.BlockName);
            }

            if (!string.IsNullOrWhiteSpace(s.Label))
            {
                DrawLabel(s.Label!, pos, layer);
            }
        }

        /// <summary>Insert the house block if it is defined in the current drawing.</summary>
        private bool TryInsertBlock(string blockName, Point3d pos, double rotationRad, double scale, string layer)
        {
            var bt = (BlockTable)_ctx.Tr.GetObject(_ctx.Db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
            {
                _ctx.Log($"Jel: a(z) '{blockName}' blokk nincs a rajzban, helyettesito jel kerul kirajzolasra.");
                return false;
            }

            var blockRef = new BlockReference(pos, bt[blockName])
            {
                Rotation = rotationRad,
                ScaleFactors = new Scale3d(scale, scale, scale),
                Layer = layer,
            };

            _ctx.Append(blockRef);

            // Pull through any constant/variable attributes defined on the block.
            var btr = (BlockTableRecord)_ctx.Tr.GetObject(bt[blockName], OpenMode.ForRead);
            if (btr.HasAttributeDefinitions)
            {
                foreach (ObjectId id in btr)
                {
                    if (_ctx.Tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition attDef && !attDef.Constant)
                    {
                        var attRef = new AttributeReference();
                        attRef.SetAttributeFromBlock(attDef, blockRef.BlockTransform);
                        attRef.TextString = attDef.TextString;
                        blockRef.AttributeCollection.AppendAttribute(attRef);
                        _ctx.Tr.AddNewlyCreatedDBObject(attRef, true);
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Draw the documented fallback marker: a circle with an inscribed cross,
        /// centered at the symbol position, on the symbol layer.
        /// </summary>
        private void DrawFallbackMarker(Point3d center, double scale, string layer, string? intendedBlock)
        {
            double r = FallbackMarkerRadius * (scale <= 0 ? 1.0 : scale);

            var circle = new Circle(center, Vector3d.ZAxis, r) { Layer = layer };
            _ctx.Append(circle);

            var hLine = new Line(new Point3d(center.X - r, center.Y, 0), new Point3d(center.X + r, center.Y, 0)) { Layer = layer };
            var vLine = new Line(new Point3d(center.X, center.Y - r, 0), new Point3d(center.X, center.Y + r, 0)) { Layer = layer };
            _ctx.Append(hLine);
            _ctx.Append(vLine);

            if (!string.IsNullOrWhiteSpace(intendedBlock))
            {
                _ctx.Log($"Jel: helyettesito jelcsoport a(z) '{intendedBlock}' blokk helyett ({center.X:0.##}, {center.Y:0.##}).");
            }
        }

        private void DrawLabel(string text, Point3d anchor, string layer)
        {
            var dbText = new DBText
            {
                TextString = text,
                Position = new Point3d(anchor.X + (FallbackMarkerRadius * 1.5), anchor.Y + (FallbackMarkerRadius * 1.5), 0),
                Height = 0.5,
                Layer = layer,
            };
            _ctx.Append(dbText);
        }
    }
}
