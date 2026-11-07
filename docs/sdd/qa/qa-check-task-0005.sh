#!/usr/bin/env bash
# qa-check-task-0005.sh - Linux/WSL twin of qa-check-task-0005.ps1.
#
# Same checks, same order, for what a platform WITHOUT the .NET SDK can prove:
# AC-06 static (ADR-25: no Version= attribute on any PackageReference; the new pins
# live only in Directory.Packages.props; the Dashboard pin resolves in
# project.assets.json) and the GAP-08 local package cache.
#
# What this twin CANNOT run is `dotnet run` (AC-01..AC-05 real hosts) and the xunit
# suite: this WSL box has no .NET SDK (documented in qa-check-task-0003.sh), so the
# host scenarios stay Windows-only and are reported unverified on Linux - never
# assumed equal to the run that passed on Windows.
#
# Usage:  bash docs/sdd/qa/qa-check-task-0005.sh
# Exit:   0 every check behaved as specified ; 1 at least one check failed ; 3 environment.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
cd "$REPO_ROOT" || exit 3

failed=0
check() { # name, ok (0 = pass), detail
  if [ "$2" -eq 0 ]; then
    echo "PASS | $1 | $3"
  else
    echo "FAIL | $1 | $3"
    failed=$((failed + 1))
  fi
}

echo "== qa-check-task-0005.sh (Linux/WSL twin) =="
echo "repo: $REPO_ROOT"
echo "when: $(date '+%Y-%m-%d %H:%M:%S')"

# --- AC-06 / ADR-25: no Version= attribute in any PackageReference -------------
csproj_count=$(find backend/src backend/tests -name '*.csproj' -not -path '*/obj/*' -not -path '*/bin/*' 2>/dev/null | wc -l | tr -d ' ')
version_hits=$(grep -R --include='*.csproj' -l -E '<PackageReference[^>]*[[:space:]]Version=' backend/src backend/tests 2>/dev/null | wc -l | tr -d ' ')
[ "$version_hits" -eq 0 ] && ok=0 || ok=1
check "AC-06/ADR-25 no Version= in any PackageReference" "$ok" "csproj=$csproj_count hits=$version_hits"

# --- AC-06: new package versions live only in Directory.Packages.props ---------
grep -q 'PackageVersion Include="Microsoft.Orleans.Dashboard" Version="10.2.2"' backend/Directory.Packages.props &&
  grep -q 'PackageVersion Include="Microsoft.Orleans.Server" Version="10.2.2"' backend/Directory.Packages.props
check "AC-06 pins live in Directory.Packages.props" "$?" 'Dashboard 10.2.2 + Server 10.2.2 as PackageVersion'

# --- GAP-08: the pinned dashboard package resolves, the 8.x alternative does not
assets_ok=1
grep -q '"Microsoft.Orleans.Dashboard/10.2.2"' backend/src/Squid.Service/obj/project.assets.json && assets_ok=0
check "GAP-08 pinned Microsoft.Orleans.Dashboard 10.2.2 resolves in project.assets.json" "$assets_ok" 'assets contains Microsoft.Orleans.Dashboard/10.2.2'
contrib_present=0
grep -q 'OrleansContrib' backend/src/Squid.Service/obj/project.assets.json && contrib_present=1
check "GAP-08 OrleansContrib 8.x alternative is absent from the restore" "$contrib_present" 'no OrleansContrib entry in project.assets.json'
cache_ok=1
{ [ -d 'backend/.packages/microsoft.orleans.dashboard/10.2.2' ] || [ -d '.dotnet-home/nuget/microsoft.orleans.dashboard/10.2.2' ]; } && cache_ok=0
check "GAP-08 local cache holds exactly the pinned 10.2.2" "$cache_ok" 'backend/.packages or .dotnet-home/nuget has microsoft.orleans.dashboard/10.2.2'

# --- platform honesty: what this box cannot run -------------------------------
if command -v dotnet >/dev/null 2>&1; then
  echo "note: dotnet present on this platform ($(dotnet --version 2>/dev/null)) - host scenarios could run here but were not executed by this twin"
else
  echo "note: dotnet absent on this platform - host scenarios AC-01..AC-05 are NOT executed here (unverified on Linux, Windows run is the evidence)"
fi

echo ""
if [ "$failed" -eq 0 ]; then
  echo "RESULT: PASS - all checks green"
  exit 0
fi
echo "RESULT: FAIL - $failed check(s) failed"
exit 1
