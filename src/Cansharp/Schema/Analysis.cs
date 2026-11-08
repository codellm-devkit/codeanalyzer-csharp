using System.Text.Json.Serialization;

namespace Cansharp.Schema;

/// <summary>The application root — carries the identity-scoped id and the symbol table.</summary>
public sealed record Application
{
    // Two-segment identity root (per python-sdk#440): can://<namespace>/<application>/csharp
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("kind")] public string Kind => "application";

    // application.name spine field (2026-09-23 spec). The resolved application name.
    [JsonPropertyName("name")] public required string Name { get; init; }

    // #440: raw resolved values retained so consumers don't re-parse the can:// id.
    [JsonPropertyName("application_name")] public required string ApplicationName { get; init; }
    [JsonPropertyName("identity_namespace")] public required string IdentityNamespace { get; init; }

    [JsonPropertyName("symbol_table")] public Dictionary<string, Module> SymbolTable { get; init; } = new();

    // call_graph / param_in / param_out appear at L2 / L4 — omitted at L1.
}

/// <summary>The analysis.json envelope. identity_version:2 is orthogonal to schema_version.</summary>
public sealed record AnalysisDocument
{
    [JsonPropertyName("schema_version")] public string SchemaVersion => "2.0.0";

    // #440 identity marker. Validated as an integer before model coercion by consumers.
    [JsonPropertyName("identity_version")] public int IdentityVersion => 2;

    [JsonPropertyName("language")] public string Language => "csharp";
    [JsonPropertyName("max_level")] public required int MaxLevel { get; init; }
    [JsonPropertyName("analyzer")] public required AnalyzerInfo Analyzer { get; init; }
    [JsonPropertyName("application")] public required Application Application { get; init; }
}

public sealed record AnalyzerInfo
{
    [JsonPropertyName("name")] public string Name => "codeanalyzer-dotnet";
    [JsonPropertyName("version")] public required string Version { get; init; }
}
