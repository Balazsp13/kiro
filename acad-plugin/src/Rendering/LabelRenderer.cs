// LabelRenderer.cs
//
// Places free-text annotations (feliratok) at model positions with the
// configured text height, rotation, and layer. Uses DBText for single-line
// labels. Label positions are authored in model meters (schema: "position:
// placement point in model meters"), so they are placed directly and NOT run
// through the georeference transform (that maps the raster base map only).

using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Model;

namespace Helyszinrajz.Rendering
{
    /// <summary>Renders free-text labels into model space.</summary>
    public sealed class LabelRenderer
    {
        /// <summary>Default text height in model meters when a label omits height_m.</summary>
        public const double DefaultHeightM = 0.5;

        private readonly RenderContext _ctx;

        public LabelRenderer(RenderContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        public void Render(IEnumerable<Label> labels)
        {
            if (labels == null)
            {
                return;
            }

            foreach (Label label in labels)
            {
                RenderOne(label);
            }
        }

        private void RenderOne(Label label)
        {
            if (string.IsNullOrEmpty(label.Text))
            {
                return;
            }

            string layer = string.IsNullOrWhiteSpace(label.Layer) ? "0" : label.Layer!;
            _ctx.Layers.EnsureLayer(layer);

            Point3d pos = RenderContext.ToPoint3d(label.Position);
            double height = label.HeightM is > 0 ? label.HeightM!.Value : DefaultHeightM;

            var text = new DBText
            {
                TextString = label.Text,
                Position = pos,
                Height = height,
                Rotation = label.RotationDeg * Math.PI / 180.0,
                Layer = layer,
            };

            _ctx.Append(text);
        }
    }
}
