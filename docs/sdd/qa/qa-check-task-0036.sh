#!/usr/bin/env bash
# QA-0036 evidence checker - twin of qa-check-task-0036.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there.
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0036.sh (cd tres niveis: qa -> sdd -> docs -> raiz)
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
if [ -n "$gate" ] && [ "$(basename "$gate")" = "20261112-055345-test-gate.log" ] \
   && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +456, Ignorado: +0, Total: +456' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 456/456"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 456/456"
fi

if grep -qE '^exit: +0$' "$runs/20261112-053508-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +25' "$runs/20261112-053508-test-filter.log"; then
  check BASELINE 0 "053508 exit 0 / 25 de 25 (24 DEV + 1 QA)"
else
  check BASELINE 1 "baseline 053508 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-051348-test-gate.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +456' "$runs/20261112-051348-test-gate.log"; then
  check DEV-GATE-BASELINE 0 "051348 exit 0 / 456 de 456 (gate do DEV)"
else
  check DEV-GATE-BASELINE 1 "gate do DEV 051348 ausente ou vermelha"
fi

mut="$qa/QA-0036-mutations.log"
missing=""
not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M11 M12 M13 M14 M15; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=[0-9]+ aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M15 todos com linha PRE${missing:+ faltando:$missing}"
[ -z "$not_red" ]; check MUT-RED $? "M1..M15 todos com RUN vermelho no log${not_red:+ sem-vermelho:$not_red}"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 15 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 15)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=25 falha=0' "$mut")
[ "$greens" -ge 1 ]; check MUT-GREEN $? "GREEN 25/25 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "0" ]; check MUT-ANOMALIA $? "nenhuma CLASSIFICACAO-ERRADA (S-22)"
grep -q 'SUMMARY cycles=15 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cycles=15 failed=0 residue=0"
grep -qE '^M2 RED-IN-LOG .*ThreadStoreTests.The_append_gates' "$mut" && grep -qE '^M2 RED-IN-LOG .*ThreadAckAndReadRoutesTests' "$mut"
check MUT-M2-CATCH $? "M2 (recusa queima seq) pego pelo store E pela rota"
grep -qE '^M4 RUN wrapper=1 logExit=1 aprovado=22 falha=3 constraint=yes' "$mut"; check MUT-M4 $? "M4 (aceite sem idempotencia) 3 falhas com UNIQUE constraint"
grep -qE '^M11 RED-IN-LOG .*A_suspended_actor_is_denied' "$mut"; check MUT-M11-CATCH $? "M11 (428 antes do 403) pego pela escada da AC-05"
grep -qE '^M13 RED-IN-LOG .*QaTask0036RouteCollisionTests' "$mut"; check MUT-M13-QATEST $? "M13 (parser) vermelho pelo teste temporario da QA"
grep -qE '^M14 RED-IN-LOG .*QaTask0036RouteCollisionTests' "$mut"; check MUT-M14-QATEST $? "M14 (aviso FR-032) vermelho pelo teste temporario da QA"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0036-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0036-* = $backups"

if [ -f backend/tests/Squid.Tests/Threads/QaTask0036RouteCollisionTests.cs ]; then check TEMP-FILE 1 "arquivo temporario da QA ainda na arvore"; else check TEMP-FILE 0 "QaTask0036RouteCollisionTests.cs ausente"; fi
if [ -f docs/sdd/qa/QaTask0036RouteCollisionTests.cs.txt ]; then check TEMP-SOURCE 0 "fonte do teste temporario preservada"; else check TEMP-SOURCE 1 "fonte ausente em docs/sdd/qa"; fi

bom=$(head -c 3 "$qa/qa-mutate-task-0036.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0036.ps1 com UTF-8 BOM ($bom)"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0036-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "2" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 2"
grep -q 'mtime-count=2 (esperado 2' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 2 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'temp-test-file-present=False' "$scope"; check SCOPE-TEMPFILE $? "scope scan: teste temporario ausente"

doc="$qa/QA-0036-aviso-nao-e2ee-e-leitura.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0036-aviso-nao-e2ee-e-leitura.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
