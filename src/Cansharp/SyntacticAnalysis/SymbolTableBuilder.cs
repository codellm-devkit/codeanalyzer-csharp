using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Cansharp.Ids;
using Cansharp.Loading;
using Cansharp.Schema;

namespace Cansharp.SyntacticAnalysis;

/// <summary>
/// L1 symbol-table builder. Cohesive unit split by node kind (architecture rule 2):
/// BuildModule → AddType → AddCallable / AddField → AddCallSites. One SemanticModel-backed
/// context shared via fields. Encodes the locked C# decisions (records, delegates,
/// property/event → field + #get/#set, ref_kind, is_async/is_iterator).
/// </summary>
public sealed class SymbolTableBuilder(CanId canId)
{
    public Module BuildModule(CompilationBuilder.ParsedFile file, SemanticModel model, string assemblyName)
    {
        var spans = new SpanFactory(file.Text);
        var root = (CompilationUnitSyntax)file.Tree.GetRoot();
        var moduleId = canId.Module(file.Source.FileKey);

        var types = new Dictionary<string, TypeNode>();
        var functions = new Dictionary<string, Callable>();

        // Primary namespace of the file (first declared, else global). D10: a string field.
        string? ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>()
            .FirstOrDefault()?.Name.ToString();

        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            // Skip nested types here; AddType recurses into them.
            if (typeDecl.Ancestors().OfType<BaseTypeDeclarationSyntax>().Any()) continue;
            var (key, node) = AddType(typeDecl, model, moduleId, spans);
            if (node is not null) types[key] = node;
        }
        foreach (var delDecl in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
        {
            var (key, node) = AddDelegate(delDecl, model, moduleId, spans);
            if (node is not null) types[key] = node;
        }

        return new Module
        {
            Id = moduleId,
            Namespace = ns,
            Project = assemblyName,
            Source = file.Text,
            Imports = CollectImports(root),
            Types = types,
            Functions = functions, // empty for C# (every callable has a containing type)
            ContentHash = ContentHash(file.Text),
        };
    }

    // ---- types ----

    private (string key, TypeNode? node) AddType(
        BaseTypeDeclarationSyntax decl, SemanticModel model, string moduleId, SpanFactory spans)
    {
        if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) return (decl.Identifier.Text, null);

        string typeName = NameWithArity(sym);
        string typeId = canId.Type(moduleId, typeName);
        string kind = TypeKind(decl, sym);

        var callables = new Dictionary<string, Callable>();
        var fields = new Dictionary<string, FieldNode>();

        foreach (var member in decl.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            // Only direct members of THIS type.
            if (member.Parent != decl) continue;
            switch (member)
            {
                case MethodDeclarationSyntax m:
                    AddCallable(m, model, typeId, spans, callables); break;
                case ConstructorDeclarationSyntax c:
                    AddCallable(c, model, typeId, spans, callables); break;
                case PropertyDeclarationSyntax p:
                    AddProperty(p, model, typeId, spans, fields, callables); break;
                case EventDeclarationSyntax e:
                    AddEvent(e, model, typeId, spans, fields, callables); break;
                case FieldDeclarationSyntax f:
                    AddPlainField(f, model, typeId, spans, fields); break;
            }
        }

        // D-records: primary-constructor positional parameters.
        List<Parameter>? positional = null;
        if (decl is RecordDeclarationSyntax { ParameterList: { } pl })
            positional = pl.Parameters.Select(pp => ToParameter(pp, model, spans)).ToList();

