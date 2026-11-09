using System.Text.Json;

namespace Cansharp.Loading;

/// <summary>
/// Reads obj/project.assets.json (written by `dotnet restore`) to resolve a project's
/// metadata references without invoking MSBuild (spec D2 / §4.3). Resolves:
///   * NuGet package dlls — from targets[tfm][id/ver].compile against packageFolders (§4.3);
///   * the framework reference pack — NOT in targets (per #40's §4.3 correction); located via
///     the net8.0 *.Ref download packages in the NuGet folder, keyed off runtimeIdentifierGraphPath.
/// Project-to-project references (targets entries with type "project") are returned separately
/// so CompilationBuilder can wire them as CompilationReferences in dependency order.
/// </summary>
public sealed class AssetsReader
{
    public sealed record Resolved(
        string Tfm,
        IReadOnlyList<string> PackageDllPaths,
        IReadOnlyList<string> FrameworkRefDllPaths,
        IReadOnlyList<string> ProjectRefPaths);   // absolute .csproj paths of <ProjectReference>s

    /// <summary>Returns null (graceful degrade, §4.3 failure mode) if no assets file exists.</summary>
    public Resolved? TryRead(string csprojAbsPath)
    {
        var objDir = Path.Combine(Path.GetDirectoryName(csprojAbsPath)!, "obj");
        var assetsPath = Path.Combine(objDir, "project.assets.json");
        if (!File.Exists(assetsPath)) return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = doc.RootElement;

        var packageFolders = root.TryGetProperty("packageFolders", out var pf)
            ? pf.EnumerateObject().Select(p => p.Name).ToList()
            : new List<string>();

        if (!root.TryGetProperty("targets", out var targets) || targets.EnumerateObject().MoveNext() == false)
            return new Resolved("", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var firstTarget = targets.EnumerateObject().First();
        string tfm = firstTarget.Name;

        var packageDlls = new List<string>();
        var projectRefs = new List<string>();

        foreach (var entry in firstTarget.Value.EnumerateObject())
        {
            // entry.Name is "Id/Version"
            var slash = entry.Name.IndexOf('/');
            if (slash < 0) continue;
            string id = entry.Name[..slash];
            string ver = entry.Name[(slash + 1)..];
            string type = entry.Value.TryGetProperty("type", out var t) ? (t.GetString() ?? "") : "";

            if (type == "project")
            {
                // Resolved later from the .csproj's own <ProjectReference> list; recorded for completeness.
                continue;
            }

            if (!entry.Value.TryGetProperty("compile", out var compile)) continue;
            foreach (var rel in compile.EnumerateObject())
            {
                // skip the "_._" placeholder (ref-only, no real dll)
                if (rel.Name.EndsWith("_._")) continue;
                foreach (var folder in packageFolders)
                {
                    var full = Path.Combine(folder, id.ToLowerInvariant(), ver,
                        rel.Name.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(full)) { packageDlls.Add(full); break; }
                }
            }
        }

        var frameworkRefs = ResolveFrameworkRefPacks(root, packageFolders, tfm);

        return new Resolved(tfm, packageDlls, frameworkRefs, projectRefs);
    }

    /// <summary>
    /// The framework pack is not in targets (#40 §4.3 correction). Its reference assemblies are
    /// delivered by the *.Ref download packages recorded under the framework's
    /// downloadDependencies; locate them in the NuGet folder for the project's TFM.
    /// </summary>
    private static IReadOnlyList<string> ResolveFrameworkRefPacks(
        JsonElement root, IReadOnlyList<string> packageFolders, string tfm)
    {
        var dlls = new List<string>();
        if (!root.TryGetProperty("project", out var project)
            || !project.TryGetProperty("frameworks", out var frameworks)) return dlls;

        foreach (var fw in frameworks.EnumerateObject())
        {
            if (!fw.Value.TryGetProperty("downloadDependencies", out var dd)) continue;
            foreach (var dep in dd.EnumerateArray())
            {
                string name = dep.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                string verRange = dep.TryGetProperty("version", out var v) ? (v.GetString() ?? "") : "";
                if (!name.EndsWith(".Ref")) continue;
                string ver = verRange.Trim('[', ']').Split(',')[0].Trim();

                foreach (var folder in packageFolders)
                {
                    // <root>/<name-lower>/<ver>/ref/<tfm-moniker>/*.dll
                    var baseDir = Path.Combine(folder, name.ToLowerInvariant(), ver, "ref");
                    if (!Directory.Exists(baseDir)) continue;
                    // pick the ref dir matching the project tfm (e.g. net8.0), else the first.
                    var tfmMoniker = TfmMoniker(tfm);
                    var refDir = Path.Combine(baseDir, tfmMoniker);
                    if (!Directory.Exists(refDir))
                        refDir = Directory.GetDirectories(baseDir).FirstOrDefault() ?? "";
                    if (refDir.Length > 0 && Directory.Exists(refDir))
                        dlls.AddRange(Directory.GetFiles(refDir, "*.dll"));
                }
            }
        }
        return dlls;
    }

    // ".NETCoreApp,Version=v8.0" or "net8.0" → "net8.0"
    private static string TfmMoniker(string tfm)
    {
        if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tfm.Contains('.') && !tfm.Contains(','))
            return tfm;
        // parse ".NETCoreApp,Version=v8.0"
        var idx = tfm.IndexOf("Version=v", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            var ver = tfm[(idx + "Version=v".Length)..].TrimEnd();
            return "net" + ver;
        }
        return tfm;
    }
}
