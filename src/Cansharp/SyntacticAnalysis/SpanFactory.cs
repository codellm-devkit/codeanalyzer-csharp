using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Cansharp.Schema;

namespace Cansharp.SyntacticAnalysis;

/// <summary>
/// Builds canonical Spans. Roslyn reports UTF-16 positions; the canonical `bytes` field is
/// UTF-8 (D15), so each file carries one UTF-16→UTF-8 offset table, computed once.
/// </summary>
public sealed class SpanFactory
{
    private readonly int[] _utf16ToUtf8;   // prefix sum: byte offset at each UTF-16 index

    public SpanFactory(string source)
    {
        // _utf16ToUtf8[i] = number of UTF-8 bytes in source[0..i) (UTF-16 code units).
        _utf16ToUtf8 = new int[source.Length + 1];
        int running = 0;
        for (int i = 0; i < source.Length; i++)
        {
            _utf16ToUtf8[i] = running;
            char c = source[i];
            if (char.IsHighSurrogate(c) && i + 1 < source.Length && char.IsLowSurrogate(source[i + 1]))
            {
                // Surrogate pair => one code point, 4 UTF-8 bytes; account across both units.
                running += 4;
                _utf16ToUtf8[i + 1] = running; // low surrogate maps to same end
                i++;
            }
            else
            {
                running += Utf8Len(c);
            }
        }
        _utf16ToUtf8[source.Length] = running;
    }

    private static int Utf8Len(char c) => c switch
    {
        <= (char)0x7F => 1,
        <= (char)0x7FF => 2,
        _ => 3, // BMP; surrogate pairs handled above
    };

    public Span From(SyntaxNode node) => From(node.GetLocation());

    public Span From(Location location)
    {
        var lineSpan = location.GetLineSpan();
        var textSpan = location.SourceSpan;
        return new Span
        {
            // line:col are 1-based for display (Roslyn is 0-based).
            Start = new[] { lineSpan.StartLinePosition.Line + 1, lineSpan.StartLinePosition.Character + 1 },
            End = new[] { lineSpan.EndLinePosition.Line + 1, lineSpan.EndLinePosition.Character + 1 },
            Bytes = new[] { ByteAt(textSpan.Start), ByteAt(textSpan.End) },
        };
    }

    private int ByteAt(int utf16Index)
    {
        if (utf16Index < 0) return 0;
        if (utf16Index >= _utf16ToUtf8.Length) return _utf16ToUtf8[^1];
        return _utf16ToUtf8[utf16Index];
    }

    /// <summary>Slice the UTF-8 source by a span's byte offsets — the get_method_body contract.</summary>
    public static string Slice(string source, Span span)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        int from = span.Bytes[0], to = span.Bytes[1];
        return Encoding.UTF8.GetString(bytes, from, to - from);
    }
}
