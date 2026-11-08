namespace Cansharp.Loading;

/// <summary>
/// Discovers the C# source files under an input root and computes stable, repo-relative
/// POSIX file keys (the symbol_table keys). No MSBuild (spec D2/§4.3): a later step reads
/// obj/project.assets.json for references. This L1 build discovers sources and keys only.
/// </summary>
public sealed class ProjectLoader(string inputRoot, bool skipTests)
{
    private static readonly string[] SkipDirs = { "bin", "obj", "node_modules", ".git", ".vs" };

    public IReadOnlyList<SourceFile> DiscoverSources()
    {
        var root = Path.GetFullPath(inputRoot);
        var results = new List<SourceFile>();

        foreach (var abs in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, abs).Replace('\\', '/');
            if (ShouldSkip(rel)) continue;
            results.Add(new SourceFile(abs, rel));
        }

        // Deterministic ordering (ordinal) so output is byte-stable across runs.
        results.Sort((a, b) => string.CompareOrdinal(a.FileKey, b.FileKey));
        return results;
    }

    private bool ShouldSkip(string relPosix)
    {
        var segments = relPosix.Split('/');
        foreach (var seg in segments[..^1])
            if (SkipDirs.Contains(seg)) return true;

        if (skipTests)
        {
            // Heuristic test-tree skip; conservative (matches the reference analyzers' posture).
            foreach (var seg in segments)
            {
                var lower = seg.ToLowerInvariant();
                if (lower is "test" or "tests" || lower.EndsWith(".tests")) return true;
            }
        }
        return false;
    }
}

public readonly record struct SourceFile(string AbsolutePath, string FileKey);
