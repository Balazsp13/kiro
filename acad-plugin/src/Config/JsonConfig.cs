// JsonConfig.cs
//
// Shared System.Text.Json options for every deserializer in the plugin. The
// spec and config files use literal snake_case / UPPERCASE keys that are matched
// by explicit JsonPropertyName attributes, so no naming policy is applied here;
// we only relax case-sensitivity and allow comments / trailing commas because
// the config files carry human-authored "note" fields and may be hand-edited.

using System.Text.Json;

namespace Helyszinrajz.Config
{
    /// <summary>Central System.Text.Json options used across the plugin.</summary>
    public static class JsonConfig
    {
        /// <summary>Lenient, case-insensitive options for reading spec/config JSON.</summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
    }
}
