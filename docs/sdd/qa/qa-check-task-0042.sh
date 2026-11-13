#!/usr/bin/env bash
# QA-0042 evidence checker - twin of qa-check-task-0042.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0042.sh
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

base="$qa/QA-0042-git-baseline.txt"
if grep -q 'HEAD: 0814968ece8d23a810078f3a93282c28296a407f' "$base" \
   && grep -q 'branch: task/TASK-0042-chave-de-assinatura-em-runtime' "$base"; then
  check BASELINE 0 "HEAD 0814968 na branch task/TASK-0042-chave-de-assinatura-em-runtime"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi
grep -qF 'command -v dotnet => [] exit=1' "$base"
check BASELINE-WSL $? "dotnet ausente no WSL registrado (suite Linux = unverified)"

# 0814968 touches only: every file of the commit inside the task's touches (+ task Log/Evidence).
outside=$(awk '/^--- commit file list ---/{f=1;next} /^--- /{f=0} f && NF' "$base" | \
  grep -vE '^(backend/src/Squid\.Api/Crypto/|backend/src/Squid\.Grains/Actors/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/|docs/sdd/tasks/TASK-0042-)' | wc -l)
[ "$outside" = "0" ]; check COMMIT-TOUCHES $? "0814968: 13 arquivos, todos nos touches ($outside fora)"

if grep -qE '^exit: +0$' "$runs/20261113-180909-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +19, Ignorado: +0, Total: +19' "$runs/20261113-180909-test-filter.log" 2>/dev/null; then
  check BASELINE-FILTER 0 "180909 exit 0 / 19 de 19 (baseline das tres classes novas da QA)"
else
  check BASELINE-FILTER 1 "baseline 180909 ausente ou vermelha"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +503, Ignorado: +0, Total: +503' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 503/503"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 503/503"
fi

mut="$qa/QA-0042-mutations.log"
missing=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
done
[ -z "$missing" ]; check MUT-IDS $? "T1..T18 (18 ciclos) todos com linha PRE${missing:+ faltando:$missing}"

not_red=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18; do
  # S-22: the wrapper console can misreport (it prints 3 for a real red); logExit is the truth.
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=1 aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$not_red" ]; check MUT-RED $? "T1..T18 todos vermelhos dentro do log${not_red:+ sem-vermelho:$not_red}"

redcount=$(grep -cE '^T[0-9]+ RUN wrapper=[0-9]+ logExit=1 ' "$mut")
[ "$redcount" = "18" ]; check MUT-LOGEXIT $? "18/18 ciclos com logExit=1 lido no cabecalho do log (S-22) achou $redcount"

catch_one() { # id
  local id="$1" expect ac ok=0 a
  # The ledger is written by Windows PowerShell: CRLF. Strip CR or the anchors never match.
  expect=$(grep -E "^$id EXPECT " "$mut" | head -1 | sed 's/^[^ ]* EXPECT //' | tr -d '\r')
  ac=$(grep -E "^$id RED-AC " "$mut" | head -1 | sed 's/^[^ ]* RED-AC //' | tr -d '\r')
  [ -n "$expect" ] && [ -n "$ac" ] || { check "CATCH-$id" 1 "EXPECT ou RED-AC ausente"; return; }
  echo "$ac" | grep -q 'NONE' && ok=1
  IFS='|' read -ra parts <<< "$expect"
  for a in "${parts[@]}"; do
    if ! printf '%s' "$ac" | grep -qF "$a"; then ok=1; fi
  done
  check "CATCH-$id" $ok "$id vermelho na AC esperada ($expect)"
}
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18; do catch_one "$id"; done

expect_ok=$(grep -cE '^T[0-9]+ EXPECT-OK ' "$mut")
miss=$(grep -c 'EXPECT-MISS' "$mut")
[ "$expect_ok" = "18" ] && [ "$miss" = "0" ]; check MUT-EXPECT $? "18/18 EXPECT-OK e 0 EXPECT-MISS (esperado 18, achou $expect_ok)"

cls=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$cls" = "0" ]; check MUT-CLASSIFICACAO $? "nenhuma CLASSIFICACAO-ERRADA (0)"

