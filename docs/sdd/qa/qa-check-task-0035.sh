#!/usr/bin/env bash
# QA-0035 evidence checker - twin of qa-check-task-0035.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there.
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0035.sh (cd tres niveis: qa -> sdd -> docs -> raiz)
set -u
cd "$(dirname "$0")/../../.." || exit 2
runs=docs/sdd/runs/test-runs
qa=docs/sdd/qa
total=0; fails=0

check() { # name ok(0=ok) detail
  total=$((total + 1))
  if [ "$2" = "0" ]; then echo "CHECK $1 OK - $3"
  else fails=$((fails + 1)); echo "CHECK $1 FAIL - $3"; fi
}

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && [ "$(basename "$gate")" = "20261112-041813-test-gate.log" ] \
   && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +450, Ignorado: +0, Total: +450' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 450/450"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 450/450"
fi

if grep -qE '^exit: +0$' "$runs/20261112-035540-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +18' "$runs/20261112-035540-test-filter.log"; then
  check BASELINE 0 "035540 exit 0 / 18 de 18"
else
  check BASELINE 1 "baseline 035540 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-040053-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +19' "$runs/20261112-040053-test-filter.log"; then
  check TEMPTEST-GREEN 0 "040053 exit 0 / 19 de 19 (teste temporario da QA verde)"
else
  check TEMPTEST-GREEN 1 "040053 ausente ou vermelha"
fi

mut="$qa/QA-0035-mutations.log"
missing=""
not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M11 M12 M13; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=[0-9]+ aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M13 todos com linha PRE${missing:+ faltando:$missing}"
[ -z "$not_red" ]; check MUT-RED $? "M1..M13 todos com RUN vermelho no log${not_red:+ sem-vermelho:$not_red}"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 14 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 14)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=19 falha=0' "$mut")
[ "$greens" -ge 2 ]; check MUT-GREEN $? "GREEN 19/19 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "1" ]; check MUT-ANOMALIA $? "exatamente 1 CLASSIFICACAO-ERRADA (M4 v1, mutante corrigido)"
grep -q 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cheio cycles=13 failed=1 residue=0"
grep -q 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-LAST $? "SUMMARY final cycles=1 failed=0 residue=0"
grep -qE '^M4 RUN wrapper=1 logExit=1 aprovado=16 falha=3 ' "$mut"; check MUT-M4V2 $? "M4 v2 vermelho (aprovado 16 / falha 3, log 041039)"
grep -qE '^M12 RED-IN-LOG .*A_seat_added_later' "$mut" && grep -qE '^M12 RED-IN-LOG .*Adding_a_seat' "$mut"
check MUT-M12-CATCH $? "M12 pego pelos testes do DEV: Adding_a_seat + A_seat_added_later"
grep -qE '^M3 RED-IN-LOG .*QaTask0035RemovedRoutesTests' "$mut"; check MUT-M3-QATEST $? "M3 vermelho inclui o teste temporario da QA"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0035-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0035-* = $backups"

if [ -f backend/tests/Squid.Tests/Threads/QaTask0035RemovedRoutesTests.cs ]; then check TEMP-FILE 1 "arquivo temporario da QA ainda na arvore"; else check TEMP-FILE 0 "QaTask0035RemovedRoutesTests.cs ausente"; fi

bom=$(head -c 3 "$qa/qa-mutate-task-0035.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0035.ps1 com UTF-8 BOM ($bom)"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0035-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "2" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 2"
grep -q 'mtime-count=2 (esperado 2' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 2 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'temp-test-file-present=False' "$scope"; check SCOPE-TEMPFILE $? "scope scan: teste temporario ausente"

doc="$qa/QA-0035-participantes-e-admin.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0035-participantes-e-admin.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
