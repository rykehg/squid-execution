#!/usr/bin/env bash
# QA-0031 evidence checker, Linux twin of qa-check-task-0031.ps1 (same eight checks).
# Usage: bash docs/sdd/qa/qa-check-task-0031.sh
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
out="$repo/docs/sdd/qa/QA-0031-check-sh.log"
runs="$repo/docs/sdd/runs/test-runs"
fails=0
lines=()

check() { # id ok detail
  if [ "$2" = "1" ]; then lines+=("$1 PASS $3"); else lines+=("$1 FAIL $3"); fails=$((fails+1)); fi
}

newest() { ls -1 "$runs" 2>/dev/null | grep -- "$1" | sort -r | head -n1; }

# C1 - final gate, classified inside the log (S-22).
gate_name="$(newest '-test-gate.log')"
if [ -n "$gate_name" ]; then
  gate_exit="$(sed -n 's/^exit: *//p' "$runs/$gate_name" | head -n1)"
  gate_sum="$(grep -o 'Com falha: *[0-9]*, *Aprovado: *[0-9]*' "$runs/$gate_name" | head -n1)"
  gate_failed="$(echo "$gate_sum" | sed -n 's/Com falha: *\([0-9]*\).*/\1/p')"
  gate_ok="$(echo "$gate_sum" | sed -n 's/.*Aprovado: *\([0-9]*\)/\1/p')"
else
  gate_exit=''; gate_failed=''; gate_ok=''; gate_sum='(ausente)'
fi
if [ "$gate_exit" = "0" ] && [ "${gate_failed:-9}" = "0" ] && [ "${gate_ok:-0}" -ge 426 ] 2>/dev/null; then
  check C1 1 "gate $gate_name exit=$gate_exit falha=$gate_failed aprovado=$gate_ok"
else
  check C1 0 "gate ${gate_name:-ausente} exit=$gate_exit falha=$gate_failed aprovado=$gate_ok"
fi

# C2 - Moderation filter over the restored sources (9 tests).
filter_name="$(newest '-test-filter.log')"
if [ -n "$filter_name" ]; then
  filter_exit="$(sed -n 's/^exit: *//p' "$runs/$filter_name" | head -n1)"
  filter_ok="$(grep -o 'Com falha: *[0-9]*, *Aprovado: *[0-9]*' "$runs/$filter_name" | head -n1 | sed -n 's/.*Aprovado: *\([0-9]*\)/\1/p')"
else
  filter_exit=''; filter_ok=''
fi
if [ "$filter_exit" = "0" ] && [ "${filter_ok:-0}" = "9" ]; then
  check C2 1 "filter $filter_name exit=$filter_exit aprovado=$filter_ok"
else
  check C2 0 "filter ${filter_name:-ausente} exit=$filter_exit aprovado=$filter_ok"
fi

# C3 - mutation summary.
summary="$(grep -h 'SUMMARY' "$repo/docs/sdd/qa/QA-0031-mutations.log" 2>/dev/null | tail -n1)"
if echo "${summary:-}" | grep -q 'cycles=5 failed=0 residue-QA-MUTATION=0 temp-backups=0'; then
  check C3 1 "${summary:-}"
else
  check C3 0 "${summary:-}"
fi

# C4 - QA-MUTATION residue in the product sources.
residue="$(grep -rl --include='*.cs' 'QA-MUTATION' "$repo/backend" 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')"
check C4 $([ "$residue" = "0" ] && echo 1 || echo 0) "residuo QA-MUTATION=$residue"

# C5 - git scope vs the pre-QA baseline.
removed="$(comm -23 <(sort "$repo/docs/sdd/qa/QA-0031-git-baseline.txt") <(git -C "$repo" status --porcelain | sort) | grep -c . || true)"
added="$(comm -13 <(sort "$repo/docs/sdd/qa/QA-0031-git-baseline.txt") <(git -C "$repo" status --porcelain | sort))"
outside="$(echo "$added" | grep -v -E 'QA-0031|qa-check-task-0031|qa-mutate-task-0031|QaTask0031Tests|test-runs' | grep -c . || true)"
check C5 $([ "$removed" = "0" ] && [ "$outside" = "0" ] && echo 1 || echo 0) "adicionadas=$(echo "$added" | grep -c . || true) removidas=$removed fora-do-escopo=$outside"
lines+=("  adicionadas: $(echo "$added" | tr '\n' '|')")

# C6 - control block intact.
task="$repo/docs/sdd/tasks/TASK-0031-denuncia-admin-suspensao.md"
if grep -q 'status: AWAITING_QA' "$task" && grep -q 'qaAttempts: 0/2' "$task" && grep -q -- '- AC-05:' "$task"; then
  check C6 1 'status=AWAITING_QA qaAttempts=0/2 AC-05 presente'
else
  check C6 0 'control block alterado'
fi

# C7 - verdict doc exists with a verdict line.
verdict="$(ls -1 "$repo"/docs/sdd/qa/QA-0031-*.md 2>/dev/null | grep -v git-baseline || true)"
verdict_ok=0
for f in $verdict; do grep -q -- '- verdict: \(PASSED\|FAILED\|PARTIAL\)' "$f" && verdict_ok=1; done
check C7 $verdict_ok "docs: $(echo "$verdict" | xargs -n1 basename 2>/dev/null | tr '\n' ' ')"

# C8 - no temp backup of this QA.
backups="$(ls -1 "${TMP:-/tmp}"/qa0031-* "${TEMP:-/nonexistent}"/qa0031-* 2>/dev/null | wc -l | tr -d ' ')"
check C8 $([ "$backups" = "0" ] && echo 1 || echo 0) "backups temporarios=$backups"

lines+=("SUMMARY total=8 fails=$fails")
printf '%s\n' "${lines[@]}" | tee "$out"
[ "$fails" -eq 0 ] || exit 1
exit 0
