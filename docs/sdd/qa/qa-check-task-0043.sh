#!/usr/bin/env bash
# QA-0043 evidence checker - Linux twin of qa-check-task-0043.ps1: the same static battery
# over the ledger, the log files, git and the QA doc, minus SDD-STATE (a Windows-only tool).
# CRLF-safe on purpose (lesson of QA-0042 run1): every file is read through tr -d '\r'
# before matching, because the driver and sdd-test write CRLF on Windows.
# It does NOT re-run the suite: dotnet is absent in this WSL (see the baseline).
# Usage: wsl -e bash docs/sdd/qa/qa-check-task-0043.sh
set -u

here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../../.." && pwd)"
runs="$repo/docs/sdd/runs/test-runs"
total=0
fails=0

check() {
  total=$((total + 1))
  if [ "$2" -eq 0 ]; then
    echo "CHECK $1 OK - $3"
  else
    fails=$((fails + 1))
    echo "CHECK $1 FAIL - $3"
  fi
}

slurp() { tr -d '\r' < "$1"; }

newest() { ls -1 "$runs"/$1 2>/dev/null | sort | tail -n 1; }

# 1. The git baseline captured before anything was written.
base="$(slurp "$here/QA-0043-git-baseline.txt")"
ok=1
printf '%s\n' "$base" | grep -Fq 'HEAD: a658bd16e5b40e000ce6efa1d60f64d6fc865ae0' && ok=0
check BASELINE-HEAD $ok 'baseline HEAD = a658bd1'
ok=1
printf '%s\n' "$base" | grep -Fq 'branch: task/TASK-0043-superficie-activitypub' && ok=0
check BASELINE-BRANCH $ok 'branch task/TASK-0043-superficie-activitypub'
ok=1
printf '%s\n' "$base" | grep -Eq 'wsl command -v dotnet => \[\] exit=1' && ok=0
check BASELINE-WSL $ok 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. The DEV commit must stay inside the touches of the task (AC-07).
files="$(printf '%s\n' "$base" | sed -n '/^--- commit file list ---$/,/^--- wsl/p' | sed '1d;$d' | sed '/^$/d')"
count="$(printf '%s\n' "$files" | sed '/^$/d' | wc -l | tr -d ' ')"
outside="$(printf '%s\n' "$files" | grep -Ev '^(backend/src/Squid.Api/Discovery/|backend/src/Squid.Api/Federation/|backend/src/Squid.Protocols/|backend/src/Squid.Service/|backend/tests/Squid.Tests/|docs/sdd/tasks/TASK-0043-)')"
ok=0
[ -n "$outside" ] && ok=1
[ "$count" -ne 11 ] && ok=1
check COMMIT-TOUCHES $ok "a658bd1: $count arquivos, todos nos touches (fora: ${outside:-nenhum})"

# 3. This QA's own baseline run of the eight scenarios before any write.
bf="$runs/20261113-194654-test-filter.log"
ok=1
if [ -f "$bf" ]; then
  bt="$(slurp "$bf")"
  if printf '%s\n' "$bt" | grep -Eq '^exit:[[:space:]]*0[[:space:]]*$' &&
     printf '%s\n' "$bt" | grep -Eq 'Com falha:[[:space:]]*0,[[:space:]]*Aprovado:[[:space:]]*8,[[:space:]]*Ignorado:[[:space:]]*0,[[:space:]]*Total:[[:space:]]*8'; then
    ok=0
  fi
fi
check BASELINE-FILTER $ok '20261113-194654-test-filter.log exit 0 / 8 de 8'

# 4. The can-fail driver self-check recorded before the run.
vt="$(slurp "$here/QA-0043-verify.log")"
hits="$(printf '%s\n' "$vt" | grep -c 'hits=1 ok=true')"
ok=1
printf '%s\n' "$vt" | grep -Fq 'VERIFY total=16 bad=0' &&
  [ "$hits" -eq 16 ] &&
  printf '%s\n' "$vt" | grep -Fq 'non-ascii-bytes=0 ok=true' && ok=0
check VERIFY-RUN $ok "QA-0043-verify.log total=16 bad=0, hits=16, ASCII OK (hits vistos: $hits)"

# 5. AC-07 gate, read from inside the newest gate log (S-22).
gate="$(newest '*-test-gate.log')"
gt=""
[ -n "$gate" ] && gt="$(slurp "$gate")"
ok=1
printf '%s\n' "$gt" | grep -Eq '^exit:[[:space:]]*0[[:space:]]*$' && ok=0
check GATE-EXIT $ok "${gate:-nenhum gate log} header exit 0"
ok=1
printf '%s\n' "$gt" | grep -Eq 'Com falha:[[:space:]]*0,[[:space:]]*Aprovado:[[:space:]]*512,[[:space:]]*Ignorado:[[:space:]]*0,[[:space:]]*Total:[[:space:]]*512' && ok=0
check GATE-SUMMARY $ok 'Com falha 0 / Aprovado 512 / Total 512'

