#!/usr/bin/env bash
# QA-0041 evidence checker - twin of qa-check-task-0041.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0041.sh
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

if grep -q 'HEAD: 9210469b3dec6866466f34e97babcde3adffc2c1' "$qa/QA-0041-git-baseline.txt" \
   && grep -q 'branch: task/TASK-0041-dominio-canonico-e-proxy' "$qa/QA-0041-git-baseline.txt"; then
  check BASELINE 0 "HEAD 9210469 na branch task/TASK-0041-dominio-canonico-e-proxy"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi
grep -q 'dotnet-missing' "$qa/QA-0041-git-baseline.txt"
check BASELINE-WSL $? "dotnet ausente no WSL registrado (suite Linux = unverified)"

if grep -qE '^exit: +0$' "$runs/20261113-153601-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +4, Ignorado: +0, Total: +4' "$runs/20261113-153601-test-filter.log" 2>/dev/null; then
  check BASELINE-FILTER 0 "153601 exit 0 / 4 de 4 (baseline CanonicalOriginConfigTests da QA)"
else
  check BASELINE-FILTER 1 "baseline 153601 ausente ou vermelha"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +484, Ignorado: +0, Total: +484' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 484/484"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 484/484"
fi

mut="$qa/QA-0041-mutations.log"
missing=""
for id in M1 M2 M3 M4a M4b M5 M6 M7 M8 M9 M10 M11; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M11 (12 ciclos) todos com linha PRE${missing:+ faltando:$missing}"

not_red=""
for id in M1 M2 M3 M4a M4b M5 M6 M7 M8 M9 M10 M11; do
  grep -qE "^$id RUN wrapper=1 logExit=1 aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$not_red" ]; check MUT-RED $? "M1..M11 todos vermelhos dentro do log${not_red:+ sem-vermelho:$not_red}"

redcount=$(grep -cE '^(M[0-9]+|M4a|M4b) RUN wrapper=1 logExit=1 ' "$mut")
[ "$redcount" = "12" ]; check MUT-LOGEXIT $? "12/12 ciclos com logExit=1 lido no cabecalho do log (S-22)"

expect_ok=$(grep -cE '^(M[0-9]+|M4a|M4b) EXPECT-OK ' "$mut")
miss=$(grep -c 'EXPECT-MISS' "$mut")
[ "$expect_ok" = "12" ] && [ "$miss" = "0" ]; check MUT-EXPECT $? "12/12 EXPECT-OK e 0 EXPECT-MISS (esperado 12, achou $expect_ok)"

catch_one() { # id anchors(separados por |)
  local id="$1" anchors="$2" a ok=0
  IFS='|' read -ra parts <<< "$anchors"
  for a in "${parts[@]}"; do
    if ! grep -qE "^$id RED-AC .*${a}" "$mut"; then ok=1; fi
  done
  if grep -qE "^$id RED-AC NONE" "$mut"; then ok=1; fi
  check "CATCH-$id" $ok "$id vermelho na AC esperada ($anchors)"
}
catch_one M1  'Ac01_without_configuration|Ac03_forwarded_headers'
catch_one M2  'Ac02_with_instance_domain'
catch_one M3  'Ac02_with_instance_domain'
catch_one M4a 'Ac01_without_configuration|Ac02_with_instance_domain|Ac03_forwarded_headers'
catch_one M4b 'Ac02_with_instance_domain'
catch_one M5  'Ac03_forwarded_headers'
catch_one M6  'Ac03_forwarded_headers'
catch_one M7  'Ac04_error_answers_carry_neither_the_configured_domain_nor_a_stack_trace'
catch_one M8  'Ac02_with_instance_domain'
catch_one M9  'Ac02_with_instance_domain'
catch_one M10 'Ac02_with_instance_domain'
catch_one M11 'Ac02_with_instance_domain'

cls=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$cls" = "0" ]; check MUT-CLASSIFICACAO $? "nenhuma CLASSIFICACAO-ERRADA (0)"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 12 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 12)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
grep -qE '^GREEN wrapper=0 logExit=0 aprovado=4 falha=0 ' "$mut"; check MUT-GREEN $? "GREEN 4/4 sobre as fontes restauradas"
grep -q 'SUMMARY cycles=12 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY $? "SUMMARY cycles=12 failed=0 residue=0 temp-backups=0 (run cheio)"

residue=$(find backend -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0041-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0041-* = $backups"

nonascii=$(tr -d '\000-\177' < "$qa/qa-mutate-task-0041.ps1" | wc -c)
[ "$nonascii" = "0" ]; check DRIVER-ASCII $? "qa-mutate-task-0041.ps1 sem byte > 127 ($nonascii)"

grep -q 'VERIFY total=12 bad=0' "$qa/QA-0041-verify.log" 2>/dev/null; check VERIFY-RECORDED $? "QA-0041-verify.log com VERIFY total=12 bad=0"

cfg=backend/tests/Squid.Tests/Discovery/CanonicalOriginConfigTests.cs
facts=$(grep -c '\[Fact\]' "$cfg")
[ "$facts" = "4" ]; check CFG-FACTS $? "CanonicalOriginConfigTests com 4 [Fact] (AC-01..AC-04), achou $facts"
helper=$(grep -l 'Origin(HttpContext context) => CanonicalOrigin.Of(context)' \
  backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs \
  backend/src/Squid.Api/Posts/PostEndpoints.cs \
  backend/src/Squid.Api/Containers/ContainerEndpoints.cs \
  backend/src/Squid.Api/Threads/ThreadEndpoints.cs 2>/dev/null | wc -l)
[ "$helper" = "4" ]; check FOUR-POINTS $? "os quatro pontos com Origin => CanonicalOrigin.Of: $helper/4"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0041-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "6" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 6"
grep -q 'mtime-count=6 (esperado 6' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 6 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"
# Nothing NEW outside docs/sdd since the baseline: every remaining entry must be either the
# pre-existing ODEV docs WIP captured at 15:41 or this QA's own artifacts in docs/sdd/qa.
outside=$(git status --porcelain -uall | sed 's/^ *//' | while IFS= read -r e; do
  case "$e" in
    *docs/sdd/qa/QA-0041*|*docs/sdd/qa/qa-check-task-0041*|*docs/sdd/qa/qa-mutate-task-0041*|*docs/sdd/tasks/TASK-0041*) ;;
    *) grep -qF "$e" "$qa/QA-0041-git-baseline.txt" || echo "$e" ;;
  esac
done | wc -l)
[ "$outside" = "0" ]; check SCOPE-GIT-SCOPE $? "git status fora do WIP pre-existente e dos artefatos da QA = $outside entradas (esperado 0)"

doc="$qa/QA-0041-dominio-canonico-e-proxy.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0041-dominio-canonico-e-proxy.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
