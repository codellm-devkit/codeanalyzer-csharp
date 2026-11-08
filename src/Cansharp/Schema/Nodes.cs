using System.Text.Json.Serialization;

namespace Cansharp.Schema;

// The canonical CLDK analysis schema v2 (analysis.json projection), L1 subset.
// See cldk-devtools keystone: canonical-schema.md. snake_case keys everywhere.
// A fact is present or absent — there is no null (except the sanctioned callee:null slot).
// All collections are omitted when empty via JsonIgnoreCondition.WhenWritingDefault/Null.

/// <summary>Source span: line:col to address/display, byte offsets to slice module.source (D15, UTF-8).</summary>
public sealed record Span
{
    [JsonPropertyName("start")] public required int[] Start { get; init; }   // [line, col]
    [JsonPropertyName("end")] public required int[] End { get; init; }       // [line, col]
    [JsonPropertyName("bytes")] public required int[] Bytes { get; init; }   // [from, to] UTF-8
}

/// <summary>A call site recorded in a callable's body at L1. callee stays null until L2.</summary>
public sealed record BodyNode
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }    // "call" at L1
    [JsonPropertyName("span")] public required Span Span { get; init; }

    // The ONE sanctioned refinement slot: null at L1, backfilled to an id at L2.
    [JsonPropertyName("callee")]
    [JsonInclude]
    public string? Callee { get; init; }   // intentionally emitted even when null

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Arguments { get; init; }
}

public sealed record Parameter
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("span")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Span? Span { get; init; }

    [JsonPropertyName("is_variadic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsVariadic { get; init; }

    // D-refkind: ref | out | in | ref_readonly. Absent = by value.
    [JsonPropertyName("ref_kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefKind { get; init; }
}

public sealed record Decorator
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Args { get; init; }
    [JsonPropertyName("span")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Span? Span { get; init; }
}

public sealed record FieldNode
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    // kind ∈ field | property | event (D-props)
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("modifiers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Modifiers { get; init; }
    [JsonPropertyName("span")] public required Span Span { get; init; }

    // D-props: compiler-synthesized auto-property/event backing.
    [JsonPropertyName("is_auto")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAuto { get; init; }
}

public sealed record Callable
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    // kind ∈ function | method | constructor | initializer | lambda
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("signature")] public required string Signature { get; init; }
    [JsonPropertyName("span")] public required Span Span { get; init; }

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Parameter>? Parameters { get; init; }

    [JsonPropertyName("return_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReturnType { get; init; }

    [JsonPropertyName("modifiers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Modifiers { get; init; }

    [JsonPropertyName("decorators")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Decorator>? Decorators { get; init; }

    // D-async: typed fields, not new kinds.
    [JsonPropertyName("is_async")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsAsync { get; init; }

    [JsonPropertyName("is_iterator")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsIterator { get; init; }

    // body holds ONLY call nodes at L1 (keyed by local id "line:col"), callee:null.
    [JsonPropertyName("body")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, BodyNode>? Body { get; init; }
}

public sealed record TypeNode
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    // D-records/D-delegate: kind ∈ class|struct|interface|enum|record|record_struct|delegate
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("span")] public required Span Span { get; init; }

    [JsonPropertyName("base_types")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? BaseTypes { get; init; }

    [JsonPropertyName("interfaces")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Interfaces { get; init; }

    [JsonPropertyName("modifiers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string>? Modifiers { get; init; }

    [JsonPropertyName("decorators")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Decorator>? Decorators { get; init; }

    // D-records: primary-constructor positional parameters.
    [JsonPropertyName("positional_parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Parameter>? PositionalParameters { get; init; }

    // D-delegate: the delegate's call shape.
    [JsonPropertyName("invoke_signature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InvokeSignature { get; init; }

    // D6: partial-type declaration sites; members declared elsewhere carry declared_in.
    [JsonPropertyName("parts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<TypePart>? Parts { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    [JsonPropertyName("callables")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, Callable>? Callables { get; init; }

    [JsonPropertyName("fields")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, FieldNode>? Fields { get; init; }
}

public sealed record TypePart
{
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("span")] public required Span Span { get; init; }
}

public sealed record Module
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("kind")] public string Kind => "module";
    [JsonPropertyName("span")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Span? Span { get; init; }

    [JsonPropertyName("namespace")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Namespace { get; init; }

    // D-identity note / D4: which project (assembly) this file belongs to.
    [JsonPropertyName("project")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Project { get; init; }

    [JsonPropertyName("source")] public required string Source { get; init; }

    [JsonPropertyName("imports")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<Import>? Imports { get; init; }

    [JsonPropertyName("types")] public Dictionary<string, TypeNode> Types { get; init; } = new();
    [JsonPropertyName("functions")] public Dictionary<string, Callable> Functions { get; init; } = new();

    [JsonPropertyName("content_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentHash { get; init; }
}

public sealed record Import
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    [JsonPropertyName("alias")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Alias { get; init; }
}