# 6. The mutation ledger (S-22 red inside the log, S-16/S-23 restore).
mut="$(slurp "$here/QA-0043-mutations.log")"
ids="T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16"
missing=""
notred=""
catchfail=""
for id in $ids; do
  printf '%s\n' "$mut" | grep -qF "$id PRE " || missing="$missing $id"
  printf '%s\n' "$mut" | grep -Eq "^$id RUN wrapper=[0-9]+ logExit=1 aprovado=[0-9]+ falha=[1-9][0-9]* " || notred="$notred $id"
  expect="$(printf '%s\n' "$mut" | grep -E "^$id EXPECT-OK expect=" | head -n 1 | sed "s/^$id EXPECT-OK expect=//")"
  ac="$(printf '%s\n' "$mut" | grep -E "^$id RED-AC " | head -n 1 | sed "s/^$id RED-AC //")"
  if [ -z "$expect" ] || [ -z "$ac" ] || [ "$ac" = "NONE" ]; then
    catchfail="$catchfail $id"
  else
    case "$ac" in
      *"$expect"*) : ;;
      *) catchfail="$catchfail $id" ;;
    esac
  fi
done
ok=0; [ -n "$missing" ] && ok=1
check MUT-IDS $ok "T1..T16 todos com linha PRE (faltando:${missing:-nenhum})"
ok=0
headmatch="$(printf '%s\n' "$mut" | grep -cE '^T[0-9]+ PRE .* head-match=true find-hits=1')"
[ "$headmatch" -ne 16 ] && ok=1
check MUT-HEAD-MATCH $ok "16/16 PRE com hash = blob do commit a658bd1 (vistos: $headmatch)"
ok=0; [ -n "$notred" ] && ok=1
check MUT-RED $ok "T1..T16 todos vermelhos DENTRO do log (logExit 1, falha >= 1) (sem vermelho:${notred:-nenhum})"
ok=0; [ -n "$catchfail" ] && ok=1
check MUT-CATCH $ok "o RED-AC de cada ciclo contem a cena da AC alvo (falhou:${catchfail:-nenhum})"
ok=1
expectok="$(printf '%s\n' "$mut" | grep -cE '^T[0-9]+ EXPECT-OK ')"
if [ "$expectok" -eq 16 ] && ! printf '%s\n' "$mut" | grep -q 'EXPECT-MISS'; then ok=0; fi
check MUT-EXPECT $ok "16/16 EXPECT-OK e nenhum EXPECT-MISS (vistos: $expectok)"
ok=1
if ! printf '%s\n' "$mut" | grep -Eq 'TOOLING-FAIL|BUILD-FAIL'; then ok=0; fi
check MUT-TOOLING $ok 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'
ok=1
restoreok="$(printf '%s\n' "$mut" | grep -c 'RESTORE-OK hash=PRE residue=0')"
if [ "$restoreok" -ge 16 ] && ! printf '%s\n' "$mut" | grep -q 'RESTORE-FAIL'; then ok=0; fi
check MUT-RESTORE $ok "RESTORE-OK = $restoreok (>= 16) e nenhum RESTORE-FAIL"
ok=1
printf '%s\n' "$mut" | grep -Eq '^GREEN wrapper=0 logExit=0 aprovado=9 falha=0 log=' && ok=0
check MUT-GREEN $ok 'GREEN 9/9 sobre as fontes restauradas (wrapper=0 logExit=0 aprovado=9 falha=0)'
ok=1
printf '%s\n' "$mut" | grep -Eq 'SUMMARY cycles=16 failed=0 residue-backend=0' && ok=0
check MUT-SUMMARY $ok 'SUMMARY cycles=16 failed=0 residue-backend=0'

# 7. The gate must be NEWER than the GREEN of the mutation run (S-23 order).
greenlog="$(printf '%s\n' "$mut" | grep -E '^GREEN ' | sed 's/.* log=//' | head -n 1)"
ok=1
if [ -n "$gate" ] && [ -n "$greenlog" ] && [ -f "$runs/$greenlog" ] && [ "$gate" -nt "$runs/$greenlog" ]; then ok=0; fi
check GATE-AGE $ok "${gate:-?} criado depois do GREEN ${greenlog:-?}"

# 8. Residue: no mutant token left in the product sources (tracked worktree under backend).
ok=0
residue_hit=""
for token in 'folloving' '#rsa-key' 'rel = "profile"' '?page=9"'; do
  if git -C "$repo" grep -I -n -F -e "$token" -- backend >/dev/null 2>&1; then
    ok=1
    residue_hit="$residue_hit $token"
  fi
