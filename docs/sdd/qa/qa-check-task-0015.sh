#!/usr/bin/env bash
# qa-check-task-0015.sh — Linux twin of qa-check-task-0015.ps1 (TASK-0015 / QA-0014, AC-01..AC-05).
#
# Same 12 check ids, same exit contract (0 = SUMMARY total=N fails=0, 1 = any FAIL), same
# baseline of git status captured when QA-0014 started. Every test classification is read from
# the log file under docs/sdd/runs/test-runs/ (rule S-22), never from a runner console.
#
# Usage:  bash docs/sdd/qa/qa-check-task-0015.sh
set -u
export LC_ALL=C

repo="$(cd "$(dirname "$0")/../../.." && pwd)"
runs="$repo/docs/sdd/runs/test-runs"
src="$repo/backend/src"
discovery="$src/Squid.Api/Discovery"
qa_test="$repo/backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs"

total=0
fails=0

check() { # check <id> <0|1> <detail>
  total=$((total + 1))
  if [ "$2" -ne 0 ]; then status=PASS; else status=FAIL; fails=$((fails + 1)); fi
  printf 'CHECK %s %s %s\n' "$1" "$status" "$3"
}

read_file() { [ -f "$1" ] && cat "$1" || true; }

count_fixed() { grep -oF "$2" <<<"$1" 2>/dev/null | wc -l | tr -d ' '; }

