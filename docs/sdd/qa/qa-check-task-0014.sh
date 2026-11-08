#!/usr/bin/env bash
# qa-check-task-0014.sh — Linux twin of qa-check-task-0014.ps1 (QA-0013 / TASK-0014).
# Same check ids, same exit contract: 0 = SUMMARY fails=0, 1 = at least one FAIL.
# Runs in WSL (bash) from any directory; every path is resolved against the repo root.
set -u
export LC_ALL=C

root="$(cd "$(dirname "$0")/../../.." && pwd)"
pass=0
fail=0

check() { # id ok(1|0) detail
  if [ "$2" = "1" ]; then
    pass=$((pass + 1)); echo "PASS $1"
  else
    fail=$((fail + 1)); echo "FAIL $1 | $3"
  fi
}
probe() { echo "PROBE $1"; }

mw="$root/backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs"
sess="$root/backend/src/Squid.Api/Auth/AuthSessions.cs"
ends="$root/backend/src/Squid.Api/Auth/AuthEndpoints.cs"
errs="$root/backend/src/Squid.Api/Auth/AuthErrors.cs"
hasher="$root/backend/src/Squid.Api/Auth/PasswordHasher.cs"
account="$root/backend/src/Squid.Api/Auth/AuthAccount.cs"
mnemonic="$root/backend/src/Squid.Api/Auth/Mnemonic.cs"
authstore="$root/backend/src/Squid.Service/SqliteAuthStore.cs"
actorstore="$root/backend/src/Squid.Service/SqliteActorEventStore.cs"
svchost="$root/backend/src/Squid.Service/SquidServiceHost.cs"
apicsproj="$root/backend/src/Squid.Api/Squid.Api.csproj"
handle_src="$root/backend/src/Squid.Grains/Actors/ActorHandle.cs"

region() { # file from to  (to='' means "to end of file")
  awk -v f="$2" -v t="$3" '
    !started && index($0, f) { started = 1 }
    started { print }
    started && t != "" && index($0, t) { exit }' "$1" | tr -d '\r'
}

pos() { # file literal  -> byte offset of first occurrence, -1 when absent
  awk -v pat="$2" 'BEGIN { off = 0; found = -1 } { if (found < 0) { i = index($0, pat); if (i > 0) found = off + i - 1 } off += length($0) + 1 } END { print found }' "$1" | tr -d '\r'
}

counto() { printf '%s\n' "$1" | grep -oF -- "$2" | wc -l; }

# --- AC-01 -------------------------------------------------------------------------------------
block=$(awk '/PreAuthenticationPaths[ \t]*=/ { f = 1 } f { print } f && /\];/ { exit }' "$mw" | tr -d '\r')
paths=$(printf '%s\n' "$block" | grep -o '"[^"]*"' | tr -d '"' | tr 'A-Z' 'a-z' | sort | paste -sd, -)
check AC01-csrf-exempt-paths "$([ "$paths" = "/api/auth/login,/api/auth/register" ] && echo 1 || echo 0)" "exempt paths = [$paths]"
probe "csrf-exempt-paths = $paths"

session_block=$(awk '/Append\(SessionCookie/ { f = 1 } f { print } /Append\(CsrfCookie/ { if (f) exit }' "$sess" | tr -d '\r')
csrf_block=$(awk '/Append\(CsrfCookie/ { f = 1 } f { print }' "$sess" | tr -d '\r')
cookie_ok=1
printf '%s\n' "$session_block" | grep -qF 'HttpOnly = true' || cookie_ok=0
printf '%s\n' "$session_block" | grep -qF 'Secure = true' || cookie_ok=0
printf '%s\n' "$session_block" | grep -qF 'SameSite = SameSiteMode.Lax' || cookie_ok=0
printf '%s\n' "$session_block" | grep -qF 'Expires = expires' || cookie_ok=0
printf '%s\n' "$csrf_block" | grep -qF 'HttpOnly = false' || cookie_ok=0
printf '%s\n' "$csrf_block" | grep -qF 'Secure = true' || cookie_ok=0
printf '%s\n' "$csrf_block" | grep -qF 'SameSite = SameSiteMode.Lax' || cookie_ok=0
check AC01-cookie-attrs "$cookie_ok" 'squid_session HttpOnly+Secure+Lax with expiry, squid_csrf not HttpOnly'

