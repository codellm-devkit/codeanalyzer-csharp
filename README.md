# codeanalyzer-csharp (`cansharp`)

A Roslyn-based static analyzer for C# that emits the canonical CLDK analysis schema v2
(`analysis.json` + a Neo4j projection). Distributed on PyPI as `cansharp`; consumed by the
`csharp` model package in `python-sdk`.

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
