#!/usr/bin/env bash
# QA-0046 evidence checker (sh twin of qa-check-task-0046.ps1): the same static verification
# from a POSIX shell - baseline, commit touches, baseline filter, driver self-check, both
# AC-05 probes, the mutation ledger (14 cycles RED with their own AC, restores, GREEN, summary),
# residue, git backend, both scope scans, static scenario markers and the QA doc verdict.
# No sdd-state here (Windows-only tool) - the ps1 twin runs it. ASCII-only.
# Usage: bash docs/sdd/qa/qa-check-task-0046.sh   (expected: exit 0, SUMMARY fails=0)
set -u

here="$(cd "$(dirname "$0")" && pwd)"
cd "$(cd "$here/../../.." && pwd)" || exit 1
runs='docs/sdd/runs/test-runs'
qa='docs/sdd/qa'

total=0
fails=0

# normalize CRLF so -E anchors written by Windows tools behave
norm() { tr -d '\r' <"$1" 2>/dev/null; }

check() { # name result(0=ok) detail
    total=$((total + 1))
    if [ "$2" -eq 0 ]; then
        printf 'CHECK %s OK - %s\n' "$1" "$3"
    else
        fails=$((fails + 1))
        printf 'CHECK %s FAIL - %s\n' "$1" "$3"
    fi
}

