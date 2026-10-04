// PlanarSimilarity.cs
//
// AutoCAD-independent form of the 2-point similarity math implemented by the
// plugin's GeoReferenceTransform (acad-plugin/src/GeoReferenceTransform.cs).
// It operates on plain doubles only: no Autodesk.* type is referenced, so this
// file compiles and runs anywhere the .NET SDK is installed.
//
// The math is identical to the production transform so a round-trip/scale test
// here exercises the same logic that places the raster base map:
//
//     model = s * R(theta) * image + t
//
//     di = P1 - P0  (image delta)   dm = Q1 - Q0  (model delta)
//     s     = |dm| / |di|
//     theta = atan2(dm.y, dm.x) - atan2(di.y, di.x)
//     a = s*cos(theta), b = s*sin(theta)
//     model.x = a*image.x - b*image.y + tx
//     model.y = b*image.x + a*image.y + ty
//     tx = Q0.x - (a*P0.x - b*P0.y)
//     ty = Q0.y - (b*P0.x + a*P0.y)
//
// This mirrors GeoReferenceTransform.FromGeoreference exactly; keep the two in
// sync when the production math changes.

using System;

namespace Helyszinrajz.Tests
{
    /// <summary>A 2D point as plain doubles (no AutoCAD dependency).</summary>
    public readonly struct Pt
    {
        public double X { get; }
        public double Y { get; }

        public Pt(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// 2-point planar similarity transform (translation + rotation + one uniform
    /// scale) mapping image/paper coordinates to model meters.
    /// </summary>
    public sealed class PlanarSimilarity
    {
        private readonly double _a;
        private readonly double _b;
        private readonly double _tx;
        private readonly double _ty;

        /// <summary>Uniform scale factor (model units per image unit).</summary>
        public double Scale { get; }

        /// <summary>Rotation applied to image vectors, in radians.</summary>
        public double RotationRad { get; }

        private PlanarSimilarity(double a, double b, double tx, double ty, double scale, double rotationRad)
        {
            _a = a;
            _b = b;
            _tx = tx;
            _ty = ty;
            Scale = scale;
            RotationRad = rotationRad;
        }

        /// <summary>Build the transform from 2 image/model point correspondences.</summary>
        public static PlanarSimilarity From(Pt image0, Pt model0, Pt image1, Pt model1)
        {
            double dix = image1.X - image0.X;
            double diy = image1.Y - image0.Y;
            double dmx = model1.X - model0.X;
            double dmy = model1.Y - model0.Y;

            double imgLen = Math.Sqrt((dix * dix) + (diy * diy));
            double modLen = Math.Sqrt((dmx * dmx) + (dmy * dmy));
            if (imgLen < 1e-12)
            {
                throw new ArgumentException("Image reference points are coincident.");
            }

            double scale = modLen / imgLen;
            double theta = Math.Atan2(dmy, dmx) - Math.Atan2(diy, dix);

            double a = scale * Math.Cos(theta);
            double b = scale * Math.Sin(theta);

            double tx = model0.X - ((a * image0.X) - (b * image0.Y));
            double ty = model0.Y - ((b * image0.X) + (a * image0.Y));

            return new PlanarSimilarity(a, b, tx, ty, scale, theta);
        }

        /// <summary>Map an image/paper point to a model point in meters.</summary>
        public Pt ToModel(Pt image)
        {
            double mx = (_a * image.X) - (_b * image.Y) + _tx;
            double my = (_b * image.X) + (_a * image.Y) + _ty;
            return new Pt(mx, my);
        }
    }
}
