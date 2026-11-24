using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UniGetUI.PackageEngine.Toolbox
{
    /// <summary>
    /// Source-generated JSON metadata for the cloud config.
    ///
    /// The published client is built with reflection-based serialization disabled
    /// (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>), which is what trim and AOT
    /// require. Any <c>JsonSerializer.Deserialize&lt;T&gt;</c> call without a resolver throws
    /// at runtime, so every Toolbox payload goes through this context instead.
    /// </summary>
    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
    [JsonSerializable(typeof(ToolboxConfig))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    public sealed partial class ToolboxJsonContext : JsonSerializerContext
    {
    }
}
