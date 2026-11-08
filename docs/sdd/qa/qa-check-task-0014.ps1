#Requires -Version 5.1
<#
qa-check-task-0014 — QA-0013 static/contract half of TASK-0014 (registro + login/sessao).

What it does: 15 checks over the sources the ACs name, plus the two greps an xunit run cannot
do (the run logs of docs/sdd/runs/test-runs for the password literals and for the RK forms the
suite left in marker files) and the AC-06 classification of `git status` against the baseline QA
captured when this attempt started. The dynamic half (the 7 HTTP scenarios) runs through
sdd-test.ps1; this script only proves the invariants those scenarios cannot see.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1

Exit codes: 0 = all checks green (SUMMARY fails=0), 1 = at least one FAIL.
#>
$ErrorActionPreference = 'Continue'

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$script:pass = 0
$script:fail = 0

function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { $script:pass++; Write-Output "PASS $Id" }
  else { $script:fail++; Write-Output "FAIL $Id | $Detail" }
}

function Probe([string]$Line) { Write-Output "PROBE $Line" }

function Src([string]$Relative) {
  $path = Join-Path $root $Relative
  if (-not (Test-Path -LiteralPath $path)) { return $null }
  return (Get-Content -LiteralPath $path -Raw -Encoding UTF8)
}

function Region([string]$Text, [string]$From, [string]$To) {
  if ($null -eq $Text) { return '' }
  $start = $Text.IndexOf($From, [StringComparison]::Ordinal)
  if ($start -lt 0) { return '' }
  if ([string]::IsNullOrEmpty($To)) { return $Text.Substring($start) }
  $end = $Text.IndexOf($To, $start + $From.Length, [StringComparison]::Ordinal)
  if ($end -lt 0) { return $Text.Substring($start) }
  return $Text.Substring($start, $end - $start)
}

# --- sources under test (the task's own product paths) ----------------------------------------
$mw = Src 'backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs'
$sess = Src 'backend/src/Squid.Api/Auth/AuthSessions.cs'
$ends = Src 'backend/src/Squid.Api/Auth/AuthEndpoints.cs'
$errs = Src 'backend/src/Squid.Api/Auth/AuthErrors.cs'
$hasher = Src 'backend/src/Squid.Api/Auth/PasswordHasher.cs'
$account = Src 'backend/src/Squid.Api/Auth/AuthAccount.cs'
$authStore = Src 'backend/src/Squid.Service/SqliteAuthStore.cs'
$actorStore = Src 'backend/src/Squid.Service/SqliteActorEventStore.cs'
$svcHost = Src 'backend/src/Squid.Service/SquidServiceHost.cs'
$apiCsproj = Src 'backend/src/Squid.Api/Squid.Api.csproj'
$handleSrc = Src 'backend/src/Squid.Grains/Actors/ActorHandle.cs'

