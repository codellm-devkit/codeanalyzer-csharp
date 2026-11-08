namespace Cansharp.Options;

public enum EmitTarget { Json, Neo4j, Schema }

/// <summary>Resolved CLI options passed to the orchestrator. See references/cli-contract.md.</summary>
public sealed record AnalysisOptions
{
    public required string? Input { get; init; }          // -i (required except --emit schema)
    public string? Output { get; init; }                  // -o; omitted => compact JSON to stdout
    public int AnalysisLevel { get; init; } = 1;          // -a; L1 is all this build implements
    public EmitTarget Emit { get; init; } = EmitTarget.Json;

    // #440 identity. --app-name is a deprecated alias for --application.
    public string? Application { get; init; }
    public string? Namespace { get; init; }

    public bool SkipTests { get; init; } = true;
    public int Verbosity { get; init; }
}
