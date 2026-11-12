#!/usr/bin/env bash
# QA-0040 evidence checker - twin of qa-check-task-0040.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0040.sh (cd trois niveaux: qa -> sdd -> docs -> racine)
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

if grep -q 'HEAD: 01b741f1e69d061cfbe17911d5b2f6ef9912337d' "$qa/QA-0040-git-baseline.txt" \
   && grep -q 'branch: task/TASK-0040-facet-do-dod-sprint4' "$qa/QA-0040-git-baseline.txt"; then
  check BASELINE 0 "HEAD 01b741f na branch task/TASK-0040-facet-do-dod-sprint4"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi

if grep -qE '^exit: +0$' "$runs/20261112-112436-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +48, Ignorado: +0, Total: +48' "$runs/20261112-112436-test-filter.log" 2>/dev/null; then
  check BASELINE-THREADS 0 "112436 exit 0 / 48 de 48 (baseline Threads da QA)"
else
  check BASELINE-THREADS 1 "baseline Threads 112436 ausente ou vermelha"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +480, Ignorado: +0, Total: +480' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 480/480"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 480/480"
fi

mut="$qa/QA-0040-mutations.log"
missing=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M9 todos com linha PRE${missing:+ faltando:$missing}"

not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9; do
  grep -qE "^$id RUN wrapper=1 logExit=1 aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$not_red" ]; check MUT-RED $? "M1..M9 todos vermelhos dentro do log${not_red:+ sem-vermelho:$not_red}"

redcount=$(grep -cE '^M[1-9] RUN wrapper=1 logExit=1 ' "$mut")
[ "$redcount" = "9" ]; check MUT-LOGEXIT $? "9/9 ciclos com logExit=1 lido no cabecalho do log (S-22)"

grep -qE '^M1 RED-FACET .*Two_users_create_a_direct' "$mut" \
   && grep -qE '^M1 RED-FACET .*The_full_flow_from_creation' "$mut" \
   && grep -qE '^M1 RED-FACET .*A_seat_spelled_read' "$mut"
check CATCH-M1 $? "M1 (marcador nunca gravado) vermelho na AC-01 + AC-06 + AC-05a"
grep -qE '^M2 RED-FACET .*A_third_user_shares' "$mut" && grep -qE '^M2 RED-FACET .*Repeating_a_reply' "$mut"
check CATCH-M2 $? "M2 (404 com traceId) vermelho na AC-02 + AC-04"
grep -qE '^M3 RED-FACET .*A_third_user_shares' "$mut" && grep -qE '^M3 RED-FACET .*Repeating_a_reply' "$mut"
check CATCH-M3 $? "M3 (parede de leitura desligada) vermelho na AC-02 + AC-04"
grep -qE '^M4 RED-FACET .*Concurrent_sends_take' "$mut" && grep -qE '^M4 RED-FACET .*Two_users_create_a_direct' "$mut"
check CATCH-M4 $? "M4 (seq colidindo) vermelho na AC-03 + AC-01"
grep -qE '^M5 RED-FACET .*Repeating_a_reply' "$mut" && grep -qE '^M5 RED-FACET .*The_full_flow_from_creation' "$mut"
check CATCH-M5 $? "M5 (reply reescreve o alvo) vermelho na AC-04 + AC-06"
grep -qE '^M6 RED-FACET .*A_seat_spelled_read' "$mut"
check CATCH-M6 $? "M6 (cadeira read engolida) vermelho so na AC-05a (falha=1)"
grep -qE '^M7 RED-FACET .*The_ack_row_carries' "$mut"
check CATCH-M7 $? "M7 (texto do aviso fora da linha) vermelho so na AC-05b (falha=1)"
grep -qE '^M8 RED-FACET .*The_full_flow_from_creation' "$mut"
check CATCH-M8 $? "M8 (ramo mensagem da midia morto) vermelho na AC-06"

grep -qE '^M9 RED-FACET NONE' "$mut" \
   && grep -qE '^M9 RUN wrapper=1 logExit=1 aprovado=[0-9]+ falha=[1-9]' "$mut"
check M9-SURVIVES-FACET $? "M9 (marcador pode regredir) sobrevive a facet mas a suite fica vermelha"
grep -qE '^M9 RED-IN-LOG .*The_read_marker_only_advances' "$mut"
check M9-CAUGHT-BY-SUITE $? "M9 RED-IN-LOG pega em The_read_marker_only_advances (monotonicidade da TASK-0036)"

cls=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$cls" = "0" ]; check MUT-CLASSIFICACAO $? "nenhuma CLASSIFICACAO-ERRADA (0)"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 9 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 9)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
grep -qE '^GREEN wrapper=0 logExit=0 aprovado=48 falha=0 ' "$mut"; check MUT-GREEN $? "GREEN 48/48 sobre as fontes restauradas"
grep -q 'SUMMARY cycles=8 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY $? "SUMMARY cycles=8 failed=0 residue=0 temp-backups=0 (run cheio)"
grep -q 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-M9 $? "SUMMARY cycles=1 failed=0 (re-execucao seletiva do M9)"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0040-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0040-* = $backups"

bom=$(head -c 3 "$qa/qa-mutate-task-0040.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0040.ps1 com UTF-8 BOM ($bom)"

grep -q 'VERIFY total=9 bad=0' "$qa/QA-0040-verify.log" 2>/dev/null; check VERIFY-RECORDED $? "QA-0040-verify.log com VERIFY total=9 bad=0"

facet=backend/tests/Squid.Tests/Threads/Sprint4ClosingFacetTests.cs
grep -q 'await Task\.WhenAll(' "$facet" && grep -q 'for (var round = 0; round < 3; round++)' "$facet"
check CONCURRENCY-REAL $? "AC-03 usa Task.WhenAll (3 rodadas), nao envio sequencial"
facts=$(grep -c '\[Fact\]' "$facet")
[ "$facts" = "7" ]; check FACET-FACTS $? "facet com 7 [Fact] (AC-01..AC-06), achou $facts"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0040-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "3" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 3"
grep -q 'mtime-count=3 (esperado 3' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 3 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"
outside=$(git status --porcelain -uall | grep -vE 'docs/sdd/(tasks/TASK-0040|qa/)' | wc -l)
[ "$outside" = "0" ]; check SCOPE-GIT-SCOPE $? "git status fora de docs/sdd = $outside entradas (esperado 0)"

doc="$qa/QA-0040-facet-do-dod-sprint4.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0040-facet-do-dod-sprint4.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