$register = Region $ends 'RegisterAsync(' 'LoginAsync('
$login = Region $ends 'LoginAsync(' 'LogoutAsync('
$logout = Region $ends 'LogoutAsync(' 'LogoutAllAsync('
$logoutAll = Region $ends 'LogoutAllAsync(' ''
Probe ("regions parsed: register={0} login={1} logout={2} logoutAll={3}" -f `
    $register.Length, $login.Length, $logout.Length, $logoutAll.Length)

# --- AC-01: the exempt paths, the two cookies, the sliding window, the kit ---------------------
$block = [regex]::Match($mw, 'PreAuthenticationPaths\s*=\s*\[(.*?)\];', 'Singleline')
$paths = @()
if ($block.Success) {
  $paths = @([regex]::Matches($block.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
}
$got = (($paths | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object) -join ',')
Check 'AC01-csrf-exempt-paths' ($got -eq '/api/auth/login,/api/auth/register') "exempt paths = [$got] (contract: exactly register+login)"
Probe ("csrf-exempt-paths = " + ($paths -join ' , '))

$iSession = $sess.IndexOf('Append(SessionCookie', [StringComparison]::Ordinal)
$iCsrf = $sess.IndexOf('Append(CsrfCookie', [StringComparison]::Ordinal)
$sessionBlock = ''
$csrfBlock = ''
if ($iSession -ge 0 -and $iCsrf -gt $iSession) {
  $sessionBlock = $sess.Substring($iSession, $iCsrf - $iSession)
  $csrfBlock = $sess.Substring($iCsrf)
}
$cookieOk = ($iSession -ge 0) -and ($iCsrf -gt $iSession) `
  -and ($sessionBlock -match 'HttpOnly = true') -and ($sessionBlock -match 'Secure = true') `
  -and ($sessionBlock -match 'SameSite = SameSiteMode\.Lax') -and ($sessionBlock -match 'Expires = expires') `
  -and ($csrfBlock -match 'HttpOnly = false') -and ($csrfBlock -match 'Secure = true') `
  -and ($csrfBlock -match 'SameSite = SameSiteMode\.Lax')
Check 'AC01-cookie-attrs' $cookieOk 'squid_session must be HttpOnly+Secure+Lax with an expiry, squid_csrf must NOT be HttpOnly'
$slideOk = ($sess -match 'Lifetime = TimeSpan\.FromDays\(30\)') -and ($mw -match 'AuthSessions\.Slide\(') -and ($mw -match 'AuthSessions\.WriteCookies\(')
Check 'AC01-sliding-window' $slideOk '30-day lifetime + slide + re-issue on the authenticated request (contract R-03)'
Probe ("session-lifetime = " + ([regex]::Match($sess, 'Lifetime = TimeSpan\.FromDays\((\d+)\)').Groups[1].Value) + "d")

$kitOk = ($register -match 'StatusCodes\.Status201Created') -and ($register -match 'kit = new') `
  -and ($register -match 'recoveryCredential = RecoveryCredential\.Format') `
  -and ($register -match 'mnemonic = Mnemonic\.FromRecoveryCode') `
  -and ($register -match 'encryptedRootKey = account\.Kit!.EncryptedRootKey') `
  -and ($register -match 'createdAt = now')
Check 'AC01-kit-shape-201' $kitOk '201 + kit with the four fields contracts/http-api.md §3 defines'
Probe 'kit-fields = recoveryCredential,mnemonic,encryptedRootKey,createdAt (201)'
$mnemonicSrc = Src 'backend/src/Squid.Api/Auth/Mnemonic.cs'
Probe ('mnemonic-word-count = ' + [regex]::Match($mnemonicSrc, 'WordCount = (\d+)').Groups[1].Value + ' (runtime asserted by Qa0013)')

# --- AC-02: every refusal returns before the first write --------------------------------------
$p422Handle = $ends.IndexOf('handle inválido', [StringComparison]::Ordinal)
$p422Password = $ends.IndexOf('senha inválida', [StringComparison]::Ordinal)
$p409 = $ends.IndexOf('handle_taken', [StringComparison]::Ordinal)
$pCreate = $ends.IndexOf('await store.CreateAsync', [StringComparison]::Ordinal)
$validateFirst = ($p422Handle -ge 0) -and ($p422Password -ge 0) -and ($p409 -ge 0) -and ($pCreate -ge 0) `
  -and ($pCreate -gt $p422Handle) -and ($pCreate -gt $p422Password) -and ($pCreate -gt $p409) `
  -and ($register -match 'ActorHandle\.IsValid\(handle\)') -and ($register -match 'password\.Length < PasswordHasher\.MinLength')
Check 'AC02-validation-before-persist' $validateFirst "order 422(handle)=$p422Handle 422(password)=$p422Password 409=$p409 firstWrite=$pCreate"
Probe ('handle-rule = ' + [regex]::Match($handleSrc, 'Rule = "([^"]+)"').Groups[1].Value)
Probe ('password-min-length = ' + [regex]::Match($hasher, 'MinLength = (\d+)').Groups[1].Value)

# --- AC-03: one 401, byte-identical, dummy hash always verified --------------------------------
$traceFalse = ([regex]::Matches($login, 'withTraceId: false')).Count
$dummy = $login -match 'account\?\.PasswordHash \?\? PasswordHasher\.DummyHash'
$one401 = ([regex]::Matches($login, 'Status401Unauthorized')).Count
$shapeNoTrace = $errs.IndexOf('new { error = new { code, message } }', [StringComparison]::Ordinal) -ge 0
$shapeTrace = $errs.IndexOf('new { error = new { code, message, traceId } }', [StringComparison]::Ordinal) -ge 0
$ac03 = ($traceFalse -eq 2) -and ($one401 -eq 2) -and $dummy -and $shapeNoTrace -and $shapeTrace `
  -and ($hasher -match 'DummyHash \{ get;\s*\} = Hash\(')
Check 'AC03-single-401-shape' $ac03 "login 401 branches=$one401 withTraceId:false=$traceFalse dummy=$dummy tracelessShape=$shapeNoTrace"
Probe ("login-401-branches = $one401 ; withTraceId:false = $traceFalse ; dummy-hash expression = $dummy")

# --- AC-04: the CSRF gate --------------------------------------------------------------------
$csrfOk = ($mw -match 'StatusCodes\.Status403Forbidden') -and ($mw -match '"csrf_failed"') `
  -and ($mw -match 'IsMutation\(') -and ($mw -match '!IsPreAuthentication\(') `
  -and ($mw -match 'AuthSessions\.CsrfMatches\(') `
  -and ($sess -match 'CryptographicOperations\.FixedTimeEquals')
Check 'AC04-csrf-gate' $csrfOk 'mutation + non-exempt path + token mismatch -> 403 csrf_failed, compared in fixed time'

# --- AC-05: revocation rows, PII, the storage shape -------------------------------------------
$revokeOk = ($logout -match '\.Id != current\.Session\.Id') -and ($logout -match 'ClearCookies\(context\)') `
  -and ($logoutAll -match 'Sessions = \[\]') -and ($logoutAll -match 'ClearCookies\(context\)')
Check 'AC05-revocation-rows' $revokeOk 'logout drops the session id, logout-all empties the list, both clear the cookies'

$plainPasswordMember = [regex]::IsMatch($account, 'public\s+(?:required\s+)?string\s+Password\b')
$phcMember = $account -match 'PasswordHash'
Check 'AC05-pii-serialized-state-phc-only' ((-not $plainPasswordMember) -and $phcMember) 'AuthAccount may carry PasswordHash (PHC), never a plaintext Password member'

# scope: only the product paths this task touches (a red elsewhere is reported, not fixed)
$scanFiles = @(Get-Item -LiteralPath @(
    (Join-Path $root 'backend/src/Squid.Service/SqliteAuthStore.cs'),
    (Join-Path $root 'backend/src/Squid.Service/SqliteActorEventStore.cs'),
    (Join-Path $root 'backend/src/Squid.Service/SquidServiceHost.cs')))
$scanFiles += @(Get-ChildItem -LiteralPath (Join-Path $root 'backend/src/Squid.Api/Auth') -Filter *.cs -File)
$logStatement = 'Log(?:Information|Warning|Error|Critical|Debug|Trace|Fatal)\s*\('
$passwordLog = @()
$fixtureInProduct = @()
foreach ($file in $scanFiles) {
  $lines = Get-Content -LiteralPath $file.FullName
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $line = $lines[$i]
    if (($line -match $logStatement) -and ($line -match 'password|senha')) {
      $passwordLog += ('{0}:{1}' -f $file.Name, ($i + 1))
    }
    if ($line -match 'qa0013-pii-probe-458291|senha-forte-1234') {
      $fixtureInProduct += ('{0}:{1}' -f $file.Name, ($i + 1))
    }
  }
}
Check 'AC05-pii-source-no-log-password' (($passwordLog.Count -eq 0) -and ($fixtureInProduct.Count -eq 0)) `
  ('log statements naming password/senha = [' + ($passwordLog -join ';') + '] ; fixture password in product = [' + ($fixtureInProduct -join ';') + ']')
Probe ('files-scanned-for-pii = ' + $scanFiles.Count)

$inlineSql = 'new SqliteConnection|CommandText|ExecuteNonQuery|ExecuteReader|ExecuteScalar'
$authStoreSql = [regex]::IsMatch($authStore, $inlineSql)
$actorStoreSql = [regex]::IsMatch($actorStore, $inlineSql)
$ddl = @()
foreach ($file in $scanFiles) {
  if ((Get-Content -LiteralPath $file.FullName -Raw) -match 'CREATE\s+TABLE|ALTER\s+TABLE|DROP\s+TABLE|CREATE\s+INDEX') { $ddl += $file.Name }
}
$storeShape = (-not $authStoreSql) -and (-not $actorStoreSql) -and ($ddl.Count -eq 0) `
  -and ($authStore -match 'StoreName = "auth"') -and ($authStore -match 'GrainId\.Create\("account", handle\)') `
  -and ($actorStore -match 'StoreName = "actors"') -and ($actorStore -match 'GrainId\.Create\("actor", handle\)')
Check 'AC05-store-shape-no-migration' $storeShape ('auth-store inline SQL=' + $authStoreSql + ' actor-store inline SQL=' + $actorStoreSql + ' DDL files=[' + ($ddl -join ',') + ']')
Probe 'credential-row = store "auth" / key "account/<handle>" ; actor-row = store "actors" / key "actor/<handle>" ; schema reused (TASK-0003)'

# --- AC-05 dynamic half: no password and no RK in any executed run log -------------------------
$passwordLiterals = @('qa0013-pii-probe-458291', 'qa0013-senha-diferente-000', 'senha-forte-1234', 'senha-diferente-99')
$logDir = Join-Path $root 'docs/sdd/runs/test-runs'
$logs = @(Get-ChildItem -LiteralPath $logDir -Filter *.log -File -ErrorAction SilentlyContinue)

$markers = @(Get-ChildItem -LiteralPath $env:TEMP -Filter 'qa0013-rk-*.txt' -File -ErrorAction SilentlyContinue)
$rkForms = @()
foreach ($marker in $markers) {
  foreach ($form in @(Get-Content -LiteralPath $marker.FullName)) {
    if (-not [string]::IsNullOrWhiteSpace($form)) { $rkForms += $form.Trim() }
  }
}

$passwordHits = @()
$rkHits = @()
foreach ($log in $logs) {
  $raw = Get-Content -LiteralPath $log.FullName -Raw
  if ($null -eq $raw) { $raw = '' }   # a 0-byte log is empty, never "clean by skipping"
  foreach ($literal in $passwordLiterals) {
    $count = [regex]::Matches($raw, [regex]::Escape($literal)).Count
    if ($count -gt 0) { $passwordHits += ('{0}:{1}x{2}' -f $log.Name, $literal, $count) }
  }
  foreach ($form in $rkForms) {
    $count = [regex]::Matches($raw, [regex]::Escape($form), 'IgnoreCase').Count
    if ($count -gt 0) {
      # 8 chars only: the QA record never carries key material
      $rkHits += ('{0}:{1}…x{2}' -f $log.Name, $form.Substring(0, [Math]::Min(8, $form.Length)), $count)
    }
  }
}
Check 'AC05-logs-no-secret' (($passwordHits.Count -eq 0) -and ($rkHits.Count -eq 0)) `
  ('password hits=[' + ($passwordHits -join ';') + '] rk hits=[' + ($rkHits -join ';') + ']')
