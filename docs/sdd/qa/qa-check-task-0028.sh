#!/usr/bin/env bash
# qa-check-task-0028.sh — evidence checker of QA-0028 (TASK-0028), Linux/WSL twin of the .ps1.
# Same checks, same exit contract (0 = all green). The .ps1 additionally runs `sdd-state.ps1 check`
# (PowerShell-only tool); this twin verifies the same rows with grep instead.
# S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
# never from a wrapper's console exit code. S-16/S-23: restore hashes come from the PRE values
# the mutation driver recorded before writing anything.
# Usage: bash docs/sdd/qa/qa-check-task-0028.sh
set -u

cd "$(dirname "$0")/../../.." || exit 2
repo=$(pwd)
qa="$repo/docs/sdd/qa"
runs="$repo/docs/sdd/runs/test-runs"
task="$repo/docs/sdd/tasks/TASK-0028-cota-midia.md"
pass=0
fail=0

check() { # name ok(0|1) detail
  if [ "$2" -eq 0 ]; then
    pass=$((pass + 1)); echo "CHECK $1 OK   $3"
  else
    fail=$((fail + 1)); echo "CHECK $1 FAIL $3"
  fi
}

log_exit() { # file -> the exit: line read INSIDE the log (S-22), -1 when absent
  if [ ! -f "$1" ]; then echo -1; return; fi
  local v
  v=$(grep -m1 -E '^exit:[[:space:]]*[0-9]+' "$1" | tr -dc '0-9')
  echo "${v:--1}"
}

# PowerShell wrote some artifacts with CRLF/BOM; every grep reads a normalized copy.
norm_dir=$(mktemp -d) || exit 2
trap 'rm -rf "$norm_dir"' EXIT
normalize() { # src dst
  [ -f "$1" ] || return 0
  if [ "$(head -c 2 "$1" | od -An -tx1 | tr -d ' \n')" = 'fffe' ]; then
    iconv -f UTF-16LE -t UTF-8 "$1" > "$2" 2>/dev/null || cp "$1" "$2"
  else
    cp "$1" "$2"
  fi
  sed -i '1s/^\xEF\xBB\xBF//' "$2"
  sed -i 's/\r$//' "$2"
}
normalize "$qa/QA-0028-mutations.log"     "$norm_dir/mutations.log"
normalize "$qa/QA-0028-observations.log"  "$norm_dir/observations.log"
normalize "$qa/QA-0028-sh-twin.log"       "$norm_dir/sh-twin.log"
normalize "$qa/QA-0028-git-baseline.txt"  "$norm_dir/baseline.txt"

# --- 1. baseline gate of this QA -----------------------------------------------------------
base_log="$runs/20261110-142613-test-gate.log"
e=$(log_exit "$base_log")
[ "$e" = "0" ]; check GATE-BASELINE-EXIT $? "20261110-142613-test-gate.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+343,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+343' "$base_log" 2>/dev/null
check GATE-BASELINE-SUMMARY $? 'Com falha 0 / Aprovado 343 / Total 343 (baseline DEV)'

# --- 2. final gate (AC-05): the newest gate log by its timestamp name -----------------------
newest=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sed 's|.*/||' | sort -r | head -1)
[ -n "$newest" ]; check GATE-FINAL-EXISTS $? "${newest:-nenhum log de gate}"
e=$(log_exit "$runs/$newest")
[ "$e" = "0" ]; check GATE-FINAL-EXIT $? "$newest exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+348,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+348' "$runs/$newest" 2>/dev/null
check GATE-FINAL-SUMMARY $? 'Com falha 0 / Aprovado 348 / Total 348 (343 DEV + 5 da QA)'

# --- 3. the QA class run and the whole Quota slice ------------------------------------------
e=$(log_exit "$runs/20261110-144402-test-filter.log")
[ "$e" = "0" ]; check QACLASS-EXIT $? "20261110-144402-test-filter.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+5,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+5' "$runs/20261110-144402-test-filter.log" 2>/dev/null
check QACLASS-SUMMARY $? 'Com falha 0 / Aprovado 5 / Total 5 (5 cenarios da QA)'
e=$(log_exit "$runs/20261110-144447-test-filter.log")
[ "$e" = "0" ]; check QUOTA-SLICE-EXIT $? "20261110-144447-test-filter.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+11,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+11' "$runs/20261110-144447-test-filter.log" 2>/dev/null
check QUOTA-SLICE-SUMMARY $? 'Com falha 0 / Aprovado 11 / Total 11 (6 DEV + 5 QA)'

