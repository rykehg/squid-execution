#!/usr/bin/env bash
# qa-check-task-0024 - Linux twin of qa-check-task-0024.ps1: same 17 check ids, same exit
# contract (0 only when SUMMARY total=17 fails=0). Same rules baked in: S-22 (the log's own
# exit: header and summary classify), S-16 (SHA256 PRE), S-23 (mtime after restore), the
# touches baseline of 2026-11-09 15:54 (-uall, full file paths). Pure ASCII.
# The .ps1 remains the Windows authority; this twin proves the checks close equal.
set -u
repo="$(cd "$(dirname "$0")/../../.." && pwd)"
cd "$repo" || exit 2
fails=0
check() { # id  ok(0|1)  detail
  if [ "$2" -eq 0 ]; then echo "CHECK $1 PASS $3"
  else fails=$((fails + 1)); echo "CHECK $1 FAIL $3"
  fi
}
runs="docs/sdd/runs/test-runs"

# --- GATE-BASELINE-EXIT / GATE-BASELINE-SUMMARY (this QA's start gate, S-22) ---
base="$runs/20261109-160508-test-gate.log"
if [ -f "$base" ] && grep -qE '^exit: +0$' "$base"; then check GATE-BASELINE-EXIT 0 '20261109-160508-test-gate.log'
else check GATE-BASELINE-EXIT 1 'baseline gate log missing or exit not 0'; fi
b_fail=$(grep -oE 'Com falha: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
b_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$base" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${b_fail:-999}" = "0" ] && [ "${b_aprov:-0}" -eq 274 ]; then
  check GATE-BASELINE-SUMMARY 0 "Com falha=$b_fail Aprovado=$b_aprov (baseline 274)"
else
  check GATE-BASELINE-SUMMARY 1 "Com falha=$b_fail Aprovado=$b_aprov (need 0 and 274)"
fi

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls -1 "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
if [ -n "$gate" ] && grep -qE '^exit: +0$' "$gate"; then check GATE-NEWEST-EXIT 0 "$(basename "$gate")"
else check GATE-NEWEST-EXIT 1 "${gate:-no gate log found}"; fi
g_fail=$(grep -oE 'Com falha: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
g_aprov=$(grep -oE 'Aprovado: +[0-9]+' "$gate" 2>/dev/null | tail -1 | grep -oE '[0-9]+' || echo -1)
if [ "${g_fail:-999}" = "0" ] && [ "${g_aprov:-0}" -eq 277 ]; then
  check GATE-NEWEST-SUMMARY 0 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 277 = 274 + 3 QA)"
else
  check GATE-NEWEST-SUMMARY 1 "Com falha=$g_fail Aprovado=$g_aprov (need 0 and 277)"
fi

# --- FILTER-DEV8: fixed log of DEV's eight new tests ---
dev8="$runs/20261109-154055-test-filter.log"
if [ -f "$dev8" ] && grep -qE '^exit: +0$' "$dev8" && grep -qE 'Com falha: +0,' "$dev8" && grep -qE 'Aprovado: +8,' "$dev8"; then
  check FILTER-DEV8 0 '20261109-154055-test-filter.log (exit 0, Com falha 0, Aprovado 8)'
else
  check FILTER-DEV8 1 '20261109-154055-test-filter.log missing or not 8/8 green'
fi

