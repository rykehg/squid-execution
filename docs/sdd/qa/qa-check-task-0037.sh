#!/usr/bin/env bash
# QA-0037 evidence checker - twin of qa-check-task-0037.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0037.sh (cd trois niveaux: qa -> sdd -> docs -> racine)
set -u
cd "$(dirname "$0")/../../.." || exit 2
runs=docs/sdd/runs/test-runs
qa=docs/sdd/qa
gate_name='20261112-065700-test-gate.log'
total=0; fails=0

check() { # name ok(0=ok) detail
  total=$((total + 1))
  if [ "$2" = "0" ]; then echo "CHECK $1 OK - $3"
  else fails=$((fails + 1)); echo "CHECK $1 FAIL - $3"; fi
}

if grep -q 'HEAD: 6dfac8530d70cfbb6483aca6a955eb9f0cb35c27' "$qa/QA-0037-git-baseline.txt" \
   && grep -q 'branch: task/TASK-0037-resposta-a-mensagem' "$qa/QA-0037-git-baseline.txt"; then
  check BASELINE 0 "HEAD 6dfac85 na branch task/TASK-0037-resposta-a-mensagem"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && [ "$(basename "$gate")" = "$gate_name" ] \
   && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +462, Ignorado: +0, Total: +462' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 462/462"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 462/462"
fi

if grep -qE '^exit: +0$' "$runs/20261112-064406-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +30' "$runs/20261112-064406-test-filter.log"; then
  check BASELINE-FILTER 0 "064406 exit 0 / 30 de 30 (baseline filtrada da QA)"
else
  check BASELINE-FILTER 1 "baseline filtrada 064406 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-065047-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +30' "$runs/20261112-065047-test-filter.log"; then
  check GREEN-FILTER 0 "065047 exit 0 / 30 de 30 (GREEN do fim do driver)"
else
  check GREEN-FILTER 1 "GREEN 065047 ausente ou vermelha"
fi

mut="$qa/QA-0037-mutations.log"
missing=""
not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9 M10; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=[0-9]+ aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M10 todos com linha PRE${missing:+ faltando:$missing}"
[ -z "$not_red" ]; check MUT-RED $? "M1..M10 todos com RUN vermelho no log${not_red:+ sem-vermelho:$not_red}"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 10 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 10)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=30 falha=0' "$mut")
[ "$greens" -ge 1 ]; check MUT-GREEN $? "GREEN 30/30 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "0" ]; check MUT-ANOMALIA $? "nenhuma CLASSIFICACAO-ERRADA (S-22)"
grep -q 'SUMMARY cycles=10 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cycles=10 failed=0 residue=0"

grep -qE '^M1 RED-IN-LOG .*ThreadStoreTests.The_append_gates' "$mut" && grep -qE '^M1 RED-IN-LOG .*A_target_from_another_thread' "$mut"
check CATCH-M1 $? "M1 (alvo de outra thread aceito) pego pelo store E pela rota"
grep -qE '^M2 RED-IN-LOG .*ThreadStoreTests.The_append_gates' "$mut" && grep -qE '^M2 RED-IN-LOG .*A_target_from_another_thread' "$mut"
check CATCH-M2 $? "M2 (alvo inexistente aceito) pego pelo store E pela rota"
grep -qE '^M3 RED-IN-LOG .*A_send_missing_both_the_acceptance_and_a_valid_target_answers_428_then_422' "$mut"
check CATCH-M3 $? "M3 (422 antes do 428) pego pela escada da ordem"
grep -qE '^M4 RED-IN-LOG .*ThreadStoreTests.The_append_gates' "$mut"; check CATCH-M4 $? "M4 (recusa que queima seq) pego pelo store"
grep -qE '^M5 RED-IN-LOG .*ThreadStoreTests.The_append_gates' "$mut" && grep -qE '^M5 RED-IN-LOG .*A_reply_to_a_message_of_the_same_thread' "$mut"
check CATCH-M5 $? "M5 (reply_to_id nao gravado) pego pelo store E pelo envelope"
grep -qE '^M6 RED-IN-LOG .*A_reply_to_a_message_of_the_same_thread' "$mut"; check CATCH-M6 $? "M6 (envelope sem replyToId) pego pela rota"
grep -qE '^M7 RED-IN-LOG .*A_target_from_another_thread' "$mut"; check CATCH-M7 $? "M7 (409 no lugar do 422) pego pela rota"
grep -qE '^M8 RED-IN-LOG .*byte_identical_404' "$mut"; check CATCH-M8 $? "M8 (sonda de participante desligada) pego pela parede"
grep -qE '^M9 RED-IN-LOG .*byte_identical_404' "$mut"; check CATCH-M9 $? "M9 (404 com traceId) pego pela parede"
grep -qE '^M10 RUN wrapper=1 logExit=1 aprovado=[0-9]+ falha=[1-9][0-9]* constraint=yes' "$mut" \
   && grep -qE '^M10 RED-IN-LOG .*ThreadMigrationTests' "$mut"
check CATCH-M10 $? "M10 (ledger da 0005 desligado) vermelho com duplicate column nas migracoes"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0037-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0037-* = $backups"

bom=$(head -c 3 "$qa/qa-mutate-task-0037.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0037.ps1 com UTF-8 BOM ($bom)"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0037-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "4" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 4"
grep -q 'mtime-count=4 (esperado 4' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 4 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"

doc="$qa/QA-0037-resposta-a-mensagem.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0037-resposta-a-mensagem.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
