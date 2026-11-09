#!/usr/bin/env bash
# qa-check-task-0022 - Linux twin of qa-check-task-0022.ps1: same 17 check ids, same exit
# contract (0 only when SUMMARY total=17 fails=0). Same rules baked in: S-22 (the log's own
# exit: header and summary classify), S-16 (SHA256 PRE), S-23 (mtime after restore), the
# touches baseline of 2026-11-09 04:06 (-uall, full file paths). Pure ASCII.
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
base="$runs/20261109-040457-test-gate.log"
if [ -f "$base" ] && grep -qE '^exit: +0$' "$base"; then check GATE-BASELINE-EXIT 0 '20261109-040457-test-gate.log'
else check GATE-BASELINE-EXIT 1 'baseline gate log missing or exit not 0'; fi
b_fail=$(grep -oE 'Com falha: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
b_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${b_fail:-999}" = "0" ] && [ "${b_aprov:-0}" -eq 242 ]; then
  check GATE-BASELINE-SUMMARY 0 "Com falha=$b_fail Aprovado=$b_aprov (baseline 242)"
else
  check GATE-BASELINE-SUMMARY 1 "Com falha=$b_fail Aprovado=$b_aprov (need 0 and 242)"
fi

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate"; then check GATE-NEWEST-EXIT 0 "$(basename "$gate")"
else check GATE-NEWEST-EXIT 1 "${gate:-no gate log found}"; fi
g_fail=$(grep -oE 'Com falha: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
g_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${g_fail:-999}" = "0" ] && [ "${g_aprov:-0}" -eq 244 ]; then
  check GATE-NEWEST-SUMMARY 0 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 244 = 242 + 2 QA)"
else
  check GATE-NEWEST-SUMMARY 1 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 244)"
