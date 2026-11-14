#!/usr/bin/env bash
# QA-0045 evidence checker, Linux twin (static; it never re-runs the suite and never calls
# sdd-state, which is a Windows tool). Same ten can-fail cycles, same log-file reads (S-22),
# same restore hashes (S-16), same scope rules as qa-check-task-0045.ps1.
# Trailing-CR stripped on every read: the logs are written on Windows and a bare \r makes
# every anchored pattern miss (the QA-0042 trap).
# Usage: wsl -e bash docs/sdd/qa/qa-check-task-0045.sh
set -u
cd "$(dirname "$0")/../../.." || exit 1

here="docs/sdd/qa"
runs="docs/sdd/runs/test-runs"
total=0
fails=0

check() { # check <name> <0|1> <detail>
  total=$((total + 1))
  if [ "$2" -eq 0 ]; then
    echo "CHECK $1 OK - $3"
  else
    fails=$((fails + 1))
    echo "CHECK $1 FAIL - $3"
  fi
}

readfile() { tr -d '\r' < "$1" 2>/dev/null || true; }

# 1. The git baseline captured before any QA write.
base="$(readfile "$here/QA-0045-git-baseline.txt")"
echo "$base" | grep -q 'HEAD: 3226429461b5790ece123cdae6c350b6bb2df3c3'
check BASELINE-HEAD "$?" 'baseline HEAD = 3226429'
echo "$base" | grep -q 'branch: task/TASK-0045-saida-fan-out-e-follow'
check BASELINE-BRANCH "$?" 'branch task/TASK-0045-saida-fan-out-e-follow'
echo "$base" | grep -q 'wsl command -v dotnet => \[\] exit=1'
check BASELINE-WSL "$?" 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. The DEV commit touches only the eight glob sets of the task's touches.
in_list=0; outside=0; files=0
while IFS= read -r line; do
  case "$line" in
    '--- commit files'*) in_list=1; continue ;;
    '--- '*)
      if [ "$in_list" -eq 1 ]; then in_list=2; fi
      continue ;;
  esac
  case "$line" in
    commit\ *) continue ;;
  esac
  if [ "$in_list" -eq 1 ] && [ -n "$(echo "$line" | tr -d '[:space:]')" ]; then
    files=$((files + 1))
    case "$line" in
      backend/src/Squid.Persistence/schema/*|backend/src/Squid.Api/Posts/*|backend/src/Squid.Api/Federation/*|backend/src/Squid.Api/Containers/*|backend/src/Squid.ReadModels/*|backend/src/Squid.Protocols/*|backend/src/Squid.Service/*|backend/tests/Squid.Tests/*) ;;
      *) outside=$((outside + 1)) ;;
    esac
  fi
done <<EOF
$base
EOF
if [ "$outside" -eq 0 ] && [ "$files" -eq 16 ]; then check COMMIT-TOUCHES 0 "3226429: 16 arquivos, todos nos touches"; else check COMMIT-TOUCHES 1 "fora=$outside files=$files (esperado 0/16)"; fi

# 3. Baseline filter (35/35 before any QA write) and the QA gap-test filter (36/36).
bt="$(readfile "$runs/20261114-010209-test-filter.log")"
echo "$bt" | grep -q '^exit: *0 *$' && echo "$bt" | grep -q 'Com falha: *0, *Aprovado: *35, *Ignorado: *0, *Total: *35'
check BASELINE-FILTER "$?" '20261114-010209-test-filter.log exit 0 / 35 de 35'
qt="$(readfile "$runs/20261114-010649-test-filter.log")"
echo "$qt" | grep -q '^exit: *0 *$' && echo "$qt" | grep -q 'Com falha: *0, *Aprovado: *36, *Ignorado: *0, *Total: *36'
check QA-TEST-FILTER "$?" '20261114-010649-test-filter.log exit 0 / 36 de 36'

# 4. The driver self-check.
vt="$(readfile "$here/QA-0045-verify.log")"
echo "$vt" | grep -q 'VERIFY total=10 bad=0'
check VERIFY-RUN "$?" 'QA-0045-verify.log VERIFY total=10 bad=0'
hits="$(echo "$vt" | grep -c 'hits=1 hits2=1 ok=true')"
[ "$hits" -eq 10 ] && echo "$vt" | grep -q 'non-ascii-bytes=0 ok=true'
check VERIFY-HITS "$?" "$hits/10 padroes hits=1 + ASCII do driver OK"

# 5. AC-07 gate: the exit header and summary INSIDE the newest gate log (S-22), and the
#    gate newer than the GREEN of the mutation run.
gate="$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)"
if [ -n "$gate" ]; then
  gt="$(readfile "$gate")"
  echo "$gt" | grep -q '^exit: *0 *$'
  check GATE-EXIT "$?" "$(basename "$gate") header exit 0"
  echo "$gt" | grep -q 'Com falha: *0, *Aprovado: *539, *Ignorado: *0, *Total: *539'
  check GATE-SUMMARY "$?" 'Com falha 0 / Aprovado 539 / Total 539 (538 do DEV + 1 da QA)'
else
  check GATE-EXIT 1 'nenhum gate log'
  check GATE-SUMMARY 1 'nenhum gate log'
fi

mut="$(readfile "$here/QA-0045-mutations.log")"
green_log="$(echo "$mut" | sed -n 's/^GREEN wrapper=0 logExit=0 aprovado=36 falha=0 log=\(.*\)$/\1/p' | tail -1)"
if [ -n "$gate" ] && [ -n "$green_log" ] && [ -f "$runs/$green_log" ]; then
  if [ "$gate" -nt "$runs/$green_log" ]; then check GATE-AGE 0 "$(basename "$gate") criado depois do GREEN $green_log"; else check GATE-AGE 1 "$(basename "$gate") nao e mais novo que $green_log"; fi
else
  check GATE-AGE 1 'GREEN ou gate log ausente'
fi

# 6. The mutation ledger: 10 cycles, each RED inside the log with its OWN AC scenario.
pre_count="$(echo "$mut" | grep -c '^T[0-9]* PRE .* head-match=true find-hits=1')"
[ "$pre_count" -eq 10 ]
check MUT-PRE "$?" "$pre_count/10 PRE com hash = blob do commit 3226429"

red_count=0
missing_red=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10; do
  if echo "$mut" | grep -q "^$id RUN wrapper=[0-9]* logExit=1 aprovado=[0-9]* falha=[1-9][0-9]* "; then
    red_count=$((red_count + 1))
  else
    missing_red="$missing_red $id"
  fi
done
[ "$red_count" -eq 10 ]
check MUT-RED "$?" "$red_count/10 vermelhos DENTRO do log (logExit 1, falha >= 1); sem:$missing_red"

expect_for() { # expect_for <id> -> the AC scenario that must be in RED-AC
  case "$1" in
    T1) echo 'Ac01_public_post_enqueues_one_delivery_per_remote_follower_and_names_the_destination' ;;
    T2) echo 'Ac01_public_post_also_enqueues_for_the_container_collection' ;;
    T3) echo 'Ac02_unlisted_delivers_only_to_followers' ;;
    T4) echo 'Ac02_members_only_delivers_only_to_active_members' ;;
    T5) echo 'Ac02_private_creates_no_delivery_at_all' ;;
    T6|T7) echo 'Ac03_follow_route_enqueues_a_signed_follow_and_the_relation_settles_on_accept' ;;
    T8) echo 'Ac04_policy_change_before_send_cancels_with_a_reason_and_sends_nothing' ;;
    T9) echo 'Ac05_publish_returns_with_every_delivery_already_pending_and_the_wire_untouched' ;;
    T10) echo 'Ac06_removing_the_post_propagates_a_signed_delete_to_the_recipients_that_received_the_create' ;;
  esac
}
catch_missing=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10; do
  want="$(expect_for "$id")"
  ac="$(echo "$mut" | sed -n "s/^$id RED-AC //p" | head -1)"
  case "$ac" in
    *"$want"*) ;;
    *) catch_missing="$catch_missing $id" ;;
  esac
done
[ -z "$catch_missing" ]
check MUT-CATCH "$?" "RED-AC contem o cenario proprio da AC em 10/10; sem:$catch_missing"

ok_count="$(echo "$mut" | grep -c '^T[0-9]* EXPECT-OK ')"
[ "$ok_count" -eq 10 ] && ! echo "$mut" | grep -q 'EXPECT-MISS'
check MUT-EXPECT "$?" "$ok_count/10 EXPECT-OK e nenhum EXPECT-MISS"
if echo "$mut" | grep -q 'TOOLING-FAIL\|BUILD-FAIL'; then
  check MUT-TOOLING 1 'TOOLING-FAIL ou BUILD-FAIL presentes no ledger'
else
  check MUT-TOOLING 0 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'
fi

restore_count="$(echo "$mut" | grep -c 'RESTORE-OK hash=PRE residue=0')"
[ "$restore_count" -ge 10 ] && ! echo "$mut" | grep -q 'RESTORE-FAIL'
check MUT-RESTORE "$?" "RESTORE-OK = $restore_count (esperado >= 10) e nenhum RESTORE-FAIL"
echo "$mut" | grep -q '^GREEN wrapper=0 logExit=0 aprovado=36 falha=0 '
check MUT-GREEN "$?" 'GREEN 36/36 sobre as fontes restauradas'
echo "$mut" | grep -q 'SUMMARY cycles=10 failed=0 residue-backend=0'
check MUT-SUMMARY "$?" 'SUMMARY cycles=10 failed=0 residue-backend=0'
echo "$mut" | grep -q 'UNTRACKED backend=1'
check MUT-UNTRACKED "$?" 'UNTRACKED backend=1 (so o teste novo da QA)'

# 7. Residue of the mutant tokens in the product sources (bin/obj excluded).
residue=0
for token in 'qa0045-broken' '// QA-0045 T2' '// QA-0045 T3' '// QA-0045 T4' '// QA-0045 T5' \
             '// QA-0045 T6' '// QA-0045 T7' '// QA-0045 T8' '// QA-0045 T9' '// QA-0045 T10' \
             '_ => followers,' '.Where(_ => true)' 'VisibilityPolicy.Unlisted => [.. followers,' \
             'VisibilityPolicy.Public => followers,'; do
  n="$(grep -rF "$token" backend/src backend/tests --include='*.cs' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l)"
  residue=$((residue + n))
done
[ "$residue" -eq 0 ]
check RESIDUE "$?" "tokens mutantes em backend (.cs, fora de bin/obj) = $residue"

bstatus="$(git status --porcelain -uall -- backend)"
bcount="$(echo "$bstatus" | grep -c .)"
[ "$bcount" -eq 1 ] && echo "$bstatus" | grep -qF '?? backend/tests/Squid.Tests/Federation/QaTask0045FanOutGapTests.cs'
check GIT-BACKEND "$?" "git status backend = $bcount entradas (esperado 1: so o teste novo da QA)"

# 8. The AC markers of the test files this run executed.
dev="$(readfile backend/tests/Squid.Tests/Federation/OutboundFanOutTests.cs)"
facts="$(echo "$dev" | grep -c '\[Fact\]')"
ac_missing=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06; do
  echo "$dev" | grep -qF "// $n" || ac_missing="$ac_missing $n"
done
[ -z "$ac_missing" ] && [ "$facts" -eq 8 ]
check TEST-AC "$?" "OutboundFanOutTests: $facts [Fact] marcados // AC-01..// AC-06; faltando:$ac_missing"
mig="$(readfile backend/tests/Squid.Tests/Federation/FederationMigrationTests.cs)"
[ "$(echo "$mig" | grep -c '\[Fact\]')" -eq 1 ] && echo "$mig" | grep -q '0008'
check TEST-MIGRATION "$?" 'FederationMigrationTests: 1 [Fact] da migration 0008'
qtests="$(readfile backend/tests/Squid.Tests/Federation/QaTask0045FanOutGapTests.cs)"
echo "$qtests" | grep -q '// AC-01' && [ "$(echo "$qtests" | grep -c '\[Fact\]')" -eq 1 ]
check TEST-QA "$?" 'QaTask0045FanOutGapTests: 1 [Fact] marcado // AC-01'
objsrc="$(readfile backend/src/Squid.Protocols/ActivityPub/ObjectJsonLd.cs)"
svcsrc="$(readfile backend/src/Squid.Service/OutboundPipeline.cs)"
echo "$objsrc" | grep -q 'PropertyName("audience")'
check WIRE-AUDIENCE "$?" 'o Create de saida carrega audience (GAP-48 / D-01) no produto'
echo "$svcsrc" | grep -q 'FederationMetrics.DeliveryCancelled()' && echo "$svcsrc" | grep -q 'PeerDeliveryState.Cancelled'
check WIRE-R05 "$?" 'a reavaliacao R-05 cancela com motivo e conta a metrica no produto'

# 9. Scope (AC-07): blob identity vs HEAD, mtime, residue, touches.
sc="$(readfile "$here/QA-0045-scope.log")"
blob_ok="$(echo "$sc" | grep -c 'match=True')"
[ "$blob_ok" -eq 3 ]
check SCOPE-BLOB "$?" "blob-identity work=HEAD em $blob_ok/3 arquivos de produto mutados"
echo "$sc" | grep -q 'mtime-count=4 (esperado 4'
check SCOPE-MTIME "$?" 'mtime desde a baseline = 3 mutados/restaurados + o teste novo da QA (4)'
echo "$sc" | grep -q 'residue=0'
check SCOPE-RESIDUE "$?" 'scope scan: residuo mutante = 0'
echo "$sc" | grep -q 'fora-dos-touches=0'
check SCOPE-TOUCHES "$?" 'scope scan: nenhum arquivo fora dos touches alterado'
echo "$sc" | grep -q 'git-backend-entries=1'
check SCOPE-GIT "$?" 'git status backend = 1 entrada (so o teste novo da QA)'

# 10. The QA doc with this verdict and the seven ACs.
dt="$(readfile "$here/QA-0045-saida-fan-out-e-follow.md")"
echo "$dt" | grep -q '^- verdict: \**PASSED'
check QA-DOC "$?" 'QA-0045-saida-fan-out-e-follow.md com verdict PASSED'
ac_doc_missing=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06 AC-07; do
  echo "$dt" | grep -qF "| $n |" || ac_doc_missing="$ac_doc_missing $n"
done
[ -z "$ac_doc_missing" ]
check QA-DOC-ACS "$?" "AC-01..AC-07 presentes na tabela do doc; faltando:$ac_doc_missing"

echo "SUMMARY total=$total fails=$fails"
if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