# --- 4. observations captured with the detailed logger --------------------------------------
obs="$norm_dir/observations.log"
missing=""
for marker in 'AC-01a observacao: status=507 code=quota_exceeded rowIgual=True limitBytesAposRecusa=777' \
              'AC-01b observacao: linhasDoLedger=0 volumeIgual=True registrosIguais=True status=507' \
              'tamanhoOriginal=9000 tamanhoGuardado=329 limiteCota=8192' \
              'AC-02a observacao: real=' 'corrigido=' 'aposExclusao=' \
              'stampAntesDoIncremento=2000-01-01T00:00:00.000Z stampAposIncremento=2000-01-01T00:00:00.000Z' \
              'incrementoNaoMoveu=True' 'Aprovados: 5'; do
  grep -qF "$marker" "$obs" 2>/dev/null || missing="$missing $marker;"
done
[ -z "$missing" ]; check OBSERVATIONS-9 $? "$([ -z "$missing" ] && echo 'as 9 observacoes da QA estao no artefato' || echo "faltando:$missing")"

# --- 5. can-fail: nine mutation cycles, each classified inside its red log ------------------
mut_log="$norm_dir/mutations.log"
proven=$(grep -c -E 'CYCLE [A-Za-z0-9-]+: PROVEN' "$mut_log" 2>/dev/null || echo 0)
[ "$proven" = "9" ]; check MUTATIONS-9-PROVEN $? "$proven/9 ciclos PROVEN"
grep -q 'SUMMARY cycles=9 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut_log" 2>/dev/null
check MUTATIONS-SUMMARY $? 'cycles=9 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue (S-16 leftovers) --------------------------------------------
residue=$(grep -rIl 'QA-MUTATION' backend/src backend/tests \
  --include='*.cs' --include='*.props' --include='*.csproj' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
[ "$residue" = "0" ]; check MUTATION-RESIDUE $? "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: sha256 prefix equals the PRE the driver recorded ----------------
# cycle order: M1 SqliteQuotaLedger, M2 MediaEndpoints, M3 QuotaReconciler,
# M4 QuotaReconcileService, M5 MediaOptions, M6 Squid.Service.csproj,
# M7 Directory.Packages.props, M8 MediaEndpoints, M9 SqliteQuotaLedger
mapfile -t pres < <(grep -o -E 'PRE=[0-9A-F]{12}' "$mut_log" | sed 's/PRE=//')
restore_ok=1
detail=""
i=0
for f in backend/src/Squid.ReadModels/Quota/SqliteQuotaLedger.cs \
         backend/src/Squid.Api/Media/MediaEndpoints.cs \
         backend/src/Squid.Worker/Quota/QuotaReconciler.cs \
         backend/src/Squid.Worker/Quota/QuotaReconcileService.cs \
         backend/src/Squid.Api/Media/MediaOptions.cs \
         backend/src/Squid.Service/Squid.Service.csproj \
         backend/Directory.Packages.props; do
  expect=${pres[$i]:-}
  hash=$(sha256sum "$f" | cut -c1-12 | tr '[:lower:]' '[:upper:]')
  [ "$hash" = "$expect" ] || restore_ok=0
  detail="$detail$(basename "$f")=$hash PRE=${expect:-?}; "
  i=$((i + 1))
done
# M8 and M9 repeat MediaEndpoints and SqliteQuotaLedger: their PRE must match too
for pair in "backend/src/Squid.Api/Media/MediaEndpoints.cs:7" \
            "backend/src/Squid.ReadModels/Quota/SqliteQuotaLedger.cs:8"; do
  f=${pair%%:*}; idx=${pair##*:}
  expect=${pres[$idx]:-}
  hash=$(sha256sum "$f" | cut -c1-12 | tr '[:lower:]' '[:upper:]')
  [ "$hash" = "$expect" ] || restore_ok=0
  detail="$detail$(basename "$f")#M$((idx + 1))=$hash PRE=${expect:-?}; "
done
[ "${#pres[@]}" -eq 9 ] || restore_ok=0
check RESTORE-SHA256 $((1 - restore_ok)) "$detail"

# --- 8. AC-05 scope: nothing product outside the touches changed since DEV started -----------
outside_list=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) \
  -newermt '2026-11-10 13:11:00' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' |
  grep -v -E '/(Squid\.Api/Media/|Squid\.ReadModels/|Squid\.Worker/|Squid\.Service/|Squid\.Tests/Quota/)' || true)
