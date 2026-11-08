#Requires -Version 5.1
<#
qa-check-task-0015 — static, log and touches checks of TASK-0015 (QA-0014, AC-01..AC-05).

Every classification comes from the log file under docs/sdd/runs/test-runs/ (rule S-22),
never from the runner console. The touches check compares git status against the baseline
captured when QA-0014 started (embedded below); new entries are allowed only for QA-0014's
own artifacts. The script is pure ASCII on purpose: Windows PowerShell 5.1 reads a UTF-8
file without BOM as cp1252 (QA-0013 finding 1), so no accented literal can be matched here.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1

Exit: 0 = SUMMARY total=N fails=0 · 1 = at least one FAIL.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$runs = Join-Path $repo 'docs\sdd\runs\test-runs'
$src = Join-Path $repo 'backend\src'
$discovery = Join-Path $src 'Squid.Api\Discovery'
$qaTest = Join-Path $repo 'backend\tests\Squid.Tests\Discovery\Qa0014WebfingerAcceptanceTests.cs'

$total = 0
$fails = 0

function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  $script:total++
  if (-not $Ok) { $script:fails++ }
  $status = if ($Ok) { 'PASS' } else { 'FAIL' }
  Write-Output ('CHECK {0} {1} {2}' -f $Id, $status, $Detail)
}

