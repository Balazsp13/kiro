// DtoPoint.cs
//
// Pont / Toreespont DTO. A plain 2D point used by every spec coordinate
// (point and vertex share the same {x, y} shape in the schema). This type is
// intentionally decoupled from Autodesk.AutoCAD.Geometry.Point3d: spec data is
// expressed in these DTO points, and only GeoReferenceTransform turns them into
// AutoCAD geometry types.

using System.Text.Json.Serialization;

namespace Helyszinrajz.Model
{
    /// <summary>Pont: a 2D point (x, y). Interpreted in model meters unless noted.</summary>
    public sealed class DtoPoint
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        public DtoPoint() { }

        public DtoPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// Toreespont: a polyline vertex in model meters. Same {x, y} shape as
    /// DtoPoint; kept as a distinct type to match the schema vocabulary.
    /// </summary>
    public sealed class Vertex
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        public Vertex() { }

        public Vertex(double x, double y)
        {
            X = x;
            Y = y;
        }

        public DtoPoint ToPoint() => new(X, Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
