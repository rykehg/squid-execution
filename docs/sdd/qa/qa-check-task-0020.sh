#!/usr/bin/env bash
# qa-check-task-0020 - Linux twin of qa-check-task-0020.ps1: same 14 check ids, same exit
# contract (0 only when SUMMARY total=14 fails=0). Same rules baked in: S-22 (the log's own
# exit: header and summary classify), S-16 (SHA256 PRE), S-23 (mtime after restore), the
# touches baseline of 2026-11-09 00:06 (-uall, full file paths). Pure ASCII.
# The .ps1 remains the Windows authority; this twin proves the checks close equal.
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

# --- GATE-BASELINE-EXIT / GATE-BASELINE-SUMMARY (this QA's start gate, S-22) ---
base="$runs/20261109-000908-test-gate.log"
if [ -f "$base" ] && grep -qE '^exit: +0$' "$base"; then check GATE-BASELINE-EXIT 0 '20261109-000908-test-gate.log'
else check GATE-BASELINE-EXIT 1 'baseline gate log missing or exit not 0'; fi
b_fail=$(grep -oE 'Com falha: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
b_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${b_fail:-999}" = "0" ] && [ "${b_aprov:-0}" -eq 214 ]; then
  check GATE-BASELINE-SUMMARY 0 "Com falha=$b_fail Aprovado=$b_aprov (baseline 214)"
else
  check GATE-BASELINE-SUMMARY 1 "Com falha=$b_fail Aprovado=$b_aprov (need 0 and 214)"
fi

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate"; then check GATE-NEWEST-EXIT 0 "$(basename "$gate")"
else check GATE-NEWEST-EXIT 1 "${gate:-no gate log found}"; fi
g_fail=$(grep -oE 'Com falha: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
g_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${g_fail:-999}" = "0" ] && [ "${g_aprov:-0}" -eq 216 ]; then
  check GATE-NEWEST-SUMMARY 0 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 216 = 214 + 2 QA)"
else
  check GATE-NEWEST-SUMMARY 1 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 216)"