newest_log() { ls -1 "$runs"/*"$1" 2>/dev/null | LC_ALL=C sort | tail -n 1; }

log_verdict() { # log_verdict <filter-suffix> <min-approved> <check-id>
  local log exit failed approved
  log="$(newest_log "$1")"
  if [ -z "$log" ]; then check "$3" 0 "no *$1 log under docs/sdd/runs/test-runs"; return; fi
  exit="$(grep -m1 -E '^exit:' "$log" | grep -oE '[0-9]+' | head -n 1)"
  failed="$(grep -oE 'Com falha:[[:space:]]+[0-9]+' "$log" | head -n 1 | grep -oE '[0-9]+$')"
  approved="$(grep -oE 'Aprovado:[[:space:]]+[0-9]+' "$log" | head -n 1 | grep -oE '[0-9]+$')"
  : "${exit:=NA}" "${failed:=NA}" "${approved:=0}"
  if [ "$exit" = "0" ] && [ "$failed" = "0" ] && [ "$approved" -ge "$2" ]; then ok=1; else ok=0; fi
  check "$3" "$ok" "$(basename "$log") exit=$exit Com falha=$failed Aprovado=$approved (S-22: read inside the log)"
}

# --- AC-01: one origin, from the request (SPEC-0007 B-09a), no config key ---------------
endpoints_file="$discovery/DiscoveryEndpoints.cs"
origin_missing=0
for bit in \
  'context.Request.Scheme}://{context.Request.Host.Value' \
  'string.Equals(domain, context.Request.Host.Value' \
  'subject = $"acct:{name}@{domain}"' \
  'href = $"{origin}/users/{name}"' \
  'profileJsonLd(state, Origin(context))'; do
  if ! grep -qF "$bit" "$endpoints_file" 2>/dev/null; then origin_missing=$((origin_missing + 1)); fi
done
if [ "$origin_missing" -eq 0 ]; then ok=1; else ok=0; fi
check "AC01-single-origin-code" "$ok" "origin=scheme+Host, subject/link/id derivation lines present; missing=$origin_missing"

cfg_hits=$(grep -roF 'Configuration[' "$src" --include='*.cs' 2>/dev/null | wc -l | tr -d ' ')
cfg_storage=$(grep -rF 'Configuration[' "$src" --include='*.cs' 2>/dev/null | grep -cF 'Storage:Path')
appsettings=$(find "$repo/backend" -name 'appsettings*.json' -type f 2>/dev/null | wc -l | tr -d ' ')
disc_cfg=$(grep -rE 'IConfiguration|Configuration\[|GetEnvironmentVariable' "$discovery" --include='*.cs' 2>/dev/null | wc -l | tr -d ' ')
proto_cfg=$(grep -rE 'IConfiguration|Configuration\[|GetEnvironmentVariable' "$src/Squid.Protocols" --include='*.cs' 2>/dev/null | wc -l | tr -d ' ')
if [ "$cfg_hits" = "1" ] && [ "$cfg_storage" -ge 1 ] && [ "$appsettings" = "0" ] && [ "$disc_cfg" = "0" ] && [ "$proto_cfg" = "0" ]; then ok=1; else ok=0; fi
check "AC01-no-domain-config" "$ok" "Configuration[ hits=$cfg_hits (only Storage:Path), appsettings=$appsettings, Discovery/Protocols config reads=$disc_cfg+$proto_cfg"

# --- AC-02: the three 404s, foreign domain among them (B-09a, no proxying) --------------
flat="$(tr '\n' ' ' <"$endpoints_file" 2>/dev/null)"
foreign_rule=$(grep -cE 'if \(!string\.Equals\(domain, context\.Request\.Host\.Value.{0,240}Results\.NotFound\(\)' <<<"$flat")
if [ "$foreign_rule" -ge 1 ]; then ok=1; else ok=0; fi
check "AC02-foreign-404-rule" "$ok" "foreign-domain guard compares resource domain to Request.Host and answers NotFound (mutation target)"

refusals=$(count_fixed "$(read_file "$endpoints_file")" 'Results.NotFound()')
if [ "$refusals" -ge 6 ]; then ok=1; else ok=0; fi
check "AC02-refusal-count" "$ok" "Results.NotFound() occurrences in DiscoveryEndpoints.cs = $refusals (>= 6)"

# --- AC-03: no forwarded headers, no auth dependency, S-25 two views in QA's scenario ----
fwd=$(grep -rE 'ForwardedHeaders|X-Forwarded' "$src" --include='*.cs' 2>/dev/null | wc -l | tr -d ' ')
if [ "$fwd" = "0" ]; then ok=1; else ok=0; fi
check "AC03-no-forwarded-read" "$ok" "ForwardedHeaders/X-Forwarded occurrences in backend/src = $fwd (parked I-11)"

middleware="$(read_file "$src/Squid.Api/Auth/AuthSessionMiddleware.cs")"
discovery_auth=$(grep -cE 'AuthSessions|ResolvedKey|Csrf|Session' "$endpoints_file" 2>/dev/null)
: "${discovery_auth:=0}"
mw_gates=0
grep -qF 'IsMutation(' <<<"$middleware" && grep -qF 'Status403Forbidden' <<<"$middleware" && mw_gates=1
mw_401=0
grep -qF 'Status401' <<<"$middleware" && mw_401=1
if [ "$discovery_auth" -eq 0 ] && [ "$mw_gates" -eq 1 ] && [ "$mw_401" -eq 0 ]; then ok=1; else ok=0; fi
check "AC03-no-auth-on-discovery" "$ok" "discovery auth refs=$discovery_auth; middleware gates mutations=$([ "$mw_gates" -eq 1 ] && echo True || echo False); middleware 401=$([ "$mw_401" -eq 1 ] && echo True || echo False) (GET must pass)"

qa_text="$(read_file "$qa_test")"
s25=1
grep -qF 'raw.Replace("\\u002B"' <<<"$qa_text" || s25=0
grep -qF 'StringValues(document)' <<<"$qa_text" || s25=0
grep -qF 'store.ReadAsync' <<<"$qa_text" || s25=0
grep -qF 'Regex' <<<"$qa_text" && s25=0
check "AC03-s25-two-views" "$s25" "QA scenario scans the response raw AND decoded, reads the row typed, no raw-regex row scan"

# --- AC-04: round-trip wiring (composition root + mapper + canonical record) ------------
composition="$(read_file "$src/Squid.Service/SquidServiceHost.cs")"
mapper="$(read_file "$src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs")"
profile_doc="$(read_file "$src/Squid.Protocols/ActivityPub/ActorProfile.cs")"
wired=0
grep -qF 'app.MapSquidDiscovery(ProfileJsonLd.Serialize)' <<<"$composition" && wired=1
mapper_ok=0
grep -qF 'public static string Serialize(ActorState state, string origin)' <<<"$mapper" &&
  grep -qF 'public static ActorProfile Materialize(string json)' <<<"$mapper" && mapper_ok=1
record_missing=0
for bit in 'public sealed record ActorProfile(' 'string Origin,' 'string PreferredUsername,' 'string? Name,' 'string RsaPublicKeyBase64,' 'string Ed25519PublicKeyBase64'; do
  grep -qF "$bit" <<<"$profile_doc" || record_missing=$((record_missing + 1))
done
if [ "$wired" -eq 1 ] && [ "$mapper_ok" -eq 1 ] && [ "$record_missing" -eq 0 ]; then ok=1; else ok=0; fi
check "AC04-roundtrip-wired" "$ok" "composition-root wired=$([ "$wired" -eq 1 ] && echo True || echo False); Serialize/Materialize public=$([ "$mapper_ok" -eq 1 ] && echo True || echo False); canonical record members missing=$record_missing"

# --- AC-05: gate green, Discovery filter green, touches, no private material in logs ----
log_verdict "test-gate.log" 1 "AC05-gate-log-green"
log_verdict "test-filter.log" 9 "AC05-filter-log-green"

# Baseline of git status --porcelain -uall captured when QA-0014 started (58 entries).
baseline=(
  'M backend/Directory.Packages.props'
  'M backend/src/Squid.Api/Squid.Api.csproj'
  'M backend/src/Squid.Protocols/Squid.Protocols.csproj'
  'M backend/src/Squid.Service/SquidServiceHost.cs'
  'M docs/sdd/ideas/BACKLOG.md'
  'M docs/sdd/plan/plan.md'
  'M docs/sdd/requirements/REQ-0001-squid-mvp.md'
  '?? backend/src/Squid.Api/Auth/AuthAccount.cs'
  '?? backend/src/Squid.Api/Auth/AuthEndpoints.cs'
  '?? backend/src/Squid.Api/Auth/AuthErrors.cs'
  '?? backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs'
  '?? backend/src/Squid.Api/Auth/AuthSessions.cs'
  '?? backend/src/Squid.Api/Auth/IAuthStore.cs'
  '?? backend/src/Squid.Api/Auth/Mnemonic.cs'
  '?? backend/src/Squid.Api/Auth/PasswordHasher.cs'
  '?? backend/src/Squid.Api/Crypto/AesGcmBox.cs'
  '?? backend/src/Squid.Api/Crypto/KeyDerivation.cs'
  '?? backend/src/Squid.Api/Crypto/KeyState.cs'
  '?? backend/src/Squid.Api/Crypto/RootKey.cs'
  '?? backend/src/Squid.Api/Crypto/RootKeyCipher.cs'
  '?? backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs'
  '?? backend/src/Squid.Grains/Actors/ActorErrors.cs'
  '?? backend/src/Squid.Grains/Actors/ActorEventStream.cs'
  '?? backend/src/Squid.Grains/Actors/ActorEvents.cs'
  '?? backend/src/Squid.Grains/Actors/ActorGrain.cs'
  '?? backend/src/Squid.Grains/Actors/ActorHandle.cs'
  '?? backend/src/Squid.Grains/Actors/ActorState.cs'
  '?? backend/src/Squid.Grains/Actors/IActorEventStore.cs'
  '?? backend/src/Squid.Protocols/ActivityPub/ActorProfile.cs'
  '?? backend/src/Squid.Protocols/ActivityPub/Base58.cs'
  '?? backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs'
  '?? backend/src/Squid.Service/SqliteActorEventStore.cs'
  '?? backend/src/Squid.Service/SqliteAuthStore.cs'
  '?? backend/tests/Squid.Tests/Actors/ActorGrainTests.cs'
  '?? backend/tests/Squid.Tests/Actors/ActorPersistedStateTests.cs'
  '?? backend/tests/Squid.Tests/Actors/ActorTestData.cs'
  '?? backend/tests/Squid.Tests/Actors/SqliteActorEventStore.cs'
  '?? backend/tests/Squid.Tests/Auth/AuthHost.cs'
  '?? backend/tests/Squid.Tests/Auth/AuthSessionsTests.cs'
  '?? backend/tests/Squid.Tests/Auth/MnemonicTests.cs'
  '?? backend/tests/Squid.Tests/Auth/PasswordHasherTests.cs'
  '?? backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs'
  '?? backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs'
  '?? backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs'
  '?? backend/tests/Squid.Tests/Discovery/WebFingerProfileTests.cs'
  '?? docs/sdd/qa/QA-0011-actor-grain-event-sourced.md'
  '?? docs/sdd/qa/QA-0012-primitivas-de-chaves.md'
  '?? docs/sdd/qa/QA-0013-registro-login-sessao.md'
  '?? docs/sdd/qa/qa-check-task-0012.ps1'
  '?? docs/sdd/qa/qa-check-task-0012.sh'
  '?? docs/sdd/qa/qa-check-task-0014.ps1'
  '?? docs/sdd/qa/qa-check-task-0014.sh'
  '?? docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md'
  '?? docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md'
  '?? docs/sdd/tasks/TASK-0013-actor-grain-event-sourced.md'
  '?? docs/sdd/tasks/TASK-0014-registro-login-sessao.md'
  '?? docs/sdd/tasks/TASK-0015-webfinger-perfil-jsonld.md'
  '?? docs/sdd/tasks/TASK-0016-kit-recuperacao.md'
)
allowed=(
  'docs/sdd/qa/QA-0014'
  'docs/sdd/qa/qa-check-task-0015'
  'backend/tests/Squid.Tests/Discovery/Qa0014'
)

git_ok=0
git_detail="git status not collected"
if ! git -C "$repo" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  git_detail="not a git working tree from this shell"
else
  git_lines="$(git -C "$repo" status --porcelain -uall)"
  missing=0
  out_of_scope=0
  current_count=0
  extras_allowed=0
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    status="$(cut -c1-2 <<<"$line" | tr -d ' ')"
    path="${line:3}"
    current_count=$((current_count + 1))
    found=0
    for b in "${baseline[@]}"; do [ "$b" = "$status $path" ] && found=1 && break; done
    if [ "$found" -eq 0 ]; then
      hit=0
      for p in "${allowed[@]}"; do case "$path" in "$p"*) hit=1;; esac; done
      if [ "$hit" -eq 1 ]; then extras_allowed=$((extras_allowed + 1)); else out_of_scope=$((out_of_scope + 1)); printf 'TOUCH-OUT-OF-SCOPE %s %s\n' "$status" "$path"; fi
    fi
  done <<<"$git_lines"
  for b in "${baseline[@]}"; do
    found=0
    while IFS= read -r line; do
      [ -z "$line" ] && continue
      status="$(cut -c1-2 <<<"$line" | tr -d ' ')"
      path="${line:3}"
      [ "$b" = "$status $path" ] && found=1 && break
    done <<<"$git_lines"
    if [ "$found" -eq 0 ]; then missing=$((missing + 1)); printf 'TOUCH-MISSING %s\n' "$b"; fi
  done
  if [ "$missing" -eq 0 ] && [ "$out_of_scope" -eq 0 ]; then git_ok=1; fi
  git_detail="baseline=58 current=$current_count missing=$missing extras-allowed=$extras_allowed out-of-scope=$out_of_scope"
fi
check "AC05-touches" "$git_ok" "$git_detail"

private_hits=0
logs_scanned=0
for pattern in test-gate.log test-filter.log; do
  log="$(newest_log "$pattern")"
  if [ -n "$log" ]; then
    logs_scanned=$((logs_scanned + 1))
    private_hits=$((private_hits + $(grep -cE 'BEGIN (RSA )?PRIVATE KEY|PRIVATE KEY-----' "$log")))
  fi
done
if [ "$private_hits" -eq 0 ] && [ "$logs_scanned" -eq 2 ]; then ok=1; else ok=0; fi
check "LOG-no-private-material" "$ok" "logs scanned=$logs_scanned; private-key PEM markers found=$private_hits"

printf 'SUMMARY total=%s fails=%s\n' "$total" "$fails"
if [ "$fails" -eq 0 ]; then
  printf 'RESULT: PASS\n'
  exit 0
fi
printf 'RESULT: FAIL\n'
exit 1
