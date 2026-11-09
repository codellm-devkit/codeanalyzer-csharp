using System.Text;
using Cansharp.Core;
using Cansharp.Options;
using Cansharp.Schema;
using Cansharp.SyntacticAnalysis;
using Xunit;

namespace Cansharp.Tests;

public sealed class L1Tests : IDisposable
{
    private readonly string _dir;

    public L1Tests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cansharp-fx-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(_dir, name), content);
    private AnalysisDocument Analyze(string? app = null, string? ns = null) =>
        new Analyzer("0.1.0").Analyze(new AnalysisOptions { Input = _dir, Application = app, Namespace = ns });

    [Fact]
    public void Envelope_carries_identity_version_2_and_schema_version()
    {
        Write("A.cs", "namespace N; public class A { }");
        var d = Analyze();
        Assert.Equal("2.0.0", d.SchemaVersion);
        Assert.Equal(2, d.IdentityVersion);           // #440 marker
        Assert.Equal("csharp", d.Language);
        Assert.Equal(1, d.MaxLevel);
    }

    [Fact]
    public void Identity_root_is_two_segments_before_language_token()
    {
        Write("A.cs", "namespace N; public class A { }");
        var d = Analyze(app: "payments", ns: "online-boutique");
        Assert.Equal("can://online-boutique/payments/csharp", d.Application.Id);
        Assert.Equal("payments", d.Application.ApplicationName);
        Assert.Equal("online-boutique", d.Application.IdentityNamespace);
    }

    [Fact]
    public void Go_precedent_default_namespace_equals_application_equals_basename()
    {
        Write("A.cs", "public class A { }");
        var d = Analyze();                              // no flags
        var basename = Path.GetFileName(_dir);
        Assert.Equal($"can://{basename}/{basename}/csharp", d.Application.Id);
    }

    [Fact]
    public void Symbol_table_keys_are_relative_posix_never_absolute_or_dotdot()
    {
        Write("A.cs", "public class A { }");
        var d = Analyze();
        foreach (var key in d.Application.SymbolTable.Keys)
        {
            Assert.False(Path.IsPathRooted(key), $"key is absolute: {key}");
            Assert.DoesNotContain("..", key);
            Assert.DoesNotContain('\\', key);
        }
    }

    [Fact]
    public void Record_and_delegate_get_first_class_kinds()
    {
        Write("A.cs", """
            namespace N;
            public record Money(decimal Amount, string Currency);
            public record struct Point(int X, int Y);
            public delegate bool Predicate(int value);
            """);
        var types = Analyze().Application.SymbolTable["A.cs"].Types;
        Assert.Equal("record", types["Money"].Kind);
        Assert.Equal("record_struct", types["Point"].Kind);
        Assert.Equal("delegate", types["Predicate"].Kind);
        Assert.NotNull(types["Predicate"].InvokeSignature);
        Assert.Equal(2, types["Money"].PositionalParameters!.Count);
    }

    [Fact]
    public void Out_parameter_carries_ref_kind()
    {
        Write("A.cs", "public class A { public bool Try(string s, out int v) { v = 0; return true; } }");
        var c = Analyze().Application.SymbolTable["A.cs"].Types["A"].Callables!.Values.First();
        var outParam = c.Parameters!.Single(p => p.Name == "v");
        Assert.Equal("out", outParam.RefKind);
    }

    [Fact]
    public void Async_and_iterator_are_typed_fields()
    {
        Write("A.cs", """
            using System.Collections.Generic;
            using System.Threading.Tasks;
            public class A {
                public async Task M() { await Task.Yield(); }
                public IEnumerable<int> Seq() { yield return 1; }
            }
            """);
        var callables = Analyze().Application.SymbolTable["A.cs"].Types["A"].Callables!;
        Assert.True(callables.Values.Single(c => c.Signature.StartsWith("M(")).IsAsync);
        Assert.True(callables.Values.Single(c => c.Signature.StartsWith("Seq(")).IsIterator);
    }

    [Fact]
    public void Property_emits_field_node_plus_get_set_accessor_callables()
    {
        Write("A.cs", """
            public class A {
                private int _n;
                public int N { get { return _n; } set { _n = value; } }
                public int M => _n;
            }
            """);
        var t = Analyze().Application.SymbolTable["A.cs"].Types["A"];
        Assert.Equal("property", t.Fields!["N"].Kind);
        Assert.Equal("property", t.Fields!["M"].Kind);
        Assert.Contains("N#get", t.Callables!.Keys);
        Assert.Contains("N#set", t.Callables!.Keys);
        Assert.Contains("M#get", t.Callables!.Keys);   // expression-bodied property is a getter
    }

    [Fact]
    public void Top_level_statements_synthesize_a_Program_type_with_Main_callable()
    {
        // D18: a top-level-statement file (no explicit class) must not vanish — it synthesizes
        // Program/<Main>$ holding the call sites. This is the microservices Program.cs case.
        Write("Program.cs", """
            using System;
            Console.WriteLine("boot");
            var x = Compute(2);
            int Compute(int n) => n * 2;
            """);
        var t = Analyze().Application.SymbolTable["Program.cs"].Types;
        Assert.Contains("Program", t.Keys);
        var prog = t["Program"];
        Assert.Equal("class", prog.Kind);
        var main = prog.Callables!.Values.Single(c => c.Signature.StartsWith("<Main>$"));
        Assert.NotNull(main.Body);                       // the top-level call sites are captured
        Assert.All(main.Body!.Values, bn => Assert.Null(bn.Callee));
    }

    [Fact]
    public void Call_sites_recorded_at_L1_with_null_callee()
    {
        Write("A.cs", "public class A { int X() => 1; int Y() { return X(); } }");
        var callables = Analyze().Application.SymbolTable["A.cs"].Types["A"].Callables!;
        var y = callables.Values.Single(c => c.Signature.StartsWith("Y("));
        Assert.NotNull(y.Body);
        Assert.All(y.Body!.Values, bn => { Assert.Equal("call", bn.Kind); Assert.Null(bn.Callee); });
    }

    [Fact]
    public void Byte_slice_guard_non_ascii_source_slices_correctly()
    {
        // A non-ASCII identifier and string: char and byte offsets DISAGREE here (the D15 guard).
        Write("A.cs", "public class Café { public string Grüße() => \"héllo\"; }");
        var module = Analyze().Application.SymbolTable["A.cs"];
        var callable = module.Types["Café"].Callables!.Values.First();
        // module.source[span.bytes] must equal the exact declaration text.
        var slice = SpanFactory.Slice(module.Source, callable.Span);
        Assert.Contains("Grüße", slice);
        Assert.Contains("héllo", slice);
    }

    [Fact]
    public void Identity_ids_are_stable_across_two_runs()
    {
        Write("A.cs", "namespace N; public class A { public int F(int x) => x; }");
        var ids1 = CollectCallableIds(Analyze());
        var ids2 = CollectCallableIds(Analyze());
        Assert.Equal(ids1, ids2);
    }

    private static List<string> CollectCallableIds(AnalysisDocument d) =>
        d.Application.SymbolTable.Values
            .SelectMany(m => m.Types.Values)
            .SelectMany(t => t.Callables?.Values ?? Enumerable.Empty<Callable>())
            .Select(c => c.Id).OrderBy(x => x).ToList();
}
