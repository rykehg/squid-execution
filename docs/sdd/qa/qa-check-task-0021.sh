#!/usr/bin/env bash
# qa-check-task-0021 - Linux twin of qa-check-task-0021.ps1: same 14 check ids, same exit
# contract (0 only when SUMMARY total=14 fails=0). Same rules baked in: S-22 (the log's own
# exit: header and summary classify), S-16 (SHA256 PRE), S-23 (mtime after restore), the
# touches baseline of 2026-11-09 02:11 (-uall, full file paths). Pure ASCII.
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
base="$runs/20261109-021141-test-gate.log"
if [ -f "$base" ] && grep -qE '^exit: +0$' "$base"; then check GATE-BASELINE-EXIT 0 '20261109-021141-test-gate.log'
else check GATE-BASELINE-EXIT 1 'baseline gate log missing or exit not 0'; fi
b_fail=$(grep -oE 'Com falha: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
b_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${b_fail:-999}" = "0" ] && [ "${b_aprov:-0}" -eq 228 ]; then
  check GATE-BASELINE-SUMMARY 0 "Com falha=$b_fail Aprovado=$b_aprov (baseline 228)"
else
  check GATE-BASELINE-SUMMARY 1 "Com falha=$b_fail Aprovado=$b_aprov (need 0 and 228)"
fi

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate"; then check GATE-NEWEST-EXIT 0 "$(basename "$gate")"
else check GATE-NEWEST-EXIT 1 "${gate:-no gate log found}"; fi
g_fail=$(grep -oE 'Com falha: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
g_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${g_fail:-999}" = "0" ] && [ "${g_aprov:-0}" -eq 235 ]; then
  check GATE-NEWEST-SUMMARY 0 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 235 = 228 + 7 QA)"
else
  check GATE-NEWEST-SUMMARY 1 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 235)"
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
pre1="b5efd5a0a3869a39fdce27f9007d9caa0c1248561bf6a9c131dec2f5aa007c1e"
pre3="57a991cafbaf642a1229b0449e5cec76ee9312c11f04203096c620d55a8f8c74"
pre4="f7d0df3268a02421b94078f9736b4fc3d30ac97be6f69aa322f3d8c300379410"
h1=$(sha256sum 'backend/src/Squid.Api/Containers/ContainerEndpoints.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h3=$(sha256sum 'backend/src/Squid.Api/Admin/AdminEndpoints.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h4=$(sha256sum 'backend/src/Squid.Domain/Containers/SuccessionRules.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
ok1=1; ok3=1; ok4=1
[ "$h1" = "$pre1" ] && ok1=0
[ "$h3" = "$pre3" ] && ok3=0
[ "$h4" = "$pre4" ] && ok4=0
if [ "$ok1" -eq 0 ] && [ "$ok3" -eq 0 ] && [ "$ok4" -eq 0 ]; then
  check RESTORE-SHA256 0 '3/3 files match PRE'
else
  check RESTORE-SHA256 1 "ContainerEndpoints=$ok1 AdminEndpoints=$ok3 SuccessionRules=$ok4 (0=match)"
fi

# --- TESTFILE-INTEGRITY: the QA test file was never a mutation target ---
qa_pre="7423d8fec6f557ea293c8489fd19db71431d4e85272b197893c0ecb1c678aa97"
qa_now=$(sha256sum 'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
if [ "$qa_now" = "$qa_pre" ]; then check TESTFILE-INTEGRITY 0 "$qa_now"
else check TESTFILE-INTEGRITY 1 "$qa_now"; fi

# --- MUTATION-RESIDUE ---
res=$(grep -rIl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
if [ "$res" -eq 0 ]; then check MUTATION-RESIDUE 0 "QA-MUTATION hits under backend/ = 0"
else check MUTATION-RESIDUE 1 "QA-MUTATION hits under backend/ = $res"; fi

# --- TOUCHES-NO-OUTSIDE (AC-06): baseline 2026-11-09 02:11 vs git, only 4 QA artifacts new ---
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
backend/tests/Squid.Tests/Containers/SuccessionHost.cs
backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs
backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs
backend/tests/Squid.Tests/Domain/PolicyActorContextTests.cs
docs/sdd/qa/QA-0016-nucleo-do-container.md
docs/sdd/qa/QA-0017-politica-auditoria-operador.md
docs/sdd/qa/QA-0018-membership-entrada-e-listagem.md
docs/sdd/qa/QA-0019-papeis-moderacao-avaliador.md
docs/sdd/qa/qa-check-task-0018.ps1
docs/sdd/qa/qa-check-task-0018.sh
docs/sdd/qa/qa-check-task-0019.ps1
docs/sdd/qa/qa-check-task-0019.sh
docs/sdd/qa/qa-check-task-0020.ps1
docs/sdd/qa/qa-check-task-0020.sh
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
backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs
docs/sdd/qa/QA-0020-sucessao-transferencia-exclusao.md
docs/sdd/qa/qa-check-task-0021.ps1
docs/sdd/qa/qa-check-task-0021.sh
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

# --- MTIME-SWEEP: only the three mutated/restored files + the QA test file after 02:06 ---
expected=$(cat <<'EOF'
backend/src/Squid.Api/Containers/ContainerEndpoints.cs
backend/src/Squid.Api/Admin/AdminEndpoints.cs
backend/src/Squid.Domain/Containers/SuccessionRules.cs
backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs
EOF
)
sw=$(find backend/src backend/tests -type f -newermt '2026-11-09 02:06:00' ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
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
  check MTIME-SWEEP 0 "files after 02:06 = 4; unexpected = 0; expected missing = 0"
else
  check MTIME-SWEEP 1 "files after 02:06 = $sw_count; unexpected = $unexpected; expected missing = $absent"
fi

# --- ROUTES-STATIC (direct read; S-24) ---
c='backend/src/Squid.Api/Containers/ContainerEndpoints.cs'
ad='backend/src/Squid.Api/Admin/AdminEndpoints.cs'
ru='backend/src/Squid.Domain/Containers/SuccessionRules.cs'
tr='backend/src/Squid.Api/Containers/IPolicyAuditStore.cs'
rs=1
grep -qF 'app.MapPost("/api/containers/{slug}/claim", ClaimAsync);' "$c" || rs=0
grep -qF 'app.MapDelete("/api/containers/{slug}", DeleteAsync);' "$c" || rs=0
grep -qF 'app.MapPost("/api/admin/containers/{slug}/transfer", TransferAsync);' "$ad" || rs=0
grep -qF 'if (membership?.State is MembershipState.Active or MembershipState.Silenced or MembershipState.Invited)' "$c" || rs=0
grep -qF 'ToString(), slug, StringComparison.Ordinal))' "$c" || rs=0
grep -qF 'if (change is MembershipChange.Ban && target.Role == MembershipRole.Owner)' "$c" || rs=0
grep -qF 'if (target.Role == MembershipRole.Owner)' "$c" || rs=0
grep -qF 'now >= ownerLastActiveAt.AddDays(inactivityDays)' "$ru" || rs=0
grep -qF 'target with { Role = MembershipRole.Owner, State = MembershipState.Active }' "$ru" || rs=0
grep -qF 'public const string Successor = "Successor";' "$tr" || rs=0
if [ "$rs" -eq 1 ]; then check ROUTES-STATIC 0 'claim+delete+admin-transfer routes + CanEnter Invited + exact confirm + Owner guards + ClaimAllowed + Transfer Active + Successor const'
else check ROUTES-STATIC 1 'one of the ten route/rule markers is missing'; fi

# --- FACTS-STATIC: 4+2+2+4 DEV + 7 QA [Fact] where they claim ---
fact_bad=""
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/SuccessionTransferDeletionTests.cs' || true)
[ "$n" -eq 4 ] || fact_bad="$fact_bad SuccessionTransferDeletion=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/AdminOverrideAndConfiguredTimersTests.cs' || true)
[ "$n" -eq 2 ] || fact_bad="$fact_bad AdminOverride=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/EntryAndOwnerInvariantTrailTests.cs' || true)
[ "$n" -eq 2 ] || fact_bad="$fact_bad EntryAndOwnerInvariant=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/SuccessionRulesTests.cs' || true)
[ "$n" -eq 4 ] || fact_bad="$fact_bad SuccessionRules=$n"
n=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Containers/Qa0021SuccessionEntryAndTrailEdgeTests.cs' || true)
[ "$n" -eq 7 ] || fact_bad="$fact_bad Qa0021=$n"
if [ -z "$fact_bad" ]; then check FACTS-STATIC 0 '4+2+2+4 DEV + 7 QA facts where expected'
else check FACTS-STATIC 1 "$fact_bad"; fi

# --- STATE-CHECK (Windows writer tool via interop; the .ps1 is the authority) ---
state=$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0021-sucessao-transferencia-exclusao.md' 2>&1)
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