Probe ('logs-scanned = ' + $logs.Count + ' ; password-literals = ' + $passwordLiterals.Count + ' ; rk-markers = ' + $markers.Count + ' ; rk-forms = ' + $rkForms.Count)
if ($markers.Count -gt 0) {
  foreach ($marker in $markers) { Remove-Item -LiteralPath $marker.FullName -Force }
  Probe ('rk-markers deleted after the grep = ' + $markers.Count)
}

# --- AC-06: no version declaration in a csproj, touches delta ---------------------------------
$csprojs = @(Get-ChildItem -LiteralPath (Join-Path $root 'backend') -Recurse -Filter *.csproj -File)
$versionAttributes = @()
$versionLiterals = @()
foreach ($csproj in $csprojs) {
  $versionAttributes += @(Select-String -LiteralPath $csproj.FullName -Pattern 'Version\s*=\s*"')
  $versionLiterals += @(Select-String -LiteralPath $csproj.FullName -Pattern 'Version=')
}
Check 'AC06-no-version-attribute' ($versionAttributes.Count -eq 0) ('Version=" attributes in ' + $csprojs.Count + ' csproj = ' + $versionAttributes.Count)
Probe ('csproj-count = ' + $csprojs.Count + ' ; Version= attributes = ' + $versionAttributes.Count + ' ; literal Version= (comments only) = ' + $versionLiterals.Count)

