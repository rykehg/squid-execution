#!/usr/bin/env bash
# QA-0029 evidence checker - Linux twin of qa-check-task-0029.ps1 (same ten checks) plus the
# platform probe of docs/sdd/_tools/sdd-test.sh recorded into QA-0029-sh-twin.log.
# Usage (WSL/bash): bash docs/sdd/qa/qa-check-task-0029.sh
set -u

script_dir="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$script_dir/../../.." && pwd)"
cd "$repo" || exit 1
run_dir="$repo/docs/sdd/runs/test-runs"
qa_dir="$repo/docs/sdd/qa"
total=0
fails=0

check() { # id  exit-status(0 = ok)  detail
  total=$((total + 1))
  if [ "$2" = "0" ]; then
    echo "CHECK $1 PASS $3"
  else
    fails=$((fails + 1))
    echo "CHECK $1 FAIL $3"
  fi
}

# C1 - final gate of AC-05, read inside the newest gate log (S-22).
gate="$(ls -1 "$run_dir"/*-test-gate.log | sort | tail -1)"
gate_name="$(basename "$gate")"
grep -Eq '^exit:[[:space:]]+0' "$gate" && grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+411,[[:space:]]+Ignorado:[[:space:]]+0,[[:space:]]+Total:[[:space:]]+411' "$gate"
check C1 $? "gate final $gate_name: exit 0 e 411/411"

# C2 - QA baseline gate: 398/398 exit 0.
base="$run_dir/20261110-172026-test-gate.log"
grep -Eq '^exit:[[:space:]]+0' "$base" && grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+398' "$base"
check C2 $? 'gate baseline da QA: 398/398 exit 0'

# C3 - mutation driver summary.
mut="$qa_dir/QA-0029-mutations.log"
grep -q 'SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"
check C3 $? 'SUMMARY cycles=6 failed=0 residue=0 backups=0'

# C4 - one PRE, one red classified in the log, one restore per cycle, one green afterwards.
pre=$(grep -cE '^M[0-9] PRE ' "$mut")
red=$(grep -cE '^M[0-9] RED-IN-LOG ' "$mut")
res=$(grep -cE '^M[0-9] RESTORE-OK hash=PRE residue=0' "$mut")
grn=$(grep -cE '^GREEN wrapper=0 logExit=0 aprovado=63 falha=0' "$mut")
[ "$pre" = "6" ] && [ "$red" = "6" ] && [ "$res" = "6" ] && [ "$grn" = "1" ]
check C4 $? "6 PRE ($pre), 6 vermelhos ($red), 6 restores ($res), GREEN ($grn)"

# C5 - no QA-MUTATION marker left in any source file.
residue=$(find "$repo/backend" -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -print0 | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]
check C5 $? "residuos QA-MUTATION em backend = $residue"

# C6 - scope: only QA-0029 artifacts became git-visible, nothing was removed.
now_f="$(mktemp)"; old_f="$(mktemp)"; added_f="$(mktemp)"; removed_f="$(mktemp)"
git -C "$repo" status --porcelain -uall | tr -d '\357\273\277' | sed 's/^ *//' | sed 's/\r$//' | grep -v '^$' | sort > "$now_f"
tr -d '\357\273\277' < "$qa_dir/QA-0029-git-baseline.txt" | sed 's/^ *//' | sed 's/\r$//' | grep -v '^$' | sort > "$old_f"
comm -13 "$old_f" "$now_f" > "$added_f"
comm -23 "$old_f" "$now_f" > "$removed_f"
added_count=$(wc -l < "$added_f" | tr -d ' ')
removed_count=$(wc -l < "$removed_f" | tr -d ' ')
bad_count=$(grep -vE '^\?\? (backend/tests/Squid.Tests/Unfurl/QaTask0029Tests\.cs|docs/sdd/qa/(QA-0029|qa-[a-z]+-task-0029))' "$added_f" | grep -c . || true)
rm -f "$now_f" "$old_f" "$added_f" "$removed_f"
[ "$removed_count" = "0" ] && [ "$bad_count" = "0" ]
check C6 $? "git: +$added_count entradas todas QA-0029, removidas=$removed_count, fora do padrao=$bad_count"

# C7 - control block QA may not write.
task="$repo/docs/sdd/tasks/TASK-0029-unfurl-ssrf.md"
clean_task="$(tr -d '\r' < "$task")"
printf '%s\n' "$clean_task" | grep -Eq '^status: AWAITING_QA$' && printf '%s\n' "$clean_task" | grep -Eq '^qaAttempts: 0/2$'
check C7 $? 'control block intacto: status AWAITING_QA, qaAttempts 0/2'

# C8 - no test of this task can open a socket or resolve a name (AC-05).
net=$(grep -RE 'HttpClient\(|Dns\.Get|WebRequest|TcpClient|SocketsHttpHandler' "$repo/backend/tests/Squid.Tests/Unfurl" --include='*.cs' 2>/dev/null | wc -l)
[ "$net" = "0" ]
check C8 $? "sem rede real nos testes da task (achados=$net)"

# C9 - the green the mutation driver ran over the restored sources.
green_name=$(grep -E '^GREEN wrapper=0 logExit=0 aprovado=63 falha=0 ' "$mut" | sed -n 's/.*log=\([0-9a-zA-Z.-]*\.log\).*/\1/p' | tail -1)
green="$run_dir/$green_name"
[ -n "$green_name" ] && grep -Eq '^exit:[[:space:]]+0' "$green" && grep -Eq 'Com falha:[[:space:]]+0,[[:space:]]+Aprovado:[[:space:]]+63' "$green"
check C9 $? "GREEN pos-restores: ${green_name:-AUSENTE} exit 0 63/63"

# C10 - the verdict document exists.
doc=$(ls -1 "$qa_dir"/QA-0029-*.md 2>/dev/null | head -1)
[ -n "$doc" ] && grep -q '# QA-0029' "$doc"
check C10 $? "doc $(basename "${doc:-AUSENTE}")"

# Platform probe (Linux = the project's second target): record what the .sh twin really does.
twin="$qa_dir/QA-0029-sh-twin.log"
{
  echo "# QA-0029 sh twin (bash) - $(date '+%Y-%m-%d %H:%M:%S')"
  echo "== sdd-test.sh probe =="
  bash "$repo/docs/sdd/_tools/sdd-test.sh" probe 2>&1
  echo "probe exit: $?"
  echo "== sdd-test.sh test =="
  bash "$repo/docs/sdd/_tools/sdd-test.sh" test 2>&1
  echo "test exit: $?"
} > "$twin"
echo "PLATFORM linux twin gravado em $twin (suite Linux = unverified: sem dotnet nesta maquina)"

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
