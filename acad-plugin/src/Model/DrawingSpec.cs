// DrawingSpec.cs
//
// System.Text.Json POCOs that mirror schema/drawing-spec.schema.json EXACTLY.
// These are plain data-transfer objects: they must round-trip the sample
// schema/samples/sample-lajosmizse.drawingspec.json with no data loss.
//
// JsonPropertyName attributes match the JSON keys verbatim (snake_case /
// UPPERCASE for title_block tags). The DTO point type (DtoPoint) is kept
// deliberately separate from Autodesk.AutoCAD.Geometry.Point3d so model data
// never leaks AutoCAD types and vice versa.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Helyszinrajz.Model
{
    /// <summary>Root drawing-spec document (helyszinrajz rajz-spec).</summary>
    public sealed class DrawingSpec
    {
        [JsonPropertyName("meta")]
        public Meta Meta { get; set; } = new();

        [JsonPropertyName("project")]
        public Project Project { get; set; } = new();

        [JsonPropertyName("raster")]
        public Raster Raster { get; set; } = new();

        /// <summary>Georeferencia: exactly 2 reference points (image/paper -> model meters).</summary>
        [JsonPropertyName("georeference")]
        public List<GeoReferencePoint> Georeference { get; set; } = new();

        /// <summary>Retegek: layer keys or names used by the drawing.</summary>
        [JsonPropertyName("layers")]
        public List<string> Layers { get; set; } = new();

        [JsonPropertyName("symbols")]
        public List<SymbolInstance> Symbols { get; set; } = new();

        [JsonPropertyName("cable_runs")]
        public List<CableRun> CableRuns { get; set; } = new();

        /// <summary>Feliratok: optional free-text annotations.</summary>
        [JsonPropertyName("labels")]
        public List<Label> Labels { get; set; } = new();

        [JsonPropertyName("legend")]
        public List<LegendEntry> Legend { get; set; } = new();

        /// <summary>
        /// Fejlec / rajzpecset: key/value map of title-block values. Keys are the
        /// UPPERCASE attribute tags defined in titleblock.config.json (TARGY, RAJZSZAM, ...).
        /// </summary>
        [JsonPropertyName("title_block")]
        public Dictionary<string, string> TitleBlock { get; set; } = new();

        [JsonPropertyName("bom")]
        public List<BomRow> Bom { get; set; } = new();

        [JsonPropertyName("summary")]
        public Summary Summary { get; set; } = new();
    }

    /// <summary>Dokumentum metaadat.</summary>
    public sealed class Meta
    {
        [JsonPropertyName("schema_version")]
        public string SchemaVersion { get; set; } = "";

        [JsonPropertyName("generator")]
        public string Generator { get; set; } = "";

        [JsonPropertyName("created_utc")]
        public string CreatedUtc { get; set; } = "";

        [JsonPropertyName("confidence_note")]
        public string ConfidenceNote { get; set; } = "";
    }

    /// <summary>Projekt adatok.</summary>
    public sealed class Project
    {
        [JsonPropertyName("subject")]
        public string Subject { get; set; } = "";

        [JsonPropertyName("rajzszam")]
        public string Rajzszam { get; set; } = "";

        [JsonPropertyName("datum")]
        public string Datum { get; set; } = "";

        [JsonPropertyName("kivitelezo")]
        public string Kivitelezo { get; set; } = "";

        [JsonPropertyName("tervezo")]
        public string Tervezo { get; set; } = "";

        [JsonPropertyName("muszaki_rajzolo")]
        public string MuszakiRajzolo { get; set; } = "";

        [JsonPropertyName("scale_denominator")]
        public int ScaleDenominator { get; set; } = 500;
    }

    /// <summary>Raszter alaterkep (e-kozmu PDF/image) and attach info.</summary>
    public sealed class Raster
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";

        [JsonPropertyName("dpi")]
        public double? Dpi { get; set; }

        [JsonPropertyName("attach")]
        public RasterAttach Attach { get; set; } = new();
    }

    /// <summary>Beillesztesi informacio: raster attach / position.</summary>
    public sealed class RasterAttach
    {
        [JsonPropertyName("insertion_point")]
        public DtoPoint InsertionPoint { get; set; } = new();

        [JsonPropertyName("scale_denominator")]
        public int ScaleDenominator { get; set; } = 500;

        [JsonPropertyName("rotation_deg")]
        public double RotationDeg { get; set; }
    }

    /// <summary>Georeferencia pont: image/paper -> model correspondence.</summary>
    public sealed class GeoReferencePoint
    {
        [JsonPropertyName("image")]
        public DtoPoint Image { get; set; } = new();

        [JsonPropertyName("model")]
        public DtoPoint Model { get; set; } = new();

        [JsonPropertyName("units")]
        public string Units { get; set; } = "meter";

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    /// <summary>Jel peldany: a placed symbol instance.</summary>
    public sealed class SymbolInstance
    {
        [JsonPropertyName("symbol_key")]
        public string SymbolKey { get; set; } = "";

        [JsonPropertyName("position")]
        public DtoPoint Position { get; set; } = new();

        [JsonPropertyName("rotation_deg")]
        public double RotationDeg { get; set; }

        /// <summary>Lepteke: symbol scale factor. Defaults to 1 when absent.</summary>
        [JsonPropertyName("scale")]
        public double Scale { get; set; } = 1.0;

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    /// <summary>Kabel nyomvonal: polyline run with vertices in model meters.</summary>
    public sealed class CableRun
    {
        [JsonPropertyName("run_id")]
        public string RunId { get; set; } = "";

        [JsonPropertyName("symbol_key")]
        public string? SymbolKey { get; set; }

        [JsonPropertyName("style")]
        public string? Style { get; set; }

        [JsonPropertyName("vertices")]
        public List<Vertex> Vertices { get; set; } = new();

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = "";

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("length_m")]
        public double? LengthM { get; set; }

        [JsonPropertyName("cable_type")]
        public string? CableType { get; set; }
    }

    /// <summary>Felirat: free-text annotation in model space.</summary>
    public sealed class Label
    {
        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("position")]
        public DtoPoint Position { get; set; } = new();

        [JsonPropertyName("height_m")]
        public double? HeightM { get; set; }

        [JsonPropertyName("rotation_deg")]
        public double RotationDeg { get; set; }

        [JsonPropertyName("layer")]
        public string? Layer { get; set; }
    }

    /// <summary>Jelmagyarazat tetel: one legend entry.</summary>
    public sealed class LegendEntry
    {
        [JsonPropertyName("symbol_key")]
        public string SymbolKey { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";
    }

    /// <summary>Anyagjegyzek sor: a BOM row.</summary>
    public sealed class BomRow
    {
        [JsonPropertyName("item")]
        public string Item { get; set; } = "";

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("qty")]
        public double Qty { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; } = "";

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }

    /// <summary>Osszegzes: summary fields shown near the title block.</summary>
    public sealed class Summary
    {
        [JsonPropertyName("meretarany")]
        public string Meretarany { get; set; } = "";

        [JsonPropertyName("erintett_kozterulet")]
        public string ErintettKozterulet { get; set; } = "";

        [JsonPropertyName("kabel_teljes_hossza_m")]
        public double KabelTeljesHosszaM { get; set; }
    }
}