# --- FILTER-QATEST: newest Projections filter (8 DEV + 3 QA) ---
qa_filter=""
for f in $(ls -1 "$runs"/*-test-filter.log 2>/dev/null | sort); do
  if head -1 "$f" | grep -q 'FullyQualifiedName~Squid.Tests.Projections'; then qa_filter="$f"; fi
done
if [ -n "$qa_filter" ] && grep -qE '^exit: +0$' "$qa_filter" && grep -qE 'Com falha: +0,' "$qa_filter" && grep -qE 'Aprovado: +11,' "$qa_filter"; then
  check FILTER-QATEST 0 "$(basename "$qa_filter") (exit 0, Com falha 0, Aprovado 11 = 8 DEV + 3 QA)"
else
  check FILTER-QATEST 1 "${qa_filter:-no Projections filter log found}"
fi

# --- MUTATION-RED: the four deliberately broken runs are red IN THE LOG and name their tests ---
mut_bad=""
for pair in \
  "20261109-161942-test-filter.log|Counter_and_log_never_diverge" \
  "20261109-162426-test-filter.log|Ac01_a_new_database_gets_every_table" \
  "20261109-163035-test-filter.log|Ac02_a_create_writes_the_post_summary,A_member_who_leaves_before_the_projection" \
  "20261109-163658-test-filter.log|Projecting_the_same_event_a_second_time"; do
  log="${pair%%|*}"
  needles="${pair##*|}"
  path="$runs/$log"
  ok=1
  if [ -f "$path" ] && grep -qE '^exit: +1$' "$path"; then
    ok=0
    oldifs=$IFS
    IFS=','
    for needle in $needles; do
      IFS=$oldifs
      grep -qF "$needle" "$path" || ok=1
      IFS=','
    done
    IFS=$oldifs
  fi
  [ "$ok" -ne 0 ] && mut_bad="$mut_bad $log"
done
if [ -z "$mut_bad" ]; then check MUTATION-RED 0 '4/4 red with the expected tests named'
else check MUTATION-RED 1 "not red or wrong test:$mut_bad"; fi

# --- MUTATION-RESTORE (S-16) ---
pre1="40d877435a6d43f25456b87b78cb2f369221db94d7969c0f9271b6d3fd0c96e8"
pre2="c82191a0162b3e6ecf3da38f27fe403995c9c066d10b73c459b7134669ce1855"
pre3="254c24bce354b54b67f99a5dc43400188e88a5444968135829e028a34f43fdf1"
h1=$(sha256sum 'backend/src/Squid.Service/SqliteActivityStore.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h2=$(sha256sum 'backend/src/Squid.Persistence/schema/0001_init_projections.sql' | cut -d' ' -f1 | tr 'A-F' 'a-f')
h3=$(sha256sum 'backend/src/Squid.ReadModels/Projections/ActivityProjector.cs' | cut -d' ' -f1 | tr 'A-F' 'a-f')
bad=""
[ "$h1" = "$pre1" ] || bad="$bad SqliteActivityStore"
[ "$h2" = "$pre2" ] || bad="$bad 0001_init_projections.sql"
[ "$h3" = "$pre3" ] || bad="$bad ActivityProjector"
if [ -z "$bad" ]; then check MUTATION-RESTORE 0 '3/3 files match PRE'
else check MUTATION-RESTORE 1 "files not matching PRE:$bad"; fi

# --- MUTATION-RESIDUE ---
res=$(grep -rIl 'QA-MUTATION' backend --include='*.cs' --include='*.csproj' --include='*.props' --include='*.targets' --include='*.sql' 2>/dev/null | grep -v -e '/bin/' -e '/obj/' | wc -l | tr -d ' ')
if [ "$res" = "0" ]; then check MUTATION-RESIDUE 0 "QA-MUTATION hits under backend/ = 0"
else check MUTATION-RESIDUE 1 "QA-MUTATION hits under backend/ = $res"; fi

# --- MTIME-SWEEP (S-23 + AC-06): every product file written since the DEV dispatch (14:05) ---
expected=$(cat <<'EOF'
backend/Directory.Packages.props
backend/src/Squid.Persistence/schema/0001_init_projections.sql
backend/src/Squid.Persistence/schema/SchemaMigrator.cs
backend/src/Squid.ReadModels/Projections/ActivityLogEntry.cs
backend/src/Squid.ReadModels/Projections/ActivityProjector.cs
backend/src/Squid.ReadModels/Projections/IActivityProjector.cs
backend/src/Squid.ReadModels/Projections/SqliteActivityLog.cs
backend/src/Squid.ReadModels/Squid.ReadModels.csproj
backend/src/Squid.Service/SchemaMigrations.cs
backend/src/Squid.Service/SqliteActivityStore.cs
backend/src/Squid.Service/Squid.Service.csproj
backend/src/Squid.Service/SquidServiceHost.cs
backend/src/Squid.Worker/Program.cs
backend/src/Squid.Worker/Projection/ActivityProjectionPump.cs
backend/src/Squid.Worker/Squid.Worker.csproj
backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs
backend/tests/Squid.Tests/Projections/ProjectionDb.cs
backend/tests/Squid.Tests/Projections/ProjectionDoubles.cs
backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs
backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs
backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs
backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs
EOF
)
sw=$(find backend -type f -newermt '2026-11-09 14:05:00' ! -path '*/bin/*' ! -path '*/obj/*' 2>/dev/null | sort)
sw_count=$(printf '%s\n' "$sw" | grep -c . || true)
unexpected=0
while IFS= read -r f; do
  [ -z "$f" ] && continue
  printf '%s\n' "$expected" | grep -qxF "$f" || unexpected=$((unexpected + 1))
done <<< "$sw"
if [ "$sw_count" = "22" ] && [ "$unexpected" -eq 0 ]; then
  check MTIME-SWEEP 0 "product files after 14:05 = 22/22 (all inside the touches); unexpected = 0"
else
  check MTIME-SWEEP 1 "product files after 14:05 = $sw_count/22; unexpected = $unexpected"
fi

# --- TOUCHES-NO-OUTSIDE (AC-06) ---
baseline=$(cat <<'EOF'
backend/Directory.Packages.props
backend/src/Squid.ReadModels/Squid.ReadModels.csproj
backend/src/Squid.Service/Squid.Service.csproj
backend/src/Squid.Service/SquidServiceHost.cs
backend/src/Squid.Worker/Program.cs
backend/src/Squid.Worker/Squid.Worker.csproj
docs/sdd/ideas/BACKLOG.md
docs/sdd/plan/plan.md
backend/src/Squid.Api/Posts/IActivityStore.cs
backend/src/Squid.Api/Posts/IActorStatusStore.cs
backend/src/Squid.Api/Posts/IObjectStore.cs
backend/src/Squid.Api/Posts/PostEndpoints.cs
backend/src/Squid.Api/Posts/PostView.cs
backend/src/Squid.Domain/Objects/ActorStatus.cs
backend/src/Squid.Domain/Objects/PostObject.cs
backend/src/Squid.Domain/Objects/PostSanitizer.cs
backend/src/Squid.Domain/Objects/ReplyDepth.cs
backend/src/Squid.Persistence/schema/0001_init_projections.sql
backend/src/Squid.Persistence/schema/SchemaMigrator.cs
backend/src/Squid.ReadModels/Projections/ActivityLogEntry.cs
backend/src/Squid.ReadModels/Projections/ActivityProjector.cs
backend/src/Squid.ReadModels/Projections/IActivityProjector.cs
backend/src/Squid.ReadModels/Projections/SqliteActivityLog.cs
backend/src/Squid.Service/SchemaMigrations.cs
backend/src/Squid.Service/SqliteActivityStore.cs
backend/src/Squid.Service/SqliteActorStatusStore.cs
backend/src/Squid.Service/SqliteObjectStore.cs
backend/src/Squid.Worker/Projection/ActivityProjectionPump.cs
backend/tests/Squid.Tests/Posts/PostContentRulesTests.cs
backend/tests/Squid.Tests/Posts/PostPublishTests.cs
backend/tests/Squid.Tests/Posts/PostReadAndReplyTests.cs
backend/tests/Squid.Tests/Posts/PostRoutes.cs
backend/tests/Squid.Tests/Posts/PostSeqTests.cs
backend/tests/Squid.Tests/Posts/QaTask0023Tests.cs
backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs
backend/tests/Squid.Tests/Projections/ProjectionDb.cs
backend/tests/Squid.Tests/Projections/ProjectionDoubles.cs
backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs
backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs
backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs
docs/sdd/qa/QA-0022-nucleo-do-objeto-post.md
docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md
docs/sdd/tasks/TASK-0023-nucleo-do-objeto-post.md
docs/sdd/tasks/TASK-0024-migracao-projecoes-feed.md
docs/sdd/tasks/TASK-0025-feed-timeline-delta.md
docs/sdd/tasks/TASK-0026-sse-stream.md
docs/sdd/tasks/TASK-0027-upload-midia-skia.md
docs/sdd/tasks/TASK-0028-cota-midia.md
docs/sdd/tasks/TASK-0029-unfurl-ssrf.md
docs/sdd/tasks/TASK-0030-remocao-post-tombstone.md
docs/sdd/tasks/TASK-0031-denuncia-admin-suspensao.md
docs/sdd/tasks/TASK-0032-thumbnail-loadtest-facet.md
EOF
)
allowed_new=$(cat <<'EOF'
backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs
docs/sdd/qa/QA-0023-migracao-projecoes-feed.md
docs/sdd/qa/qa-check-task-0024.ps1
docs/sdd/qa/qa-check-task-0024.sh
docs/sdd/qa/qa-worker-fixture-task-0024.py
docs/sdd/qa/qa-worker-probe-task-0024.ps1
docs/sdd/qa/qa-worker-probe-task-0024.sh
EOF
)
porcelain=$(git -c safe.directory='*' status --porcelain -uall 2>/dev/null)
git_rc=$?
missing=0; extras=0; new_allowed=0
if [ "$git_rc" -eq 0 ]; then
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    path="${line:3}"
    if ! printf '%s\n' "$baseline" | grep -qxF "$path"; then
      if printf '%s\n' "$allowed_new" | grep -qxF "$path"; then new_allowed=$((new_allowed + 1))
      else extras=$((extras + 1)); fi
    fi
  done <<< "$porcelain"
  while IFS= read -r b; do
    [ -z "$b" ] && continue
    printf '%s\n' "$porcelain" | cut -c4- | grep -qxF "$b" || missing=$((missing + 1))
  done <<< "$baseline"
fi
if [ "$git_rc" -eq 0 ] && [ "$missing" -eq 0 ] && [ "$extras" -eq 0 ]; then
  check TOUCHES-NO-OUTSIDE 0 "git=1 missing=0 out-of-scope=0 new-allowed=$new_allowed/7"
else
  check TOUCHES-NO-OUTSIDE 1 "git_rc=$git_rc missing=$missing out-of-scope=$extras new-allowed=$new_allowed/7"
fi

# --- FACTS-STATIC: 8 DEV + 3 QA [Fact] where they claim ---
fact_bad=""
for spec in \
  "backend/tests/Squid.Tests/Projections/SchemaMigrationTests.cs:3" \
  "backend/tests/Squid.Tests/Projections/ActivityProjectorTests.cs:1" \
  "backend/tests/Squid.Tests/Projections/ProjectionPumpTests.cs:3" \
  "backend/tests/Squid.Tests/Projections/ProjectionOffWritePathTests.cs:1" \
  "backend/tests/Squid.Tests/Projections/QaTask0024Tests.cs:3"; do
  f="${spec%:*}"; want="${spec##*:}"
  n=$(grep -c '\[Fact\]' "$f" || true)
  [ "$n" -eq "$want" ] || fact_bad="$fact_bad $f=$n"
done
if [ -z "$fact_bad" ]; then check FACTS-STATIC 0 '3+1+3+1 DEV (8) + 3 QA facts where expected'
else check FACTS-STATIC 1 "$fact_bad"; fi

# --- WORKER-PROBE ---
probe="$runs/20261109-164624-qa-worker-probe.log"
ok=1
if [ -f "$probe" ] && grep -qE '^exit: +0$' "$probe" && grep -qF 'PROBE verify PASS' "$probe" && grep -qF 'negative-control ok' "$probe"; then ok=0; fi
if [ "$ok" -eq 0 ]; then check WORKER-PROBE 0 '20261109-164624-qa-worker-probe.log (exit 0, negative control then PASS)'
else check WORKER-PROBE 1 "$probe missing or not PASS"; fi

# --- WORKER-PROBE-LINUX (classified environment, never assumed green) ---
linux="$runs/20261109-164701-qa-worker-probe-linux.log"
ok=1
if [ -f "$linux" ] && grep -qE '^exit: +3$' "$linux" && grep -qF 'no dotnet on PATH' "$linux"; then ok=0; fi
if [ "$ok" -eq 0 ]; then check WORKER-PROBE-LINUX 0 '20261109-164701-qa-worker-probe-linux.log (exit 3 environment)'
else check WORKER-PROBE-LINUX 1 "$linux missing or not classified as environment"; fi

# --- SIDE-EFFECTS ---
left=$(ls -d "${TMPDIR:-/tmp}"/qa0023* 2>/dev/null | wc -l | tr -d ' ')
if [ "$left" = "0" ]; then check SIDE-EFFECTS 0 'qa0023* entries left in temp = 0'
else check SIDE-EFFECTS 1 "qa0023* entries left in temp = $left"; fi

# --- QA-DOC ---
doc='docs/sdd/qa/QA-0023-migracao-projecoes-feed.md'
if [ -f "$doc" ] && grep -q -- '- task: TASK-0024' "$doc" && grep -q -- '- verdict: PASSED' "$doc"; then
  check QA-DOC 0 "$doc (task id + verdict)"
else
  check QA-DOC 1 "$doc missing or without task id/verdict"
fi

# --- STATE-CHECK (Windows writer tool via interop; the .ps1 is the authority) ---
state=$(powershell.exe -NoProfile -ExecutionPolicy Bypass -File 'docs\sdd\_tools\sdd-state.ps1' check 'docs/sdd/tasks/TASK-0024-migracao-projecoes-feed.md' 2>&1)
state_rc=$?
state_line=$(printf '%s\n' "$state" | grep -m1 'check:' || true)
if [ "$state_rc" -eq 0 ] && printf '%s' "$state_line" | grep -q 'check: OK'; then
  check STATE-CHECK 0 "$state_line"
else
  check STATE-CHECK 1 "rc=$state_rc ${state_line:-missing check: line}"
fi

echo "SUMMARY total=17 fails=$fails"
if [ "$fails" -eq 0 ]; then echo 'RESULT: PASS'; exit 0; fi
echo 'RESULT: FAIL'; exit 1