in_touches() {
    case "$1" in
        backend/src/Squid.Grains/Peers/* | backend/src/Squid.PeerClient/* | \
        backend/src/Squid.Service/* | backend/src/Squid.ReadModels/* | \
        backend/tests/Squid.Tests/*) return 0 ;;
        *) return 1 ;;
    esac
}

base="$qa/QA-0046-git-baseline.txt"

# 1. baseline
grep -q 'HEAD: 61c12cecc1e6b14e370b6a861122595a2e021348' "$base"
check BASELINE-HEAD $? 'baseline HEAD = 61c12ce'
grep -q 'branch: task/TASK-0046-peer-grain-fila-e-dlq' "$base"
check BASELINE-BRANCH $? 'branch task/TASK-0046-peer-grain-fila-e-dlq'
grep -q 'wsl dotnet: \[\] exit=1' "$base"
check BASELINE-WSL $? 'dotnet ausente no WSL registrado na baseline (suite Linux = unverified)'

# 2. commit files inside the touches
in_list=0
cfiles=0
cout=0
while IFS= read -r line; do
    if [ "$in_list" -eq 0 ]; then
        case "$line" in *'--- commit files'*) in_list=1 ;; esac
        continue
    fi
    case "$line" in '--- '*) break ;; esac
    [ -z "$line" ] && continue
    cfiles=$((cfiles + 1))
    if ! in_touches "$line"; then
        printf 'COMMIT-OUTSIDE %s\n' "$line"
        cout=$((cout + 1))
    fi
done <"$base"
if [ "$cout" -eq 0 ] && [ "$cfiles" -eq 18 ]; then
    check COMMIT-TOUCHES 0 '61c12ce: 18 arquivos, todos nos touches'
else
    check COMMIT-TOUCHES 1 "cfiles=$cfiles fora=$cout (esperado 18/0)"
fi

# 3. baseline filter before any QA write
norm "$runs/20261114-024950-test-filter.log" | grep -qE '^exit: +0$' &&
    norm "$runs/20261114-024950-test-filter.log" |
        grep -qE 'Com falha: +0, Aprovado: +51, Ignorado: +0, Total: +51'
check BASELINE-FILTER $? '20261114-024950-test-filter.log exit 0 / 51 de 51'

# 4. driver self-check
grep -q 'VERIFY total=14 bad=0' "$qa/QA-0046-verify.log"
check VERIFY-RUN $? 'QA-0046-verify.log VERIFY total=14 bad=0'
hits=$(grep -c 'hits=1 hits2=1 ok=true' "$qa/QA-0046-verify.log")
if [ "$hits" -eq 14 ] && grep -q 'non-ascii-bytes=0 ok=true' "$qa/QA-0046-verify.log"; then
    check VERIFY-HITS 0 '14/14 padroes hits=1 + ASCII do driver OK'
else
    check VERIFY-HITS 1 "hits=$hits (esperado 14)"
fi

# 5. AC-05 probes, both shells
grep -q 'SUMMARY checks=5 fails=0' "$qa/QA-0046-ac05-cli.log"
check AC05-CLI-SUMMARY $? 'QA-0046-ac05-cli.log SUMMARY checks=5 fails=0'
a5ok=$(grep -cE 'CHECK SCENARIO-(500|TIMEOUT|SLOW) OK' "$qa/QA-0046-ac05-cli.log")
grep -q 'curl-exit=28' "$qa/QA-0046-ac05-cli.log" &&
    grep -q 'http=500' "$qa/QA-0046-ac05-cli.log" &&
    grep -q 'esperado >= 1200' "$qa/QA-0046-ac05-cli.log"
a5detail=$?
if [ "$a5ok" -eq 3 ] && [ "$a5detail" -eq 0 ]; then
    check AC05-CLI-SCENARIOS 0 'os tres cenarios ao vivo: 500, tempo esgotado (28) e lentidao (>= 1200 ms)'
else
    check AC05-CLI-SCENARIOS 1 "cenarios-ok=$a5ok a5detail=$a5detail"
fi
grep -q 'SUMMARY checks=5 fails=0' "$qa/QA-0046-ac05-cli-sh.log"
check AC05-CLI-SH-SUMMARY $? 'QA-0046-ac05-cli-sh.log SUMMARY checks=5 fails=0 (gêmeo bash)'

# 6. gate (AC-07): newest gate log, header exit 0, 554 green
gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -n 1)
if [ -n "$gate" ]; then
    norm "$gate" | grep -qE '^exit: +0$'
    check GATE-EXIT $? "$(basename "$gate") header exit 0"
    norm "$gate" | grep -qE 'Com falha: +0, Aprovado: +554, Ignorado: +0, Total: +554'
    check GATE-SUMMARY $? 'Com falha 0 / Aprovado 554 / Total 554'
else
    check GATE-EXIT 1 'nenhum gate log'
    check GATE-SUMMARY 1 'nenhum gate log'
fi

mut="$qa/QA-0046-mutations.log"
# The summary lines of sdd-test are space-padded ('exit:    0') and the ledger is CRLF: run 1
# of this checker failed exactly on those two facts (QA-0046-check-sh-run1.log) - '+0' below
# and the tr -d '\r' on green_log are the fix.
green_log=$(grep -E '^GREEN wrapper=0 logExit=0 aprovado=51 falha=0 log=' "$mut" | head -n 1 | sed 's/.*log=//' | tr -d '\r')
if [ -n "$green_log" ] && [ -n "$gate" ] && [ -e "$runs/$green_log" ]; then
    if [ "$gate" -nt "$runs/$green_log" ]; then
        check GATE-AGE 0 "$(basename "$gate") criado depois do GREEN $green_log"
    else
        check GATE-AGE 1 'gate mais antigo que o GREEN'
    fi
else
    check GATE-AGE 1 "GREEN ou gate log ausente (green=$green_log)"
fi

# 7. mutation ledger: 14 cycles
ids='T1 T2 T3 T4 T5 T6 T7 T8 T9 T10 T11 T12 T13 T14'
missing=''
for id in $ids; do
    grep -q "^$id PRE " "$mut" || missing="$missing $id"
done
if [ -z "$missing" ]; then check MUT-IDS 0 'T1..T14 (14 ciclos) todos com linha PRE'
else check MUT-IDS 1 "faltando:$missing"; fi

pre_ok=$(grep -cE '^T[0-9]+ PRE .* head-match=true find-hits=1' "$mut")
if [ "$pre_ok" -eq 14 ]; then check MUT-HEAD-MATCH 0 '14/14 PRE com hash = blob do commit 61c12ce (head-match=true)'
else check MUT-HEAD-MATCH 1 "head-match=$pre_ok (esperado 14)"; fi

notred=''
for id in $ids; do
    grep -qE "^$id RUN wrapper=[0-9]+ logExit=1 aprovado=[0-9]+ falha=[1-9]" "$mut" || notred="$notred $id"
done
if [ -z "$notred" ]; then check MUT-RED 0 'T1..T14 todos vermelhos DENTRO do log (logExit: 1, falha >= 1)'
else check MUT-RED 1 "sem vermelho:$notred"; fi

catch_miss=''
declare -A expect_of=(
    [T1]='Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    [T2]='Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    [T3]='Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert'
    [T4]='Ac02_after_eight_attempts_the_delivery_hits_the_dlq_with_motive_and_alert'
    [T5]='Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt'
    [T6]='Ac02_a_permanent_404_dead_letters_the_delivery_on_the_first_attempt'
    [T7]='Ac03_an_open_circuit_pushes_a_new_delivery_straight_to_pending'
    [T8]='Backoff_doubles_with_jitter_and_hits_the_six_hour_ceiling_exactly'
    [T9]='Ac04_a_blocked_peer_receives_nothing'
    [T10]='Ac04_the_per_pair_rate_limit_decides_who_leaves'
    [T11]='Ac01_a_500_goes_back_to_pending_with_attempts_and_a_growing_backoff'
    [T12]='Ac05_the_timeout_scenario_fails_the_send_and_keeps_the_row_scheduled'
    [T13]='Ac05_the_slow_scenario_loses_to_the_send_budget'
    [T14]='Ac06_every_send_carries_the_peer_domain_trace_attribute_without_pii'
)
for id in $ids; do
    grep "^$id RED-AC " "$mut" | grep -qF "${expect_of[$id]}" || catch_miss="$catch_miss $id"
done
if [ -z "$catch_miss" ]; then check MUT-CATCH 0 'T1..T14: a cena da propria AC esta no RED-AC de cada ciclo'
else check MUT-CATCH 1 "sem a cena da AC:$catch_miss"; fi

expect_ok=$(grep -c 'EXPECT-OK' "$mut")
if [ "$expect_ok" -eq 14 ] && ! grep -q 'EXPECT-MISS' "$mut"; then
    check MUT-EXPECT 0 '14/14 EXPECT-OK e nenhum EXPECT-MISS'
else
    check MUT-EXPECT 1 "EXPECT-OK=$expect_ok tem	EXPECT-MISS=$(grep -c EXPECT-MISS "$mut")"
fi
if ! grep -qE 'TOOLING-FAIL|BUILD-FAIL' "$mut"; then check MUT-TOOLING 0 'nenhum TOOLING-FAIL ou BUILD-FAIL'
else check MUT-TOOLING 1 'há TOOLING-FAIL ou BUILD-FAIL'; fi

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
if [ "$restore_ok" -ge 14 ] && ! grep -q 'RESTORE-FAIL' "$mut"; then
    check MUT-RESTORE 0 "RESTORE-OK = $restore_ok, nenhum RESTORE-FAIL"
else
    check MUT-RESTORE 1 "RESTORE-OK=$restore_ok tem RESTORE-FAIL=$(grep -c RESTORE-FAIL "$mut")"
fi
if [ -n "$green_log" ]; then check MUT-GREEN 0 "GREEN 51/51 sobre as fontes restauradas (log=$green_log)"
else check MUT-GREEN 1 'sem linha GREEN'; fi
grep -q 'SUMMARY cycles=14 failed=0 residue-backend=0' "$mut"
check MUT-SUMMARY $? 'SUMMARY cycles=14 failed=0 residue-backend=0'
grep -q 'UNTRACKED backend=0' "$mut"
check MUT-UNTRACKED $? 'UNTRACKED backend=0 (a QA nao acrescentou arquivos em backend)'

# 8. residue + git backend
residue=$(grep -rn 'QA-0046 T' backend --include='*.cs' 2>/dev/null | grep -Ev '/(bin|obj)/' | grep -c . || true)
if [ "$residue" -eq 0 ]; then check RESIDUE 0 'tokens mutantes em backend (.cs, fora de bin/obj) = 0'
else check RESIDUE 1 "residue=$residue"; fi
be=$(git status --porcelain -uall -- backend | grep -c . || true)
if [ "$be" -eq 0 ]; then check GIT-BACKEND 0 'git status backend = 0 entradas'
else check GIT-BACKEND 1 "git-backend-entries=$be"; fi

# 9. both scope scans
sc="$qa/QA-0046-scope.log"
grep -q 'blob-identity=4 of 4' "$sc" && grep -q 'mtime-count=4 (esperado 4)' "$sc" &&
    grep -q 'residue=0' "$sc" && grep -q 'fora-dos-touches=0' "$sc" &&
    grep -q 'git-backend-entries=0' "$sc"
check SCOPE $? 'scope ps1: blob 4/4, mtime 4, residuo 0, fora-dos-touches 0'
scs="$qa/QA-0046-scope-sh.log"
grep -q 'fora-dos-touches=0' "$scs" && grep -q 'blob-identity=4 of 4' "$scs" &&
    grep -q 'residue=0' "$scs" && grep -q 'git-backend-entries=0' "$scs"
check SCOPE-SH $? 'gêmeo bash do scope: fora-dos-touches=0, blob 4/4, residuo 0'

# 10. static scenario markers of the runs
motor='backend/tests/Squid.Tests/Federation/PeerDeliveryMotorTests.cs'
facts=$(grep -c '\[Fact\]' "$motor")
acmiss=''
for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06; do
    grep -q "// $n" "$motor" || acmiss="$acmiss $n"
done
if [ -z "$acmiss" ] && [ "$facts" -eq 11 ]; then
    check TEST-MOTOR 0 'PeerDeliveryMotorTests: 11 [Fact] marcados // AC-01..// AC-06'
else
    check TEST-MOTOR 1 "facts=$facts acmiss:$acmiss"
fi
gfacts=$(grep -c '\[Fact\]' 'backend/tests/Squid.Tests/Federation/PeerGrainTests.cs')
if [ "$gfacts" -eq 4 ]; then check TEST-GRAIN 0 'PeerGrainTests: 4 [Fact] (backoff/circuito/bloqueio/cota)'
else check TEST-GRAIN 1 "facts=$gfacts (esperado 4)"; fi

grep -q 'MaxAttempts = 8' backend/src/Squid.Grains/Peers/PeerGrain.cs &&
    grep -q 'BackoffCap = TimeSpan.FromHours(6)' backend/src/Squid.Grains/Peers/PeerGrain.cs
check SPEC-CONSTANTS $? 'constantes do produto: N=8 e teto 6 h (data-model 3.4)'
grep -q 'RateLimitPerMinute' backend/src/Squid.Grains/Peers/PeerState.cs
check SPEC-RATELIMIT $? 'o par carrega RateLimitPerMinute (data-model 2.9)'
grep -q 'PeerScenario.Timeout' backend/src/Squid.PeerClient/PeerScenarioServer.cs &&
    grep -q 'PeerScenario.Slow' backend/src/Squid.PeerClient/PeerScenarioServer.cs &&
    grep -q '500 Internal Server Error' backend/src/Squid.PeerClient/PeerScenarioServer.cs &&
    grep -q 'case "timeout"' backend/src/Squid.PeerClient/Program.cs &&
    grep -q 'case "slow"' backend/src/Squid.PeerClient/Program.cs
check PEERCLIENT-SCENARIOS $? 'PeerClient expoe 500/timeout/slow (enum + CLI)'

# 11. QA doc verdict and the seven AC rows
doc="$qa/QA-0046-peer-grain-fila-e-dlq.md"
if [ -f "$doc" ]; then
    grep -E '^- verdict: \**PASSED' "$doc" >/dev/null
    check QA-DOC $? 'QA-0046-peer-grain-fila-e-dlq.md com verdict PASSED'
    docmiss=''
    for n in AC-01 AC-02 AC-03 AC-04 AC-05 AC-06 AC-07; do
        grep -qF "| $n |" "$doc" || docmiss="$docmiss $n"
    done
    if [ -z "$docmiss" ]; then check QA-DOC-ACS 0 'AC-01..AC-07 presentes na tabela do doc'
    else check QA-DOC-ACS 1 "faltando:$docmiss"; fi
else
    check QA-DOC 1 'doc da QA ausente'
    check QA-DOC-ACS 1 'doc da QA ausente'
fi

printf 'SUMMARY total=%s fails=%s\n' "$total" "$fails"
if [ "$fails" -gt 0 ]; then exit 1; fi
exit 0
