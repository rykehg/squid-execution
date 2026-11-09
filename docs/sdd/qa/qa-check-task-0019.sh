#!/usr/bin/env bash
# qa-check-task-0019 — Linux twin of qa-check-task-0019.ps1: same 13 check ids, same exit
# contract (0 only when SUMMARY total=13 fails=0). Same rules baked in: S-22 (the log's own
# exit: header and summary classify), S-16 (SHA256 PRE), S-23 (mtime after restore), the
# touches baseline of 2026-11-08 22:05, AC-07 origin rule, SC-006 loop, TASK-0020 boundary.
# Pure ASCII. The .ps1 remains the Windows authority; this twin proves the checks close equal.
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$repo" || exit 2
fails=0
check() { # id  ok(0|1)  detail
  if [ "$2" -eq 0 ]; then echo "CHECK $1 PASS $3"
  else fails=$((fails + 1)); echo "CHECK $1 FAIL $3"
  fi
}
runs="docs/sdd/runs/test-runs"

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate"; then check GATE-NEWEST-EXIT 0 "$(basename "$gate")"
else check GATE-NEWEST-EXIT 1 "${gate:-no gate log found}"; fi
g_fail=$(grep -oE 'Com falha: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
g_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${g_fail:-999}" = "0" ] && [ "${g_aprov:-0}" -ge 201 ]; then check GATE-NEWEST-SUMMARY 0 "Com falha=$g_fail Aprovado=$g_aprov (need falha=0 and aprovado>=201)"
else check GATE-NEWEST-SUMMARY 1 "Com falha=$g_fail Aprovado=$g_aprov (need falha=0 and aprovado>=201)"; fi

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY ---
filter=$(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
if [ -n "$filter" ] && grep -qE '^exit: +0$' "$filter"; then check FILTER-NEWEST-EXIT 0 "$(basename "$filter")"
else check FILTER-NEWEST-EXIT 1 "${filter:-no filter log found}"; fi
f_fail=$(grep -oE 'Com falha: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
f_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${f_fail:-999}" = "0" ] && [ "${f_aprov:-0}" -ge 34 ]; then check FILTER-NEWEST-SUMMARY 0 "Com falha=$f_fail Aprovado=$f_aprov (need falha=0 and aprovado>=34)"
else check FILTER-NEWEST-SUMMARY 1 "Com falha=$f_fail Aprovado=$f_aprov (need falha=0 and aprovado>=34)"; fi

# --- RESTORE-SHA256 (S-16) ---
pre1="4e56720255d5e50e04afc0c76a890fb7d144893075e09d7d234c1d2db311a979"
pre2="57312633e7978231d4c309913b72dcd172f90a92cc843f8f7e4d025368734803"
h1=$(sha256sum 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h2=$(sha256sum 'backend/src/Squid.Domain/Containers/MembershipRules.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
ok1=1; ok2=1
[ "$h1" = "$pre1" ] && ok1=0
[ "$h2" = "$pre2" ] && ok2=0
if [ "$ok1" -eq 0 ] && [ "$ok2" -eq 0 ]; then check RESTORE-SHA256 0 "2/2 files match PRE"
else check RESTORE-SHA256 1 "ContainerEndpoints match=$ok1 MembershipRules match=$ok2 (0=match)"; fi

# --- MUTATION-RESIDUE ---
res=$(grep -rIl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
if [ "$res" -eq 0 ]; then check MUTATION-RESIDUE 0 "QA-MUTATION hits under backend/ = 0"
else check MUTATION-RESIDUE 1 "QA-MUTATION hits under backend/ = $res"; fi

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline 2026-11-08 22:05 vs git, only 4 QA artifacts new ---
baseline=$(cat <<'EOF'
backend/src/Squid.Service/SquidServiceHost.cs
docs/sdd/ideas/BACKLOG.md
docs/sdd/plan/plan.md
backend/src/Squid.Api/Admin/
backend/src/Squid.Api/Containers/
backend/src/Squid.Domain/Containers/
backend/src/Squid.Service/SqliteContainerStore.cs
backend/src/Squid.Service/SqlitePolicyAuditStore.cs
backend/tests/Squid.Tests/Containers/
docs/sdd/qa/QA-0016-nucleo-do-container.md
docs/sdd/qa/QA-0017-politica-auditoria-operador.md
docs/sdd/qa/qa-check-task-0018.ps1
docs/sdd/qa/qa-check-task-0018.sh
docs/sdd/specs/SPEC-0008-containers-sociais-p03.md
docs/sdd/tasks/TASK-0017-nucleo-do-container.md
docs/sdd/tasks/TASK-0018-politica-auditoria-operador.md
docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md
docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md
docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md
docs/sdd/tasks/TASK-0022-descoberta-listagem.md
EOF
)
allowed_new=$(cat <<'EOF'
docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md
docs/sdd/qa/qa-check-task-0019.ps1
docs/sdd/qa/qa-check-task-0019.sh
docs/sdd/qa/qa-mutate-task-0019.ps1
EOF
)
porcelain=$(git -c safe.directory='*' status --porcelain 2>/dev/null)
git_rc=$?
missing=0; extras=0; new_allowed=0
if [ "$git_rc" -eq 0 ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line:3}"
    if ! printf '%s\n' "$baseline" | grep -qxF "$path"; then
      if printf '%s\n' "$allowed_new" | grep -qxF "$path"; then new_allowed=$((new_allowed + 1))
      else extras=$((extras + 1)); fi
    fi
  done <<< "$porcelain"
  while IFS= read -r b; do
    [ -z "$b" ] && continue
    printf '%s\n' "$porcelain" | cut -c4- | grep -qxF "$b" || missing=$((missing + 1))
  done <<< "$baseline"
fi
if [ "$git_rc" -eq 0 ] && [ "$missing" -eq 0 ] && [ "$extras" -eq 0 ]; then
  check TOUCHES-NO-OUTSIDE 0 "git=1 missing=0 out-of-scope=0 new-allowed=$new_allowed/4"
else
  check TOUCHES-NO-OUTSIDE 1 "git_rc=$git_rc missing=$missing out-of-scope=$extras new-allowed=$new_allowed/4"
fi

# --- MTIME-SWEEP: only the partial artifact + the two mutated/restored files after 19:55 ---
expected=$(cat <<'EOF'
backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs
backend/src/Squid.Domain/Containers/MembershipRules.cs
backend/src/Squid.Api/Containers/ContainerEndpoints.cs
EOF
)
sw=$(find backend/src backend/tests -type f -newermt '2026-11-08 19:55:00' ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
sw_count=$(printf '%s\n' "$sw" | grep -c . || true)
unexpected=0
while IFS= read -r f; do
  [ -z "$f" ] && continue
  printf '%s\n' "$expected" | grep -qxF "$f" || unexpected=$((unexpected + 1))
done <<< "$sw"
absent=0
while IFS= read -r e; do
  [ -z "$e" ] && continue
  printf '%s\n' "$sw" | grep -qxF "$e" || absent=$((absent + 1))
done <<< "$expected"
if [ "$sw_count" -eq 3 ] && [ "$unexpected" -eq 0 ] && [ "$absent" -eq 0 ]; then
  check MTIME-SWEEP 0 "files after 19:55 = 3; unexpected = 0; expected missing = 0"
else
  check MTIME-SWEEP 1 "files after 19:55 = $sw_count; unexpected = $unexpected; expected missing = $absent"
fi

# --- AC07-STATIC ---
f_routes='backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs'
f_patch='backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs'
f_audit='backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs'
https_left=$(grep -oh 'https://' "$f_routes" "$f_patch" "$f_audit" 2>/dev/null | wc -l | tr -d ' ')
scheme_n=$(grep -oh 'BaseAddress!\.Scheme' "$f_routes" "$f_patch" "$f_audit" 2>/dev/null | wc -l | tr -d ' ')
prefix=1
grep -qF 'Assert.StartsWith("http://127.0.0.1:"' "$f_routes" || prefix=0
if [ "$https_left" -eq 0 ] && [ "$scheme_n" -ge 5 ] && [ "$prefix" -eq 1 ]; then
  check AC07-STATIC 0 "https:// left = 0; scheme+Host assertions = $scheme_n; http prefix assert = 1"
else
  check AC07-STATIC 1 "https:// left = $https_left; scheme+Host assertions = $scheme_n; http prefix assert = $prefix"
fi

# --- SC006-STATIC ---
m='backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs'
sc=1
grep -qF 'var attempts = 0;' "$m" || sc=0
grep -qF 'attempts++' "$m" || sc=0
grep -qF 'Assert.Equal(100, attempts)' "$m" || sc=0
grep -qF 'for (var actor = 0; actor < 10; actor++)' "$m" || sc=0
grep -qF 'for (var attempt = 0; attempt < 10; attempt++)' "$m" || sc=0
if [ "$sc" -eq 1 ]; then check SC006-STATIC 0 '10x10 loop + attempts=100 assertion read from MembershipRoutesTests.cs'
else check SC006-STATIC 1 'one of the five SC-006 markers is missing'; fi

# --- ROUTES-STATIC ---
c='backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
rs=1
grep -qF 'app.MapPost("/api/containers/{slug}/join", JoinAsync);' "$c" || rs=0
grep -qF 'app.MapPost("/api/containers/{slug}/invites", InviteAsync);' "$c" || rs=0
grep -qF 'app.MapPost("/api/containers/{slug}/leave", LeaveAsync);' "$c" || rs=0
grep -qF 'app.MapGet("/api/containers/{slug}/members", MembersAsync);' "$c" || rs=0
grep -qF '$"{context.Request.Scheme}://{context.Request.Host.Value}"' "$c" || rs=0
grep -qF 'private const int DefaultLimit = 100;' "$c" || rs=0
grep -qF 'private const int MaxLimit = 1000;' "$c" || rs=0
grep -qF 'state == MembershipState.Active' "$c" || rs=0
if [ "$rs" -eq 1 ]; then check ROUTES-STATIC 0 'join/invites/leave/members + scheme+Host origin + limit 100/1000 + Active filter'
else check ROUTES-STATIC 1 'one of the eight route markers is missing'; fi

# --- STATE-CHECK (Windows writer tool via interop; the .ps1 is the authority) ---
state=$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0019-membership-entrada-e-listagem.md' 2>&1)
state_rc=$?
state_line=$(printf '%s\n' "$state" | grep -m1 'check:' || true)
if [ "$state_rc" -eq 0 ] && printf '%s' "$state_line" | grep -q 'check: OK'; then
  check STATE-CHECK 0 "$state_line"
else
  check STATE-CHECK 1 "rc=$state_rc ${state_line:-$(printf '%s' "$state" | head -1)}"
fi

# --- EVALUATOR-UNTOUCHED (TASK-0020 boundary) ---
ev="94cddfa4dc578b2b6655a702136177a2bdb68f25045318f8ac73a2517d6d6dcf"
ev_now=$(sha256sum 'backend/src/Squid.Domain/IPolicyEvaluator.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
if [ "$ev_now" = "$ev" ]; then check EVALUATOR-UNTOUCHED 0 "$ev_now"
else check EVALUATOR-UNTOUCHED 1 "$ev_now"; fi

echo "SUMMARY total=13 fails=$fails"
if [ "$fails" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'; exit 1
