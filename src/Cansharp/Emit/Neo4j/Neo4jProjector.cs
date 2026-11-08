using System.Text;
using Cansharp.Schema;

namespace Cansharp.Emit.Neo4j;

/// <summary>
/// Co-primary Neo4j projection (seam present from day one). Per-language prefixed per D16:
/// nodes under :CsNode + a kind label; containment as CS_HAS_MODULE / CS_DECLARES /
/// CS_HAS_METHOD / CS_HAS_FIELD. Always full implemented depth (L1 here). Emits Cypher.
/// Overlay relationships (CS_CALLS etc.) arrive with L2+.
/// </summary>
public static class Neo4jProjector
{
    public static string ProjectCypher(AnalysisDocument a)
    {
        var sb = new StringBuilder();
        var app = a.Application;
        sb.AppendLine($"MERGE (app:CsNode:Application {{id: {Q(app.Id)}, name: {Q(app.Name)}}});");

        foreach (var (fileKey, module) in app.SymbolTable)
        {
            sb.AppendLine($"MERGE (m:CsNode:Module {{id: {Q(module.Id)}, file_key: {Q(fileKey)}}});");
            sb.AppendLine($"MATCH (app:Application {{id: {Q(app.Id)}}}), (m:Module {{id: {Q(module.Id)}}}) MERGE (app)-[:CS_HAS_MODULE]->(m);");

            foreach (var (_, type) in module.Types)
            {
                sb.AppendLine($"MERGE (t:CsNode:Type {{id: {Q(type.Id)}, kind: {Q(type.Kind)}}});");
                sb.AppendLine($"MATCH (m:Module {{id: {Q(module.Id)}}}), (t:Type {{id: {Q(type.Id)}}}) MERGE (m)-[:CS_DECLARES]->(t);");

                foreach (var (_, c) in type.Callables ?? new())
                {
                    sb.AppendLine($"MERGE (c:CsNode:Callable {{id: {Q(c.Id)}, kind: {Q(c.Kind)}}});");
                    sb.AppendLine($"MATCH (t:Type {{id: {Q(type.Id)}}}), (c:Callable {{id: {Q(c.Id)}}}) MERGE (t)-[:CS_HAS_METHOD]->(c);");
                }
                foreach (var (_, f) in type.Fields ?? new())
                {
                    sb.AppendLine($"MERGE (f:CsNode:Field {{id: {Q(f.Id)}, kind: {Q(f.Kind)}}});");
                    sb.AppendLine($"MATCH (t:Type {{id: {Q(type.Id)}}}), (f:Field {{id: {Q(f.Id)}}}) MERGE (t)-[:CS_HAS_FIELD]->(f);");
                }
            }
        }
        return sb.ToString();
    }

    private static string Q(string s) => "'" + s.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
}
