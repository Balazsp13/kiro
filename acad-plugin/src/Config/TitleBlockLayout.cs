// TitleBlockLayout.cs
//
// Loader + POCOs for schema/config/titleblock.config.json. Defines the title
// block (rajzpecset) block name, field order, Hungarian labels, the AutoCAD
// attribute tag the plugin populates in the house block, and paper-millimeter
// fallback placements used when no house template block exists.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helyszinrajz.Config
{
    /// <summary>Paper-millimeter placement fallback for a title-block field.</summary>
    public sealed class FallbackXy
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }
    }

    /// <summary>Egy fejlec mezo: one title-block field definition.</summary>
    public sealed class TitleBlockField
    {
        /// <summary>Logical key into project/summary data (e.g. "subject").</summary>
        [JsonPropertyName("key")]
        public string Key { get; set; } = "";

        [JsonPropertyName("label_hu")]
        public string LabelHu { get; set; } = "";

        [JsonPropertyName("label_full_hu")]
        public string LabelFullHu { get; set; } = "";

        /// <summary>AutoCAD attribute tag populated in the house block (e.g. "TARGY").</summary>
        [JsonPropertyName("attribute_tag")]
        public string AttributeTag { get; set; } = "";

        [JsonPropertyName("fallback_xy_mm")]
        public FallbackXy FallbackXyMm { get; set; } = new();
    }

    /// <summary>Root shape of titleblock.config.json.</summary>
    public sealed class TitleBlockLayout
    {
        [JsonPropertyName("note")]
        public string? Note { get; set; }

        /// <summary>Fejlec/rajzpecset block name in the house template.</summary>
        [JsonPropertyName("block_name")]
        public string BlockName { get; set; } = "RAJZPECSET";

        [JsonPropertyName("fallback_text_height_mm")]
        public double FallbackTextHeightMm { get; set; } = 2.5;

        [JsonPropertyName("fields")]
        public List<TitleBlockField> Fields { get; set; } = new();

        [JsonPropertyName("summary_fields")]
        public List<TitleBlockField> SummaryFields { get; set; } = new();

        /// <summary>All fields (project + summary) in display order.</summary>
        public IEnumerable<TitleBlockField> AllFields()
        {
            foreach (TitleBlockField f in Fields)
            {
                yield return f;
            }

            foreach (TitleBlockField f in SummaryFields)
            {
                yield return f;
            }
        }

        /// <summary>Deserialize the layout from a JSON file path.</summary>
        public static TitleBlockLayout Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"titleblock.config.json not found at '{path}'.", path);
            }

            string json = File.ReadAllText(path);
            TitleBlockLayout? layout = JsonSerializer.Deserialize<TitleBlockLayout>(json, JsonConfig.Options);
            if (layout == null)
            {
                throw new InvalidOperationException($"Could not parse titleblock.config.json at '{path}'.");
            }

            return layout;
        }
    }
}
