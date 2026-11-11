#!/usr/bin/env bash
# qa-worker-probe-task-0024 - Linux twin of qa-worker-probe-task-0024.ps1: same fixture, same
# grading, same contract (exit 0 only when the negative control fails and the verify passes).
# Starts the built Squid.Worker entry point with dotnet (the Linux build CI produces) instead of
# the Windows apphost. Exit codes: 0 pass, 1 fail, 3 environment refused (no .NET runtime here -
# never a product verdict, same rule as docs/sdd/_tools/sdd-test.sh).
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$repo" || exit 3
fixture="$repo/docs/sdd/qa/qa-worker-fixture-task-0024.py"
tmp="$(mktemp -d "${TMPDIR:-/tmp}/qa0023-worker.XXXXXX")"
db="$tmp/worker.db"
stdout="$tmp/worker.stdout.log"
stderr="$tmp/worker.stderr.log"
code=1

cleanup() {
  if [ "$code" -eq 0 ]; then rm -rf "$tmp"; else echo "PROBE FAIL (exit $code) - artifacts kept: $tmp"; fi
}
trap cleanup EXIT

if ! command -v python3 >/dev/null 2>&1 && ! command -v python >/dev/null 2>&1; then
  echo 'PROBE environment: no python on PATH - not a product verdict'
  code=3; exit 3
fi
pybin=python3
command -v python3 >/dev/null 2>&1 || pybin=python

if ! command -v dotnet >/dev/null 2>&1; then
  echo 'PROBE environment: no dotnet on PATH in this shell - the Linux path stays unverified here'
  code=3; exit 3
fi

dll="$repo/backend/src/Squid.Worker/bin/Debug/net10.0/Squid.Worker.dll"
if [ ! -f "$dll" ]; then
  echo "PROBE FAIL the built worker is missing: $dll (run the gate first)"
  code=1; exit 1
fi

if ! "$pybin" "$fixture" prepare "$db"; then echo 'PROBE FAIL fixture prepare'; code=1; exit 1; fi

"$pybin" "$fixture" verify "$db" >/dev/null 2>&1
if [ "$?" -ne 1 ]; then
  echo 'PROBE FAIL negative control: verify did not exit 1 before the process started'
  code=1; exit 1
fi
echo 'PROBE negative-control ok (verify=1 before the process starts)'

dotnet "$dll" "--Storage:Path=$db" '--Logging:LogLevel:Default=Warning' >"$stdout" 2>"$stderr" &
worker=$!
started=$(date +%s)
code=1
while :; do
  "$pybin" "$fixture" verify "$db" >/dev/null 2>&1
  rc=$?
  if [ "$rc" -eq 0 ]; then code=0; break; fi
  if ! kill -0 "$worker" 2>/dev/null; then code=1; break; fi
  now=$(date +%s)
  if [ $((now - started)) -ge 90 ]; then code=1; break; fi
  sleep 3
done

verify_out=$("$pybin" "$fixture" verify "$db" 2>&1)
code=$?
printf '%s\n' "$verify_out" | sed 's/^/PROBE /'
[ -s "$stdout" ] && echo "WORKER-LOG stdout: $(tail -3 "$stdout" | tr '\n' ' ')"
[ -s "$stderr" ] && echo "WORKER-LOG stderr: $(tail -3 "$stderr" | tr '\n' ' ')"
kill "$worker" 2>/dev/null
wait "$worker" 2>/dev/null
if [ "$code" -eq 0 ]; then echo "PROBE PASS (exit 0) - temp removed: $tmp"; fi
exit "$code"