fi

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY ---
filter=$(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
if [ -n "$filter" ] && grep -qE '^exit: +0$' "$filter"; then check FILTER-NEWEST-EXIT 0 "$(basename "$filter")"
else check FILTER-NEWEST-EXIT 1 "${filter:-no filter log found}"; fi
f_fail=$(grep -oE 'Com falha: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
f_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${f_fail:-999}" = "0" ] && [ "${f_aprov:-0}" -eq 7 ]; then
  check FILTER-NEWEST-SUMMARY 0 "Com falha=$f_fail Aprovado=$f_aprov (need 0 and 7)"
else
  check FILTER-NEWEST-SUMMARY 1 "Com falha=$f_fail Aprovado=$f_aprov (need 0 and 7)"
fi

# --- RESTORE-SHA256 (S-16) ---
pre1="f50555171261ebac49a26aef85276d9ea54deb1813f38b47b2bdd5185e57366f"
pre2="bd83790f627272a5fd8509da2bfe80ea75cb624e49f213a9c13e6ff99a402a00"
pre3="e04cd59c9d73e79ed61d5b56ae4a69757fe11ee838cf3cc77ab233d27c87db47"
h1=$(sha256sum 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h2=$(sha256sum 'backend/src/Squid.Domain/IPolicyEvaluator.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h3=$(sha256sum 'backend/src/Squid.Domain/Containers/MembershipRules.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
ok1=1; ok2=1; ok3=1
[ "$h1" = "$pre1" ] && ok1=0
[ "$h2" = "$pre2" ] && ok2=0
[ "$h3" = "$pre3" ] && ok3=0
if [ "$ok1" -eq 0 ] && [ "$ok2" -eq 0 ] && [ "$ok3" -eq 0 ]; then check RESTORE-SHA256 0 '3/3 files match PRE'
else check RESTORE-SHA256 1 "ContainerEndpoints=$ok1 IPolicyEvaluator=$ok2 MembershipRules=$ok3 (0=match)"; fi

# --- TESTFILE-INTEGRITY: the QA test file was never a mutation target ---
qa_pre="cb2e3bcb10a973999cf63ae508348cfe51f1b301bd968f34ee91fb8b7953e1b1"
qa_now=$(sha256sum 'backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
if [ "$qa_now" = "$qa_pre" ]; then check TESTFILE-INTEGRITY 0 "$qa_now"
else check TESTFILE-INTEGRITY 1 "$qa_now"; fi

# --- MUTATION-RESIDUE ---
res=$(grep -rIl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
if [ "$res" -eq 0 ]; then check MUTATION-RESIDUE 0 "QA-MUTATION hits under backend/ = 0"
else check MUTATION-RESIDUE 1 "QA-MUTATION hits under backend/ = $res"; fi

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline 2026-11-09 00:06 vs git, only 4 QA artifacts new ---
baseline=$(cat <<'EOF'
backend/src/Squid.Domain/IPolicyEvaluator.cs
backend/src/Squid.Service/SquidServiceHost.cs
docs/sdd/ideas/BACKLOG.md
docs/sdd/plan/plan.md
backend/src/Squid.Api/Admin/AdminEndpoints.cs
backend/src/Squid.Api/Containers/ContainerEndpoints.cs
backend/src/Squid.Api/Containers/ContainerView.cs
backend/src/Squid.Api/Containers/IContainerStore.cs
backend/src/Squid.Api/Containers/IPolicyAuditStore.cs
backend/src/Squid.Domain/Containers/Container.cs
backend/src/Squid.Domain/Containers/ContainerMembership.cs
backend/src/Squid.Domain/Containers/ContainerSlug.cs
backend/src/Squid.Domain/Containers/MembershipRules.cs
backend/src/Squid.Service/SqliteContainerStore.cs
backend/src/Squid.Service/SqlitePolicyAuditStore.cs
backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs
backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs
backend/tests/Squid.Tests/Containers/ContainerTests.cs
backend/tests/Squid.Tests/Containers/MemberModerationRoutesTests.cs
backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs
backend/tests/Squid.Tests/Containers/MembershipRulesTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs
backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs
backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs
backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs
docs/sdd/qa/QA-0016-nucleo-do-container.md
docs/sdd/qa/QA-0017-politica-auditoria-operador.md
docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md
docs/sdd/qa/qa-check-task-0018.ps1
docs/sdd/qa/qa-check-task-0018.sh
docs/sdd/qa/qa-check-task-0019.ps1
docs/sdd/qa/qa-check-task-0019.sh
docs/sdd/qa/qa-mutate-task-0019.ps1
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
backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs
docs/sdd/qa/QA-0019-papeis-moderacao-avaliador.md
docs/sdd/qa/qa-check-task-0020.ps1
docs/sdd/qa/qa-check-task-0020.sh
EOF
)
porcelain=$(git -c safe.directory='*' status --porcelain -uall 2>/dev/null)
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

# --- MTIME-SWEEP: only the three mutated/restored files + the QA test file after 00:05 ---
expected=$(cat <<'EOF'
backend/src/Squid.Api/Containers/ContainerEndpoints.cs
backend/src/Squid.Domain/IPolicyEvaluator.cs
backend/src/Squid.Domain/Containers/MembershipRules.cs
backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs
EOF
)
sw=$(find backend/src backend/tests -type f -newermt '2026-11-09 00:05:00' ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
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
if [ "$sw_count" -eq 4 ] && [ "$unexpected" -eq 0 ] && [ "$absent" -eq 0 ]; then
  check MTIME-SWEEP 0 "files after 00:05 = 4; unexpected = 0; expected missing = 0"
else
  check MTIME-SWEEP 1 "files after 00:05 = $sw_count; unexpected = $unexpected; expected missing = $absent"
fi

# --- ROUTES-STATIC (direct read; S-24) ---
c='backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
ev='backend/src/Squid.Domain/IPolicyEvaluator.cs'
ru='backend/src/Squid.Domain/Containers/MembershipRules.cs'
tr='backend/src/Squid.Api/Containers/IPolicyAuditStore.cs'
rs=1
grep -qF 'app.MapPatch("/api/containers/{slug}/members/{*actorId}", PatchMemberAsync);' "$c" || rs=0
grep -qF 'if (forRead && membership?.State == MembershipState.Banned) return false;' "$c" || rs=0
grep -qF 'MembershipRules.Apply(target.State, change.Value)' "$c" || rs=0
grep -qF 'public sealed record PolicyActor(ContainerRole Role, MembershipState? State, bool IsPublisher);' "$ev" || rs=0
grep -qF 'public const string ChangeRole = "ChangeRole";' "$tr" || rs=0
grep -qF 'MembershipChange.Reject) => new MembershipTransition(true, null)' "$ru" || rs=0
if [ "$rs" -eq 1 ]; then check ROUTES-STATIC 0 'PATCH members route + forRead Banned guard + Apply + PolicyActor record + ChangeRole const + reject edge'
else check ROUTES-STATIC 1 'one of the six route/rule markers is missing'; fi

# --- FACTS-STATIC: 7+5+5+2 [Fact] where they claim ---
fact_bad=""
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs' || true)
[ "$n" -eq 7 ] || fact_bad="$fact_bad PolicyActorContext=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/MembershipRulesTests.cs' || true)
[ "$n" -eq 5 ] || fact_bad="$fact_bad MembershipRules=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/MemberModerationRoutesTests.cs' || true)
[ "$n" -eq 5 ] || fact_bad="$fact_bad MemberModeration=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs' || true)
[ "$n" -eq 2 ] || fact_bad="$fact_bad Qa0019=$n"
if [ -z "$fact_bad" ]; then check FACTS-STATIC 0 '7+5+5+2 facts where expected'
else check FACTS-STATIC 1 "$fact_bad"; fi

# --- STATE-CHECK (Windows writer tool via interop; the .ps1 is the authority) ---
state=$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0020-papeis-moderacao-avaliador.md' 2>&1)
state_rc=$?
state_line=$(printf '%s\n' "$state" | grep -m1 'check:' || true)
if [ "$state_rc" -eq 0 ] && printf '%s' "$state_line" | grep -q 'check: OK'; then
  check STATE-CHECK 0 "$state_line"
else
  check STATE-CHECK 1 "rc=$state_rc ${state_line:-$(printf '%s' "$state" | head -1)}"
fi

echo "SUMMARY total=14 fails=$fails"
if [ "$fails" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'; exit 1
