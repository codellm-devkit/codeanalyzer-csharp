using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cansharp.Loading;

/// <summary>
/// Builds one CSharpCompilation per project (spec §4.3 / D2 — no MSBuild). Each project's
/// references come from its project.assets.json (AssetsReader): NuGet package dlls + the
/// framework reference pack. &lt;ProjectReference&gt;s are wired as CompilationReferences in
/// dependency order so a downstream project sees upstream symbols.
///
/// Graceful degradation (§4.3 failure mode): a project with no assets file falls back to the
/// host runtime's trusted-platform-assemblies, so analysis still runs (lower fidelity) rather
/// than failing.
/// </summary>
public sealed class CompilationBuilder
{
    public readonly record struct ParsedFile(SourceFile Source, SyntaxTree Tree, string Text, string AssemblyName);

    public sealed record Built(IReadOnlyList<ParsedFile> Files,
                               IReadOnlyDictionary<string, SemanticModel> ModelByFileKey);

    private readonly AssetsReader _assets = new();

    public Built Build(ProjectLoader.Loaded loaded)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        // Parse every file once; keep trees grouped per project.
        var treesByProject = new Dictionary<string, List<(ParsedFile pf, SyntaxTree tree)>>();
        var allParsed = new List<ParsedFile>();

        void ParseInto(string asmName, IEnumerable<SourceFile> files, string key)
        {
            var list = treesByProject.TryGetValue(key, out var ex) ? ex : (treesByProject[key] = new());
            foreach (var src in files)
            {
                var text = File.ReadAllText(src.AbsolutePath);
                var tree = CSharpSyntaxTree.ParseText(text, parseOptions, path: src.FileKey);
                var pf = new ParsedFile(src, tree, text, asmName);
                list.Add((pf, tree));
                allParsed.Add(pf);
            }
        }

        foreach (var proj in loaded.Projects)
            ParseInto(proj.AssemblyName, proj.Files, proj.CsprojAbsPath);
        if (loaded.Orphans.Count > 0)
            ParseInto("(orphans)", loaded.Orphans, "(orphans)");

        // Build compilations per project, two-pass so ProjectReferences can be wired.
        var compilationByKey = new Dictionary<string, CSharpCompilation>();

        CSharpCompilation BaseCompilation(string asmName, List<(ParsedFile pf, SyntaxTree tree)> trees,
                                           IEnumerable<MetadataReference> refs) =>
            CSharpCompilation.Create(asmName, trees.Select(t => t.tree),
                refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Pass 1: each project with its own metadata refs (no project-refs yet).
        foreach (var proj in loaded.Projects)
        {
            var refs = ResolveMetadataRefs(proj.CsprojAbsPath);
            compilationByKey[proj.CsprojAbsPath] =
                BaseCompilation(proj.AssemblyName, treesByProject[proj.CsprojAbsPath], refs);
        }
        if (treesByProject.ContainsKey("(orphans)"))
            compilationByKey["(orphans)"] =
                BaseCompilation("(orphans)", treesByProject["(orphans)"], HostTpaRefs());

        // Pass 2: add CompilationReferences for <ProjectReference> (dependency edges).
        foreach (var proj in loaded.Projects)
        {
            var refs = new List<MetadataReference>();
            foreach (var depPath in proj.ProjectReferenceCsprojPaths)
                if (compilationByKey.TryGetValue(depPath, out var depComp))
                    refs.Add(depComp.ToMetadataReference());
            if (refs.Count > 0)
                compilationByKey[proj.CsprojAbsPath] =
                    compilationByKey[proj.CsprojAbsPath].AddReferences(refs);
        }

        // Map each file key to its compilation's SemanticModel.
        var modelByFileKey = new Dictionary<string, SemanticModel>();
        foreach (var (key, trees) in treesByProject)
        {
            var comp = compilationByKey[key];
            foreach (var (pf, tree) in trees)
                modelByFileKey[pf.Source.FileKey] = comp.GetSemanticModel(tree);
        }

        return new Built(allParsed, modelByFileKey);
    }

    private IEnumerable<MetadataReference> ResolveMetadataRefs(string csprojPath)
    {
        var resolved = _assets.TryRead(csprojPath);
        if (resolved is null)
            return HostTpaRefs();   // §4.3 graceful degrade: no restore → host BCL only

        var refs = new List<MetadataReference>();
        foreach (var dll in resolved.FrameworkRefDllPaths.Concat(resolved.PackageDllPaths))
            TryAdd(refs, dll);

        // If assets produced nothing usable, still fall back so the project can parse+bind BCL.
        if (refs.Count == 0)
            return HostTpaRefs();
        return refs;
    }

    private static void TryAdd(List<MetadataReference> refs, string dllPath)
    {
        try { refs.Add(MetadataReference.CreateFromFile(dllPath)); } catch { /* skip unreadable */ }
    }

    private static IEnumerable<MetadataReference> HostTpaRefs()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrEmpty(tpa)) yield break;
        var sep = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var path in tpa.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            MetadataReference? r = null;
            try { r = MetadataReference.CreateFromFile(path); } catch { }
            if (r is not null) yield return r;
        }
    }
}