slide_ok=1
grep -qE 'Lifetime = TimeSpan\.FromDays\(30\)' "$sess" || slide_ok=0
grep -qF 'AuthSessions.Slide(' "$mw" || slide_ok=0
grep -qF 'AuthSessions.WriteCookies(' "$mw" || slide_ok=0
check AC01-sliding-window "$slide_ok" '30-day lifetime + slide + re-issue (contract R-03)'
probe "session-lifetime = $(grep -oE 'FromDays\([0-9]+\)' "$sess" | tr -d 'FromDays()')d"

register=$(region "$ends" 'RegisterAsync(' 'LoginAsync(')
kit_ok=1
printf '%s\n' "$register" | grep -qF 'StatusCodes.Status201Created' || kit_ok=0
printf '%s\n' "$register" | grep -qF 'kit = new' || kit_ok=0
printf '%s\n' "$register" | grep -qF 'recoveryCredential = RecoveryCredential.Format' || kit_ok=0
printf '%s\n' "$register" | grep -qF 'mnemonic = Mnemonic.FromRecoveryCode' || kit_ok=0
printf '%s\n' "$register" | grep -qF 'encryptedRootKey = account.Kit!.EncryptedRootKey' || kit_ok=0
printf '%s\n' "$register" | grep -qF 'createdAt = now' || kit_ok=0
check AC01-kit-shape-201 "$kit_ok" '201 + kit with the four contract fields (§3)'
probe 'kit-fields = recoveryCredential,mnemonic,encryptedRootKey,createdAt (201)'
probe "mnemonic-word-count = $(grep -oE 'WordCount = [0-9]+' "$mnemonic" | grep -oE '[0-9]+')"

