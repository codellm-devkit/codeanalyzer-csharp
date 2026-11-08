namespace Cansharp.Ids;

/// <summary>
/// Resolves the two identity root segments per python-sdk#440, following the shipped
/// codeanalyzer-go precedent EXACTLY (no .NET-specific logic):
///   - application: --application, else the selected input-root basename.
///   - namespace:   --namespace, else the resolved application name.
///   - an OMITTED namespace is distinguished from an explicit empty one (the latter is rejected).
/// </summary>
public static class IdentityResolver
{
    public readonly record struct Identity(string Namespace, string Application);

    public static Identity Resolve(string inputRootPath, string? applicationFlag, string? namespaceFlag)
    {
        string application = Clean(applicationFlag) ?? InputRootBasename(inputRootPath);
        Reject(application, "application");

        // Omitted (null) namespace defaults to the application name.
        // Explicit-empty (non-null but blank) is a user error and is rejected.
        string ns;
        if (namespaceFlag is null)
        {
            ns = application;
        }
        else
        {
            ns = namespaceFlag;
            Reject(ns, "namespace");
        }

        return new Identity(ns, application);
    }

    private static string? Clean(string? v) => v is null ? null : v; // raw, case-sensitive, no trim

    private static string InputRootBasename(string inputRootPath)
    {
        var trimmed = inputRootPath.TrimEnd('/', '\\');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private static void Reject(string value, string which)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new IdentityException($"{which} name must not be empty or whitespace-only");
        if (value is "." or "..")
            throw new IdentityException($"{which} name must not be '.' or '..'");
        if (value.Contains('/') || value.Contains('\\'))
            throw new IdentityException($"{which} name must not contain '/' or '\\'");
        foreach (var ch in value)
        {
            if (char.IsControl(ch))
                throw new IdentityException($"{which} name must not contain control characters");
            if (char.IsSurrogate(ch) && !char.IsSurrogatePair(value, value.IndexOf(ch)))
                throw new IdentityException($"{which} name must not contain unpaired surrogates");
        }
    }
}

public sealed class IdentityException(string message) : Exception(message);
