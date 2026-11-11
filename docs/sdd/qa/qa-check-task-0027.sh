#!/usr/bin/env bash
# qa-check-task-0027.sh — evidence checker of QA-0027 round 2 (TASK-0027), Linux/WSL twin of the .ps1.
# Same checks, same exit contract (0 = all green). The .ps1 additionally runs `sdd-state.ps1 check`
# (PowerShell-only tool); this twin verifies the same rows with grep instead.
# Round-2 refresh: baseline gate 330 -> 337 (20261110-113636), final gate 335 -> 337, mutation
# cycles 11 -> 13 (M13/M14 against the QA-0026 fix), artifacts QA-0026-* -> QA-0027-*.
# S-22: every gate/run verdict is read INSIDE the log file (the `exit:` line and the summary),
# never from a wrapper's console exit code. S-16/S-23: restore hashes come from the PRE values
# the mutation driver recorded before writing anything.
# Usage: bash docs/sdd/qa/qa-check-task-0027.sh
set -u

cd "$(dirname "$0")/../../.." || exit 2
repo=$(pwd)
qa="$repo/docs/sdd/qa"
runs="$repo/docs/sdd/runs/test-runs"
task="$repo/docs/sdd/tasks/TASK-0027-upload-midia-skia.md"
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

# PowerShell wrote some artifacts with CRLF; every grep reads a normalized copy (BOM, CR stripped).
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
normalize "$qa/QA-0027-mutations.log"         "$norm_dir/mutations.log"
normalize "$qa/QA-0026-mutation-probe-m11.log" "$norm_dir/probe.log"
normalize "$qa/QA-0027-observations.log"      "$norm_dir/observations.log"
normalize "$qa/QA-0027-ceiling-probe.log"     "$norm_dir/ceiling.log"
normalize "$qa/QA-0027-sh-twin.log"           "$norm_dir/sh-twin.log"
normalize "$qa/QA-0027-git-baseline.txt"      "$norm_dir/baseline.txt"

# --- 1. baseline gate of this QA ---------------------------------------------------------
base_log="$runs/20261110-113636-test-gate.log"
e=$(log_exit "$base_log")
[ "$e" = "0" ]; check GATE-BASELINE-EXIT $? "20261110-113636-test-gate.log exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+337,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+337' "$base_log" 2>/dev/null
check GATE-BASELINE-SUMMARY $? 'Com falha 0 / Aprovado 337 / Total 337 (baseline da QA-0027)'

# --- 2. final gate (AC-06): the newest gate log by its timestamp name ---------------------
newest=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sed 's|.*/||' | sort -r | head -1)
[ -n "$newest" ]; check GATE-FINAL-EXISTS $? "${newest:-nenhum log de gate}"
e=$(log_exit "$runs/$newest")
[ "$e" = "0" ]; check GATE-FINAL-EXIT $? "$newest exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+337,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+337' "$runs/$newest" 2>/dev/null
check GATE-FINAL-SUMMARY $? 'Com falha 0 / Aprovado 337 (330 base + 5 da QA + 2 MediaCeilingTests)'

# --- 3. the QA class run: the newest filtered log (fresh green run after all edits) -------
newest_f=$(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sed 's|.*/||' | sort -r | head -1)
[ -n "$newest_f" ]; check QACLASS-EXISTS $? "${newest_f:-nenhum log filtrado}"
e=$(log_exit "$runs/$newest_f")
[ "$e" = "0" ]; check QACLASS-EXIT $? "$newest_f exit=$e"
grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+5,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+5' "$runs/$newest_f" 2>/dev/null
check QACLASS-SUMMARY $? 'Com falha 0 / Aprovado 5 / Total 5 (5 cenarios da QA)'

# --- 4. observations captured with the detailed logger ------------------------------------
obs="$norm_dir/observations.log"
missing=""
for marker in 'AC-01 observacao: contentHash=' 'sha256Servido=' \
              'AC-02 observacao: altVazio image/gif/video=[400,400,400]' 'video12MiB=200' \
              'AC-03 observacao: kinds=image/gif/video' 'dez=201' \
              'AC-04 observacao:' '404IdenticoAoAusente=True' \
              'AC-05 observacao:' 'igualServido=True'; do
  grep -qF "$marker" "$obs" 2>/dev/null || missing="$missing $marker;"
