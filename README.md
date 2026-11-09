# codeanalyzer-csharp (`cansharp`)

A Roslyn-based static analyzer for C# that emits the canonical CLDK analysis schema v2
(`analysis.json` + a Neo4j projection). Distributed on PyPI as `cansharp`; consumed by the
`csharp` model package in `python-sdk`.

> **Naming.** This analyzer is `codeanalyzer-csharp` — one repo per language. (The original
> design named it `codeanalyzer-dotnet` for the .NET platform family; that was reversed in
> favour of a C#-only repo. VB.NET/F#, if ever added, get their own `codeanalyzer-*` repos.)

Design: org `.github` specs (the design spec, originally filed as `codeanalyzer-dotnet.md`,
D1–D16) and `2026-11-08-dotnet-application-identity.md` (two-segment identity, python-sdk#440).
Decision transcript: [`.claude/SCHEMA_DECISIONS.md`](.claude/SCHEMA_DECISIONS.md).

## Status

**L1 (symbol table) — implemented.** The containment tree to callable depth + `call` nodes
(`callee: null`), both projections, application-scoped identity (`identity_version: 2`).

- [x] L1 symbol table — `can://<namespace>/<application>/csharp/...`, records/delegates,
      properties→field+accessors, `ref_kind`, `is_async`/`is_iterator`, UTF-8 byte spans
- [ ] L2 call graph (`callee` backfill, `call_graph` edges) — next
- [ ] L3 intraprocedural dataflow — own design session
- [ ] L4 interprocedural SDG — own design session (needs a points-to oracle)

## Build & test

```sh
dotnet build src/Cansharp -c Release
dotnet test  test/Cansharp.Tests
```

## Run

```sh
# symbol table to stdout (compact JSON)
dotnet run --project src/Cansharp -- -i <path-to-csharp-project>

# to a directory, with explicit application identity
dotnet run --project src/Cansharp -- -i <path> -o /tmp/out \
  --namespace online-boutique --application payments

# Neo4j projection (Cypher)
dotnet run --project src/Cansharp -- -i <path> -o /tmp/out --emit neo4j
```

Contract: stdout carries only analysis JSON; all diagnostics go to stderr; flag errors exit
non-zero. `-a` defaults to 1 (only L1 is implemented; `-a 2+` errors).
