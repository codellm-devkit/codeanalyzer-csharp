# Schema decisions — codeanalyzer-dotnet (`cansharp`)

Transcript of the design-loop decisions (see org `.github` specs:
`codeanalyzer-dotnet.md` D1–D16, and the 2026-11-08 amendment
`2026-11-08-dotnet-application-identity.md`). Each entry is auditable; a later session
can see why the schema looks the way it does.

## Identity (amended 2026-11-08, per python-sdk#440)

- **D-identity.** Two-segment application-scoped root:
  `can://<namespace>/<application>/csharp/<file>/<Type>/<member-signature>`. Derivation follows
  the shipped codeanalyzer-go precedent EXACTLY: `--application` ⇒ else input-root basename;
  `--namespace` ⇒ else resolved application name; omitted ≠ explicit-empty (latter rejected).
  Envelope + application anchor carry integer `identity_version: 2` alongside
  `schema_version: "2.0.0"`. Raw `application_name` / `identity_namespace` retained on the
  application record. `--app-name` is a deprecated alias for `--application`.
  Encoding (#435): uppercase percent-escapes outside ASCII `[A-Za-z0-9-._~]`, no trim/normalize.

## Language modelling (L1)

- **D-records** (spec D8). `record` / `record_struct` as first-class `type` kinds alongside
  class/struct/interface/enum. Records carry `positional_parameters[]` (primary constructor).
- **D-delegate** (spec D8). `delegate` as a first-class `type` kind carrying `invoke_signature`.
- **D-props** (spec D7). Properties/events emit BOTH a `field` node (`kind: property|event`,
  `is_auto` for compiler-synthesized backing) AND, for non-auto accessors, `#get`/`#set`
  callables. **Recorded as a DIVERGENCE** from codeanalyzer-typescript's first-class accessor
  kinds (`getter|setter`), per codeanalyzer-dotnet#40 — not a precedent match. An
  expression-bodied property (`=> expr`) is a getter (emits `#get`), not an auto-property.
- **D-refkind** (spec D9). `param.ref_kind ∈ {ref, out, in, ref_readonly}`; absent = by value.
  At L4 (future), by-ref params also get an `@formal_out:<name>` vertex.
- **D-async** (spec, 2026-11-08 session). `is_async` / `is_iterator` are typed boolean fields on
  `callable`, NOT new kinds — per the keystone's language-expansion rubric.

## Deferred (NOT in this L1 build)

- **D-cfg.** C#-specific CFG edge kinds `await_resume` / `yield_resume` are **deferred to the L3
  design session**, not fixed here.
- **L2** call-graph resolution (`callee` backfill, `call_graph` edges) — next increment.
- **L3/L4** — each gated on its own design session (L4 needs a points-to design).

## Invariants carried from the base spec

- **D15.** Span offsets are UTF-8 bytes; one UTF-16→UTF-8 offset table per file.
- **D16.** Neo4j labels per-language prefixed: `:CsNode` + kind label; `CS_HAS_MODULE`,
  `CS_DECLARES`, `CS_HAS_METHOD`, `CS_HAS_FIELD` (overlays `CS_*` arrive with L2+).
- **D2/§4.3.** No MSBuild. (L1 uses host trusted-platform-assemblies for the BCL; reading
  `obj/project.assets.json` for full package references lands with the L2 resolver.)
