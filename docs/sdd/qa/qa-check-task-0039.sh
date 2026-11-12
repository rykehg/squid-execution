#!/usr/bin/env bash
# QA-0039 evidence checker - twin of qa-check-task-0039.ps1 (same static checks under bash).
# S-22: every exit and summary is read from the LOG FILES, never from a wrapper's console code.
# The twin covers the log/hash/git/doc checks; sdd-state's Windows tool stays ps1-only there
# (and the test suite itself is Windows-only on this machine: dotnet is missing in WSL).
# Run from anywhere: bash docs/sdd/qa/qa-check-task-0039.sh (cd trois niveaux: qa -> sdd -> docs -> racine)
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

if grep -q 'HEAD: 3d8a1d99d23b96a833384c7781bac3cb8b89be56' "$qa/QA-0039-git-baseline.txt" \
   && grep -q 'branch: task/TASK-0039-anexo-em-mensagem' "$qa/QA-0039-git-baseline.txt"; then
  check BASELINE 0 "HEAD 3d8a1d9 na branch task/TASK-0039-anexo-em-mensagem"
else
  check BASELINE 1 "git baseline ausente ou diferente"
fi

gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate" && grep -qE 'Com falha: +0, Aprovado: +473, Ignorado: +0, Total: +473' "$gate"; then
  check GATE 0 "$(basename "$gate") header exit 0 e 473/473"
else
  check GATE 1 "gate: ${gate:-ausente} sem exit 0 ou 473/473"
fi

if grep -qE '^exit: +0$' "$runs/20261112-093821-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +41' "$runs/20261112-093821-test-filter.log"; then
  check BASELINE-THREADS 0 "093821 exit 0 / 41 de 41 (Threads baseline da QA)"
else
  check BASELINE-THREADS 1 "baseline Threads 093821 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-093902-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +27' "$runs/20261112-093902-test-filter.log"; then
  check BASELINE-MEDIA 0 "093902 exit 0 / 27 de 27 (Media baseline da QA)"
else
  check BASELINE-MEDIA 1 "baseline Media 093902 ausente ou vermelha"
fi

if grep -qE '^exit: +0$' "$runs/20261112-094107-test-filter.log" 2>/dev/null \
   && grep -qE 'Com falha: +0, Aprovado: +42' "$runs/20261112-094107-test-filter.log"; then
  check QA-TEMP-FILTER 0 "094107 exit 0 / 42 de 42 (com o teste temporario da QA)"
else
  check QA-TEMP-FILTER 1 "verde com teste temporario 094107 ausente ou vermelho"
fi

mut="$qa/QA-0039-mutations.log"
missing=""
not_red=""
for id in M1 M2 M3 M4 M5 M6 M7 M8 M9 M10 M11 M12 M13; do
  grep -q "$id PRE " "$mut" || missing="$missing $id"
done
[ -z "$missing" ]; check MUT-IDS $? "M1..M13 todos com linha PRE${missing:+ faltando:$missing}"

for id in M1 M3 M4 M5 M6 M7 M8 M9 M10 M11 M12 M13; do
  grep -qE "^$id RUN wrapper=[0-9]+ logExit=[0-9]+ aprovado=[0-9]+ falha=[1-9][0-9]*" "$mut" || not_red="$not_red $id"
done
[ -z "$not_red" ]; check MUT-RED $? "M1, M3..M13 todos com RUN vermelho no log${not_red:+ sem-vermelho:$not_red}"

m2_last=$(grep -E '^M2 RUN ' "$mut" | tail -1 | sed -E 's/.*falha=([0-9]+).*/\1/')
[ -n "$m2_last" ] && [ "$m2_last" -ge 1 ]; check MUT-RED-M2 $? "ultima execucao do M2 corrigido: falha=${m2_last:-nenhuma}"

