using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cansharp.Loading;

/// <summary>
/// Builds a CSharpCompilation over the discovered sources so L1 has a SemanticModel for
/// symbol/type resolution. Spec D2: no MSBuild. For this L1 increment references come from
/// the running runtime's trusted-platform-assemblies (sufficient to bind BCL types for the
/// symbol table); reading obj/project.assets.json for full package references is the §4.3
/// enrichment and lands with the L2 resolver work.
/// </summary>
public sealed class CompilationBuilder
{
    public sealed record Built(CSharpCompilation Compilation, IReadOnlyList<ParsedFile> Files);

    public readonly record struct ParsedFile(SourceFile Source, SyntaxTree Tree, string Text);

    public Built Build(string assemblyName, IReadOnlyList<SourceFile> sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var parsed = new List<ParsedFile>(sources.Count);
        var trees = new List<SyntaxTree>(sources.Count);

        foreach (var src in sources)
        {
            // Read as UTF-8; the raw text becomes module.source and the byte-offset basis (D15).
            var text = File.ReadAllText(src.AbsolutePath);
            var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path: src.FileKey);
            parsed.Add(new ParsedFile(src, tree, text));
            trees.Add(tree);
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName,
            syntaxTrees: trees,
            references: ResolveReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return new Built(compilation, parsed);
    }

    private static IEnumerable<MetadataReference> ResolveReferences()
    {
        // Trusted platform assemblies of the host runtime — gives SemanticModel the BCL.
        // Degrades gracefully (§4.3): if unavailable, the compilation still parses and L1
        // emits a syntax-grounded tree with best-effort type names.
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpa)) yield break;

        var sep = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var path in tpa.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            MetadataReference? r = null;
            try { r = MetadataReference.CreateFromFile(path); } catch { /* skip unreadable */ }
            if (r is not null) yield return r;
        }
    }
}
