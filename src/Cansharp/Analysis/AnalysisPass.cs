using Cansharp.Schema;

namespace Cansharp.Analysis;

// The pluggable pass layer SEAM (architecture rule 4). Built-in list starts empty, but the
// ABC + registry + run_pipeline exist from day one so entrypoint/CRUD finders drop in without
// touching core. Populated by the codeanalyzer-extension-builder work, not this rung.

public sealed record AnalysisContext(string ApplicationId);

public sealed record AnalysisResult
{
    public IReadOnlyList<string> Entrypoints { get; init; } = Array.Empty<string>();
}

public abstract class AnalysisPass
{
    public abstract string Name { get; }
    public virtual IReadOnlyList<string> Provides => Array.Empty<string>();
    public virtual IReadOnlyList<string> Requires => Array.Empty<string>();
    public abstract AnalysisResult Run(Application app, AnalysisContext ctx);
}

public static class PassRegistry
{
    // discover → topo-order → run. Built-ins empty for now; the seam is real.
    public static IReadOnlyList<AnalysisPass> Discover() => Array.Empty<AnalysisPass>();

    public static Application RunPipeline(Application app)
    {
        var ctx = new AnalysisContext(app.Id);
        foreach (var pass in Discover())
            _ = pass.Run(app, ctx); // results merged here once passes exist
        return app;
    }
}
