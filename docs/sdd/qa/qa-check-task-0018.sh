#!/usr/bin/env bash
# qa-check-task-0018 — Linux twin of qa-check-task-0018.ps1: same 9 check ids, same baseline,
# same exit contract (0 only when SUMMARY total=9 fails=0). S-22: green/red comes from the log's
# own exit: header and summary, never from the wrapper console. S-16/S-23: SHA256 PRE of the
# three files QA-0017 mutated. S-24: sources are read from the filesystem with grep -F, never
# with git grep (the tree is untracked). Pure ASCII on purpose.
set -u
cd "$(dirname "$0")/../../.." || exit 1

fails=0
check() { # id ok(0/1) detail
  if [ "$2" -eq 1 ]; then
    echo "CHECK $1 PASS $3"
  else
    fails=$((fails + 1))
    echo "CHECK $1 FAIL $3"
  fi
}

baseline=$(cat <<'EOF'
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
backend/src/Squid.Service/SqliteContainerStore.cs
backend/src/Squid.Service/SqlitePolicyAuditStore.cs
backend/tests/Squid.Tests/Containers/ContainerRoutesTests.cs
backend/tests/Squid.Tests/Containers/ContainerStoreTests.cs
backend/tests/Squid.Tests/Containers/ContainerTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditRoutesTests.cs
backend/tests/Squid.Tests/Containers/PolicyAuditStoreTests.cs
backend/tests/Squid.Tests/Containers/Qa0016ContainerPatchScopeTests.cs
docs/sdd/qa/QA-0016-nucleo-do-container.md
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
docs/sdd/qa/QA-0017-politica-auditoria-operador.md
docs/sdd/qa/qa-check-task-0018.ps1
docs/sdd/qa/qa-check-task-0018.sh
EOF
)

runs=docs/sdd/runs/test-runs

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
gate_ok=0
if [ -n "$gate" ] && grep -Eq '^exit:[[:space:]]+0[[:space:]]*$' "$gate"; then gate_ok=1; fi
check GATE-NEWEST-EXIT "$gate_ok" "${gate:-no gate log found}"

gate_falha=-1
gate_aprov=-1
if [ -n "$gate" ]; then
  line=$(grep -Eo 'Com falha:[[:space:]]+[0-9]+' "$gate" | tail -1)
  [ -n "$line" ] && gate_falha=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
  line=$(grep -Eo 'Aprovado:[[:space:]]+[0-9]+' "$gate" | tail -1)
  [ -n "$line" ] && gate_aprov=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
