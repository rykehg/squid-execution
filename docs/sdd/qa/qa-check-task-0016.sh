#!/usr/bin/env bash
# qa-check-task-0016 — Linux twin of qa-check-task-0016.ps1: same 7 check ids, same baseline,
# same exit contract (0 only when SUMMARY total=7 fails=0). S-22: green/red comes from the log's
# own exit: header and summary, never from the wrapper console. S-24: Recovery/** is untracked,
# so sources are read from the filesystem with grep -F, never with git grep.
# Pure ASCII on purpose.
set -u
cd "$(dirname "$0")/../../.." || exit 1

fails=0
check() { # id ok(0/1) detail
  if [ "$2" -eq 1 ]; then
    echo "CHECK $1 PASS $3"
  else
    fails=$((fails + 1))
    echo "CHECK $1 FAIL $3"
  fi
}

baseline=$(cat <<'EOF'
backend/Directory.Packages.props
backend/src/Squid.Api/Squid.Api.csproj
backend/src/Squid.Protocols/Squid.Protocols.csproj
backend/src/Squid.Service/SquidServiceHost.cs
docs/sdd/ideas/BACKLOG.md
docs/sdd/plan/plan.md
docs/sdd/requirements/REQ-0001-squid-mvp.md
backend/src/Squid.Api/Auth/AuthAccount.cs
backend/src/Squid.Api/Auth/AuthEndpoints.cs
backend/src/Squid.Api/Auth/AuthErrors.cs
backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs
backend/src/Squid.Api/Auth/AuthSessions.cs
backend/src/Squid.Api/Auth/IAuthStore.cs
backend/src/Squid.Api/Auth/Mnemonic.cs
backend/src/Squid.Api/Auth/PasswordHasher.cs
backend/src/Squid.Api/Crypto/AesGcmBox.cs
backend/src/Squid.Api/Crypto/KeyDerivation.cs
backend/src/Squid.Api/Crypto/KeyState.cs
backend/src/Squid.Api/Crypto/RootKey.cs
backend/src/Squid.Api/Crypto/RootKeyCipher.cs
backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs
backend/src/Squid.Api/Recovery/IRecoveryStore.cs
backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs
backend/src/Squid.Api/Recovery/RecoveryWarnings.cs
backend/src/Squid.Grains/Actors/ActorErrors.cs
backend/src/Squid.Grains/Actors/ActorEventStream.cs
backend/src/Squid.Grains/Actors/ActorEvents.cs
backend/src/Squid.Grains/Actors/ActorGrain.cs
backend/src/Squid.Grains/Actors/ActorHandle.cs
backend/src/Squid.Grains/Actors/ActorState.cs
backend/src/Squid.Grains/Actors/IActorEventStore.cs
backend/src/Squid.Protocols/ActivityPub/ActorProfile.cs
backend/src/Squid.Protocols/ActivityPub/Base58.cs
backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs
backend/src/Squid.Service/SqliteActorEventStore.cs
backend/src/Squid.Service/SqliteAuthStore.cs
backend/src/Squid.Service/SqliteRecoveryStore.cs
backend/tests/Squid.Tests/Actors/ActorGrainTests.cs
backend/tests/Squid.Tests/Actors/ActorPersistedStateTests.cs
backend/tests/Squid.Tests/Actors/ActorTestData.cs
backend/tests/Squid.Tests/Actors/SqliteActorEventStore.cs
backend/tests/Squid.Tests/Auth/AuthHost.cs
backend/tests/Squid.Tests/Auth/AuthSessionsTests.cs
backend/tests/Squid.Tests/Auth/MnemonicTests.cs
backend/tests/Squid.Tests/Auth/PasswordHasherTests.cs
backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs
backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs
backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs
backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs
backend/tests/Squid.Tests/Discovery/WebFingerProfileTests.cs
backend/tests/Squid.Tests/Recovery/RecoveryImportTests.cs
backend/tests/Squid.Tests/Recovery/RecoveryKitTests.cs
backend/tests/Squid.Tests/Recovery/RecoveryWarningsTests.cs
docs/sdd/qa/QA-0011-actor-grain-event-sourced.md
docs/sdd/qa/QA-0012-primitivas-de-chaves.md
docs/sdd/qa/QA-0013-registro-login-sessao.md
docs/sdd/qa/QA-0014-webfinger-perfil-jsonld.md
docs/sdd/qa/qa-check-task-0012.ps1
docs/sdd/qa/qa-check-task-0012.sh
docs/sdd/qa/qa-check-task-0014.ps1
docs/sdd/qa/qa-check-task-0014.sh
docs/sdd/qa/qa-check-task-0015.ps1
docs/sdd/qa/qa-check-task-0015.sh
docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md
docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md
docs/sdd/tasks/TASK-0013-actor-grain-event-sourced.md
docs/sdd/tasks/TASK-0014-registro-login-sessao.md
docs/sdd/tasks/TASK-0015-webfinger-perfil-jsonld.md
docs/sdd/tasks/TASK-0016-kit-recuperacao.md
EOF
)

