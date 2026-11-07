#!/usr/bin/env bash
# qa-check-task-0008.sh - Linux twin of qa-check-task-0008.ps1 for TASK-0008 (SPEC-0004).
# Executed inside the WSL podman machine (bash + podman present; no dotnet SDK, no compose
# provider there - so the compose orchestration half of the ACs runs only on Windows and is
# recorded as unverified on Linux, never assumed equal).
#
# Nothing here starts or stops the stack. Probes that cannot run in this platform print
# UNVERIFIED and are NOT counted green.
#
# Usage:  wsl -u root -e bash /mnt/c/dev/squid-squad-net/squid-execution/docs/sdd/qa/qa-check-task-0008.sh
#         (-u root: the distro's default user has its own EMPTY rootless store; root shares the
#          rootful store the Windows podman client uses, so the image probes see the same image)
# Exit:   0 = all checks green - 1 = at least one check failed
set -u

repo="$(cd "$(dirname "$0")/../../.." && pwd)"
failed=0
unverified=0

pass() { echo "PASS | $1 | $2"; }
fail() { echo "FAIL | $1 | $2"; failed=$((failed + 1)); }
unv()  { echo "UNVERIFIED | $1 | $2"; unverified=$((unverified + 1)); }

cf="$repo/infra/Containerfile"
co="$repo/infra/compose.yaml"
st="$repo/docs/sdd/_tools/sdd-test.ps1"

for f in "$cf" "$co" "$st"; do
  if [ ! -f "$f" ]; then
    fail "prerequisite present" "$f missing"
    echo "RESULT: FAIL - $failed check(s) failed"
    exit 1
  fi
done

# ------------------------------------------------------------------ AC-01 static (Containerfile)
from_count=$(grep -cE '^[[:space:]]*FROM ' "$cf")
if [ "$from_count" -ge 2 ]; then pass "AC-01 multi-stage (>=2 FROM)" "FROM count=$from_count"
else fail "AC-01 multi-stage (>=2 FROM)" "FROM count=$from_count"; fi

last_from=$(grep -E '^[[:space:]]*FROM ' "$cf" | tail -1)
if echo "$last_from" | grep -qE '^[[:space:]]*FROM[[:space:]]+[^[:space:]]*/runtime:[^[:space:]]+[[:space:]]+AS[[:space:]]+runtime[[:space:]]*$'
then pass "AC-01 final FROM is the runtime image AS runtime" "$(echo "$last_from" | tr -d '\r')"
else fail "AC-01 final FROM is the runtime image AS runtime" "$(echo "$last_from" | tr -d '\r')"; fi

if echo "$last_from" | grep -qE 'AS[[:space:]]+sdk[[:space:]]*$'
then fail "AC-01 final stage is never sdk" "$(echo "$last_from" | tr -d '\r')"
else pass "AC-01 final stage is never sdk" "final stage alias is runtime"; fi

last_user=$(awk '/^[[:space:]]*FROM /{u=""} /^[[:space:]]*USER /{u=$0} END{print u}' "$cf" | tr -d '\r')
case "$last_user" in
  "USER 0"|"USER root"|"USER 0:0") fail "AC-01 non-root USER after final FROM" "$last_user" ;;
  "USER "*) pass "AC-01 non-root USER after final FROM" "$last_user" ;;
  *) fail "AC-01 non-root USER after final FROM" "no USER after final FROM" ;;
esac

# reset on every FROM: count only the publish lines that come after the LAST FROM
publish_after=$(awk '/^[[:space:]]*FROM /{c=0} /dotnet[[:space:]]+publish/{c++} END{print c+0}' "$cf")
if [ "$publish_after" -eq 0 ]; then pass "AC-01 dotnet publish only in earlier stage" "lines after final FROM=0"
else fail "AC-01 dotnet publish only in earlier stage" "lines after final FROM=$publish_after"; fi

if grep -qE 'COPY[[:space:]]+--from=build' "$cf"
then pass "AC-01 final image gets the published output (COPY --from=build)" "present"
else fail "AC-01 final image gets the published output (COPY --from=build)" "absent"; fi

