// SymbolCatalog.cs
//
// Loader + POCOs for schema/config/symbols.config.json. The catalog maps a
// symbol_key to its Hungarian display name, layer, ACI color index, linetype,
// optional AutoCAD block name, and a fallback-geometry hint. Edit the JSON (not
// this code) to retarget blocks, layers, colors, and linetypes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Helyszinrajz.Config
{
    /// <summary>Egy jel definicio: one symbol/layer definition from symbols.config.json.</summary>
    public sealed class SymbolDef
    {
        [JsonPropertyName("display_name_hu")]
        public string DisplayNameHu { get; set; } = "";

        [JsonPropertyName("layer")]
        public string Layer { get; set; } = "0";

        /// <summary>ACI color index (1..255). 7 is white/black default.</summary>
        [JsonPropertyName("color")]
        public short Color { get; set; } = 7;

        [JsonPropertyName("linetype")]
        public string Linetype { get; set; } = "CONTINUOUS";

        /// <summary>House block name; null means draw fallback geometry instead.</summary>
        [JsonPropertyName("block_name")]
        public string? BlockName { get; set; }

        [JsonPropertyName("fallback_geometry")]
        public string? FallbackGeometry { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>Root shape of symbols.config.json.</summary>
    public sealed class SymbolCatalogFile
    {
        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("symbols")]
        public Dictionary<string, SymbolDef> Symbols { get; set; } = new();
    }

    /// <summary>Loaded symbol catalog with lookup helpers keyed by symbol_key.</summary>
    public sealed class SymbolCatalog
    {
        private readonly Dictionary<string, SymbolDef> _symbols;

        public SymbolCatalog(Dictionary<string, SymbolDef> symbols)
        {
            _symbols = symbols ?? new Dictionary<string, SymbolDef>();
        }

        /// <summary>All defined symbol keys.</summary>
        public IReadOnlyCollection<string> Keys => _symbols.Keys;

        /// <summary>All layer definitions distinct by layer name.</summary>
        public IEnumerable<SymbolDef> Definitions => _symbols.Values;

        /// <summary>Look up by symbol_key; returns null when the key is unknown.</summary>
        public SymbolDef? Get(string? symbolKey)
        {
            if (string.IsNullOrEmpty(symbolKey))
            {
                return null;
            }

            return _symbols.TryGetValue(symbolKey, out SymbolDef? def) ? def : null;
        }

        /// <summary>True when the key is known.</summary>
        public bool Contains(string? symbolKey) =>
            !string.IsNullOrEmpty(symbolKey) && _symbols.ContainsKey(symbolKey);

        /// <summary>Deserialize a SymbolCatalog from a JSON file path.</summary>
        public static SymbolCatalog Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"symbols.config.json not found at '{path}'.", path);
            }

            string json = File.ReadAllText(path);
            SymbolCatalogFile? file = JsonSerializer.Deserialize<SymbolCatalogFile>(json, JsonConfig.Options);
            if (file == null)
            {
                throw new InvalidOperationException($"Could not parse symbols.config.json at '{path}'.");
            }

            return new SymbolCatalog(file.Symbols);
        }
    }
}
