#!/usr/bin/env bash
# QA-0033 evidence checker - twin of qa-check-task-0033.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0033.sh (cd três níveis: qa -> sdd -> docs -> raiz)
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
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +438, Ignorado: +0, Total: +438' "$gate"; then
  check GATE-EXIT 0 "$(basename "$gate") header exit 0 e 438/438"
else
  check GATE-EXIT 1 "gate: ${gate:-ausente} sem exit 0 ou 438/438"
fi

filt=$(ls "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
if [ -n "$filt" ] && [ "$(basename "$filt")" \> "20261112-012742-test-filter.log" -o "$(basename "$filt")" = "20261112-012742-test-filter.log" ] \
   && grep -qE '^exit: +0$' "$filt" && grep -qE 'Com falha: +0, Aprovado: +9' "$filt"; then
  check FILTER-GREEN 0 "$(basename "$filt") exit 0 / 9 de 9"
else
  check FILTER-GREEN 1 "filtro: ${filt:-ausente} nao e o GREEN pos-M10"
fi

mut="$qa/QA-0033-mutations.log"
restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 14 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 14)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
summaries_bad=$(grep 'SUMMARY' "$mut" | grep -vc 'residue-QA-MUTATION=0 temp-backups=0')
[ "$summaries_bad" = "0" ]; check MUT-SUMMARYS $? "SUMMARYs todas com residue=0 e temp-backups=0 (fora=$summaries_bad)"
missing=""
for id in M1 M2 M3 M4A M4B M4C M4D M5 M6 M7 M8 M9 M10; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M10 todos com linha PRE${missing:+ faltando:$missing}"
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=9 falha=0' "$mut")
[ "$greens" -ge 3 ]; check MUT-GREEN $? "GREEN 9/9 = $greens"
anom=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$anom" = "1" ]; check MUT-ANOMALIA $? "exatamente 1 CLASSIFICACAO-ERRADA (M4D quebrou o build, corrigido)"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0033-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0033-* = $backups"

hash_bad=""
declare -A expected=(
  ["backend/src/Squid.Service/SqliteThreadStore.cs"]=86679F604F0A03A9CA2BA86ADA108AE58E3C6687C8EC32012858057EA31F002A
  ["backend/src/Squid.Persistence/schema/SchemaMigrator.cs"]=7B51779F621A054B497A2E2550AAFC57F178861D0D594DDE9ACB4843CE5EAF07
  ["backend/src/Squid.Persistence/schema/0002_threads.sql"]=EA1D8BE3BF489BE94B452B34A04E631A40A8CA254FE340769650B8CF48F923A5
  ["backend/src/Squid.Api/Threads/ThreadEndpoints.cs"]=758FDCDE5A01DE42EF04E8F1FD9002A7FD4D049ED952E9621EA69B1B40104CA6
  ["backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs"]=F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948
)
for f in "${!expected[@]}"; do
  h=$(sha256sum "$f" 2>/dev/null | awk '{print toupper($1)}')
  [ "$h" = "${expected[$f]}" ] || hash_bad="$hash_bad $f"
done
[ -z "$hash_bad" ]; check HASH-PRE $? "5 arquivos mutados = PRE/HEAD (S-16)${hash_bad:+ divergente:$hash_bad}"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

doc="$qa/QA-0033-nucleo-da-thread.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0033-nucleo-da-thread.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