done
check RESIDUE $ok "tokens mutantes em backend = ${residue_hit:-nenhum}"
backups="$(ls -1 /tmp/qa0043-* 2>/dev/null | wc -l | tr -d ' ')"
ok=0; [ "$backups" -ne 0 ] && ok=1
check TEMP-BACKUPS $ok "backups qa0043-* em /tmp = $backups"
ok=1
be="$(git -C "$repo" status --porcelain -uall -- backend | sed '/^$/d' | wc -l | tr -d ' ')"
qaentry="$(git -C "$repo" status --porcelain -uall -- backend | sed '/^$/d' | head -n 1 | sed 's/^[[:space:]]*//')"
[ "$be" -eq 1 ] && [ "$qaentry" = '?? backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs' ] && ok=0
check GIT-BACKEND $ok "git status backend = $be entradas (esperado 1: so o teste novo da QA, dentro dos touches)"

# 9. The test file is the AC scenario set and the product still wires the behaviour.
tests="$repo/backend/tests/Squid.Tests/Federation/ActivityPubSurfaceTests.cs"
ok=0
acmissing=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06; do
  grep -Fq "// $n" "$tests" || acmissing="$acmissing $n"
done
facts="$(grep -c '\[Fact\]' "$tests")"
[ -n "$acmissing" ] && ok=1
[ "$facts" -ne 8 ] && ok=1
check TEST-AC $ok "ActivityPubSurfaceTests: $facts [Fact] marcados // AC-01..// AC-06 (faltando:${acmissing:-nenhum})"
qat="$repo/backend/tests/Squid.Tests/Federation/QaTask0043AcceptTests.cs"
ok=1
if [ -f "$qat" ] && grep -q '\[Fact\]' "$qat" && grep -q '// AC-06' "$qat" &&
  grep -q 'The_federation_routes_negotiate_the_accept_before_any_lookup' "$qat"; then ok=0; fi
check QA-TEST $ok 'QaTask0043AcceptTests: 1 [Fact] marcado // AC-06 (o cenario que T16 derruba)'
svc="$repo/backend/src/Squid.Service/SquidServiceHost.cs"
ok=1
grep -Fq 'app.MapSquidFederation(new FederationSeams(' "$svc" && grep -Fq 'InboxSignatureGate.VerifyAsync' "$svc" && ok=0
check WIRE-SEAMS $ok 'host costura MapSquidFederation com InboxSignatureGate.VerifyAsync'
disc="$repo/backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs"
ok=1
if ! grep -Fq 'rel = "profile"' "$disc" && grep -Fq 'rel = "self"' "$disc"; then ok=0; fi
check WIRE-WEBFINGER $ok 'webfinger publica so rel=self (sem rel=profile, P-08)'

# 10. Scope (AC-07), from the scope log this QA wrote.
sc=""
[ -f "$here/QA-0043-scope.log" ] && sc="$(slurp "$here/QA-0043-scope.log")"
blobmatch="$(printf '%s\n' "$sc" | grep -c 'match=True')"
ok=1; [ "$blobmatch" -eq 7 ] && ok=0
check SCOPE-BLOB $ok "blob-identity work=HEAD em 7/7 arquivos de produto mutados (vistos: $blobmatch)"
ok=1
printf '%s\n' "$sc" | grep -Eq 'mtime-count=8 \(esperado 8' && ok=0
check SCOPE-MTIME $ok 'mtime desde a baseline = os 7 arquivos mutados/restaurados + o teste novo da QA (8)'
ok=1
printf '%s\n' "$sc" | grep -Eq 'residue=0' && ok=0
check SCOPE-RESIDUE $ok 'scope scan: residuo mutante = 0'
ok=1
printf '%s\n' "$sc" | grep -Eq 'fora-dos-touches=0' && ok=0
check SCOPE-TOUCHES $ok 'scope scan: nenhum arquivo fora dos touches alterado'
ok=1
printf '%s\n' "$sc" | grep -Eq 'git-backend-entries=1' && ok=0
check SCOPE-GIT-SCOPE $ok 'git status backend = 1 entrada (so o teste novo da QA, dentro dos touches)'

# 11. The QA doc carries the verdict this checker verifies, with all seven ACs.
doc="$here/QA-0043-superficie-activitypub.md"
dt=""
[ -f "$doc" ] && dt="$(slurp "$doc")"
ok=1
printf '%s\n' "$dt" | grep -Eq '^- verdict: [*]*PASSED' && ok=0
check QA-DOC $ok 'QA-0043-superficie-activitypub.md com verdict PASSED'
docmissing=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06 AC-07; do
  printf '%s\n' "$dt" | grep -Fq "| $n |" || docmissing="$docmissing $n"
done
ok=0; [ -n "$docmissing" ] && ok=1
check QA-DOC-ACS $ok "AC-01..AC-07 presentes na tabela do doc (faltando:${docmissing:-nenhum})"

# 12. SDD-STATE is a Windows tool: it is checked by the ps1 twin only (recorded, not skipped
#     silently).
check SDD-STATE-SKIPPED 0 'sdd-state e ferramenta Windows - so no ps1 twin (ver QA-0043-check.log)'

echo "SUMMARY total=$total fails=$fails"
[ "$fails" -gt 0 ] && exit 1
exit 0
