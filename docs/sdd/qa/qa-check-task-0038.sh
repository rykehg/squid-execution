#!/usr/bin/env bash
# QA-0038 evidence checker - twin of qa-check-task-0038.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0038.sh (cd trois niveaux: qa -> sdd -> docs -> racine)
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

if grep -q 'HEAD: 8903e675c5f9a3855f7e87ebca4dd12202886edb' "$qa/QA-0038-git-baseline.txt" \
   && grep -q 'branch: task/TASK-0038-reacao-whitelist' "$qa/QA-0038-git-baseline.txt"; then
  check BASELINE 0 "HEAD 8903e67 na branch task/TASK-0038-reacao-whitelist"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +468, Ignorado: +0, Total: +468' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 468/468"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 468/468"
fi

if grep -qE '^exit: +0$' "$runs/20261112-075557-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +36' "$runs/20261112-075557-test-filter.log"; then
  check BASELINE-FILTER 0 "075557 exit 0 / 36 de 36 (baseline filtrada da QA)"
else
  check BASELINE-FILTER 1 "baseline filtrada 075557 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-080317-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +38' "$runs/20261112-080317-test-filter.log"; then
  check QA-TEMP-FILTER 0 "080317 exit 0 / 38 de 38 (com o teste temporario da QA)"
else
  check QA-TEMP-FILTER 1 "verde com teste temporario 080317 ausente ou vermelho"
fi

mut="$qa/QA-0038-mutations.log"
missing=""
not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M11; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=[0-9]+ aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M11 todos com linha PRE${missing:+ faltando:$missing}"
[ -z "$not_red" ]; check MUT-RED $? "M1..M11 todos com RUN vermelho no log${not_red:+ sem-vermelho:$not_red}"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 14 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 14)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=38 falha=0' "$mut")
[ "$greens" -ge 2 ]; check MUT-GREEN $? "GREEN 38/38 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "0" ]; check MUT-ANOMALIA $? "nenhuma CLASSIFICACAO-ERRADA (S-22)"
grep -q 'SUMMARY cycles=11 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cycles=11 failed=0 residue=0"
grep -q 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-M4 $? "SUMMARY cycles=1 failed=0 (M4 corrigido)"

grep -qE '^M1 RED-IN-LOG .*The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing' "$mut"
check CATCH-M1 $? "M1 (sem ON CONFLICT) pego pela AC-01"
grep -qE '^M2 RED-IN-LOG .*The_same_message_actor_emoji_is_one_row_and_repeating_it_changes_nothing' "$mut"
check CATCH-M2 $? "M2 (chave sem o emoji) pego pela AC-01"
grep -qE '^M3 RED-IN-LOG .*An_emoji_outside_Reactions_AllowedEmojis_is_422_and_never_reaches_a_row' "$mut"
check CATCH-M3 $? "M3 (Allows sempre true) pego pela AC-02"
if grep -E '^M4 RED-IN-LOG ' "$mut" | tail -1 | grep -q 'ThreadMigrationTests' \
   && grep -qE '^M4 RUN wrapper=1 logExit=1 aprovado=37 falha=1' "$mut"; then
  check CATCH-M4 0 "M4 (contador VALIDO) vermelho so na migration 0006 (37/1)"
else
  check CATCH-M4 1 "M4 nao pegou so a migration 0006"
fi
grep -qE '^M5 RED-IN-LOG .*A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes' "$mut"
check CATCH-M5 $? "M5 (422 antes da sonda) pego pela AC-04"
# CATCH-M6 reads M6's own RUN LOG (S-22): xUnit wrapped the long Fact A name inside the [FAIL]
# line, so the ledger fragment is not contiguous - the "Com falha ... <full name>" summary line
# of the raw log is the proof.
m6log=$(grep -E '^M6 RUN ' "$mut" | tail -1 | sed 's/.*log=//' | tr -d '\r')
if grep -q '^M6 RED-IN-LOG ' "$mut" \
   && [ -n "$m6log" ] && grep -q 'Com falha Squid.Tests.Threads.QaTask0038ReactionRereadTests.Mutating_the_config_source_changes_the_accepted_set_without_a_restart' "$runs/$m6log"; then
  check CATCH-M6 0 "M6 (cache de config) pego pelo Fact A no log $m6log"
else
  check CATCH-M6 1 "M6 nao pegou o Fact A (log ${m6log:-desconhecido})"
fi
grep -qE '^M7 RED-IN-LOG .*Deleting_takes_the_actor_row_the_count_follows_the_rows_and_a_missing_one_is_404' "$mut"
check CATCH-M7 $? "M7 (DELETE sem actor_id) pego pela AC-03"
grep -qE '^M8 RED-IN-LOG .*A_stranger_gets_one_404_body_on_read_and_on_both_reaction_routes' "$mut"
check CATCH-M8 $? "M8 (parede desligada) pego pela AC-04"
grep -qE '^M9 RED-IN-LOG .*ThreadReactionRoutesTests' "$mut"
check CATCH-M9 $? "M9 (404 com traceId) pego pela byte-identidade"
grep -qE '^M10 RED-IN-LOG .*The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404' "$mut" \
   && grep -qE '^M10 RED-DETAIL .*Expected: NotFound' "$mut"
check CATCH-M10 $? "M10 (GET aberto) pego pelo Fact B (Expected NotFound)"
grep -qE '^M11 RED-IN-LOG .*The_reaction_ladder_answers_401_first_and_GET_stays_the_shared_404' "$mut" \
   && grep -qE '^M11 RED-DETAIL .*Expected: Unauthorized' "$mut"
check CATCH-M11 $? "M11 (401 virou 404) pego pelo Fact B (Expected Unauthorized)"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0038-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0038-* = $backups"

bom=$(head -c 3 "$qa/qa-mutate-task-0038.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0038.ps1 com UTF-8 BOM ($bom)"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

if [ ! -f backend/tests/Squid.Tests/Threads/QaTask0038ReactionRereadTests.cs ]; then
  check TEMP-TEST-GONE 0 "teste temporario removido da arvore"
else
  check TEMP-TEST-GONE 1 "teste temporario ainda no projeto"
fi
[ -f "$qa/QaTask0038ReactionRereadTests.cs.txt" ]; check TEMP-TEST-SOURCE $? "fonte .cs.txt preservada em docs/sdd/qa"

scope="$qa/QA-0038-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "4" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 4"
grep -q 'mtime-count=4 (esperado 4' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 4 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"

doc="$qa/QA-0038-reacao-whitelist.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0038-reacao-whitelist.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
