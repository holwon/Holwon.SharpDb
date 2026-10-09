#!/usr/bin/env bash
#
# Catches identifiers this project has retired — renamed, or a rejected design —
# reappearing outside the documents whose job is to explain the retirement.
#
# Why this exists: ADR 0006 retired the single-underscore env prefix, but the old
# name survived in two Answer sections for days. Nothing checked. A reader cannot
# tell a stale name from a live one.
#
# Scope is deliberately narrow. It checks identifiers with ONE canonical spelling,
# NOT the GLOSSARY's `_Avoid_` lists — those are semantic guidance, and a
# banned-word grep against them is ~0 signal / heavy noise (`MySqlConnector`
# contains "Connector"; `Driver` is itself a defined term; "engine" is ordinary
# domain vocabulary).
#
# Usage:  bash tools/check-retired-identifiers.sh
#         (on Windows, via Git's bash: "C:\Program Files\Git\bin\bash.exe")
# Exit:   0 clean · 1 violations found

set -uo pipefail

cd "$(dirname "$0")/.." || exit 2

fail=0

# check <retired> <canonical> [ "path|retired" ... ]
#   Allowlist entries name an exact path where that exact identifier is a
#   deliberate explanation of the retirement, not an accidental reuse.
check() {
  local retired="$1" canonical="$2"; shift 2
  local -a allow=("$@")

  local hits
  hits=$(grep -rn --fixed-strings \
              --include='*.md' --include='*.cs' --include='*.csproj' \
              --include='*.json' --include='*.slnx' \
              --exclude-dir=.git --exclude-dir=bin --exclude-dir=obj \
              --exclude='check-retired-identifiers.sh' \
              -- "$retired" . 2>/dev/null || true)
  [ -z "$hits" ] && return 0

  local line path a allowed
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line%%:*}"; path="${path#./}"
    allowed=0
    for a in ${allow[@]+"${allow[@]}"}; do
      if [ "${a%%|*}" = "$path" ] && [ "${a##*|}" = "$retired" ]; then
        allowed=1; break
      fi
    done
    if [ "$allowed" -eq 0 ]; then
      printf '  %s\n    -> %s is retired; use %s\n' "$line" "$retired" "$canonical"
      fail=1
    fi
  done <<< "$hits"
}

echo 'Checking retired identifiers...'

# --- Env-var prefix: ADR 0006 settled on the double underscore. ---------------
# `__` is the .NET configuration hierarchy separator; the single-underscore form
# is a DIFFERENT variable that SILENTLY fails to bind. Every allowlisted path
# below is a document whose job is to explain that very contrast.
check 'SHARPDB_TYPE' 'SHARPDB__TYPE' \
  'docs/adr/0006-double-underscore-env-prefix.md|SHARPDB_TYPE' \
  'docs/research/mcp-sdk-mechanics.md|SHARPDB_TYPE' \
  '.scratch/sharpdb-mcp/issues/06-config-startup-and-distribution.md|SHARPDB_TYPE' \
  '.scratch/sharpdb-v1/spec.md|SHARPDB_TYPE'

check 'SHARPDB_CONNECTION_STRING' 'SHARPDB__CONNECTIONSTRING' \
  '.scratch/sharpdb-mcp/issues/06-config-startup-and-distribution.md|SHARPDB_CONNECTION_STRING'

# --- Rejected interface shape: ADR 0003 chose one deep interface. ------------
# `DbProviderFactory` was never adopted; Core talks to a plain DbConnection.
check 'DbProviderFactory' 'a plain DbConnection (ADR 0003)'

if [ "$fail" -eq 0 ]; then
  echo '  OK — no retired identifiers outside their explanation sites.'
fi
exit "$fail"
