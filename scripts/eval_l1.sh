#!/usr/bin/env bash
# eval_l1.sh — L1 evaluation runner for codeanalyzer-csharp (cansharp).
#
# House split (mirrors codeanalyzer-go/scripts/eval_l3.sh): this shell OWNS pass/fail —
#   * a run that exits non-zero fails the app
#   * -j determinism: the -j1 and -jN analysis.json must be byte-identical (cmp -s)
#   * l1_metrics.py is a pure measurement emitter; a nonzero check_failures fails the app
# l1_metrics.py OWNS measurement (descriptive JSONL + check_failures), nothing more.
#
# Exit codes (house convention): 2 = usage/build/missing-binary; 1 = run/check failures; 0 = ok.
#
# Requirements: dotnet, python3, cmp (coreutils/BSD). jq optional (not required).
#
# Usage:
#   scripts/eval_l1.sh <input-dir-or-csproj-or-sln> [more inputs...]
#   scripts/eval_l1.sh --all                 # run every dir under $EVAL_INPUTS
#   scripts/eval_l1.sh --clean               # remove $EVAL_OUT
#   scripts/eval_l1.sh -h | --help
#
# Env:
#   CANSHARP     path to a prebuilt cansharp binary; if unset, built from ./src/Cansharp
#   EVAL_OUT     output root (default /tmp/cansharp-eval-l1)
#   EVAL_INPUTS  directory of input apps for --all (default: none)
#   JOBS         worker count for the -jN run (default: CPU count)

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
METRICS="$SCRIPT_DIR/l1_metrics.py"

EVAL_OUT="${EVAL_OUT:-/tmp/cansharp-eval-l1}"
EVAL_INPUTS="${EVAL_INPUTS:-}"
JOBS="${JOBS:-$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)}"
NOTE="$EVAL_OUT/l1-$(date +%Y%m%d-%H%M%S).md"

die()  { echo "error: $*" >&2; exit 2; }
info() { echo ">> $*" >&2; }
append_row() {
  # name status modules types callables call_sites jdet checks
  printf '| %s | %s | %s | %s | %s | %s | %s | %s |\n' "$@" >> "$NOTE"
}

usage() {
  sed -n '2,30p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
  exit 0
}

# ---- arg parse ----
MODE="run"
INPUTS=()
[ $# -eq 0 ] && usage
for a in "$@"; do
  case "$a" in
    -h|--help) usage ;;
    --clean)   MODE="clean" ;;
    --all)     MODE="all" ;;
    --*)       die "unknown flag '$a'" ;;
    *)         INPUTS+=("$a") ;;
  esac
done

if [ "$MODE" = "clean" ]; then
  case "$EVAL_OUT" in /|"") die "refusing to clean '$EVAL_OUT'";; esac
  rm -rf "$EVAL_OUT"; info "cleaned $EVAL_OUT"; exit 0
fi

# ---- locate or build the analyzer ----
if [ -n "${CANSHARP:-}" ]; then
  [ -x "$CANSHARP" ] || die "CANSHARP='$CANSHARP' is not executable"
  RUN=( "$CANSHARP" )
else
  info "building cansharp (Release)…"
  dotnet build "$REPO_ROOT/src/Cansharp" -c Release --nologo >/dev/null \
    || die "build failed"
  RUN=( dotnet run --project "$REPO_ROOT/src/Cansharp" -c Release --no-build -- )
fi

# ---- collect inputs ----
if [ "$MODE" = "all" ]; then
  [ -n "$EVAL_INPUTS" ] && [ -d "$EVAL_INPUTS" ] || die "--all needs EVAL_INPUTS=<dir>"
  while IFS= read -r d; do INPUTS+=("$d"); done \
    < <(find "$EVAL_INPUTS" -mindepth 1 -maxdepth 1 -type d | sort)
fi
[ "${#INPUTS[@]}" -gt 0 ] || die "no inputs"

mkdir -p "$EVAL_OUT"
: > "${NOTE}"
{
  echo "# cansharp L1 eval — $(date)"
  echo
  echo "| app | status | modules | types | callables | call_sites | -j det | checks |"
  echo "|---|---|---|---|---|---|---|---|"
} >> "$NOTE"

FAILS=0
JSONL="$EVAL_OUT/metrics.jsonl"
: > "$JSONL"

for input in "${INPUTS[@]}"; do
  [ -e "$input" ] || { info "skip missing: $input"; continue; }
  name="$(basename "${input%/}")"
  d1="$EVAL_OUT/$name-j1"
  dN="$EVAL_OUT/$name-jN"
  mkdir -p "$d1" "$dN"
  status="OK"; det="ok"

  # run -j 1
  if ! "${RUN[@]}" -i "$input" -o "$d1" -a 1 >/dev/null 2>"$d1/err.txt"; then
    status="FAIL(run -j1)"; FAILS=$((FAILS+1))
    append_row "$name" "$status" "-" "-" "-" "-" "-" "-"
    continue
  fi
  # run -j N  (determinism comparison)
  if ! "${RUN[@]}" -i "$input" -o "$dN" -a 1 >/dev/null 2>"$dN/err.txt"; then
    status="FAIL(run -jN)"; FAILS=$((FAILS+1))
    append_row "$name" "$status" "-" "-" "-" "-" "-" "-"
    continue
  fi

  # -j determinism gate: byte-identical analysis.json
  if ! cmp -s "$d1/analysis.json" "$dN/analysis.json"; then
    status="FAIL(-j differs)"; det="DIFF"; FAILS=$((FAILS+1))
  fi

  # measure (python owns measurement only)
  rec="$(python3 -I "$METRICS" "$name=$d1/analysis.json")" || {
    status="FAIL(metrics)"; FAILS=$((FAILS+1))
    append_row "$name" "$status" "-" "-" "-" "-" "$det" "-"; continue; }
  echo "$rec" >> "$JSONL"

  # gate: nonzero check_failures fails the app
  checks="$(printf '%s' "$rec" | python3 -I -c '
import json,sys
d=json.load(sys.stdin)
cf=d.get("check_failures") or {}
print(";".join(f"{k}={v}" for k,v in cf.items()) if cf else "clean")
')"
  if [ "$checks" != "clean" ]; then
    status="FAIL(checks)"; FAILS=$((FAILS+1))
  fi

  # pull a few descriptive counts for the note row
  read -r mods typs cals sites < <(printf '%s' "$rec" | python3 -I -c '
import json,sys
d=json.load(sys.stdin)
print(d.get("modules",0), d.get("types",0), d.get("callables",0), d.get("call_sites",0))
')
  append_row "$name" "$status" "$mods" "$typs" "$cals" "$sites" "$det" "$checks"
done

info "note:    $NOTE"
info "metrics: $JSONL"
if [ "$FAILS" -gt 0 ]; then
  info "FAILURES: $FAILS"
  exit 1
fi
info "all passed"
exit 0