cls=$(grep -c 'CLASSIFICACAO-ERRADA' "$mut")
[ "$cls" = "1" ]; check MUT-BUILD-DEAD-1 $? "exatamente 1 CLASSIFICACAO-ERRADA (M2 primitivo morreu no build)"

restore_ok=$(grep -c 'RESTORE-OK hash=PRE residue=0' "$mut")
[ "$restore_ok" -ge 14 ]; check MUT-RESTORE $? "RESTORE-OK = $restore_ok (esperado >= 14)"
if grep -q 'RESTORE-FAIL' "$mut"; then check MUT-RESTOREFAIL 1 "RESTORE-FAIL presente no ledger"; else check MUT-RESTOREFAIL 0 "nenhum RESTORE-FAIL no ledger"; fi
greens=$(grep -c 'GREEN wrapper=0 logExit=0 aprovado=42 falha=0' "$mut")
[ "$greens" -ge 2 ]; check MUT-GREEN $? "GREEN 42/42 = $greens"
grep -q 'SUMMARY cycles=13 failed=1 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-FULL $? "SUMMARY cycles=13 failed=1 residue=0"
grep -q 'SUMMARY cycles=1 failed=0 residue-QA-MUTATION=0 temp-backups=0' "$mut"; check MUT-SUMMARY-M2 $? "SUMMARY cycles=1 failed=0 (M2 corrigido)"

grep -qE '^M1 RED-IN-LOG .*Send_carries_contentHashes_and_the_read_envelope_returns_the_Attachment_shape' "$mut"
check CATCH-M1 $? "M1 (0007 nunca persistido) pego pelo roundtrip da AC-01"
grep -qE '^M2 RED-IN-LOG .*Send_carries_contentHashes' "$mut" \
   && grep -qE '^M2 RED-DETAIL .*foto da mensagem' "$mut"
check CATCH-M2 $? "M2 (alt fora do envelope) pego pelo campo alt"
grep -qE '^M3 RED-IN-LOG .*QaTask0039AttachmentCapTests' "$mut"
check CATCH-M3 $? "M3 (cap truncado) pego pelo Fact da QA com hash real"
grep -qE '^M4 RED-IN-LOG .*Send_carries_contentHashes' "$mut"
check CATCH-M4 $? "M4 (hash ignorado) pego pela AC-01"
grep -qE '^M5 RED-IN-LOG .*Send_carries_contentHashes' "$mut" && grep -qE '^M5 RED-DETAIL .*Expected: 1' "$mut"
check CATCH-M5 $? "M5 (validacao depois da CAS) pego pelo seq 1 da AC-01"
grep -qE '^M6 RED-IN-LOG .*An_image_without_alt_is_refused_only_after_the_428_and_the_422' "$mut"
check CATCH-M6 $? "M6 (regra Alt desligada) pego pela AC-02"
grep -qE '^M7 RED-IN-LOG .*ThreadAttachmentQuotaTests' "$mut" \
   && grep -qE '^M7 RUN wrapper=1 logExit=1 aprovado=41 falha=1' "$mut"
check CATCH-M7 $? "M7 (envio cobra) pego pela AC-03 (41/1)"
grep -qE '^M8 RED-IN-LOG .*GetMedia_serves_the_participant' "$mut" \
   && grep -qE '^M8 RUN wrapper=1 logExit=1 aprovado=68 falha=1' "$mut"
check CATCH-M8 $? "M8 (ramo mensagem morto) so a mensagem vermelha, 68 aprovados (POST verde)"
grep -qE '^M9 RED-IN-LOG .*GetMedia_serves_the_participant' "$mut"
check CATCH-M9 $? "M9 (parede de cadeiras fora) pego pela AC-04"
grep -qE '^M10 RED-IN-LOG .*An_image_without_alt' "$mut" && grep -qE '^M10 RED-DETAIL .*Expected: PreconditionRequired' "$mut"
check LADDER-428 $? "M10 (428 pulado com anexo) pego: Expected PreconditionRequired"
grep -qE '^M11 RED-IN-LOG .*An_image_without_alt' "$mut" && grep -qE '^M11 RED-DETAIL .*Expected: UnprocessableEntity' "$mut"
check LADDER-422 $? "M11 (422 pulado com anexo) pego: Expected UnprocessableEntity"
grep -qE '^M12 RED-IN-LOG .*ThreadAttachmentQuotaTests' "$mut" \
   && grep -qE '^M12 RUN wrapper=1 logExit=1 aprovado=41 falha=1' "$mut"