fi

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY (the Containers area) ---
filter=$(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
if [ -n "$filter" ] && grep -qE '^exit: +0$' "$filter"; then check FILTER-NEWEST-EXIT 0 "$(basename "$filter")"
else check FILTER-NEWEST-EXIT 1 "${filter:-no filter log found}"; fi
f_fail=$(grep -oE 'Com falha: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
f_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$filter" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${f_fail:-999}" = "0" ] && [ "${f_aprov:-0}" -eq 70 ]; then
  check FILTER-NEWEST-SUMMARY 0 "Com falha=$f_fail Aprovado=$f_aprov (need 0 and 70)"
else
  check FILTER-NEWEST-SUMMARY 1 "Com falha=$f_fail Aprovado=$f_aprov (need 0 and 70)"
fi

# --- FILTER-DEV7: fixed log of the seven new DEV tests ---
dev7="$runs/20261109-040703-test-filter.log"
if [ -f "$dev7" ] && grep -qE '^exit: +0$' "$dev7" && grep -qE 'Com falha: +0,' "$dev7" && grep -qE 'Aprovado: +7,' "$dev7"; then
  check FILTER-DEV7 0 '20261109-040703-test-filter.log (exit 0, Com falha 0, Aprovado 7)'
else
  check FILTER-DEV7 1 '20261109-040703-test-filter.log missing or not 7/7 green'
fi

# --- FILTER-QATEST: newest log of the QA's own filter must be green with the two QA facts ---
qa_filter=""
for f in $(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sort); do
  if head -1 "$f" | grep -q 'FullyQualifiedName~Qa0022'; then qa_filter="$f"; fi
done
if [ -n "$qa_filter" ] && grep -qE '^exit: +0$' "$qa_filter" && grep -qE 'Com falha: +0,' "$qa_filter" && grep -qE 'Aprovado: +2,' "$qa_filter"; then
  check FILTER-QATEST 0 "$(basename "$qa_filter") (exit 0, Com falha 0, Aprovado 2)"
else
  check FILTER-QATEST 1 "${qa_filter:-no Qa0022 filter log found}"
fi

# --- RESTORE-SHA256 (S-16) ---
pre1="7a7ba9c2cd679d7ea011f6400123ac5327616a62a9dd6c5fc3c02bcdd504f507"
pre2="daee4cc7440c4d3c0abe7e6d72c572edae1f0049f038d4759fe61b3c187725c0"
pre3="f35f5a56683f301a12e7f30955023fbdb5b70f3e8396ea4efb81fd039dfbca5a"
h1=$(sha256sum 'backend/src/Squid.Service/SqliteContainerStore.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h2=$(sha256sum 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h3=$(sha256sum 'backend/src/Squid.Api/Containers/Ownership.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
ok1=1; ok2=1; ok3=1
[ "$h1" = "$pre1" ] && ok1=0
[ "$h2" = "$pre2" ] && ok2=0
[ "$h3" = "$pre3" ] && ok3=0
if [ "$ok1" -eq 0 ] && [ "$ok2" -eq 0 ] && [ "$ok3" -eq 0 ]; then
  check RESTORE-SHA256 0 '3/3 files match PRE'
else
  check RESTORE-SHA256 1 "SqliteContainerStore=$ok1 ContainerEndpoints=$ok2 Ownership=$ok3 (0=match)"
fi

# --- TESTFILE-INTEGRITY: the QA test file was never a mutation target ---
qa_pre="927533c3e775d0e31afdd2047e73820c2611ab8da912b554a4b141c70f9f2c3e"
qa_now=$(sha256sum 'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
if [ "$qa_now" = "$qa_pre" ]; then check TESTFILE-INTEGRITY 0 "$qa_now"
else check TESTFILE-INTEGRITY 1 "$qa_now"; fi

# --- MUTATION-RESIDUE ---
res=$(grep -rIl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
if [ "$res" -eq 0 ]; then check MUTATION-RESIDUE 0 "QA-MUTATION hits under backend/ = 0"
else check MUTATION-RESIDUE 1 "QA-MUTATION hits under backend/ = $res"; fi

# --- TOUCHES-NO-OUTSIDE (AC-05): baseline 2026-11-09 04:06 vs git, only 4 QA artifacts new ---
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
backend/src/Squid.Api/Containers/Ownership.cs
backend/src/Squid.Domain/Containers/Container.cs
backend/src/Squid.Domain/Containers/ContainerMembership.cs
backend/src/Squid.Domain/Containers/ContainerSlug.cs
backend/src/Squid.Domain/Containers/MembershipRules.cs
backend/src/Squid.Domain/Containers/SuccessionRules.cs
backend/src/Squid.Service/SqliteContainerStore.cs
backend/src/Squid.Service/SqlitePolicyAuditStore.cs
backend/tests/Squid.Tests/Containers/AdminOverrideAndConfiguredTimersTests.cs
backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs
backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs
backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs
backend/tests/Squid.Tests/Containers/ContainerTests.cs
backend/tests/Squid.Tests/Containers/EntryAndOwnerInvariantTrailTests.cs
backend/tests/Squid.Tests/Containers/MemberModerationRoutesTests.cs
backend/tests/Squid.Tests/Containers/MembershipRoutesTests.cs
backend/tests/Squid.Tests/Containers/MembershipRulesTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs
backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs
backend/tests/Squid.Tests/Containers/Qa0018MembershipPageAndTrailTests.cs
backend/tests/Squid.Tests/Containers/Qa0019MemberModerationEdgeTests.cs
backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs
backend/tests/Squid.Tests/Containers/Sprint2ClosingFacetTests.cs
backend/tests/Squid.Tests/Containers/SuccessionHost.cs
backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs
backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs
backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs
docs/sdd/qa/QA-0016-nucleo-do-container.md
docs/sdd/qa/QA-0017-politica-auditoria-operador.md
docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md
docs/sdd/qa/QA-0019-papeis-moderacao-avaliador.md
docs/sdd/qa/QA-0020-sucessao-transferencia-exclusao.md
docs/sdd/qa/qa-check-task-0018.ps1
docs/sdd/qa/qa-check-task-0018.sh
docs/sdd/qa/qa-check-task-0019.ps1
docs/sdd/qa/qa-check-task-0019.sh
docs/sdd/qa/qa-check-task-0020.ps1
docs/sdd/qa/qa-check-task-0020.sh
docs/sdd/qa/qa-check-task-0021.ps1
docs/sdd/qa/qa-check-task-0021.sh
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
backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs
docs/sdd/qa/QA-0021-descoberta-listagem.md
docs/sdd/qa/qa-check-task-0022.ps1
docs/sdd/qa/qa-check-task-0022.sh
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

# --- MTIME-SWEEP: only the three mutated/restored files + the QA test file after 04:06 ---
expected=$(cat <<'EOF'
backend/src/Squid.Api/Containers/ContainerEndpoints.cs
backend/src/Squid.Api/Containers/Ownership.cs
backend/src/Squid.Service/SqliteContainerStore.cs
backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs
EOF
)
sw=$(find backend/src backend/tests -type f -newermt '2026-11-09 04:06:00' ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
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
  check MTIME-SWEEP 0 "files after 04:06 = 4; unexpected = 0; expected missing = 0"
else
  check MTIME-SWEEP 1 "files after 04:06 = $sw_count; unexpected = $unexpected; expected missing = $absent"
fi

# --- ROUTES-STATIC (direct read; S-24) ---
c='backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
st='backend/src/Squid.Service/SqliteContainerStore.cs'
ow='backend/src/Squid.Api/Containers/Ownership.cs'
rs=1
grep -qF 'app.MapGet("/api/containers/search", SearchAsync);' "$c" || rs=0
grep -qF 'if (query.Length == 0)' "$c" || rs=0
grep -qF 'return Results.Json(new { items });' "$c" || rs=0
grep -qF 'seen ? container.MemberCount : null' "$c" || rs=0
grep -qF 'membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited' "$c" || rs=0
grep -qF '&& container.Policy.Join is JoinPolicy.Open or JoinPolicy.Request;' "$c" || rs=0
grep -qF 'ActorId = membership?.ActorId ?? ActorId(context, current.Handle),' "$c" || rs=0
grep -qF 'request.PublisherIds ?? before.PublisherIds' "$c" || rs=0
grep -qF 'container.IsSearchable' "$st" || rs=0
grep -qF 'container.Name.Contains(query, StringComparison.OrdinalIgnoreCase)' "$st" || rs=0
grep -qF 'SuccessionRules.Transfer(members, container.Slug, newOwnerHandle, newOwnerActorId, now)' "$ow" || rs=0
grep -qF 'OwnerActorId = newOwnerActorId,' "$ow" || rs=0
if [ "$rs" -eq 1 ]; then check ROUTES-STATIC 0 'search route + blank-q 400 + items envelope + memberCount gate + CanEnter Invited + join actor + publisher seat + IsSearchable + ci match + Ownership move (12 markers)'
else check ROUTES-STATIC 1 'one of the twelve route/rule markers is missing'; fi

# --- FACTS-STATIC: 7 DEV (3+3+6) + 2 QA [Fact] where they claim ---
fact_bad=""
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/ContainerSearchRoutesTests.cs' || true)
[ "$n" -eq 3 ] || fact_bad="$fact_bad ContainerSearchRoutes=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/Sprint2ClosingFacetTests.cs' || true)
[ "$n" -eq 3 ] || fact_bad="$fact_bad Sprint2ClosingFacet=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs' || true)
[ "$n" -eq 6 ] || fact_bad="$fact_bad ContainerStore=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/Qa0022SearchDiscoveryTests.cs' || true)
[ "$n" -eq 2 ] || fact_bad="$fact_bad Qa0022=$n"
if [ -z "$fact_bad" ]; then check FACTS-STATIC 0 '3+3+6 DEV (7 new) + 2 QA facts where expected'
else check FACTS-STATIC 1 "$fact_bad"; fi

# --- QA-DOC: the verdict document exists, names this task and carries the verdict ---
doc='docs/sdd/qa/QA-0021-descoberta-listagem.md'
if [ -f "$doc" ] && grep -q -- '- task: TASK-0022' "$doc" && grep -q -- '- verdict: PASSED' "$doc"; then
  check QA-DOC 0 "$doc (task id + verdict)"
else
  check QA-DOC 1 "$doc missing or without task id/verdict"
fi

# --- STATE-CHECK (Windows writer tool via interop; the .ps1 is the authority) ---
state=$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0022-descoberta-listagem.md' 2>&1)
state_rc=$?
state_line=$(printf '%s\n' "$state" | grep -m1 'check:' || true)
if [ "$state_rc" -eq 0 ] && printf '%s' "$state_line" | grep -q 'check: OK'; then
  check STATE-CHECK 0 "$state_line"
else
  check STATE-CHECK 1 "rc=$state_rc ${state_line:-$(printf '%s' "$state" | head -1)}"
fi

echo "SUMMARY total=17 fails=$fails"
if [ "$fails" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'; exit 1