hits=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) \
  -newermt '2026-11-10 13:11:00' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
outside=$(printf '%s' "$outside_list" | grep -c . || true)
[ -z "$outside_list" ]; check TOUCHES-NO-OUTSIDE $? "mtime desde 13:11:00: $hits arquivos, fora dos toques: ${outside:-0}$( [ -n "$outside_list" ] && printf ' -> %s' "$outside_list" )"

# --- 9. git scope: only QA/flow artifacts are new since the QA baseline capture --------------
git status --porcelain -uall | sort > /tmp/qa0028-git-current.txt
sort "$norm_dir/baseline.txt" > /tmp/qa0028-git-baseline.txt
new_all=$(comm -13 /tmp/qa0028-git-baseline.txt /tmp/qa0028-git-current.txt)
bad=$(printf '%s' "$new_all" | grep -v -E 'docs/sdd/(qa/QA-0028|qa/qa-[a-z]+-task-0028|tasks/TASK-0028|runs/test-runs/)' |
  grep -v -E 'backend/(src/Squid.Api/Media/|src/Squid.ReadModels/|src/Squid.Worker/|src/Squid.Service/|tests/Squid.Tests/Quota/)' || true)
new_count=$(printf '%s' "$new_all" | grep -c . || true)
[ -z "$bad" ]; check GIT-NEW-ONLY-QA-AND-TOUCHES $? "novas entradas=$new_count fora do permitido=$(printf '%s' "$bad" | grep -c . || true)$( [ -n "$bad" ] && printf ' -> %s' "$bad" )"
rm -f /tmp/qa0028-git-current.txt /tmp/qa0028-git-baseline.txt

# --- 10. task file rows: Log and Evidence rows of this QA are present (append-only) ----------
qa_log_rows=$(grep -c -E '^- [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
qa_evi_rows=$(grep -c -E '^\| [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
[ "$qa_log_rows" -ge 4 ]; check TASK-LOG-ROWS $? "linhas de log do qa=$qa_log_rows (>=4)"
[ "$qa_evi_rows" -ge 8 ]; check TASK-EVI-ROWS $? "linhas de evidencia do qa=$qa_evi_rows (>=8)"

# --- 11. the QA's own test file and verdict document -----------------------------------------
facts=$(grep -c '\[Fact\]' "$repo/backend/tests/Squid.Tests/Quota/QaTask0028Tests.cs" 2>/dev/null || echo 0)
[ "$facts" = "5" ]; check QA-TEST-FACTS $? "[Fact] em QaTask0028Tests.cs = $facts"
grep -q 'verdict: PASSED' "$qa/QA-0028-cota-midia.md" 2>/dev/null
check QA-DOC $? 'QA-0028-cota-midia.md existe e verdict: PASSED'

# --- 12. AC-06 declaration (the static gate of the human approval) ---------------------------
grep -qF '<PackageVersion Include="SkiaSharp" Version="4.155.0" />' "$repo/backend/Directory.Packages.props" 2>/dev/null &&
  grep -qF '<PackageVersion Include="SkiaSharp.NativeAssets.Linux" Version="4.155.0" />' "$repo/backend/Directory.Packages.props" 2>/dev/null &&
  grep -qF '<PackageReference Include="SkiaSharp.NativeAssets.Linux" />' "$repo/backend/src/Squid.Service/Squid.Service.csproj" 2>/dev/null
check AC06-DECLARATION $? 'SkiaSharp e NativeAssets.Linux ambos em 4.155.0 no props + PackageReference no csproj'

# --- 13. platform twin record (the .sh gate has no dotnet in this WSL - Linux unverified) -----
grep -q 'dotnet: command not found' "$norm_dir/sh-twin.log" 2>/dev/null &&
  grep -q 'exit 127' "$norm_dir/sh-twin.log" 2>/dev/null
check SH-TWIN-RECORDED $? 'sdd-test.sh: dotnet ausente no WSL - plataforma Linux registrada como unverified (pre-existente re-confirmado)'

echo "SUMMARY total=$((pass + fail)) fails=$fail"
if [ "$fail" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'
exit 1
