// SpecLoader.cs
//
// Reads a drawing-spec JSON file into the DrawingSpec POCO graph using the
// shared JsonConfig options.

using System;
using System.IO;
using System.Text.Json;
using Helyszinrajz.Model;

namespace Helyszinrajz.Config
{
    /// <summary>Loads a drawing-spec JSON document into <see cref="DrawingSpec"/>.</summary>
    public static class SpecLoader
    {
        public static DrawingSpec Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Drawing-spec JSON not found at '{path}'.", path);
            }

            string json = File.ReadAllText(path);
            DrawingSpec? spec = JsonSerializer.Deserialize<DrawingSpec>(json, JsonConfig.Options);
            if (spec == null)
            {
                throw new InvalidOperationException($"Could not parse drawing-spec JSON at '{path}'.");
            }

            return spec;
        }
    }
}