grep -qE '^T16 EXPECT-OK .*falha=2([^0-9]|$)' "$mut"; check MUT-T16-2CASES $? "T16 (theory draft) com falha=2: rsa-sha256 e hs2019 juntos"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 18 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 18)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
grep -qE '^GREEN wrapper=0 logExit=0 aprovado=19 falha=0 ' "$mut"; check MUT-GREEN $? "GREEN 19/19 sobre as fontes restauradas"
grep -q 'SUMMARY cycles=18 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY $? "SUMMARY cycles=18 failed=0 residue=0 temp-backups=0 (run cheio)"

residue=$(find backend -type f -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0042-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0042-* = $backups"

nonascii=$(tr -d '\000-\177' < "$qa/qa-mutate-task-0042.ps1" | wc -c)
[ "$nonascii" = "0" ]; check DRIVER-ASCII $? "qa-mutate-task-0042.ps1 sem byte > 127 ($nonascii)"

grep -q 'VERIFY total=18 bad=0' "$qa/QA-0042-verify.log" 2>/dev/null; check VERIFY-RECORDED $? "QA-0042-verify.log com VERIFY total=18 bad=0"

fed=backend/tests/Squid.Tests/ServiceHost/FederationRuntimeKeyTests.cs
facts=$(grep -c '\[Fact\]' "$fed")
acs=$(grep -c '// AC-0' "$fed")
if [ "$facts" = "3" ] && [ "$acs" -ge 3 ]; then check FED-AC 0 "FederationRuntimeKeyTests: 3 [Fact] marcados AC-01..AC-03"
else check FED-AC 1 "FederationRuntimeKeyTests: facts=$facts acs=$acs"; fi

grep -q '// AC-04' backend/tests/Squid.Tests/Protocols/HttpSignatureTests.cs \
  && grep -q '// AC-05' backend/tests/Squid.Tests/Protocols/HttpSignatureTests.cs
check SIG-AC $? "HttpSignatureTests com marcadores // AC-04 e // AC-05"

grep -qF 'RuntimeSigningKeyRing.FromConfiguration(configuration["Squid:RecoveryKey"])' backend/src/Squid.Service/SquidServiceHost.cs \
  && grep -q 'federation output disabled' backend/src/Squid.Service/SquidServiceHost.cs
check WIRE-RING $? "host le Squid:RecoveryKey no boot e avisa saida federada desabilitada"

grep -q 'ActivateSigningKeyAsync' backend/src/Squid.Grains/Actors/ActorGrain.cs \
  && grep -q 'SigningKeyActivationException' backend/src/Squid.Grains/Actors/ActorGrain.cs
check WIRE-GRAIN $? "ActorGrain com ActivateSigningKeyAsync e SigningKeyActivationException auditavel"

grep -q 'RsaWrapInfo = "fed/rsa-wrap"' backend/src/Squid.Api/Crypto/KeyDerivation.cs
check WIRE-HKDF $? "KeyDerivation: HKDF info fed/rsa-wrap (regra TASK-0012, AC-02)"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

scope="$qa/QA-0042-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "5" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 5"
grep -q 'mtime-count=5 (esperado 5' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 5 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"
# Nothing NEW outside this QA's artifacts since the baseline: every remaining entry must be
# either the pre-existing ODEV docs WIP captured at the start or a QA-0042 artifact.
outside=$(git status --porcelain -uall | sed 's/^ *//' | while IFS= read -r e; do
  case "$e" in
    *docs/sdd/qa/*0042*) ;;
    *) grep -qF "$e" "$base" || echo "$e" ;;
  esac
done | wc -l)
[ "$outside" = "0" ]; check SCOPE-GIT-SCOPE $? "git status fora do WIP pre-existente e dos artefatos da QA = $outside entradas (esperado 0)"

doc="$qa/QA-0042-chave-de-assinatura-em-runtime.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0042-chave-de-assinatura-em-runtime.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

acmiss=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06; do
  grep -qF "| $n |" "$doc" 2>/dev/null || acmiss="$acmiss $n"
done
[ -z "$acmiss" ]; check QA-DOC-ACS $? "AC-01..AC-06 presentes na tabela do doc${acmiss:+ faltando:$acmiss}"

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
