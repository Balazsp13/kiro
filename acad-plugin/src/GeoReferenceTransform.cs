// GeoReferenceTransform.cs
//
// Two-point similarity transform mapping the RASTER base map's image/paper
// coordinates to AutoCAD model coordinates in meters (1 drawing unit = 1 meter).
//
// SCOPE: this transform applies ONLY to the raster base map (see
// RasterRenderer). Symbol positions, cable vertices, label positions, and the
// legend/title-block/BOM placements are authored in model meters per the schema
// and are placed directly, WITHOUT this transform. Running already-in-meters
// geometry through this transform would double-count the image->model scale and
// shrink the plan relative to the base map.
//
// A similarity transform preserves shape (translation + rotation + a single
// uniform scale, no shear). Two point correspondences fully determine it:
//
//     model = s * R(theta) * image + t
//
// Given image points P0, P1 and their model images Q0, Q1:
//
//     di = P1 - P0   (vector in image space)
//     dm = Q1 - Q0   (vector in model space)
//
//     s     = |dm| / |di|                      (uniform scale)
//     theta = atan2(dm.y, dm.x) - atan2(di.y, di.x)   (rotation)
//
// With a = s*cos(theta), b = s*sin(theta) the forward map is:
//
//     model.x = a*image.x - b*image.y + tx
//     model.y = b*image.x + a*image.y + ty
//
// and the translation is solved so P0 maps exactly onto Q0:
//
//     tx = Q0.x - (a*P0.x - b*P0.y)
//     ty = Q0.y - (b*P0.x + a*P0.y)
//
// For the Lajosmizse sample the image delta (200,0) maps to model (100,0): the
// scale is 0.5, rotation is 0, translation is 0. This scale is applied to the
// raster underlay/image so that the base map maps onto model meters; the model
// space itself is in meters, so 1 drawing unit = 1 meter is enforced by the
// "units":"meter" georeference contract.

using System;
using Autodesk.AutoCAD.Geometry;
using Helyszinrajz.Model;

namespace Helyszinrajz
{
    /// <summary>Image/paper -> model-meter similarity transform from 2 reference points.</summary>
    public sealed class GeoReferenceTransform
    {
        // Forward coefficients: model = (a,b) rotation-scale applied to image + (tx,ty).
        private readonly double _a;
        private readonly double _b;
        private readonly double _tx;
        private readonly double _ty;

        /// <summary>Uniform scale factor (model units per image unit).</summary>
        public double Scale { get; }

        /// <summary>Rotation applied to image vectors, in radians.</summary>
        public double RotationRad { get; }

        private GeoReferenceTransform(double a, double b, double tx, double ty, double scale, double rotationRad)
        {
            _a = a;
            _b = b;
            _tx = tx;
            _ty = ty;
            Scale = scale;
            RotationRad = rotationRad;
        }

        /// <summary>
        /// Build the transform from exactly 2 georeference points. The "units"
        /// field must be "meter" so that model coordinates are in meters and
        /// 1 drawing unit = 1 meter.
        /// </summary>
        public static GeoReferenceTransform FromGeoreference(System.Collections.Generic.IReadOnlyList<GeoReferencePoint> points)
        {
            if (points == null || points.Count != 2)
            {
                throw new ArgumentException("Georeference requires exactly 2 reference points.", nameof(points));
            }

            GeoReferencePoint p0 = points[0];
            GeoReferencePoint p1 = points[1];

            foreach (GeoReferencePoint p in points)
            {
                if (!string.Equals(p.Units, "meter", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        $"Georeference units must be 'meter' (1 unit = 1 meter), got '{p.Units}'.");
                }
            }

            double dix = p1.Image.X - p0.Image.X;
            double diy = p1.Image.Y - p0.Image.Y;
            double dmx = p1.Model.X - p0.Model.X;
            double dmy = p1.Model.Y - p0.Model.Y;

            double imgLen = Math.Sqrt((dix * dix) + (diy * diy));
            double modLen = Math.Sqrt((dmx * dmx) + (dmy * dmy));
            if (imgLen < 1e-12)
            {
                throw new ArgumentException("Georeference image reference points are coincident.");
            }

            double scale = modLen / imgLen;
            double theta = Math.Atan2(dmy, dmx) - Math.Atan2(diy, dix);

            double a = scale * Math.Cos(theta);
            double b = scale * Math.Sin(theta);

            double tx = p0.Model.X - ((a * p0.Image.X) - (b * p0.Image.Y));
            double ty = p0.Model.Y - ((b * p0.Image.X) + (a * p0.Image.Y));

            return new GeoReferenceTransform(a, b, tx, ty, scale, theta);
        }

        /// <summary>
        /// Map a raster image/paper point to a model point in meters. Use only for
        /// the raster base map; model-meter geometry is placed directly.
        /// </summary>
        public DtoPoint ToModel(DtoPoint image)
        {
            double mx = (_a * image.X) - (_b * image.Y) + _tx;
            double my = (_b * image.X) + (_a * image.Y) + _ty;
            return new DtoPoint(mx, my);
        }

        /// <summary>
        /// Map a raster image/paper point to an AutoCAD model Point3d (z = 0). Use
        /// only for the raster base map; model-meter geometry is placed directly.
        /// </summary>
        public Point3d ToModelPoint3d(DtoPoint image)
        {
            DtoPoint m = ToModel(image);
            return new Point3d(m.X, m.Y, 0.0);
        }

        /// <summary>
        /// AutoCAD Matrix3d for the same forward transform, suitable for placing a
        /// raster underlay or transforming geometry. Column-major rotation-scale with
        /// translation; z is identity.
        /// </summary>
        public Matrix3d ToMatrix3d()
        {
            // Row-major 4x4 as expected by Matrix3d(double[16]):
            //   [ a  -b  0  tx ]
            //   [ b   a  0  ty ]
            //   [ 0   0  1  0  ]
            //   [ 0   0  0  1  ]
            double[] m =
            {
                _a, -_b, 0.0, _tx,
                _b,  _a, 0.0, _ty,
                0.0, 0.0, 1.0, 0.0,
                0.0, 0.0, 0.0, 1.0,
            };
            return new Matrix3d(m);
        }
    }
}
