// CableRenderer.cs
//
// Draws each cable run as a lightweight Polyline through its vertices (model
// meters), with layer/linetype/color taken from the run's layer and, when
// available, the symbol catalog entry referenced by symbol_key. Adds the run
// label as DBText near the midpoint and annotates the length.
//
// Vertices are authored in model meters (schema: "vertices: ordered polyline
// vertices in model meters"), so they are placed directly and NOT run through
// the georeference transform (that maps the raster base map only).
//
// The length annotation is computed from the ACTUAL drawn polyline geometry so
// it can never contradict what is drawn. cable_runs[].length_m remains an
// optional authored hint in the schema; when it is supplied and disagrees with
// the drawn length by more than a small tolerance a warning is logged, but the
// label always reflects the drawn geometry.

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Config;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Renders cable-run polylines, labels, and lengths.</summary>
    public sealed class CableRenderer
    {
        private readonly RenderContext _ctx;

        public CableRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Render(IEnumerable<CableRun> runs)
        {
            if (runs == null)
            {
                return;
            }

            foreach (CableRun run in runs)
            {
                RenderOne(run);
            }
        }

        private void RenderOne(CableRun run)
        {
            if (run.Vertices == null || run.Vertices.Count < 2)
            {
                _ctx.Log($"Kabel: a(z) '{run.RunId}' nyomvonalhoz legalabb 2 toreespont szukseges, kihagyva.");
                return;
            }

            SymbolDef? def = _ctx.Symbols.Get(run.SymbolKey);
            string layer = !string.IsNullOrWhiteSpace(run.Layer) ? run.Layer : (def?.Layer ?? "0");
            short color = def?.Color ?? 7;
            string linetype = def?.Linetype ?? "CONTINUOUS";

            _ctx.Layers.EnsureLayer(layer, color, linetype);

            var pline = new Polyline { Layer = layer };
            if (!string.IsNullOrWhiteSpace(linetype))
            {
                pline.Linetype = linetype;
            }

            var modelPts = new List<Point3d>(run.Vertices.Count);
            int i = 0;
            foreach (Vertex v in run.Vertices)
            {
                Point3d p = RenderContext.ToPoint3d(v.ToPoint());
                modelPts.Add(p);
                pline.AddVertexAt(i++, new Point2d(p.X, p.Y), 0, 0, 0);
            }

            _ctx.Append(pline);

            // Label near the midpoint of the run.
            // Warn if the authored length_m hint disagrees with the drawn geometry.
            double drawnLength = ComputeLength(modelPts);
            if (run.LengthM is > 0 && Math.Abs(run.LengthM.Value - drawnLength) > 0.05)
            {
                _ctx.Log(
                    $"Kabel: a(z) '{run.RunId}' megadott length_m={run.LengthM.Value:0.##} m elter a " +
                    $"rajzolt nyomvonal hosszatol ({drawnLength:0.##} m); a felirat a rajzolt hosszt mutatja.");
            }

            string? label = run.Label ?? run.CableType;
            if (!string.IsNullOrWhiteSpace(label))
            {
                Point3d mid = MidPoint(modelPts);
                var text = new DBText
                {
                    TextString = AppendLength(label!, drawnLength),
                    Position = new Point3d(mid.X, mid.Y + 0.5, 0),
                    Height = 0.5,
                    Layer = layer,
                };
                _ctx.Append(text);
            }
        }

        /// <summary>
        /// Append the length annotation to the label text. The length is taken
        /// from the actual drawn polyline so the annotation can never contradict
        /// the drawn geometry.
        /// </summary>
        private static string AppendLength(string label, double drawnLength)
        {
            if (drawnLength <= 0)
            {
                return label;
            }

            return $"{label} (L={drawnLength:0.##} m)";
        }

        private static double ComputeLength(IReadOnlyList<Point3d> pts)
        {
            double total = 0.0;
            for (int i = 1; i < pts.Count; i++)
            {
                total += pts[i - 1].DistanceTo(pts[i]);
            }

            return total;
        }

        private static Point3d MidPoint(IReadOnlyList<Point3d> pts)
        {
            // Midpoint by arc length along the polyline.
            double total = ComputeLength(pts);
            double half = total / 2.0;
            double acc = 0.0;
            for (int i = 1; i < pts.Count; i++)
            {
                double seg = pts[i - 1].DistanceTo(pts[i]);
                if (acc + seg >= half)
                {
                    double t = seg < 1e-9 ? 0 : (half - acc) / seg;
                    return new Point3d(
                        pts[i - 1].X + ((pts[i].X - pts[i - 1].X) * t),
                        pts[i - 1].Y + ((pts[i].Y - pts[i - 1].Y) * t),
                        0);
                }

                acc += seg;
            }

            return pts[pts.Count - 1];
        }
    }
}
