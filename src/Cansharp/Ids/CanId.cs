using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cansharp.Ids;

/// <summary>
/// The ONE canonical-id builder. Every id — caller-side and callee-side — is produced here,
/// so a call_graph edge join is a byte-match rather than a fuzzy lookup.
///
/// Grammar (python-sdk#440, two-segment application-scoped root):
///   can://&lt;namespace&gt;/&lt;application&gt;/csharp/&lt;file&gt;/&lt;Type&gt;[/&lt;Nested&gt;]/&lt;member-signature&gt;
///
/// Root segments are case-sensitive raw strings; bytes outside ASCII letters/digits/-._~ are
/// percent-encoded as UPPERCASE escapes, no trimming, no Unicode normalization (shared with #435).
/// </summary>
public sealed class CanId
{
    private const string Scheme = "can://";
    private const string Lang = "csharp";

    private readonly string _rootPrefix;   // can://<ns>/<app>/csharp

    public string Namespace { get; }
    public string Application { get; }

    public CanId(string identityNamespace, string application)
    {
        Namespace = identityNamespace;
        Application = application;
        _rootPrefix = $"{Scheme}{Encode(identityNamespace)}/{Encode(application)}/{Lang}";
    }

    /// <summary>The application root id: can://&lt;ns&gt;/&lt;app&gt;/csharp</summary>
    public string ApplicationId => _rootPrefix;

    /// <summary>can://&lt;ns&gt;/&lt;app&gt;/csharp/&lt;file&gt; — file is repo-relative POSIX, never absolute/.. </summary>
    public string Module(string fileKey) => $"{_rootPrefix}/{fileKey}";

    /// <summary>A type id under its module. Nested types append further segments.</summary>
    public string Type(string moduleId, string typePathWithinModule) =>
        $"{moduleId}/{typePathWithinModule}";

    /// <summary>A member (callable/field) id: &lt;type-id&gt;/&lt;member-signature&gt;.</summary>
    public string Member(string typeId, string memberSignature) =>
        $"{typeId}/{memberSignature}";

    /// <summary>Ordinal id below the callable: &lt;callable-id&gt;@&lt;line&gt;:&lt;col&gt;.</summary>
    public static string BodyLocal(int line, int col) => $"{line}:{col}";

    // --- the single signatureOf(), used for both the id's last segment and the `signature` field ---

    private static readonly SymbolDisplayFormat SigFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.ExpandNullable);

    /// <summary>
    /// Produce the member signature segment, e.g. Save(System.String,System.Int32)System.Boolean,
    /// Map&lt;T&gt;(T)T, IFoo.Bar(). Accessors append #get/#set (handled by callers via suffix).
    /// </summary>
    public static string SignatureOf(IMethodSymbol method)
    {
        var sb = new StringBuilder();

        // Explicit interface implementations keep the qualifier (IFoo.Bar()).
        if (method.ExplicitInterfaceImplementations.Length > 0)
        {
            var impl = method.ExplicitInterfaceImplementations[0];
            sb.Append(impl.ContainingType.ToDisplayString(SigFormat)).Append('.');
        }

        sb.Append(MethodName(method));

        if (method.TypeParameters.Length > 0)
            sb.Append('<').Append(string.Join(",", method.TypeParameters.Select(tp => tp.Name))).Append('>');

        sb.Append('(');
        sb.Append(string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(SigFormat))));
        sb.Append(')');

        // Return type appended (constructors and void methods handled by MethodName/ReturnType).
        if (method.MethodKind != MethodKind.Constructor && !method.ReturnsVoid)
            sb.Append(method.ReturnType.ToDisplayString(SigFormat));

        return sb.ToString();
    }

    private static string MethodName(IMethodSymbol m) => m.MethodKind switch
    {
        MethodKind.Constructor => ".ctor",
        MethodKind.StaticConstructor => ".cctor",
        MethodKind.Destructor => "Finalize",
        _ => m.Name,
    };

    // --- #435 shared encoding: percent-encode bytes outside ASCII [A-Za-z0-9-._~], uppercase hex ---

    internal static string Encode(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var b in Encoding.UTF8.GetBytes(raw))
        {
            bool unreserved =
                (b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') || (b >= '0' && b <= '9') ||
                b == '-' || b == '.' || b == '_' || b == '~';
            if (unreserved) sb.Append((char)b);
            else sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
