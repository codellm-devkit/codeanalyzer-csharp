using Cansharp.Analysis;
using Cansharp.Ids;
using Cansharp.Loading;
using Cansharp.Options;
using Cansharp.Schema;
using Cansharp.SyntacticAnalysis;

namespace Cansharp.Core;

/// <summary>
/// ORCHESTRATOR ONLY (architecture rule 1). Short, pure delegation: resolve identity →
/// discover sources → build compilation → build symbol table (L1) → run pass pipeline →
/// assemble the Analysis envelope. No per-node parsing inlined here.
/// </summary>
public sealed class Analyzer(string version)
{
    public AnalysisDocument Analyze(AnalysisOptions opts)
    {
        if (opts.Input is null)
            throw new IdentityException("input (-i) is required");

        var identity = IdentityResolver.Resolve(opts.Input, opts.Application, opts.Namespace);
        var canId = new CanId(identity.Namespace, identity.Application);

        var loaded = new ProjectLoader(opts.Input, opts.SkipTests).Load();
        var built = new CompilationBuilder().Build(loaded);
        var builder = new SymbolTableBuilder(canId);

        var symbolTable = new Dictionary<string, Module>();
        foreach (var file in built.Files)
        {
            var model = built.ModelByFileKey[file.Source.FileKey];
            // Per-project assembly name on the module (spec D4: `project` field).
            var module = builder.BuildModule(file, model, file.AssemblyName);
            symbolTable[file.Source.FileKey] = module;
        }

        var app = new Application
        {
            Id = canId.ApplicationId,
            Name = identity.Application,
            ApplicationName = identity.Application,
            IdentityNamespace = identity.Namespace,
            SymbolTable = symbolTable,
        };

        app = PassRegistry.RunPipeline(app); // seam: no-op until finders exist

        return new AnalysisDocument
        {
            MaxLevel = 1, // this build implements L1 only
            Analyzer = new AnalyzerInfo { Version = version },
            Application = app,
        };
    }
}
