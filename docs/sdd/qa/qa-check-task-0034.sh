#!/usr/bin/env bash
# QA-0034 evidence checker - twin of qa-check-task-0034.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there.
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0034.sh (cd tres niveis: qa -> sdd -> docs -> raiz)
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
if [ -n "$gate" ] && [ "$(basename "$gate")" = "20261112-025653-test-gate.log" ] \
   && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +443, Ignorado: +0, Total: +443' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 443/443"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 443/443"
fi

if grep -qE '^exit: +0$' "$runs/20261112-023919-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +11' "$runs/20261112-023919-test-filter.log"; then
  check BASELINE 0 "023919 exit 0 / 11 de 11"
else
  check BASELINE 1 "baseline 023919 ausente ou vermelha"
fi

greens_ok=0
for g in 20261112-024603-test-filter.log 20261112-025001-test-filter.log; do
  if grep -qE '^exit: +0$' "$runs/$g" 2>/dev/null && grep -qE 'Com falha: +0, Aprovado: +11' "$runs/$g"; then
    greens_ok=$((greens_ok + 1))
  fi
done
[ "$greens_ok" = "2" ]; check GREENS $? "GREENs 11/11 pos-restore = $greens_ok de 2"

mut="$qa/QA-0034-mutations.log"
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
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=11 falha=0' "$mut")
[ "$greens" -ge 2 ]; check MUT-GREEN $? "GREEN 11/11 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "1" ]; check MUT-ANOMALIA $? "exatamente 1 CLASSIFICACAO-ERRADA (M12 v1, mutante corrigido)"
grep -q 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cheio cycles=13 failed=1 residue=0"
grep -q 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-LAST $? "SUMMARY final cycles=1 failed=0 residue=0"

pr="$qa/QA-0034-unique-probe.log"
grep -q 'PRISTINE wrapper=0 logExit=0 aprovado=1 falha=0' "$pr"; check PROBE-PRISTINE $? "schema original: probe verde"
grep -q 'MUTATED wrapper=1 logExit=1 aprovado=0 falha=1' "$pr"; check PROBE-MUTATED $? "0003 sem UNIQUE: probe vermelho (can-fail)"
grep -q 'MUTATED RED Squid.Tests.Threads.QaTask0034UniqueProbeTests' "$pr"; check PROBE-MUTATED-RED $? "vermelho nomeado no teste do probe"
grep -q 'RESTORE-OK hash=PRE residue=0' "$pr"; check PROBE-RESTORE $? "0003 restaurado byte-exact"
grep -q 'GIT-BACKEND entradas=0' "$pr"; check PROBE-GIT $? "arquivo temporario removido, git backend limpo"
grep -q 'SUMMARY pristine=green mutated=red restore=OK testfile-removed=yes git-backend=0 fails=0' "$pr"; check PROBE-SUMMARY $? "SUMMARY do probe fails=0"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0034-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0034-* = $backups"

if [ -f backend/tests/Squid.Tests/Threads/QaTask0034UniqueProbeTests.cs ]; then check PROBE-FILE 1 "arquivo temporario do probe ainda na arvore"; else check PROBE-FILE 0 "QaTask0034UniqueProbeTests.cs ausente"; fi

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0034-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "3" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 3"
grep -q 'mtime-count=3 (esperado 3)' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 3 arquivos mutados"

doc="$qa/QA-0034-envio-e-leitura.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0034-envio-e-leitura.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