allowed_new=$(cat <<'EOF'
backend/tests/Squid.Tests/Recovery/Qa0015RecoveryAcceptanceTests.cs
docs/sdd/qa/QA-0015-kit-recuperacao.md
docs/sdd/qa/qa-check-task-0016.ps1
docs/sdd/qa/qa-check-task-0016.sh
EOF
)

runs=docs/sdd/runs/test-runs

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22) ---
gate=$(ls "$runs"/*-test-gate.log 2>/dev/null | sort | tail -1)
gate_ok=0
if [ -n "$gate" ] && grep -Eq '^exit:[[:space:]]+0[[:space:]]*$' "$gate"; then gate_ok=1; fi
check GATE-NEWEST-EXIT "$gate_ok" "${gate:-no gate log found}"

gate_falha=-1
gate_aprov=-1
if [ -n "$gate" ]; then
  line=$(grep -Eo 'Com falha:[[:space:]]+[0-9]+' "$gate" | tail -1)
  [ -n "$line" ] && gate_falha=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
  line=$(grep -Eo 'Aprovado:[[:space:]]+[0-9]+' "$gate" | tail -1)
  [ -n "$line" ] && gate_aprov=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
fi
gate_sum=0
if [ "$gate_falha" -eq 0 ] && [ "$gate_aprov" -ge 163 ]; then gate_sum=1; fi
check GATE-NEWEST-SUMMARY "$gate_sum" "Com falha=$gate_falha Aprovado=$gate_aprov (need falha=0 and aprovado>=163)"

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY ---
filt=$(ls "$runs"/*-test-filter.log 2>/dev/null | sort | tail -1)
filt_ok=0
if [ -n "$filt" ] && grep -Eq '^exit:[[:space:]]+0[[:space:]]*$' "$filt"; then filt_ok=1; fi
check FILTER-NEWEST-EXIT "$filt_ok" "${filt:-no filter log found}"

filt_falha=-1
filt_aprov=-1
if [ -n "$filt" ]; then
  line=$(grep -Eo 'Com falha:[[:space:]]+[0-9]+' "$filt" | tail -1)
  [ -n "$line" ] && filt_falha=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
  line=$(grep -Eo 'Aprovado:[[:space:]]+[0-9]+' "$filt" | tail -1)
  [ -n "$line" ] && filt_aprov=$(printf '%s' "$line" | grep -Eo '[0-9]+$')
fi
filt_sum=0
if [ "$filt_falha" -eq 0 ] && [ "$filt_aprov" -ge 11 ]; then filt_sum=1; fi
check FILTER-NEWEST-SUMMARY "$filt_sum" "Com falha=$filt_falha Aprovado=$filt_aprov (need falha=0 and aprovado>=11)"

# --- TOUCHES-NO-OUTSIDE (AC-05) ---
git_ok=0
missing=0
extras=0
new_allowed=0
if git status --porcelain -uall > /tmp/qa0016-porcelain.txt 2>/dev/null; then
  git_ok=1
  cut -c4- /tmp/qa0016-porcelain.txt > /tmp/qa0016-paths.txt
  while IFS= read -r p; do
    [ -z "$p" ] && continue
    grep -Fxq "$p" /tmp/qa0016-paths.txt || missing=$((missing + 1))
  done <<< "$baseline"
  while IFS= read -r path; do
    [ -z "$path" ] && continue
    if ! printf '%s\n' "$baseline" | grep -Fxq "$path"; then
      if printf '%s\n' "$allowed_new" | grep -Fxq "$path"; then
        new_allowed=$((new_allowed + 1))
      else
        extras=$((extras + 1))
      fi
    fi
  done < /tmp/qa0016-paths.txt
  rm -f /tmp/qa0016-porcelain.txt /tmp/qa0016-paths.txt
fi
check TOUCHES-NO-OUTSIDE "$([ "$git_ok" -eq 1 ] && [ "$missing" -eq 0 ] && [ "$extras" -eq 0 ] && echo 1 || echo 0)" "git=$git_ok missing=$missing out-of-scope=$extras new-allowed=$new_allowed/4"

# --- ROUTES-STATIC (S-24: filesystem read, no git grep on untracked paths) ---
routes_ok=1
grep -Fq 'app.MapPost("/api/recovery/kit", KitAsync);' backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs || routes_ok=0
grep -Fq 'app.MapPost("/api/recovery/kit/verify", VerifyAsync);' backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs || routes_ok=0
grep -Fq 'app.MapPost("/api/recovery/import", ImportAsync);' backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs || routes_ok=0
grep -Fq 'app.MapGet("/api/auth/me", MeAsync);' backend/src/Squid.Api/Auth/AuthEndpoints.cs || routes_ok=0
check ROUTES-STATIC "$routes_ok" '3 recovery MapPost + GET /api/auth/me read from the filesystem'

# --- RESTORE-SHA256 (S-16) ---
restore_hash=$(sha256sum backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs | cut -d' ' -f1 | tr 'a-f' 'A-F')
restore_ok=0
[ "$restore_hash" = '1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E' ] && restore_ok=1
check RESTORE-SHA256 "$restore_ok" "SHA256=$restore_hash"

echo "SUMMARY total=7 fails=$fails"
if [ "$fails" -eq 0 ]; then
  echo 'RESULT: PASS'
  exit 0
fi
echo 'RESULT: FAIL'
exit 1