# Baseline captured by QA-0013 at the start of this attempt (`git status --porcelain -uall`),
# WITHOUT QA's own artefacts — every new entry has to justify itself by the allowed prefixes below.
$baselineText = @'
backend/Directory.Packages.props
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
docs/sdd/tasks/TASK-0016-kit-recuperacao.md
'@
$baseline = @($baselineText -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$allowed = @(
  '^backend/src/Squid.Api/Auth/',
  '^backend/src/Squid.Service/',
  '^backend/tests/Squid.Tests/Auth/',
  '^docs/sdd/qa/QA-0013',
  '^docs/sdd/qa/qa-check-task-0014',
  '^docs/sdd/runs/',
  '^docs/sdd/tasks/TASK-0014'
)
$statusLines = @()
$gitExit = 0
$statusLines = & git -C $root status --porcelain -uall 2>&1
$gitExit = $LASTEXITCODE
$unexpected = @()
if ($gitExit -ne 0) {
  $unexpected += 'git status failed: ' + ($statusLines -join ' ')
} else {
  foreach ($line in $statusLines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $path = $line.Substring(3).Trim().Trim('"')
    if ($baseline -contains $path) { continue }
    $hit = $false
    foreach ($pattern in $allowed) { if ($path -match $pattern) { $hit = $true; break } }
    if (-not $hit) { $unexpected += $line }
  }
}
Check 'AC06-touches-delta' ($unexpected.Count -eq 0) ('entries outside baseline and outside the touches/QA prefixes = [' + ($unexpected -join ' ; ') + ']')
Probe ('git-status-entries = ' + @($statusLines).Count + ' ; baseline = ' + $baseline.Count + ' ; unexpected = ' + $unexpected.Count)

# --- adjudications this attempt has to respect ------------------------------------------------
$allAuth = ''
Get-ChildItem -LiteralPath (Join-Path $root 'backend/src/Squid.Api/Auth') -Filter *.cs -File |
  ForEach-Object { $allAuth += Get-Content -LiteralPath $_.FullName -Raw }
Check 'ADJ-me-route-absent' ($allAuth -notmatch '/api/auth/me') 'GET /api/auth/me belongs to TASK-0016 (ODEV adjudication 2026-11-07), not here'

$wiring = ($svcHost -match 'AddSingleton<IActorEventStore>') -and ($svcHost -match 'new SqliteActorEventStore\(') `
  -and ($svcHost -match 'AddSingleton<IAuthStore>') -and ($svcHost -match 'new SqliteAuthStore\(') `
  -and ($svcHost -match 'app\.MapSquidAuth\(\)') -and ($apiCsproj -notmatch 'Squid\.Persistence')
Check 'S21-wiring-and-adr35' $wiring 'production IActorEventStore (backlog S-21) + IAuthStore registered at the composition root, Squid.Api still free of Squid.Persistence (ADR-35)'

# --- summary ----------------------------------------------------------------------------------
$total = $script:pass + $script:fail
$result = if ($script:fail -eq 0) { 'PASS' } else { 'FAIL' }
Write-Output "SUMMARY total=$total fails=$script:fail"
Write-Output "RESULT: $result"
if ($script:fail -eq 0) { exit 0 } else { exit 1 }