# ------------------------------------------------------------------ AC-02/03 static (compose.yaml)
if grep -qE '^[[:space:]]*-[[:space:]]*"?8080:8080"?[[:space:]]*$' "$co"
then pass "AC-02 API port exposed 8080:8080" "present" else fail "AC-02 API port exposed 8080:8080" "absent"; fi
if grep -qE '^[[:space:]]*-[[:space:]]*"?16686:16686"?[[:space:]]*$' "$co"
then pass "AC-02 collector query port 16686:16686" "present" else fail "AC-02 collector query port 16686:16686" "absent"; fi
if grep -qE '^[[:space:]]*-[[:space:]]*collector[[:space:]]*$' "$co"
then pass "AC-02 squid depends_on collector" "present" else fail "AC-02 squid depends_on collector" "absent"; fi
if grep -qE '^[[:space:]]*squid-data:[[:space:]]*$' "$co"
then pass "AC-03 volume named squid-data declared" "present" else fail "AC-03 volume named squid-data declared" "absent"; fi
if grep -q 'squid-data:/data' "$co"
then pass "AC-03 squid-data mapped to /data" "present" else fail "AC-03 squid-data mapped to /data" "absent"; fi
if grep -qE 'Storage__Path:[[:space:]]*/data/' "$co"
then pass "AC-03 SQLite lives under /data" "Storage__Path under /data" else fail "AC-03 SQLite lives under /data" "absent"; fi
if grep -q 'OTEL_EXPORTER_OTLP_ENDPOINT:[[:space:]]*http://collector:4317' "$co"
then pass "AC-04 OTLP endpoint points at the collector service" "http://collector:4317"
else fail "AC-04 OTLP endpoint points at the collector service" "absent"; fi

secret_hits=$(grep -vE '^[[:space:]]*#' "$co" | grep -icE 'password|passwd|secret|token|credential|api[_-]?key|private[_-]?key' || true)
if [ "$secret_hits" -eq 0 ]; then pass "AC-05 no secret-shaped line in compose.yaml" "non-comment hits=0"
else fail "AC-05 no secret-shaped line in compose.yaml" "non-comment hits=$secret_hits"; fi

# ------------------------------------------------------------------ AC-06 static (sdd-test.ps1)
if grep -q "ValidateSet('probe', 'build', 'test', 'health', 'compose')" "$st"
then pass "AC-06 compose mode declared in ValidateSet" "probe,build,test,health,compose"
else fail "AC-06 compose mode declared in ValidateSet" "absent"; fi
if grep -q 'podman ausente - ambiente (exit 3)' "$st"
then pass "AC-06 podman-absent classified environment exit 3" "guard present"
else fail "AC-06 podman-absent classified environment exit 3" "absent"; fi
if grep -q 'provider de compose ausente - ambiente (exit 3)' "$st"
then pass "AC-06 broken/missing compose provider classified environment exit 3" "guard present"
else fail "AC-06 broken/missing compose provider classified environment exit 3" "absent"; fi
down_v=$(grep -vE '^[[:space:]]*#' "$st" | grep -cE 'compose[[:space:]]+down\b.*([[:space:]]-v\b|--volumes\b)' || true)
if [ "$down_v" -eq 0 ]; then pass "AC-03 no \`down -v\` invocation in sdd-test.ps1" "code-line hits=0"
else fail "AC-03 no \`down -v\` invocation in sdd-test.ps1" "code-line hits=$down_v"; fi

# ------------------------------------------------- AC-01/AC-05 image probes (Linux execution)
if ! command -v podman >/dev/null 2>&1; then
  unv "AC-01 image probes" "podman not available in this platform"