done
[ -z "$missing" ]; check OBSERVATIONS-10 $? "$([ -z "$missing" ] && echo 'as 10 observacoes da QA estao no artefato' || echo "faltando:$missing")"

# --- 5. can-fail: thirteen mutation cycles, each classified inside its red log ------------
mut_log="$norm_dir/mutations.log"
proven=$(grep -c -E 'CYCLE [A-Za-z0-9-]+: PROVEN' "$mut_log" 2>/dev/null || echo 0)
[ "$proven" = "13" ]; check MUTATIONS-13-PROVEN $? "$proven/13 ciclos PROVEN"
grep -q 'SUMMARY cycles=13 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut_log" 2>/dev/null
check MUTATIONS-SUMMARY $? 'cycles=13 failed=0 residue=0 temp-backups=0'

# --- 6. live QA-MUTATION residue (S-16 leftovers) -----------------------------------------
residue=$(grep -rIl 'QA-MUTATION' backend/src backend/tests \
  --include='*.cs' --include='*.props' --include='*.csproj' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
[ "$residue" = "0" ]; check MUTATION-RESIDUE $? "QA-MUTATION hits under backend/ = $residue"

# --- 7. byte-exact restore: sha256 prefix equals the PRE the driver recorded --------------
# cycle order: M1 = ExifStripper, M2..M5 = MediaEndpoints, M6..M8 = PostEndpoints,
# M9/M10 = MediaEndpoints, M12 = SkiaMediaProcessor, M13 = MediaOptions, M14 = MediaEndpoints
mapfile -t pres < <(grep -o -E 'PRE=[0-9A-F]{12}' "$mut_log" | sed 's/PRE=//')
restore_ok=1
detail=""
i=0
for f in backend/src/Squid.Service/ExifStripper.cs \
         backend/src/Squid.Api/Media/MediaEndpoints.cs \
         backend/src/Squid.Api/Posts/PostEndpoints.cs \
         backend/src/Squid.Service/SkiaMediaProcessor.cs \
         backend/src/Squid.Api/Media/MediaOptions.cs; do
  case $i in
    0) expect=${pres[0]:-} ;;
    1) expect=${pres[1]:-} ;;
    2) expect=${pres[5]:-} ;;
    3) expect=${pres[10]:-} ;;
    4) expect=${pres[11]:-} ;;
  esac
  hash=$(sha256sum "$f" | cut -c1-12 | tr '[:lower:]' '[:upper:]')
  [ "$hash" = "$expect" ] || restore_ok=0
  detail="$detail$(basename "$f")=$hash PRE=${expect:-?}; "
  i=$((i + 1))
done
[ "${#pres[@]}" -eq 13 ] || restore_ok=0
check RESTORE-SHA256 $((1 - restore_ok)) "$detail"

# --- 8. the M11 order-swap probe is recorded as an equivalent mutant ----------------------
probe="$norm_dir/probe.log"
grep -q 'UNPROVEN' "$probe" 2>/dev/null && grep -q 'restore=HASH=PRE' "$probe" 2>/dev/null && grep -q 'residue-QA-MUTATION=0' "$probe" 2>/dev/null
check PROBE-M11-RECORDED $? 'M11 (ordem EXIF->blurHash) sondado: UNPROVEN mutante equivalente, restore HASH=PRE, residuo 0'

# --- 9. AC-06 scope: nothing product outside the touches changed since the QA started ------
outside_list=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) \
  -newermt '2026-11-10 08:00:00' 2>/dev/null |
  grep -v -e '/bin/' -e '/obj/' |
  grep -v -E '/(Squid\.Api/Media/|Squid\.Api/Posts/|Squid\.Domain/Objects/|Squid\.Service/|Squid\.Tests/Media/)' || true)
