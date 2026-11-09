using System.Text.RegularExpressions;

namespace Cansharp.Loading;

/// <summary>
/// Discovers C# projects and source files under an input root and computes stable,
/// repo-relative POSIX file keys (the symbol_table keys). No MSBuild (spec D2/§4.3): each
/// project's references come from its obj/project.assets.json (AssetsReader). Source files are
/// assigned to the nearest enclosing .csproj so each can be compiled with its own references.
/// </summary>
public sealed partial class ProjectLoader(string inputRoot, bool skipTests)
{
    private static readonly string[] SkipDirs = { "bin", "obj", "node_modules", ".git", ".vs" };

    public sealed record Project(string CsprojAbsPath, string AssemblyName, List<SourceFile> Files)
    {
        public List<string> ProjectReferenceCsprojPaths { get; } = new();
    }

    public sealed record Loaded(string Root, IReadOnlyList<Project> Projects, IReadOnlyList<SourceFile> Orphans);

    public Loaded Load()
    {
        var root = Path.GetFullPath(inputRoot);

        // 1. discover projects
        var csprojs = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsUnderSkipDir(Path.GetRelativePath(root, p).Replace('\\', '/')))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var projects = csprojs.Select(p => new Project(
            CsprojAbsPath: p,
            AssemblyName: Path.GetFileNameWithoutExtension(p),
            Files: new List<SourceFile>())).ToList();

        // longest project-dir prefix wins when assigning a source file to a project
        var byDir = projects
            .Select(p => (dir: Path.GetDirectoryName(p.CsprojAbsPath)! + Path.DirectorySeparatorChar, proj: p))
            .OrderByDescending(x => x.dir.Length)
            .ToList();

        // 2. discover sources, assign to owning project (or orphan)
        var orphans = new List<SourceFile>();
        foreach (var abs in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, abs).Replace('\\', '/');
            if (ShouldSkip(rel)) continue;
            var sf = new SourceFile(abs, rel);
            var owner = byDir.FirstOrDefault(x => abs.StartsWith(x.dir, StringComparison.Ordinal)).proj;
            if (owner is not null) owner.Files.Add(sf);
            else orphans.Add(sf);
        }

        // 3. parse <ProjectReference> from each csproj (dependency wiring for CompilationBuilder)
        foreach (var proj in projects)
            ReadProjectReferences(proj);

        foreach (var proj in projects)
            proj.Files.Sort((a, b) => string.CompareOrdinal(a.FileKey, b.FileKey));
        orphans.Sort((a, b) => string.CompareOrdinal(a.FileKey, b.FileKey));

        return new Loaded(root, projects, orphans);
    }

    private void ReadProjectReferences(Project proj)
    {
        string xml;
        try { xml = File.ReadAllText(proj.CsprojAbsPath); } catch { return; }
        var dir = Path.GetDirectoryName(proj.CsprojAbsPath)!;
        foreach (Match m in ProjectRefRegex().Matches(xml))
        {
            var relInclude = m.Groups["inc"].Value.Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(dir, relInclude));
            if (File.Exists(full)) proj.ProjectReferenceCsprojPaths.Add(full);
        }
    }

    private bool ShouldSkip(string relPosix)
    {
        if (IsUnderSkipDir(relPosix)) return true;
        if (skipTests)
        {
            foreach (var seg in relPosix.Split('/'))
            {
                var lower = seg.ToLowerInvariant();
                if (lower is "test" or "tests" || lower.EndsWith(".tests")) return true;
            }
        }
        return false;
    }

    private static bool IsUnderSkipDir(string relPosix)
    {
        var segs = relPosix.Split('/');
        foreach (var seg in segs[..^1])
            if (SkipDirs.Contains(seg)) return true;
        return false;
    }

    [GeneratedRegex("<ProjectReference\\s+Include\\s*=\\s*\"(?<inc>[^\"]+)\"")]
    private static partial Regex ProjectRefRegex();
}

public readonly record struct SourceFile(string AbsolutePath, string FileKey);