# --- AC-02 -------------------------------------------------------------------------------------
p422h=$(pos "$ends" 'handle inválido')
p422p=$(pos "$ends" 'senha inválida')
p409=$(pos "$ends" 'handle_taken')
pwrite=$(pos "$ends" 'await store.CreateAsync')
order_ok=1
[ "$p422h" -ge 0 ] || order_ok=0
[ "$p422p" -ge 0 ] || order_ok=0
[ "$p409" -ge 0 ] || order_ok=0
[ "$pwrite" -gt "$p422h" ] || order_ok=0
[ "$pwrite" -gt "$p422p" ] || order_ok=0
[ "$pwrite" -gt "$p409" ] || order_ok=0
printf '%s\n' "$register" | grep -qF 'ActorHandle.IsValid(handle)' || order_ok=0
printf '%s\n' "$register" | grep -qF 'password.Length < PasswordHasher.MinLength' || order_ok=0
check AC02-validation-before-persist "$order_ok" "order 422(handle)=$p422h 422(password)=$p422p 409=$p409 firstWrite=$pwrite"
probe "handle-rule = $(grep -oE 'Rule = "[^"]+"' "$handle_src" | sed 's/Rule = //')"
probe "password-min-length = $(grep -oE 'MinLength = [0-9]+' "$hasher" | grep -oE '[0-9]+')"

# --- AC-03 -------------------------------------------------------------------------------------
login=$(region "$ends" 'LoginAsync(' 'LogoutAsync(')
trace_false=$(counto "$login" 'withTraceId: false')
n401=$(counto "$login" 'Status401Unauthorized')
dummy=1
printf '%s\n' "$login" | grep -qF 'account?.PasswordHash ?? PasswordHasher.DummyHash' || dummy=0
shape_no_trace=1
grep -qF 'new { error = new { code, message } }' "$errs" || shape_no_trace=0
grep -qF 'new { error = new { code, message, traceId } }' "$errs" || shape_no_trace=0
grep -qE 'DummyHash \{ get;' "$hasher" || dummy=0
ac03=1
[ "$trace_false" = "2" ] || ac03=0
[ "$n401" = "2" ] || ac03=0
[ "$dummy" = "1" ] || ac03=0
[ "$shape_no_trace" = "1" ] || ac03=0
check AC03-single-401-shape "$ac03" "login 401 branches=$n401 withTraceId:false=$trace_false dummy=$dummy tracelessShape=$shape_no_trace"
probe "login-401-branches = $n401 ; withTraceId:false = $trace_false ; dummy-hash expression = $dummy"

# --- AC-04 -------------------------------------------------------------------------------------
csrf_ok=1
grep -qF 'StatusCodes.Status403Forbidden' "$mw" || csrf_ok=0
grep -qF '"csrf_failed"' "$mw" || csrf_ok=0
grep -qF 'IsMutation(' "$mw" || csrf_ok=0
grep -qF '!IsPreAuthentication(' "$mw" || csrf_ok=0
grep -qF 'AuthSessions.CsrfMatches(' "$mw" || csrf_ok=0
grep -qF 'CryptographicOperations.FixedTimeEquals' "$sess" || csrf_ok=0
check AC04-csrf-gate "$csrf_ok" 'mutation + non-exempt path + token mismatch -> 403 csrf_failed, fixed time'

# --- AC-05 -------------------------------------------------------------------------------------
logout=$(region "$ends" 'LogoutAsync(' 'LogoutAllAsync(')
logout_all=$(region "$ends" 'LogoutAllAsync(' '')
revoke_ok=1
printf '%s\n' "$logout" | grep -qF '.Id != current.Session.Id' || revoke_ok=0
printf '%s\n' "$logout" | grep -qF 'ClearCookies(context)' || revoke_ok=0
printf '%s\n' "$logout_all" | grep -qF 'Sessions = []' || revoke_ok=0
printf '%s\n' "$logout_all" | grep -qF 'ClearCookies(context)' || revoke_ok=0
check AC05-revocation-rows "$revoke_ok" 'logout drops the id, logout-all empties the list, both clear cookies'

plain_member=0
grep -qE 'public[[:space:]]+(required[[:space:]]+)?string[[:space:]]+Password\b' "$account" && plain_member=1
phc_member=0
grep -qF 'PasswordHash' "$account" && phc_member=1
pii_state=0
[ "$plain_member" = "0" ] && [ "$phc_member" = "1" ] && pii_state=1
check AC05-pii-serialized-state-phc-only "$pii_state" "plaintext Password member=$plain_member PHC member=$phc_member"

scan_files="$root/backend/src/Squid.Service/SqliteAuthStore.cs
$root/backend/src/Squid.Service/SqliteActorEventStore.cs
$root/backend/src/Squid.Service/SquidServiceHost.cs"
scan_files="$scan_files
$(find "$root/backend/src/Squid.Api/Auth" -name '*.cs' -type f | sort)"
file_count=$(printf '%s\n' "$scan_files" | grep -c .)
password_log=0
fixture_hits=0
while IFS= read -r f; do
  [ -n "$f" ] || continue
  hits=$(grep -E 'Log(Information|Warning|Error|Critical|Debug|Trace|Fatal)\(' "$f" 2>/dev/null | grep -icE 'password|senha' || true)
  password_log=$((password_log + ${hits:-0}))
  fix=$(grep -cE 'qa0013-pii-probe-458291|senha-forte-1234' "$f" 2>/dev/null || true)
  fixture_hits=$((fixture_hits + ${fix:-0}))
done <<EOF
$scan_files
EOF
check AC05-pii-source-no-log-password "$([ "$password_log" = "0" ] && [ "$fixture_hits" = "0" ] && echo 1 || echo 0)" "log statements naming password/senha=$password_log ; fixture password in product=$fixture_hits"
probe "files-scanned-for-pii = $file_count"

inline='new SqliteConnection|CommandText|ExecuteNonQuery|ExecuteReader|ExecuteScalar'
auth_sql=$(grep -cE "$inline" "$authstore" || true)
actor_sql=$(grep -cE "$inline" "$actorstore" || true)
ddl=0
while IFS= read -r f; do
  [ -n "$f" ] || continue
  if grep -qiE 'CREATE[[:space:]]+TABLE|ALTER[[:space:]]+TABLE|DROP[[:space:]]+TABLE|CREATE[[:space:]]+INDEX' "$f"; then ddl=$((ddl + 1)); fi
done <<EOF
$scan_files
EOF
store_ok=1
[ "${auth_sql:-0}" = "0" ] || store_ok=0
[ "${actor_sql:-0}" = "0" ] || store_ok=0
[ "$ddl" = "0" ] || store_ok=0
grep -qF 'StoreName = "auth"' "$authstore" || store_ok=0
grep -qF 'GrainId.Create("account", handle)' "$authstore" || store_ok=0
grep -qF 'StoreName = "actors"' "$actorstore" || store_ok=0
grep -qF 'GrainId.Create("actor", handle)' "$actorstore" || store_ok=0
check AC05-store-shape-no-migration "$store_ok" "inline SQL auth=$auth_sql actor=$actor_sql ddl-files=$ddl"
probe 'credential-row = store "auth" / key "account/<handle>" ; actor-row = store "actors" / key "actor/<handle>" ; schema reused (TASK-0003)'

# AC-05 dynamic half: no password and no RK in any executed run log
password_literals="qa0013-pii-probe-458291
qa0013-senha-diferente-000
senha-forte-1234
senha-diferente-99"
log_dir="$root/docs/sdd/runs/test-runs"
patterns=$(mktemp)
trap 'rm -f "$patterns"' EXIT
printf '%s\n' "$password_literals" > "$patterns"

temp_win=$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')
temp_dir=""
if [ -n "$temp_win" ]; then temp_dir=$(wslpath -u "$temp_win" 2>/dev/null || true); fi
marker_count=0
form_count=0
log_secret_ok=1
secret_detail=""
if [ ! -d "${temp_dir:-/nonexistent}" ]; then
  log_secret_ok=0
  secret_detail="windows TEMP unresolved ('$temp_win') - the RK half cannot run"
else
  for marker in "$temp_dir"/qa0013-rk-*.txt; do
    [ -f "$marker" ] || continue
    marker_count=$((marker_count + 1))
    while IFS= read -r form; do
      [ -n "$form" ] || continue
      form_count=$((form_count + 1))
      printf '%s\n' "$form" >> "$patterns"
    done < "$marker"
    rm -f "$marker"   # same as the .ps1 twin: the marker is consumed by the grep
  done
  log_count=0
  for log in "$log_dir"/*.log; do
    [ -f "$log" ] || continue
    log_count=$((log_count + 1))
    if grep -qiF -f "$patterns" "$log"; then
      log_secret_ok=0
      secret_detail="pattern found in $(basename "$log")"
    fi
  done
  probe "logs-scanned = $log_count ; password-literals = 4 ; rk-markers = $marker_count ; rk-forms = $form_count"
  if [ "$marker_count" = "0" ]; then
    probe 'rk-half note = 0 markers (the suite regenerates them; this invocation only proves the password half)'
  else
    probe "rk-markers consumed by the grep = $marker_count (same contract as the .ps1 twin)"
  fi
fi
check AC05-logs-no-secret "$log_secret_ok" "${secret_detail:-password hits=0 rk hits=0}"

# --- AC-06 -------------------------------------------------------------------------------------
version_attr=0
while IFS= read -r csproj; do
  n=$(grep -cE 'Version[[:space:]]*=[[:space:]]*"' "$csproj" 2>/dev/null || true)
  version_attr=$((version_attr + ${n:-0}))
done < <(find "$root/backend" -name '*.csproj' -type f | sort)
csproj_count=$(find "$root/backend" -name '*.csproj' -type f | wc -l)
version_literal=0
while IFS= read -r csproj; do
  n=$(grep -cE 'Version=' "$csproj" 2>/dev/null || true)
  version_literal=$((version_literal + ${n:-0}))
done < <(find "$root/backend" -name '*.csproj' -type f | sort)
check AC06-no-version-attribute "$([ "$version_attr" = "0" ] && echo 1 || echo 0)" "Version=\" attributes in $csproj_count csproj = $version_attr"
probe "csproj-count = $csproj_count ; Version= attributes = $version_attr ; literal Version= (comments only) = $version_literal"

baseline='backend/Directory.Packages.props
backend/src/Squid.Api/Squid.Api.csproj
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
backend/src/Squid.Grains/Actors/ActorErrors.cs
backend/src/Squid.Grains/Actors/ActorEventStream.cs
backend/src/Squid.Grains/Actors/ActorEvents.cs
backend/src/Squid.Grains/Actors/ActorGrain.cs
backend/src/Squid.Grains/Actors/ActorHandle.cs
backend/src/Squid.Grains/Actors/ActorState.cs
backend/src/Squid.Grains/Actors/IActorEventStore.cs
backend/src/Squid.Service/SqliteActorEventStore.cs
backend/src/Squid.Service/SqliteAuthStore.cs
backend/tests/Squid.Tests/Actors/ActorGrainTests.cs
backend/tests/Squid.Tests/Actors/ActorPersistedStateTests.cs
backend/tests/Squid.Tests/Actors/ActorTestData.cs
backend/tests/Squid.Tests/Actors/SqliteActorEventStore.cs
backend/tests/Squid.Tests/Auth/AuthHost.cs
backend/tests/Squid.Tests/Auth/AuthSessionsTests.cs
backend/tests/Squid.Tests/Auth/MnemonicTests.cs
backend/tests/Squid.Tests/Auth/PasswordHasherTests.cs
backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs
backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs
docs/sdd/qa/QA-0011-actor-grain-event-sourced.md
docs/sdd/qa/QA-0012-primitivas-de-chaves.md
docs/sdd/qa/qa-check-task-0012.ps1
docs/sdd/qa/qa-check-task-0012.sh
docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md
docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md
docs/sdd/tasks/TASK-0013-actor-grain-event-sourced.md
docs/sdd/tasks/TASK-0014-registro-login-sessao.md
docs/sdd/tasks/TASK-0015-webfinger-perfil-jsonld.md
docs/sdd/tasks/TASK-0016-kit-recuperacao.md'
allowed='^backend/src/Squid.Api/Auth/
^backend/src/Squid.Service/
^backend/tests/Squid.Tests/Auth/
^docs/sdd/qa/QA-0013
^docs/sdd/qa/qa-check-task-0014
^docs/sdd/runs/
^docs/sdd/tasks/TASK-0014'

unexpected=""
entry_count=0
baseline_count=$(printf '%s\n' "$baseline" | grep -c .)
if ! git -C "$root" status --porcelain -uall > /tmp/qa0013-status.$$ 2>/dev/null; then
  unexpected="git status failed"
else
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    entry_count=$((entry_count + 1))
    path=$(printf '%s' "$line" | cut -c4- | tr -d '"')
    if printf '%s\n' "$baseline" | grep -qxF -- "$path"; then continue; fi
    hit=0
    while IFS= read -r pat; do
      [ -n "$pat" ] || continue
      if printf '%s' "$path" | grep -qE -- "$pat"; then hit=1; break; fi
    done <<EOF
$allowed
EOF
    [ "$hit" = "1" ] || unexpected="$unexpected ; $line"
  done < /tmp/qa0013-status.$$
fi
rm -f /tmp/qa0013-status.$$
check AC06-touches-delta "$([ -z "$unexpected" ] && echo 1 || echo 0)" "entries outside baseline and outside the touches/QA prefixes = [$unexpected]"
probe "git-status-entries = $entry_count ; baseline = $baseline_count ; unexpected = ${unexpected:-none}"

# --- adjudications ----------------------------------------------------------------------------
all_auth=$(cat "$root"/backend/src/Squid.Api/Auth/*.cs)
check ADJ-me-route-absent "$([ "$(printf '%s' "$all_auth" | grep -c '/api/auth/me')" = "0" ] && echo 1 || echo 0)" 'GET /api/auth/me belongs to TASK-0016 (ODEV adjudication), not here'

wiring=1
grep -qF 'AddSingleton<IActorEventStore>' "$svchost" || wiring=0
grep -qE 'new SqliteActorEventStore\(' "$svchost" || wiring=0
grep -qF 'AddSingleton<IAuthStore>' "$svchost" || wiring=0
grep -qE 'new SqliteAuthStore\(' "$svchost" || wiring=0
grep -qF 'app.MapSquidAuth()' "$svchost" || wiring=0
grep -qF 'Squid.Persistence' "$apicsproj" && wiring=0
check S21-wiring-and-adr35 "$wiring" 'production IActorEventStore (S-21) + IAuthStore at the composition root; Squid.Api free of Squid.Persistence (ADR-35)'

total=$((pass + fail))
if [ "$fail" = "0" ]; then result=PASS; else result=FAIL; fi
echo "SUMMARY total=$total fails=$fail"
echo "RESULT: $result"
[ "$fail" = "0" ] && exit 0 || exit 1
