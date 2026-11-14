#!/usr/bin/env bash
# QA-0044 evidence checker, Linux twin (static; it never re-runs the suite and never calls
# sdd-state, which is a Windows tool). Same twenty can-fail cycles, same log-file reads
# (S-22), same restore hashes (S-16), same scope rules as qa-check-task-0044.ps1.
# Trailing-CR stripped on every read: the logs are written on Windows and a bare \r makes
# every anchored pattern miss (the QA-0042 trap).
# Usage: wsl -e bash docs/sdd/qa/qa-check-task-0044.sh
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
base="$(readfile "$here/QA-0044-git-baseline.txt")"
echo "$base" | grep -q 'HEAD: 92db2c7413c3a1a4c92c63b09bbbffe0c7dbba1a'
check BASELINE-ADDR "$?" 'baseline HEAD = 92db2c7'
echo "$base" | grep -q 'branch: task/TASK-0044-inbox-verificacao-e-aplicacao'
check BASELINE-BRANCH "$?" 'branch task/TASK-0044-inbox-verificacao-e-aplicacao'
echo "$base" | grep -q 'wsl command -v dotnet => \[\] exit=1'
check BASELINE-WSL "$?" 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. The commit touches only the six glob sets of the task's touches.
in_list=0; outside=0; files=0
while IFS= read -r line; do
  case "$line" in
    '--- commit files ---') in_list=1; continue ;;
    '--- '*)
      if [ "$in_list" -eq 1 ]; then in_list=2; fi
      continue ;;
  esac
  if [ "$in_list" -eq 1 ] && [ -n "$(echo "$line" | tr -d '[:space:]')" ]; then
    files=$((files + 1))
    case "$line" in
      backend/src/Squid.Grains/Inbox/*|backend/src/Squid.Api/Federation/*|backend/src/Squid.Api/Posts/*|backend/src/Squid.Protocols/*|backend/src/Squid.Service/*|backend/tests/Squid.Tests/*) ;;
      *) outside=$((outside + 1)) ;;
    esac
  fi
done <<EOF
$base
EOF
if [ "$outside" -eq 0 ] && [ "$files" -eq 24 ]; then check COMMIT-TOUCHES 0 "92db2c7: 24 arquivos, todos nos touches"; else check COMMIT-TOUCHES 1 "fora=$outside files=$files (esperado 0/24)"; fi

# 3. Baseline filter (24/24 before any QA write) and the QA gap-test filter (26/26).
bt="$(readfile "$runs/20261113-225032-test-filter.log")"
echo "$bt" | grep -q '^exit: *0 *$' && echo "$bt" | grep -q 'Com falha: *0, *Aprovado: *24, *Ignorado: *0, *Total: *24'
check BASELINE-FILTER "$?" '20261113-225032-test-filter.log exit 0 / 24 de 24'
qt="$(readfile "$runs/20261113-225836-test-filter.log")"
echo "$qt" | grep -q '^exit: *0 *$' && echo "$qt" | grep -q 'Com falha: *0, *Aprovado: *26, *Ignorado: *0, *Total: *26'
check QA-TEST-FILTER "$?" '20261113-225836-test-filter.log exit 0 / 26 de 26'

# 4. The driver self-check.
vt="$(readfile "$here/QA-0044-verify.log")"
echo "$vt" | grep -q 'VERIFY total=20 bad=0'
check VERIFY-RUN "$?" 'QA-0044-verify.log VERIFY total=20 bad=0'
hits="$(echo "$vt" | grep -c 'hits=1 ok=true')"
[ "$hits" -eq 20 ] && echo "$vt" | grep -q 'non-ascii-bytes=0 ok=true'
check VERIFY-HITS "$?" "$hits/20 padroes hits=1 + ASCII do driver OK"

# 5. AC-08 gate: the exit header and summary INSIDE the newest gate log (S-22), and the
#    gate newer than the GREEN of the mutation run.
gate="$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)"
if [ -n "$gate" ]; then
  gt="$(readfile "$gate")"
  echo "$gt" | grep -q '^exit: *0 *$'
  check GATE-EXIT "$?" "$(basename "$gate") header exit 0"
  echo "$gt" | grep -q 'Com falha: *0, *Aprovado: *529, *Ignorado: *0, *Total: *529'
  check GATE-SUMMARY "$?" 'Com falha 0 / Aprovado 529 / Total 529 (527 do DEV + 2 da QA)'
else
  check GATE-EXIT 1 'nenhum gate log'
  check GATE-SUMMARY 1 'nenhum gate log'
fi

mut="$(readfile "$here/QA-0044-mutations.log")"
green_log="$(echo "$mut" | sed -n 's/^GREEN wrapper=0 logExit=0 aprovado=26 falha=0 log=\(.*\)$/\1/p' | tail -1)"
if [ -n "$gate" ] && [ -n "$green_log" ] && [ -f "$runs/$green_log" ]; then
  if [ "$gate" -nt "$runs/$green_log" ]; then check GATE-AGE 0 "$(basename "$gate") criado depois do GREEN $green_log"; else check GATE-AGE 1 "$(basename "$gate") nao e mais novo que $green_log"; fi
else
  check GATE-AGE 1 'GREEN ou gate log ausente'
fi

# 6. The mutation ledger: 20 cycles, each RED inside the log with its OWN AC scenario.
pre_count="$(echo "$mut" | grep -c '^T[0-9]* PRE .* head-match=true find-hits=1')"
[ "$pre_count" -eq 20 ]
check MUT-PRE "$?" "$pre_count/20 PRE com hash = blob do commit 92db2c7"

red_count=0
missing_red=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18 T19 T20; do
  if echo "$mut" | grep -q "^$id RUN wrapper=[0-9]* logExit=1 aprovado=[0-9]* falha=[1-9][0-9]* "; then
    red_count=$((red_count + 1))
  else
    missing_red="$missing_red $id"
  fi
done
[ "$red_count" -eq 20 ]
check MUT-RED "$?" "$red_count/20 vermelhos DENTRO do log (logExit 1, falha >= 1); sem:$missing_red"

expect_for() { # expect_for <id> -> the AC scenario that must be in RED-AC
  case "$1" in
    T1) echo 'A_remote_create_with_valid_headers_becomes_a_post_in_the_named_container' ;;
    T2) echo 'The_same_activity_id_delivered_twice_is_accepted_without_reprocessing' ;;
    T3|T4|T5|T6) echo 'The_contract_order_refuses_each_step_with_the_state_intact' ;;
    T7) echo 'Over_the_per_domain_quota_the_inbox_answers_429_with_retry_after' ;;
    T8) echo 'The_default_quota_is_sixty_per_domain_without_configuration' ;;
    T9|T10|T11) echo 'A_follow_creates_the_relation_answers_accept_and_undo_removes_it' ;;
    T12) echo 'A_remote_delete_tombstones_the_post_and_it_leaves_the_feed' ;;
    T13) echo 'The_same_actor_liking_twice_keeps_the_count_at_one' ;;
    T14) echo 'A_flag_files_one_denunciation_and_a_repeat_files_no_second' ;;
    T15) echo 'A_members_only_create_applies_for_a_member_and_not_for_a_stranger' ;;
    T16) echo 'A_private_create_is_never_applied' ;;
    T17) echo 'An_unknown_type_is_accepted_and_counted_by_the_metric' ;;
    T18) echo 'Remote_attachments_are_verified_by_sha256_and_a_failed_one_stays_absent' ;;
    T19) echo 'The_production_media_client_never_touches_a_blocked_address' ;;
    T20) echo 'A_create_without_any_named_destination_is_accepted_without_applying' ;;
  esac
}
catch_missing=""
for id in T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14 T15 T16 T17 T18 T19 T20; do
  want="$(expect_for "$id")"
  ac="$(echo "$mut" | sed -n "s/^$id RED-AC //p" | head -1)"
  case "$ac" in
    *"$want"*) ;;
    *) catch_missing="$catch_missing $id" ;;
  esac
done
[ -z "$catch_missing" ]
check MUT-CATCH "$?" "RED-AC contem o cenario proprio da AC em 20/20; sem:$catch_missing"

ok_count="$(echo "$mut" | grep -c '^T[0-9]* EXPECT-OK ')"
[ "$ok_count" -eq 20 ] && ! echo "$mut" | grep -q 'EXPECT-MISS'
check MUT-EXPECT "$?" "$ok_count/20 EXPECT-OK e nenhum EXPECT-MISS"
if echo "$mut" | grep -q 'TOOLING-FAIL\|BUILD-FAIL'; then
  check MUT-TOOLING 1 'TOOLING-FAIL ou BUILD-FAIL presentes no ledger'
else
  check MUT-TOOLING 0 'nenhum TOOLING-FAIL ou BUILD-FAIL (todo vermelho e falha de produto)'
fi

restore_count="$(echo "$mut" | grep -c 'RESTORE-OK hash=PRE residue=0')"
[ "$restore_count" -ge 20 ] && ! echo "$mut" | grep -q 'RESTORE-FAIL'
check MUT-RESTORE "$?" "RESTORE-OK = $restore_count (esperado >= 20) e nenhum RESTORE-FAIL"
echo "$mut" | grep -q '^GREEN wrapper=0 logExit=0 aprovado=26 falha=0 '
check MUT-GREEN "$?" 'GREEN 26/26 sobre as fontes restauradas'
echo "$mut" | grep -q 'SUMMARY cycles=20 failed=0 residue-backend=0'
check MUT-SUMMARY "$?" 'SUMMARY cycles=20 failed=0 residue-backend=0'
echo "$mut" | grep -q 'UNTRACKED backend=1'
check MUT-UNTRACKED "$?" 'UNTRACKED backend=1 (so o teste novo da QA)'

# 7. Residue of the mutant tokens in the product sources (bin/obj excluded).
residue=0
for token in 'if (false) references.AddRange' 'if (await _inbox.RememberAsync(Recipient(target), activity.Id!))' \
             'Status400BadRequest, "digest_invalid"' '6_291_456' 'Status406NotAcceptable, "payload_too_deep"' \
             '_ => AuthErrors.Write(context, StatusCodes.Status400BadRequest,' 'window.Count >= int.MaxValue' \
             'DefaultLimit = 600' '// QA-0044 T9' '// QA-0044 T11' '// QA-0044 T12' 'case "flag_disabled"' \
             'MembersOnly && await MemberRecipientAsync' '// QA-0044 T16' '// QA-0044 T17' \
             'qa0044-never-matches' 'IPAddress.TryParse(host, out var literal)) return true;' \
             'SearchAsync("Comunidade", 1, 0)'; do
  n="$(grep -rF "$token" backend/src backend/tests --include='*.cs' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l)"
  residue=$((residue + n))
done
[ "$residue" -eq 0 ]
check RESIDUE "$?" "tokens mutantes em backend (.cs, fora de bin/obj) = $residue"

bstatus="$(git status --porcelain -uall -- backend)"
bcount="$(echo "$bstatus" | grep -c .)"
[ "$bcount" -eq 1 ] && echo "$bstatus" | grep -qF '?? backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs'
check GIT-BACKEND "$?" "git status backend = $bcount entradas (esperado 1: so o teste novo da QA)"

# 8. The AC markers of the test files this run executed.
tests="$(readfile backend/tests/Squid.Tests/Federation/InboxReceiveTests.cs)"
facts="$(echo "$tests" | grep -c '\[Fact\]')"
ac_missing=""
for n in AC-01 AC-02 AC-03 AC-05 AC-06 AC-07 D-01; do
  echo "$tests" | grep -qF "// $n" || ac_missing="$ac_missing $n"
done
[ -z "$ac_missing" ] && [ "$facts" -eq 14 ]
check TEST-AC "$?" "InboxReceiveTests: $facts [Fact] marcados // AC-01..// AC-07 + // D-01; faltando:$ac_missing"
rl="$(readfile backend/tests/Squid.Tests/Federation/InboxRateLimitTests.cs)"
echo "$rl" | grep -q '// AC-04' && [ "$(echo "$rl" | grep -c '\[Fact\]')" -eq 1 ]
check TEST-AC04 "$?" 'InboxRateLimitTests: 1 [Fact] marcado // AC-04'
qtests="$(readfile backend/tests/Squid.Tests/Federation/QaTask0044InboxGapTests.cs)"
echo "$qtests" | grep -q '// AC-03' && echo "$qtests" | grep -q '// AC-04' && [ "$(echo "$qtests" | grep -c '\[Fact\]')" -eq 2 ]
check TEST-QA "$?" 'QaTask0044InboxGapTests: 2 [Fact] marcados // AC-03 e // AC-04'

# 9. Scope (AC-08): blob identity vs HEAD, mtime, residue, touches.
sc="$(readfile "$here/QA-0044-scope.log")"
blob_ok="$(echo "$sc" | grep -c 'match=True')"
[ "$blob_ok" -eq 7 ]
check SCOPE-BLOB "$?" "blob-identity work=HEAD em $blob_ok/7 arquivos de produto mutados"
echo "$sc" | grep -q 'mtime-count=8 (esperado 8'
check SCOPE-MTIME "$?" 'mtime desde a baseline = 7 mutados/restaurados + o teste novo da QA (8)'
echo "$sc" | grep -q 'residue=0'
check SCOPE-RESIDUE "$?" 'scope scan: residuo mutante = 0'
echo "$sc" | grep -q 'fora-dos-touches=0'
check SCOPE-TOUCHES "$?" 'scope scan: nenhum arquivo fora dos touches alterado'
echo "$sc" | grep -q 'git-backend-entries=1'
check SCOPE-GIT "$?" 'git status backend = 1 entrada (so o teste novo da QA)'

# 10. The QA doc with this verdict and the eight ACs.
dt="$(readfile "$here/QA-0044-inbox-verificacao-e-aplicacao.md")"
echo "$dt" | grep -q '^- verdict: \**PASSED'
check QA-DOC "$?" 'QA-0044-inbox-verificacao-e-aplicacao.md com verdict PASSED'
ac_doc_missing=""
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06 AC-07 AC-08; do
  echo "$dt" | grep -qF "| $n |" || ac_doc_missing="$ac_doc_missing $n"
done
[ -z "$ac_doc_missing" ]
check QA-DOC-ACS "$?" "AC-01..AC-08 presentes na tabela do doc; faltando:$ac_doc_missing"

echo "SUMMARY total=$total fails=$fails"
if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
