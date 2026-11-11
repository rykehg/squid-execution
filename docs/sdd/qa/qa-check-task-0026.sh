#!/usr/bin/env bash
# qa-check-task-0026.sh — evidence checker of QA-0025 (TASK-0026), Linux/WSL twin of the .ps1.
# Same nineteen checks, same exit contract (0 = all green).
# S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
# never from a wrapper's console exit code. S-16/S-23: restore hashes come from the PRE values
# the mutation driver recorded before writing anything.
# Usage: bash docs/sdd/qa/qa-check-task-0026.sh
set -u

cd "$(dirname "$0")/../../.." || exit 2
repo=$(pwd)
qa="$repo/docs/sdd/qa"
runs="$repo/docs/sdd/runs/test-runs"
task="$repo/docs/sdd/tasks/TASK-0026-sse-stream.md"
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

# PowerShell wrote some artifacts with CRLF; the originals stay untouched, every grep reads a
# normalized copy (BOM stripped, CR stripped).
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
normalize "$qa/QA-0025-mutations.log"    "$norm_dir/mutations.log"
normalize "$qa/QA-0025-observations.log" "$norm_dir/observations.log"
normalize "$qa/QA-0025-sh-twin.log"      "$norm_dir/sh-twin.log"
normalize "$qa/QA-0025-git-baseline.txt" "$norm_dir/baseline.txt"

# --- 1. baseline gate of this QA ---------------------------------------------------------
base_log="$runs/20261109-213326-test-gate.log"
e=$(log_exit "$base_log")
[ "$e" = "0" ]; check GATE-BASELINE-EXIT $? "20261109-213326-test-gate.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+304,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+304' "$base_log" 2>/dev/null
check GATE-BASELINE-SUMMARY $? 'Com falha 0 / Aprovado 304 / Total 304 (baseline do DEV)'

# --- 2. final gate (AC-06): the newest gate log by its timestamp name ---------------------
newest=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sed 's|.*/||' | sort -r | head -1)
[ -n "$newest" ]; check GATE-FINAL-EXISTS $? "${newest:-nenhum log de gate}"
e=$(log_exit "$runs/$newest")
[ "$e" = "0" ]; check GATE-FINAL-EXIT $? "$newest exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+310,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+310' "$runs/$newest" 2>/dev/null
check GATE-FINAL-SUMMARY $? 'Com falha 0 / Aprovado 310 (304 + 6 da QA)'

# --- 3. the QA class run -----------------------------------------------------------------
qa_run="$runs/20261109-220026-test-filter.log"
e=$(log_exit "$qa_run")
[ "$e" = "0" ]; check QACLASS-EXIT $? "20261109-220026-test-filter.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+6,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+6' "$qa_run" 2>/dev/null
check QACLASS-SUMMARY $? 'Com falha 0 / Aprovado 6 / Total 6'

# --- 4. observations captured with the detailed logger ------------------------------------
obs="$norm_dir/observations.log"
missing=""
for marker in 'AC-01 observacao: round' 'AC-02 observacao: cursor=' 'AC-03 observacao: intervalo entre pings' \
              'AC-04 observacao: cursor=' 'AC-04b observacao: cursor=' 'AC-05 observacao: 8 publicacoes'; do
  grep -qF "$marker" "$obs" 2>/dev/null || missing="$missing $marker;"
done
[ -z "$missing" ]; check OBSERVATIONS-6 $? "$([ -z "$missing" ] && echo 'as 6 observacoes da QA estao no artefato' || echo "faltando:$missing")"

# --- 5. can-fail: six mutation cycles, each classified inside its red log ------------------
mut_log="$norm_dir/mutations.log"
proven=$(grep -c -E 'CYCLE [A-Za-z0-9-]+: PROVEN' "$mut_log" 2>/dev/null || echo 0)
[ "$proven" = "7" ]; check MUTATIONS-7-PROVEN $? "$proven/7 ciclos PROVEN"
grep -q 'SUMMARY cycles=7 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut_log" 2>/dev/null
check MUTATIONS-SUMMARY $? 'cycles=7 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue (S-16 leftovers) -----------------------------------------
residue=$(grep -rIl 'QA-MUTATION' backend/src backend/tests \
  --include='*.cs' --include='*.sql' --include='*.props' --include='*.csproj' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
[ "$residue" = "0" ]; check MUTATION-RESIDUE $? "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: sha256 prefix equals the PRE the driver recorded --------------
# cycle order: M1/M2 = StreamSession, M3/M4/M5/M7 = StreamEndpoints, M6 = StreamHub
mapfile -t pres < <(grep -o -E 'PRE=[0-9A-F]{12}' "$mut_log" | sed 's/PRE=//')
restore_ok=1
detail=""
i=0
for f in backend/src/Squid.Api/Posts/StreamSession.cs \
         backend/src/Squid.Api/Posts/StreamEndpoints.cs \
         backend/src/Squid.Api/Posts/StreamHub.cs; do
  case $i in
    0) expect=${pres[0]:-}; ;;
    1) expect=${pres[2]:-}; ;;
    2) expect=${pres[5]:-}; ;;
  esac
  hash=$(sha256sum "$f" | cut -c1-12 | tr '[:lower:]' '[:upper:]')
  [ "$hash" = "$expect" ] || restore_ok=0
  detail="$detail$(basename "$f")=$hash PRE=${expect:-?}; "
  i=$((i + 1))