fi
gate_sum=0
if [ "$gate_falha" -eq 0 ] && [ "$gate_aprov" -ge 188 ]; then gate_sum=1; fi
check GATE-NEWEST-SUMMARY "$gate_sum" "Com falha=$gate_falha Aprovado=$gate_aprov (need falha=0 and aprovado>=188)"

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY ---
filt=$(ls "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
filt_ok=0
if [ -n "$filt" ] && grep -Eq '^exit:[[:space:]]+0[[:space:]]*$' "$filt"; then filt_ok=1; fi
check FILTER-NEWEST-EXIT "$filt_ok" "${filt:-no filter log found}"

filt_falha=-1
filt_aprov=-1
if [ -n "$filt" ]; then
  line=$(grep -Eo 'Com falha:[[:space:]]+[0-9]+' "$filt" | tail -1)
  [ -n "$line" ] && filt_falha=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
  line=$(grep -Eo 'Aprovado:[[:space:]]+[0-9]+' "$filt" | tail -1)
  [ -n "$line" ] && filt_aprov=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
fi
filt_sum=0
if [ "$filt_falha" -eq 0 ] && [ "$filt_aprov" -ge 7 ]; then filt_sum=1; fi
check FILTER-NEWEST-SUMMARY "$filt_sum" "Com falha=$filt_falha Aprovado=$filt_aprov (need falha=0 and aprovado>=7)"

# --- RESTORE-SHA256 (S-16) ---
hash_ok=1
hash_detail=''
check_hash() { # path expected
  got=$(sha256sum "$1" | cut -d' ' -f1 | tr 'a-f' 'A-F')
  if [ "$got" != "$2" ]; then
    hash_ok=0
    hash_detail="$hash_detail $1=$got"
  fi
}
check_hash backend/src/Squid.Api/Containers/ContainerEndpoints.cs 'D0704FD83FF00EABD74B22F47F2CE7E5ECFCD006B26DEDAE85FA27031F5C14D1'
check_hash backend/src/Squid.Api/Admin/AdminEndpoints.cs '754C1B69FCEC797185A4440052D3D884797E85BEF2509B90D2DD025DB4806B3B'
check_hash backend/src/Squid.Service/SqlitePolicyAuditStore.cs 'CE485D798383E5FE79D73CBBAF1847CB06BF4FD430DBF71CA09B2BB509C26BF7'
if [ "$hash_ok" -eq 1 ]; then hash_detail='3/3 files match PRE'; fi
check RESTORE-SHA256 "$hash_ok" "$hash_detail"

# --- MUTATION-RESIDUE: no QA-MUTATION marker left under backend/ (S-16 leftovers) ---
residue=0
while IFS= read -r hit; do
  [ -n "$hit" ] && residue=$((residue + 1))
done < <(grep -rl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' 2>/dev/null | grep -vE '/(bin|obj)/' || true)
check MUTATION-RESIDUE "$([ "$residue" -eq 0 ] && echo 1 || echo 0)" "QA-MUTATION hits under backend/ = $residue"

# --- TOUCHES-NO-OUTSIDE (AC-06) ---
git_ok=0
missing=0
extras=0
new_allowed=0
if git status --porcelain -uall > /tmp/qa0018-porcelain.txt 2>/dev/null; then
  git_ok=1
  cut -c4- /tmp/qa0018-porcelain.txt > /tmp/qa0018-paths.txt
  while IFS= read -r p; do
    [ -z "$p" ] && continue
    grep -Fxq "$p" /tmp/qa0018-paths.txt || missing=$((missing + 1))
  done <<< "$baseline"
  while IFS= read -r path; do
    [ -z "$path" ] && continue
    if ! printf '%s\n' "$baseline" | grep -Fxq "$path"; then
      if printf '%s\n' "$allowed_new" | grep -Fxq "$path"; then
        new_allowed=$((new_allowed + 1))
      else
        extras=$((extras + 1))
      fi
    fi
  done < /tmp/qa0018-paths.txt
  rm -f /tmp/qa0018-porcelain.txt /tmp/qa0018-paths.txt
fi
check TOUCHES-NO-OUTSIDE "$([ "$git_ok" -eq 1 ] && [ "$missing" -eq 0 ] && [ "$extras" -eq 0 ] && echo 1 || echo 0)" "git=$git_ok missing=$missing out-of-scope=$extras new-allowed=$new_allowed/3"

# --- MTIME-SWEEP: only the three mutated files are newer than DEV close (15:59:17) ---
swept=$(find backend/src backend/tests -type f \( -name '*.cs' -o -name '*.csproj' \) \
  -not -path '*/bin/*' -not -path '*/obj/*' -newermt '2026-11-08 15:59:17' 2>/dev/null)
sweep_n=$(printf '%s' "$swept" | grep -c . || true)
bad_sweep=0
while IFS= read -r rel; do
  [ -z "$rel" ] && continue
  case "$rel" in
    backend/src/Squid.Api/Containers/*|backend/src/Squid.Api/Admin/*|backend/src/Squid.Service/*|backend/tests/Squid.Tests/Containers/*) in_touches=1 ;;
    *) in_touches=0 ;;
  esac
  in_mutated=0
  case "$rel" in
    backend/src/Squid.Api/Containers/ContainerEndpoints.cs|backend/src/Squid.Api/Admin/AdminEndpoints.cs|backend/src/Squid.Service/SqlitePolicyAuditStore.cs) in_mutated=1 ;;
  esac
  if [ "$in_touches" -ne 1 ] || [ "$in_mutated" -ne 1 ]; then bad_sweep=$((bad_sweep + 1)); fi
done <<< "$swept"
check MTIME-SWEEP "$([ "$bad_sweep" -eq 0 ] && echo 1 || echo 0)" "files after 15:59:17 = $sweep_n; not one of the 3 mutated inside touches = $bad_sweep"

# --- ROUTES-STATIC (S-24: filesystem read, no git grep on untracked paths) ---
routes_ok=1
grep -Fq 'app.MapPut("/api/containers/{slug}/policy", PutPolicyAsync);' backend/src/Squid.Api/Containers/ContainerEndpoints.cs || routes_ok=0
grep -Fq 'app.MapGet("/api/admin/audit", AuditAsync);' backend/src/Squid.Api/Admin/AdminEndpoints.cs || routes_ok=0
grep -Fq 'app.MapSquidAdmin();' backend/src/Squid.Service/SquidServiceHost.cs || routes_ok=0
grep -Fq 'public const string ChangePolicy = "ChangePolicy";' backend/src/Squid.Api/Containers/IPolicyAuditStore.cs || routes_ok=0
check ROUTES-STATIC "$routes_ok" 'PUT /policy + GET /api/admin/audit + MapSquidAdmin + Action ChangePolicy read from the filesystem'

echo "SUMMARY total=9 fails=$fails"
if [ "$fails" -eq 0 ]; then
  echo 'RESULT: PASS'
  exit 0
fi
echo 'RESULT: FAIL'
exit 1
