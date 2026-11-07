#!/usr/bin/env bash
# qa-check-task-0006.sh — Linux/WSL twin of qa-check-task-0006.ps1, static half of AC-05 only:
# ADR-25 (no Version= in any .csproj), the human-gate package/version scope, and the newest gate
# log (exit 0, new tests counted). This box has no .NET SDK, so the SUITE is not executed here:
# every execution check stays unverified on Linux and is proven by the Windows run in QA-0005.
#
# Usage: wsl -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0006.sh
# Exit:  0 = all checks green · 1 = at least one check failed
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
failed=0
pass() { echo "PASS | $1 | $2"; }
fail() { echo "FAIL | $1 | $2"; failed=$((failed + 1)); }

# 1. ADR-25 — versions only in Directory.Packages.props.
csproj_count=$(find "$repo/backend" -name '*.csproj' | wc -l | tr -d ' ')
hits=$(grep -rE 'PackageReference[^>]*[[:space:]]Version=' "$repo/backend" --include='*.csproj' 2>/dev/null | wc -l | tr -d ' ')
if [ "$hits" -eq 0 ]; then pass 'ADR-25 no Version= in any csproj' "csproj=$csproj_count hits=$hits"
else fail 'ADR-25 no Version= in any csproj' "csproj=$csproj_count hits=$hits"; fi

# 2. Human-gate scope of 2026-11-06 — exact package + version set, only as PackageVersion.
props="$repo/backend/Directory.Packages.props"
for pin in \
  'OpenTelemetry.Extensions.Hosting 1.20.1' \
  'OpenTelemetry.Exporter.OpenTelemetryProtocol 1.20.1' \
  'OpenTelemetry.Instrumentation.AspNetCore 1.20.1' \
  'Serilog 4.4.0' \
  'Serilog.AspNetCore 10.0.0'; do
  id="${pin% *}"; ver="${pin#* }"
  if grep -qF "PackageVersion Include=\"$id\" Version=\"$ver\"" "$props"; then
    pass 'pin present with gate version' "$id $ver"
  else
    fail 'pin present with gate version' "$id $ver"
  fi
done
pv=$(grep -r 'PackageVersion' "$repo/backend" --include='*.csproj' 2>/dev/null | wc -l | tr -d ' ')
if [ "$pv" -eq 0 ]; then pass 'PackageVersion lives only in Directory.Packages.props' 'no PackageVersion inside any csproj'
else fail 'PackageVersion lives only in Directory.Packages.props' "hits=$pv"; fi

# 3. Newest gate log — exit 0 and more tests than the pre-change baseline.
run_folder="$repo/docs/sdd/runs/test-runs"
newest=$(ls -1 "$run_folder"/*-test-gate.log 2>/dev/null | sort -r | head -n 1)
if [ -n "$newest" ]; then
  exit_code=$(grep -m1 -E '^exit:' "$newest" | awk '{print $2}')
  total=$(grep -oE 'Aprovado:[[:space:]]*[0-9]+' "$newest" | tail -n 1 | grep -oE '[0-9]+')
  if [ "${exit_code:-x}" = '0' ]; then pass 'newest gate log exit 0' "$(basename "$newest") exit=$exit_code"
  else fail 'newest gate log exit 0' "$(basename "$newest") exit=${exit_code:-missing}"; fi
  baseline="$run_folder/20261106-124547-test-gate.log"
  if [ -f "$baseline" ]; then
    base_total=$(grep -oE 'Aprovado:[[:space:]]*[0-9]+' "$baseline" | tail -n 1 | grep -oE '[0-9]+')
    if [ -n "${total:-}" ] && [ -n "${base_total:-}" ] && [ "$total" -gt "$base_total" ]; then
      pass 'new tests counted in the gate total' "newest=$total baseline=$base_total"
    else
      fail 'new tests counted in the gate total' "newest=${total:-missing} baseline=${base_total:-missing}"
    fi
  else
    fail 'baseline gate log present' '20261106-124547-test-gate.log missing'
  fi
else
  fail 'a gate log exists' 'no *-test-gate.log under docs/sdd/runs/test-runs'
fi

if [ "$failed" -eq 0 ]; then echo 'RESULT: PASS - all checks green'; exit 0; fi
echo "RESULT: FAIL - $failed check(s) failed"; exit 1
