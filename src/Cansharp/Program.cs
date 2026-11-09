using Cansharp.Core;
using Cansharp.Emit;
using Cansharp.Emit.Neo4j;
using Cansharp.Ids;
using Cansharp.Options;

// CLI entry (architecture rule: thin). Honors the CLI contract:
//   stdout = analysis JSON ONLY; stderr = all diagnostics; non-zero exit on flag errors.
const string Version = "0.1.0";

try
{
    var opts = ParseArgs(args, out bool showHelp);
    if (showHelp) { Console.Error.WriteLine(HelpText()); return 0; }

    if (opts.Emit == EmitTarget.Schema)
    {
        // --emit schema needs no -i; emits the static Neo4j schema contract (stub for L1).
        Console.WriteLine("{\"schema\":\"neo4j\",\"language\":\"csharp\",\"prefix\":\"Cs\"}");
        return 0;
    }

    if (opts.Input is null)
    {
        Console.Error.WriteLine("error: --input (-i) is required");
        return 2;
    }

    var analysis = new Analyzer(Version).Analyze(opts);

    if (opts.Emit == EmitTarget.Neo4j)
    {
        var cypher = Neo4jProjector.ProjectCypher(analysis);
        WriteOut(opts.Output, "graph.cypher", cypher);
        return 0;
    }

    // JSON: pretty to a file, compact to stdout.
    bool toStdout = opts.Output is null;
    var json = JsonEmitter.Serialize(analysis, indented: !toStdout);
    if (toStdout) Console.Out.Write(json);
    else WriteOut(opts.Output, "analysis.json", json);
    return 0;
}
catch (IdentityException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"fatal: {ex.Message}");
    return 1;
}

static void WriteOut(string? dir, string fileName, string content)
{
    Directory.CreateDirectory(dir!);
    File.WriteAllText(Path.Combine(dir!, fileName), content);
}

static AnalysisOptions ParseArgs(string[] args, out bool showHelp)
{
    showHelp = false;
    string? input = null, output = null, app = null, ns = null;
    int level = 1, verbosity = 0;
    var emit = EmitTarget.Json;
    bool skipTests = true;

    for (int i = 0; i < args.Length; i++)
    {
        string a = args[i];
        string? Next() => i + 1 < args.Length ? args[++i] : null;
        switch (a)
        {
            case "-h" or "--help": showHelp = true; break;
            case "-i" or "--input": input = Next(); break;
            case "-o" or "--output": output = Next(); break;
            case "-a" or "--analysis-level": level = int.Parse(Next() ?? "1"); break;
            case "--application": app = Next(); break;
            case "--app-name": app = Next(); break;        // deprecated alias
            case "--namespace": ns = Next(); break;
            case "--emit":
                emit = (Next() ?? "json").ToLowerInvariant() switch
                {
                    "json" => EmitTarget.Json,
                    "neo4j" => EmitTarget.Neo4j,
                    "schema" => EmitTarget.Schema,
                    var v => throw new IdentityException($"unknown --emit value '{v}'"),
                };
                break;
            case "--skip-tests": skipTests = true; break;
            case "--include-tests": skipTests = false; break;
            case "-v": verbosity++; break;
            default:
                throw new IdentityException($"unknown flag '{a}'");
        }
    }

    if (level is < 1 or > 4)
        throw new IdentityException($"--analysis-level must be 1..4 (got {level})");
    if (level > 1)
        throw new IdentityException($"analysis level {level} is not yet implemented; this build supports -a 1");

    return new AnalysisOptions
    {
        Input = input, Output = output, AnalysisLevel = level, Emit = emit,
        Application = app, Namespace = ns, SkipTests = skipTests, Verbosity = verbosity,
    };
}

static string HelpText() =>
    "cansharp — codeanalyzer-csharp (C# → canonical CLDK analysis.json)\n" +
    "usage: cansharp -i <path> [-o <dir>] [-a 1] [--emit json|neo4j|schema]\n" +
    "               [--application <name>] [--namespace <name>] [--include-tests]\n";