        var node = new TypeNode
        {
            Id = typeId,
            Kind = kind,
            Span = spans.From(decl),
            BaseTypes = BaseTypeIds(sym),
            Interfaces = InterfaceIds(sym),
            Modifiers = Modifiers(decl.Modifiers),
            Decorators = Attributes(decl.AttributeLists, spans),
            PositionalParameters = positional,
            Namespace = sym.ContainingNamespace?.ToDisplayString(),
            Callables = callables.Count > 0 ? callables : null,
            Fields = fields.Count > 0 ? fields : null,
        };
        return (typeName, node);
    }

    // D-delegate: a named function-type declared at type scope.
    private (string key, TypeNode? node) AddDelegate(
        DelegateDeclarationSyntax decl, SemanticModel model, string moduleId, SpanFactory spans)
    {
        if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol sym) return (decl.Identifier.Text, null);
        string name = NameWithArity(sym);
        string ret = decl.ReturnType.ToString();
        string invoke = $"({string.Join(",", decl.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "?"))})->{ret}";
        var node = new TypeNode
        {
            Id = canId.Type(moduleId, name),
            Kind = "delegate",
            Span = spans.From(decl),
            Modifiers = Modifiers(decl.Modifiers),
            InvokeSignature = invoke,
            Namespace = sym.ContainingNamespace?.ToDisplayString(),
        };
        return (name, node);
    }

    private static string TypeKind(BaseTypeDeclarationSyntax decl, INamedTypeSymbol sym) => decl switch
    {
        // D-records: record / record_struct as first-class kinds.
        RecordDeclarationSyntax r =>
            r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record_struct" : "record",
        StructDeclarationSyntax => "struct",
        InterfaceDeclarationSyntax => "interface",
        EnumDeclarationSyntax => "enum",
        _ => "class",
    };

    // ---- callables ----

    private void AddCallable(
        BaseMethodDeclarationSyntax decl, SemanticModel model, string typeId,
        SpanFactory spans, Dictionary<string, Callable> callables)
    {
        if (model.GetDeclaredSymbol(decl) is not IMethodSymbol sym) return;
        string sig = CanId.SignatureOf(sym);
        callables[sig] = BuildCallable(sym, decl, decl.Body, decl.ExpressionBody, typeId, sig, spans, model);
    }

    private Callable BuildCallable(
        IMethodSymbol sym, SyntaxNode decl, BlockSyntax? body, ArrowExpressionClauseSyntax? arrow,
        string typeId, string sig, SpanFactory spans, SemanticModel model)
    {
        bool isIterator = body is not null && body.DescendantNodes()
            .OfType<YieldStatementSyntax>().Any();

        return new Callable
        {
            Id = canId.Member(typeId, sig),
            Kind = sym.MethodKind == MethodKind.Constructor ? "constructor" : "method",
            Signature = sig,
            Span = spans.From(decl),
            Parameters = sym.Parameters.Select(p => ToParameter(p)).ToList() is { Count: > 0 } ps ? ps : null,
            ReturnType = sym.MethodKind == MethodKind.Constructor ? null : sym.ReturnType.ToDisplayString(),
            Modifiers = Modifiers((decl as BaseMethodDeclarationSyntax)?.Modifiers ?? default),
            IsAsync = sym.IsAsync,
            IsIterator = isIterator,
            Body = BuildCallSites(body, arrow, spans),
        };
    }

    // ---- fields / properties / events (D-props) ----

    private void AddProperty(
        PropertyDeclarationSyntax p, SemanticModel model, string typeId, SpanFactory spans,
        Dictionary<string, FieldNode> fields, Dictionary<string, Callable> callables)
    {
        if (model.GetDeclaredSymbol(p) is not IPropertySymbol sym) return;

        // An expression-bodied property (`public string P => expr;`) is a getter with a body —
        // NOT an auto-property. Auto = has an AccessorList whose accessors all have no body.
        bool isExpressionBodied = p.ExpressionBody is not null;
        bool isAuto = !isExpressionBodied
            && (p.AccessorList?.Accessors.All(a => a.Body is null && a.ExpressionBody is null) ?? false);

        fields[sym.Name] = new FieldNode
        {
            Id = canId.Member(typeId, sym.Name),
            Kind = "property",
            Type = sym.Type.ToDisplayString(),
            Modifiers = Modifiers(p.Modifiers),
            Span = spans.From(p),
            IsAuto = isAuto,
        };

        // Non-auto accessors ALSO appear as callables keyed Name#get / Name#set (D-props).
        // Recorded as a divergence from TS getter|setter kinds in SCHEMA_DECISIONS.md.
        if (isAuto) return;

        if (isExpressionBodied)
        {
            // `=> expr` is the getter.
            string asig = sym.Name + "#get";
            callables[asig] = new Callable
            {
                Id = canId.Member(typeId, asig),
                Kind = "method",
                Signature = asig,
                Span = spans.From(p.ExpressionBody!),
                ReturnType = sym.Type.ToDisplayString(),
                Body = BuildCallSites(null, p.ExpressionBody, spans),
            };
            return;
        }

        foreach (var acc in p.AccessorList?.Accessors ?? default)
        {
            string suffix = acc.Keyword.ValueText switch { "get" => "#get", "set" or "init" => "#set", _ => null! };
            if (suffix is null) continue;
            string asig = sym.Name + suffix;
            callables[asig] = new Callable
            {
                Id = canId.Member(typeId, asig),
                Kind = "method",
                Signature = asig,
                Span = spans.From(acc),
                ReturnType = suffix == "#get" ? sym.Type.ToDisplayString() : "System.Void",
                Body = BuildCallSites(acc.Body, acc.ExpressionBody, spans),
            };
        }
    }

    private void AddEvent(
        EventDeclarationSyntax e, SemanticModel model, string typeId, SpanFactory spans,
        Dictionary<string, FieldNode> fields, Dictionary<string, Callable> callables)
    {
        if (model.GetDeclaredSymbol(e) is not IEventSymbol sym) return;
        fields[sym.Name] = new FieldNode
        {
            Id = canId.Member(typeId, sym.Name),
            Kind = "event",
            Type = sym.Type.ToDisplayString(),
            Modifiers = Modifiers(e.Modifiers),
            Span = spans.From(e),
            IsAuto = false,
        };
    }

    private void AddPlainField(
        FieldDeclarationSyntax f, SemanticModel model, string typeId, SpanFactory spans,
        Dictionary<string, FieldNode> fields)
    {
        foreach (var v in f.Declaration.Variables)
        {
            if (model.GetDeclaredSymbol(v) is not IFieldSymbol sym) continue;
            fields[sym.Name] = new FieldNode
            {
                Id = canId.Member(typeId, sym.Name),
                Kind = "field",
                Type = sym.Type.ToDisplayString(),
                Modifiers = Modifiers(f.Modifiers),
                Span = spans.From(v),
            };
        }
    }

    // ---- call sites (L1 body: call nodes only, callee:null) ----

    private static Dictionary<string, BodyNode>? BuildCallSites(
        BlockSyntax? body, ArrowExpressionClauseSyntax? arrow, SpanFactory spans)
    {
        SyntaxNode? scope = (SyntaxNode?)body ?? arrow;
        if (scope is null) return null;

        var result = new Dictionary<string, BodyNode>();
        var seenAtPos = new Dictionary<string, int>();

        foreach (var inv in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var loc = inv.GetLocation().GetLineSpan().StartLinePosition;
            string baseKey = CanId.BodyLocal(loc.Line + 1, loc.Character + 1);
            // Chained calls on one line disambiguate with /k (D20 precedent).
            string key = baseKey;
            if (seenAtPos.TryGetValue(baseKey, out int k))
            {
                k++; seenAtPos[baseKey] = k; key = $"{baseKey}/{k}";
            }
            else seenAtPos[baseKey] = 0;

            result[key] = new BodyNode
            {
                Kind = "call",
                Span = spans.From(inv),
                Callee = null,                 // the one refinement slot; backfilled at L2
                Arguments = null,
            };
        }
        return result.Count > 0 ? result : null;
    }

    // ---- shared helpers ----

    private Parameter ToParameter(IParameterSymbol p) => new()
    {
        Name = p.Name,
        Type = p.Type.ToDisplayString(),
        IsVariadic = p.IsParams,
        RefKind = RefKindOf(p.RefKind),
    };

    private Parameter ToParameter(ParameterSyntax p, SemanticModel model, SpanFactory spans)
    {
        var sym = model.GetDeclaredSymbol(p);
        return new Parameter
        {
            Name = p.Identifier.Text,
            Type = sym?.Type.ToDisplayString() ?? p.Type?.ToString() ?? "?",
            Span = spans.From(p),
            RefKind = sym is not null ? RefKindOf(sym.RefKind) : null,
        };
    }

    // D-refkind: ref | out | in | ref_readonly; absent (null) = by value.
    private static string? RefKindOf(RefKind rk) => rk switch
    {
        RefKind.Ref => "ref",
        RefKind.Out => "out",
        RefKind.In => "in",
        RefKind.RefReadOnlyParameter => "ref_readonly",
        _ => null,
    };

    private static List<string>? Modifiers(SyntaxTokenList mods) =>
        mods.Count > 0 ? mods.Select(m => m.ValueText).ToList() : null;

    private static List<Decorator>? Attributes(SyntaxList<AttributeListSyntax> lists, SpanFactory spans)
    {
        var all = lists.SelectMany(l => l.Attributes).Select(a => new Decorator
        {
            Name = a.Name.ToString(),
            Args = a.ArgumentList?.Arguments.Select(ar => ar.ToString()).ToList(),
            Span = spans.From(a),
        }).ToList();
        return all.Count > 0 ? all : null;
    }

    private static List<Import>? CollectImports(CompilationUnitSyntax root)
    {
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Select(u => new Import
        {
            Name = u.Name?.ToString() ?? u.NamespaceOrType?.ToString() ?? "",
            Alias = u.Alias?.Name.Identifier.Text,
        }).Where(i => i.Name.Length > 0).ToList();
        return usings.Count > 0 ? usings : null;
    }

    private static List<string>? BaseTypeIds(INamedTypeSymbol sym)
    {
        if (sym.BaseType is { SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType } bt)
            return new List<string> { bt.ToDisplayString() };
        return null;
    }

    private static List<string>? InterfaceIds(INamedTypeSymbol sym) =>
        sym.Interfaces.Length > 0 ? sym.Interfaces.Select(i => i.ToDisplayString()).ToList() : null;

    private static string NameWithArity(INamedTypeSymbol sym) =>
        sym.TypeParameters.Length > 0 ? $"{sym.Name}`{sym.TypeParameters.Length}" : sym.Name;

    private static string ContentHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(bytes)[..16];
    }
}