function ReadText([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
  [System.IO.File]::ReadAllText($Path)
}

function MatchCount([string]$Text, [string]$Pattern) {
  if ($null -eq $Text) { return -1 }
  ([regex]::Matches($Text, $Pattern)).Count
}

function NewestLog([string]$Filter) {
  Get-ChildItem -LiteralPath $runs -Filter $Filter -File -ErrorAction SilentlyContinue |
    Sort-Object Name |
    Select-Object -Last 1
}

function LogVerdict([string]$Filter, [int]$MinApproved, [string]$CheckId) {
  $log = NewestLog $Filter
  if ($null -eq $log) {
    Check $CheckId $false ("no {0} log under docs/sdd/runs/test-runs" -f $Filter)
    return
  }
  $text = [System.IO.File]::ReadAllText($log.FullName)
  $exit = [regex]::Match($text, '(?m)^exit:\s+(\d+)').Groups[1].Value
  $failed = [regex]::Match($text, 'Com falha:\s+(\d+)').Groups[1].Value
  $approved = [regex]::Match($text, 'Aprovado:\s+(\d+)').Groups[1].Value
  $approvedInt = 0
  [void][int]::TryParse($approved, [ref]$approvedInt)
  $ok = ($exit -eq '0') -and ($failed -eq '0') -and ($approvedInt -ge $MinApproved)
  Check $CheckId $ok ("{0} exit={1} Com falha={2} Aprovado={3} (S-22: read inside the log)" -f $log.Name, $exit, $failed, $approved)
}

# --- AC-01: one origin, from the request (SPEC-0007 B-09a), no config key ---------------
$endpoints = ReadText (Join-Path $discovery 'DiscoveryEndpoints.cs')
$originBits = @(
  'context.Request.Scheme}://{context.Request.Host.Value',
  'string.Equals(domain, context.Request.Host.Value',
  'subject = $"acct:{name}@{domain}"',
  'href = $"{origin}/users/{name}"',
  'profileJsonLd(state, Origin(context))'
)
$originMissing = @()
foreach ($bit in $originBits) {
  if (($null -eq $endpoints) -or (-not $endpoints.Contains($bit))) { $originMissing += $bit }
}
Check 'AC01-single-origin-code' ($originMissing.Count -eq 0) `
  ("origin=scheme+Host, subject/link/id derivation lines present; missing={0}" -f $originMissing.Count)

$cfgHits = @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
  Select-String -Pattern 'Configuration\[')
$appsettings = @(Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue).Count
$discCfg = @(Get-ChildItem -LiteralPath $discovery -Filter '*.cs' -ErrorAction SilentlyContinue |
  Select-String -Pattern 'IConfiguration|Configuration\[|GetEnvironmentVariable').Count
$protoCfg = @(Get-ChildItem -LiteralPath (Join-Path $src 'Squid.Protocols') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
  Select-String -Pattern 'IConfiguration|Configuration\[|GetEnvironmentVariable').Count
$cfgOk = ($cfgHits.Count -eq 1) -and ($cfgHits[0].Line -match 'Storage:Path') -and ($appsettings -eq 0) -and ($discCfg -eq 0) -and ($protoCfg -eq 0)
Check 'AC01-no-domain-config' $cfgOk `
  ("Configuration[ hits={0} (only Storage:Path), appsettings={1}, Discovery/Protocols config reads={2}+{3}" -f $cfgHits.Count, $appsettings, $discCfg, $protoCfg)

# --- AC-02: the three 404s, foreign domain first among them (B-09a, no proxying) --------
$foreignRule = $false
if ($null -ne $endpoints) {
  $foreignRule = [regex]::IsMatch($endpoints, 'if \(!string\.Equals\(domain, context\.Request\.Host\.Value[\s\S]{0,240}?Results\.NotFound\(\)')
}
Check 'AC02-foreign-404-rule' $foreignRule `
  'foreign-domain guard compares resource domain to Request.Host and answers NotFound (mutation target)'

$refusals = MatchCount $endpoints 'Results\.NotFound\(\)'
Check 'AC02-refusal-count' ($refusals -ge 6) ("Results.NotFound() occurrences in DiscoveryEndpoints.cs = {0} (>= 6)" -f $refusals)

# --- AC-03: no forwarded headers, no auth dependency, S-25 two views in QA's scenario ----
$fwd = @(Get-ChildItem -LiteralPath $src -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
  Select-String -Pattern 'ForwardedHeaders|X-Forwarded')
Check 'AC03-no-forwarded-read' ($fwd.Count -eq 0) `
  ("ForwardedHeaders/X-Forwarded occurrences in backend/src = {0} (parked I-11)" -f $fwd.Count)

$middleware = ReadText (Join-Path $src 'Squid.Api\Auth\AuthSessionMiddleware.cs')
$discoveryAuthRefs = MatchCount $endpoints 'AuthSessions|ResolvedKey|Csrf|Session'
$mwGatesMutations = ($null -ne $middleware) -and $middleware.Contains('IsMutation(') -and $middleware.Contains('Status403Forbidden')
$mwHas401 = ($null -ne $middleware) -and $middleware.Contains('Status401')
Check 'AC03-no-auth-on-discovery' (($discoveryAuthRefs -eq 0) -and $mwGatesMutations -and (-not $mwHas401)) `
  ("discovery auth refs={0}; middleware gates mutations={1}; middleware 401={2} (GET must pass)" -f $discoveryAuthRefs, $mwGatesMutations, $mwHas401)

$qaText = ReadText $qaTest
$s25 = ($null -ne $qaText) -and $qaText.Contains('raw.Replace("\\u002B"') -and $qaText.Contains('StringValues(document)') `
  -and $qaText.Contains('store.ReadAsync') -and (-not $qaText.Contains('Regex'))
Check 'AC03-s25-two-views' $s25 `
  'QA scenario scans the response raw AND decoded, reads the row typed, no raw-regex row scan'

# --- AC-04: round-trip wiring (composition root + mapper + canonical record) ------------
$compositionRoot = ReadText (Join-Path $src 'Squid.Service\SquidServiceHost.cs')
$mapper = ReadText (Join-Path $src 'Squid.Protocols\ActivityPub\ProfileJsonLd.cs')
$profileDoc = ReadText (Join-Path $src 'Squid.Protocols\ActivityPub\ActorProfile.cs')
$wired = ($null -ne $compositionRoot) -and $compositionRoot.Contains('app.MapSquidDiscovery(ProfileJsonLd.Serialize)')
$mapperOk = ($null -ne $mapper) -and $mapper.Contains('public static string Serialize(ActorState state, string origin)') `
  -and $mapper.Contains('public static ActorProfile Materialize(string json)')
$recordBits = @('public sealed record ActorProfile(', 'string Origin,', 'string PreferredUsername,', 'string? Name,', 'string RsaPublicKeyBase64,', 'string Ed25519PublicKeyBase64')
$recordMissing = 0
if ($null -eq $profileDoc) { $recordMissing = $recordBits.Count }
else { foreach ($bit in $recordBits) { if (-not $profileDoc.Contains($bit)) { $recordMissing++ } } }
Check 'AC04-roundtrip-wired' ($wired -and $mapperOk -and ($recordMissing -eq 0)) `
  ("composition-root wired={0}; Serialize/Materialize public={1}; canonical record members missing={2}" -f $wired, $mapperOk, $recordMissing)

# --- AC-05: gate green, Discovery filter green, touches, no private material in logs ----
LogVerdict '*-test-gate.log' 1 'AC05-gate-log-green'
LogVerdict '*-test-filter.log' 9 'AC05-filter-log-green'

# Baseline of git status --porcelain -uall captured when QA-0014 started (58 entries).
$baseline = @(
  'M backend/Directory.Packages.props',
  'M backend/src/Squid.Api/Squid.Api.csproj',
  'M backend/src/Squid.Protocols/Squid.Protocols.csproj',
  'M backend/src/Squid.Service/SquidServiceHost.cs',
  'M docs/sdd/ideas/BACKLOG.md',
  'M docs/sdd/plan/plan.md',
  'M docs/sdd/requirements/REQ-0001-squid-mvp.md',
  '?? backend/src/Squid.Api/Auth/AuthAccount.cs',
  '?? backend/src/Squid.Api/Auth/AuthEndpoints.cs',
  '?? backend/src/Squid.Api/Auth/AuthErrors.cs',
  '?? backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs',
  '?? backend/src/Squid.Api/Auth/AuthSessions.cs',
  '?? backend/src/Squid.Api/Auth/IAuthStore.cs',
  '?? backend/src/Squid.Api/Auth/Mnemonic.cs',
  '?? backend/src/Squid.Api/Auth/PasswordHasher.cs',
  '?? backend/src/Squid.Api/Crypto/AesGcmBox.cs',
  '?? backend/src/Squid.Api/Crypto/KeyDerivation.cs',
  '?? backend/src/Squid.Api/Crypto/KeyState.cs',
  '?? backend/src/Squid.Api/Crypto/RootKey.cs',
  '?? backend/src/Squid.Api/Crypto/RootKeyCipher.cs',
  '?? backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs',
  '?? backend/src/Squid.Grains/Actors/ActorErrors.cs',
  '?? backend/src/Squid.Grains/Actors/ActorEventStream.cs',
  '?? backend/src/Squid.Grains/Actors/ActorEvents.cs',
  '?? backend/src/Squid.Grains/Actors/ActorGrain.cs',
  '?? backend/src/Squid.Grains/Actors/ActorHandle.cs',
  '?? backend/src/Squid.Grains/Actors/ActorState.cs',
  '?? backend/src/Squid.Grains/Actors/IActorEventStore.cs',
  '?? backend/src/Squid.Protocols/ActivityPub/ActorProfile.cs',
  '?? backend/src/Squid.Protocols/ActivityPub/Base58.cs',
  '?? backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs',
  '?? backend/src/Squid.Service/SqliteActorEventStore.cs',
  '?? backend/src/Squid.Service/SqliteAuthStore.cs',
  '?? backend/tests/Squid.Tests/Actors/ActorGrainTests.cs',
  '?? backend/tests/Squid.Tests/Actors/ActorPersistedStateTests.cs',
  '?? backend/tests/Squid.Tests/Actors/ActorTestData.cs',
  '?? backend/tests/Squid.Tests/Actors/SqliteActorEventStore.cs',
  '?? backend/tests/Squid.Tests/Auth/AuthHost.cs',
  '?? backend/tests/Squid.Tests/Auth/AuthSessionsTests.cs',
  '?? backend/tests/Squid.Tests/Auth/MnemonicTests.cs',
  '?? backend/tests/Squid.Tests/Auth/PasswordHasherTests.cs',
  '?? backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs',
  '?? backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs',
  '?? backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs',
  '?? backend/tests/Squid.Tests/Discovery/WebFingerProfileTests.cs',
  '?? docs/sdd/qa/QA-0011-actor-grain-event-sourced.md',
  '?? docs/sdd/qa/QA-0012-primitivas-de-chaves.md',
  '?? docs/sdd/qa/QA-0013-registro-login-sessao.md',
  '?? docs/sdd/qa/qa-check-task-0012.ps1',
  '?? docs/sdd/qa/qa-check-task-0012.sh',
  '?? docs/sdd/qa/qa-check-task-0014.ps1',
  '?? docs/sdd/qa/qa-check-task-0014.sh',
  '?? docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md',
  '?? docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md',
  '?? docs/sdd/tasks/TASK-0013-actor-grain-event-sourced.md',
  '?? docs/sdd/tasks/TASK-0014-registro-login-sessao.md',
  '?? docs/sdd/tasks/TASK-0015-webfinger-perfil-jsonld.md',
  '?? docs/sdd/tasks/TASK-0016-kit-recuperacao.md'
)
$allowed = @(
  'docs/sdd/qa/QA-0014',
  'docs/sdd/qa/qa-check-task-0015',
  'backend/tests/Squid.Tests/Discovery/Qa0014'
)

$gitOk = $false
$gitDetail = 'git status not collected'
Push-Location $repo
try {
  $raw = @(git status --porcelain -uall 2>&1)
  $gitExit = $LASTEXITCODE
}
finally {
  Pop-Location
}
if ($gitExit -ne 0) {
  $gitDetail = "git status --porcelain -uall exit=$gitExit"
}
else {
  $current = @()
  foreach ($line in $raw) {
    $text = "$line"
    if ($text.Length -lt 4) { continue }
    # porcelain v1: two status characters + one space, then the path.
    $status = $text.Substring(0, 2).Trim()
    $current += ('{0} {1}' -f $status, $text.Substring(3))
  }
  $missing = @($baseline | Where-Object { $current -notcontains $_ })
  $extras = @($current | Where-Object { $baseline -notcontains $_ })
  $outOfScope = @()
  foreach ($extra in $extras) {
    $space = $extra.IndexOf(' ')
    $extraPath = if ($space -ge 0) { $extra.Substring($space + 1) } else { $extra }
    $allowedHit = $false
    foreach ($prefix in $allowed) { if ($extraPath.StartsWith($prefix)) { $allowedHit = $true } }
    if (-not $allowedHit) { $outOfScope += $extra }
  }
  $gitOk = ($missing.Count -eq 0) -and ($outOfScope.Count -eq 0)
  $gitDetail = "baseline=58 current={0} missing={1} extras-allowed={2} out-of-scope={3}" -f $current.Count, $missing.Count, ($extras.Count - $outOfScope.Count), $outOfScope.Count
  foreach ($entry in $missing) { Write-Output ("TOUCH-MISSING {0}" -f $entry) }
  foreach ($entry in $outOfScope) { Write-Output ("TOUCH-OUT-OF-SCOPE {0}" -f $entry) }
}
Check 'AC05-touches' $gitOk $gitDetail

$privateHits = 0
$logsScanned = 0
foreach ($pattern in @('*-test-gate.log', '*-test-filter.log')) {
  $log = NewestLog $pattern
  if ($null -ne $log) {
    $logsScanned++
    $text = [System.IO.File]::ReadAllText($log.FullName)
    $privateHits += [regex]::Matches($text, 'BEGIN (RSA )?PRIVATE KEY|PRIVATE KEY-----').Count
  }
}
Check 'LOG-no-private-material' (($privateHits -eq 0) -and ($logsScanned -eq 2)) `
  ("logs scanned={0}; private-key PEM markers found={1}" -f $logsScanned, $privateHits)

Write-Output ('SUMMARY total={0} fails={1}' -f $total, $fails)
if ($fails -eq 0) {
  Write-Output 'RESULT: PASS'
  exit 0
}
Write-Output 'RESULT: FAIL'
exit 1