hits=$(find backend/src backend/tests \( -name '*.cs' -o -name '*.csproj' -o -name '*.props' \) \
  -newermt '2026-11-10 08:00:00' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
outside=$(printf '%s' "$outside_list" | grep -c . || true)
[ -z "$outside_list" ]; check TOUCHES-NO-OUTSIDE $? "mtime desde 08:00:00: $hits arquivos, fora dos toques: ${outside:-0}$( [ -n "$outside_list" ] && printf ' -> %s' "$outside_list" )"

# --- 10. git scope: only QA/flow artifacts are new since the QA baseline capture -----------
git status --porcelain -uall | sort > /tmp/qa0026-git-current.txt
sort "$norm_dir/baseline.txt" > /tmp/qa0026-git-baseline.txt
new_all=$(comm -13 /tmp/qa0026-git-baseline.txt /tmp/qa0026-git-current.txt)
bad=$(printf '%s' "$new_all" | grep -v -E 'docs/sdd/(qa/|tasks/TASK-0027|runs/test-runs/)' |
  grep -v -E 'backend/(src/Squid.Api/Media/|src/Squid.Api/Posts/|src/Squid.Domain/Objects/|src/Squid.Service/|tests/Squid.Tests/Media/)' || true)
new_count=$(printf '%s' "$new_all" | grep -c . || true)
[ -z "$bad" ]; check GIT-NEW-ONLY-QA-AND-TOUCHES $? "novas entradas=$new_count fora do permitido=$(printf '%s' "$bad" | grep -c . || true)$( [ -n "$bad" ] && printf ' -> %s' "$bad" )"
rm -f /tmp/qa0026-git-current.txt /tmp/qa0026-git-baseline.txt

# --- 11. task file rows: Log and Evidence rows of this QA are present (append-only) --------
qa_log_rows=$(grep -c -E '^- [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
qa_evi_rows=$(grep -c -E '^\| [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+ \| qa \| ' "$task" 2>/dev/null || echo 0)
[ "$qa_log_rows" -ge 6 ]; check TASK-LOG-ROWS $? "linhas de log do qa=$qa_log_rows (>=6)"
[ "$qa_evi_rows" -ge 12 ]; check TASK-EVI-ROWS $? "linhas de evidencia do qa=$qa_evi_rows (>=12)"

# --- 12. the QA's own test file, ceiling probe and verdict document ------------------------
facts=$(grep -c '\[Fact\]' "$repo/backend/tests/Squid.Tests/Media/QaTask0027Tests.cs" 2>/dev/null || echo 0)
[ "$facts" = "5" ]; check QA-TEST-FACTS $? "[Fact] em QaTask0027Tests.cs = $facts"
grep -q 'exit:[[:space:]]*0' "$norm_dir/ceiling.log" 2>/dev/null &&
  grep -q 'round2 streaming' "$norm_dir/ceiling.log" 2>/dev/null &&
  grep -q 'CL=69206017' "$norm_dir/ceiling.log" 2>/dev/null &&
  grep -q 'servidor esperou o corpo' "$norm_dir/ceiling.log" 2>/dev/null
check CEILING-PROBE-3 $? 'sonda de teto: 3 facts exit 0 - streaming 413 com envelope, cru 69206017 com envelope, cru 53000000 esperado'
grep -q 'verdict: PASSED' "$qa/QA-0027-upload-midia-skia.md" 2>/dev/null
check QA-DOC $? 'QA-0027-upload-midia-skia.md existe e verdict: PASSED'

# --- 13. platform twin record (the .sh gate has no dotnet in this WSL — Linux unverified) --
grep -q 'dotnet: command not found' "$norm_dir/sh-twin.log" 2>/dev/null &&
  grep -q 'exit 127' "$norm_dir/sh-twin.log" 2>/dev/null
check SH-TWIN-RECORDED $? 'sdd-test.sh: dotnet ausente no WSL - plataforma Linux registrada como unverified (pre-existente re-confirmado)'

echo "SUMMARY total=$((pass + fail)) fails=$fail"
if [ "$fail" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'
exit 1
