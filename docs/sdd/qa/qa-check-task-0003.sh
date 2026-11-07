#!/usr/bin/env bash
# qa-check-task-0003.sh - Linux/WSL twin of qa-check-task-0003.ps1.
#
# Same checks, same order, so a platform that runs only bash can still prove AC-06 (store name
# never a literal), ADR-25 (no Version= on a PackageReference) and the GAP-04 SQL shape.
# What this twin CANNOT run is `dotnet test`: this WSL box has no .NET SDK, so the xunit suite
# stays Windows-only here and is reported as unverified on Linux.
#
# Usage:  bash docs/sdd/qa/qa-check-task-0003.sh
# Exit:   0 every check behaved as specified ; 1 at least one check failed ; 3 environment.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
PERSISTENCE="$REPO_ROOT/backend/src/Squid.Persistence"
STORAGE_SRC="$PERSISTENCE/SqliteGrainStorage.cs"
BACKEND="$REPO_ROOT/backend"
PACKAGES_PROPS="$BACKEND/Directory.Packages.props"
PATTERN='(actors?|containers?)'

failures=0
pass() { printf 'ok    %s - %s\n' "$1" "$2"; }
fail() { printf 'FAIL  %s - %s\n' "$1" "$2"; failures=$((failures + 1)); }

printf 'repo      : %s\n' "$REPO_ROOT"
printf 'uname     : %s\n' "$(uname -srm)"
printf '\n'

# ---------------------------------------------------------------- 1. AC-06 positive
if [ ! -d "$PERSISTENCE" ]; then
  printf 'environment: missing %s\n' "$PERSISTENCE"; exit 3
fi
hits="$(find "$PERSISTENCE" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 \
  | xargs -0 grep -inE "\b${PATTERN}\b" 2>/dev/null || true)"
if [ -z "$hits" ]; then
  pass 'AC-06 positive' "0 store-name literal in $PERSISTENCE"
else
  fail 'AC-06 positive' "$(printf '%s' "$hits" | tr '\n' ' ')"
fi

# ---------------------------------------------------------------- 2. AC-06 negative probe
fixture="$(mktemp -d "${TMPDIR:-/tmp}/squid-qa0003-XXXXXX")"
trap 'rm -rf "$fixture"' EXIT
printf 'namespace Fixture;\npublic static class Injected { public const string Store = "actors"; }\n' \
  > "$fixture/Injected.cs"
fixture_hits="$(grep -inE "\b${PATTERN}\b" "$fixture/Injected.cs" || true)"
if [ -n "$fixture_hits" ]; then
  pass 'AC-06 can-fail' "the same scanner found the injected literal: $fixture_hits"
else
  fail 'AC-06 can-fail' 'the scanner found nothing in a file that contains a store literal'
fi

# ---------------------------------------------------------------- 3. ADR-25
version_attrs="$(grep -rn --include='*.csproj' '<PackageReference' "$BACKEND" 2>/dev/null \
  | grep -E '[[:space:]]Version[[:space:]]*=' || true)"
csproj_count="$(find "$BACKEND" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | wc -l | tr -d ' ')"
if [ -z "$version_attrs" ]; then
  pass 'ADR-25' "$csproj_count csproj scanned, 0 PackageReference carries Version="
else
  fail 'ADR-25' "$(printf '%s' "$version_attrs" | tr '\n' ' ')"
fi

pinned="$(grep -oE '<PackageVersion[[:space:]]+Include="[^"]+"[[:space:]]+Version="[^"]+"' "$PACKAGES_PROPS" \
  | sed -E 's/.*Include="([^"]+)"[[:space:]]+Version="([^"]+)"/\1 \2/' | sort)"
printf '      pinned in Directory.Packages.props: %s\n' "$(printf '%s' "$pinned" | tr '\n' ', ')"
for wanted in 'Microsoft.Data.Sqlite 10.0.14' 'Microsoft.Orleans.Core 10.2.2'; do
  if printf '%s\n' "$pinned" | grep -qxF "$wanted"; then pass 'ADR-25 pinned' "$wanted"
  else fail 'ADR-25 pinned' "expected $wanted"; fi
done

# ---------------------------------------------------------------- 4. GAP-04 static
for statement in 'UPDATE GrainState' 'DELETE FROM GrainState'; do
  window="$(grep -A3 -F "$statement" "$STORAGE_SRC" 2>/dev/null | head -5 || true)"
  if printf '%s' "$window" | grep -qE 'Version[[:space:]]*=[[:space:]]*@Version'; then
    pass 'GAP-04 SQL' "$statement compares Version = @Version"
  else
    fail 'GAP-04 SQL' "$statement has no Version = @Version comparison"
  fi
done

# ---------------------------------------------------------------- 5. AC-07 leftovers
leftovers="$(find "${TMPDIR:-/tmp}" -maxdepth 1 -name 'squid-grainstate-*' -type f 2>/dev/null || true)"
if [ -z "$leftovers" ]; then
  pass 'AC-07 leftovers' "0 squid-grainstate-* file in ${TMPDIR:-/tmp}"
else
  fail 'AC-07 leftovers' "$(printf '%s' "$leftovers" | tr '\n' ' ')"
  printf '%s\n' "$leftovers" | xargs rm -f
fi

printf '\n'
if [ "$failures" -eq 0 ]; then
  printf 'summary: all checks behaved as specified - exit 0\n'; exit 0
fi
printf 'summary: %s check(s) failed - exit 1\n' "$failures"; exit 1
