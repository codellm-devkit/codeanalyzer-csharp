using System.Text.Json;
using Cansharp.Schema;

namespace Cansharp.Emit;

/// <summary>Serializes the Analysis envelope to analysis.json (or compact to stdout).</summary>
public static class JsonEmitter
{
    private static JsonSerializerOptions Options(bool indented) => new()
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static string Serialize(AnalysisDocument analysis, bool indented) =>
        JsonSerializer.Serialize(analysis, Options(indented));
}