else
  img_user=$(podman image inspect squid-service:dev --format '{{.Config.User}}' 2>/dev/null)
  if [ -z "$img_user" ] && ! podman image exists squid-service:dev 2>/dev/null; then
    unv "AC-01 image inspectable" "image squid-service:dev not reachable from this podman (shared store?); Windows run carries this check"
  else
    if [ "$img_user" = "10001:10001" ]; then pass "AC-01/AC-05 Config.User is 10001:10001 (non-root)" "$img_user"
    else fail "AC-01/AC-05 Config.User is 10001:10001 (non-root)" "got '$img_user'"; fi

    sdks=$(podman run --rm --entrypoint dotnet squid-service:dev --list-sdks 2>/dev/null)
    if [ -z "$sdks" ]; then pass "AC-01 no SDK inside the final image (dotnet --list-sdks empty)" "empty"
    else fail "AC-01 no SDK inside the final image (dotnet --list-sdks empty)" "got '$sdks'"; fi

    runtimes=$(podman run --rm --entrypoint dotnet squid-service:dev --list-runtimes 2>/dev/null)
    if echo "$runtimes" | grep -q 'Microsoft.NETCore.App' && echo "$runtimes" | grep -q 'Microsoft.AspNetCore.App'
    then pass "AC-01 runtimes present (ASP.NET shared framework copied in)" "$(echo "$runtimes" | tr '\n' ';')"
    else fail "AC-01 runtimes present (ASP.NET shared framework copied in)" "$(echo "$runtimes" | tr '\n' ';')"; fi

    # Base-image derivation: only sdk:10.0 carries DOTNET_SDK_VERSION, only aspnet/sdk carry
    # ASPNET_VERSION, runtime:10.0 carries neither (probed on this machine, not remembered).
    img_env=$(podman image inspect squid-service:dev --format '{{range .Config.Env}}{{.}};{{end}}' 2>/dev/null)
    if echo "$img_env" | grep -q 'DOTNET_SDK_VERSION='
    then fail "AC-01 final base is not sdk (no DOTNET_SDK_VERSION in env)" "present"
    else pass "AC-01 final base is not sdk (no DOTNET_SDK_VERSION in env)" "absent"; fi
    if echo "$img_env" | grep -q 'ASPNET_VERSION='
    then fail "AC-01 final base is not aspnet (no ASPNET_VERSION in env)" "present"
    else pass "AC-01 final base is not aspnet (no ASPNET_VERSION in env)" "absent"; fi
    if echo "$img_env" | grep -q 'ASPNETCORE_URLS=http://+:8080' && echo "$img_env" | grep -q 'HOME=/app'
    then pass "AC-01 Containerfile ENV applied (ASPNETCORE_URLS, HOME)" "present"
    else fail "AC-01 Containerfile ENV applied (ASPNETCORE_URLS, HOME)" "absent"; fi

    # GNU id refuses -u -g together ("cannot print only of more than one choice") - parse the
    # full `id` line instead.
    idline=$(podman run --rm --entrypoint id squid-service:dev 2>/dev/null | tr -d '\r' | grep 'uid=' | head -1)
    if echo "$idline" | grep -qE 'uid=10001\b' && echo "$idline" | grep -qE 'gid=10001\b'
    then pass "AC-05 container process runs as uid/gid 10001" "$idline"
    else fail "AC-05 container process runs as uid/gid 10001" "got '$idline'"; fi
  fi
fi

# compose orchestration (AC-02/03/04 dynamic) + exit-3 gate classification: Windows-only here -
# podman-compose is pip-installed on the Windows host PATH, not inside this WSL machine.
unv "AC-02/AC-03/AC-04 compose orchestration on Linux" "podman-compose provider not installed in this WSL machine (D-01 gate covers the Windows host only)"
unv "AC-06 exit-3 classification on Linux" "sdd-test.ps1 compose mode is PowerShell; Windows run carries this check"

if [ "$unverified" -gt 0 ]; then
  echo "note: $unverified check(s) UNVERIFIED on this platform - not green"
fi
if [ "$failed" -eq 0 ]; then
  echo "RESULT: PASS - all executed checks green"
  exit 0
fi
echo "RESULT: FAIL - $failed check(s) failed"
exit 1