done
[ "${#pres[@]}" -eq 7 ] || restore_ok=0
check RESTORE-SHA256 $((1 - restore_ok)) "$detail"

# --- 8. AC-06 scope: nothing product outside the touches changed since the DEV dispatch ----
outside_list=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.sql' -o -name '*.props' \) \
  -newermt '2026-11-09 20:19:00' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' |
  grep -v -E '/(Squid\.Api/Posts/|Squid\.Service/|Squid\.Tests/Stream/)' || true)
hits=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.sql' -o -name '*.props' \) \
  -newermt '2026-11-09 20:19:00' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
outside=$(printf '%s' "$outside_list" | grep -c . || true)
[ -z "$outside_list" ]; check TOUCHES-NO-OUTSIDE $? "mtime desde o dispatch 20:19:00: $hits arquivos, fora dos toques: ${outside:-0}"

# --- 9. git scope: only QA/flow artifacts are new since the QA baseline capture ------------
git status --porcelain -uall | sort > /tmp/qa0025-git-current.txt
sort "$norm_dir/baseline.txt" > /tmp/qa0025-git-baseline.txt
new_all=$(comm -13 /tmp/qa0025-git-baseline.txt /tmp/qa0025-git-current.txt)
bad=$(printf '%s' "$new_all" | grep -v -E 'docs/sdd/(qa/|tasks/TASK-0026|runs/test-runs/)' |
  grep -v -E 'backend/(src/Squid.Api/Posts/|src/Squid.Service/|tests/Squid.Tests/Stream/)' || true)
new_count=$(printf '%s' "$new_all" | grep -c . || true)
[ -z "$bad" ]; check GIT-NEW-ONLY-QA-AND-TOUCHES $? "novas entradas=$new_count fora do permitido=$(printf '%s' "$bad" | grep -c . || true)$( [ -n "$bad" ] && printf ' -> %s' "$bad" )"
rm -f /tmp/qa0025-git-current.txt /tmp/qa0025-git-baseline.txt

# --- 10. task file rows: Log and Evidence rows of this QA are present (append-only) --------
qa_log_rows=$(grep -c -E '^- [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
qa_evi_rows=$(grep -c -E '^\| [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
[ "$qa_log_rows" -ge 8 ]; check TASK-LOG-ROWS $? "linhas de log do qa=$qa_log_rows (>=8)"
[ "$qa_evi_rows" -ge 8 ]; check TASK-EVI-ROWS $? "linhas de evidencia do qa=$qa_evi_rows (>=8)"

# --- 11. the QA's own test file and verdict document --------------------------------------
facts=$(grep -c '\[Fact\]' "$repo/backend/tests/Squid.Tests/Stream/QaTask0026Tests.cs" 2>/dev/null || echo 0)
[ "$facts" = "6" ]; check QA-TEST-FACTS $? "[Fact] em QaTask0026Tests.cs = $facts"
grep -q 'verdict: PASSED' "$qa/QA-0025-sse-stream-ao-vivo.md" 2>/dev/null
check QA-DOC $? 'QA-0025-sse-stream-ao-vivo.md existe e verdict: PASSED'

# --- 12. platform twin record (the .sh gate cannot find this repo's .slnx — pre-existing) --
grep -q 'no \*\.sln found within depth 3' "$norm_dir/sh-twin.log" 2>/dev/null &&
  grep -q 'exit 127' "$norm_dir/sh-twin.log" 2>/dev/null
check SH-TWIN-RECORDED $? 'sdd-test.sh: sem *.sln dentro do repo e sem dotnet no WSL (achado pre-existente re-confirmado)'

echo "SUMMARY total=$((pass + fail)) fails=$fail"
if [ "$fail" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'
exit 1