check CATCH-M12 $? "M12 (507 desligado) pego pela AC-03 (41/1)"
grep -qE '^M13 RED-IN-LOG .*ThreadMigrationTests' "$mut" \
   && grep -qE '^M13 RUN wrapper=1 logExit=1 aprovado=39 falha=3' "$mut"
check CATCH-M13 $? "M13 (contador na 0007) vermelho so nas migration tests (39/3)"

residue=$(find backend -type f \( -name '*.cs' -o -name '*.sql' \) -not -path '*/bin/*' -not -path '*/obj/*' -print0 2>/dev/null | xargs -0 grep -l 'QA-MUTATION' 2>/dev/null | wc -l)
[ "$residue" = "0" ]; check RESIDUE $? "QA-MUTATION em backend = $residue"

backups=$(find "${TMP:-/tmp}" -maxdepth 1 -name 'qa0039-*' 2>/dev/null | wc -l)
[ "$backups" = "0" ]; check TEMP-BACKUPS $? "backups qa0039-* = $backups"

bom=$(head -c 3 "$qa/qa-mutate-task-0039.ps1" | od -An -tx1 | tr -d ' \n')
[ "$bom" = "efbbbf" ]; check DRIVER-BOM $? "qa-mutate-task-0039.ps1 com UTF-8 BOM ($bom)"

grep -q 'VERIFY total=13 bad=0' "$qa/QA-0039-verify.log" 2>/dev/null; check VERIFY-RECORDED $? "QA-0039-verify.log com VERIFY total=13 bad=0"

status=$(git status --porcelain -uall -- backend 2>&1)
st=$?
if [ "$st" = "0" ] && [ -z "$status" ]; then check GIT-BACKEND 0 "git status backend = 0 entradas"
else check GIT-BACKEND 1 "git backend: st=$st '$status'"; fi

if [ ! -f backend/tests/Squid.Tests/Threads/QaTask0039AttachmentCapTests.cs ]; then
  check TEMP-TEST-GONE 0 "teste temporario removido da arvore"
else
  check TEMP-TEST-GONE 1 "teste temporario ainda no projeto"
fi
[ -f "$qa/QaTask0039AttachmentCapTests.cs.txt" ]; check TEMP-TEST-SOURCE $? "fonte .cs.txt preservada em docs/sdd/qa"

scope="$qa/QA-0039-scope.log"
blob_ok=$(grep -c 'match=True' "$scope" 2>/dev/null)
[ "$blob_ok" = "4" ]; check SCOPE-BLOB $? "blob-identity match=True = $blob_ok de 4"
grep -q 'mtime-count=4 (esperado 4' "$scope"; check SCOPE-MTIME $? "mtime desde a baseline = 4 arquivos mutados"
grep -q 'residue=0' "$scope"; check SCOPE-RESIDUE $? "scope scan: residuo 0"
grep -q 'fora-dos-touches=0' "$scope"; check SCOPE-TOUCHES $? "scope scan: nenhum produto fora dos touches"

doc="$qa/QA-0039-anexo-em-mensagem.md"
if [ -f "$doc" ] && grep -qE '^- verdict: \*+PASSED' "$doc"; then check QA-DOC 0 "QA-0039-anexo-em-mensagem.md com verdict PASSED"
else check QA-DOC 1 "doc ausente ou sem verdict PASSED"; fi

echo "SUMMARY total=$total fails=$fails"
[ "$fails" = "0" ] || exit 1
exit 0
