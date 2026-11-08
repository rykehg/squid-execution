# qa-check-task-0016 — static, log and touches checks for TASK-0016 (QA-0015).
# Exit 0 only when every check passes: SUMMARY total=7 fails=0.
# Rules baked in: S-22 (green/red is read from the log's own exit: header and summary, never
# from the wrapper console), S-24 (Recovery/** is untracked, so source is read from the
# filesystem and git grep is never used on it), and the touches baseline of this attempt
# (git status -uall captured before QA wrote anything; only the four QA artifacts may be new).
# Pure ASCII on purpose: the PS 5.1 BOM trap of QA-0013 cannot apply.
$ErrorActionPreference = 'Continue'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $repo

$script:failures = 0
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  if ($Ok) { Write-Output "CHECK $Id PASS $Detail" }
  else { $script:failures++; Write-Output "CHECK $Id FAIL $Detail" }
}

# Baseline captured at QA start (git status --porcelain -uall), status prefixes stripped.
$baseline = @(
  'backend/Directory.Packages.props',
  'backend/src/Squid.Api/Squid.Api.csproj',
  'backend/src/Squid.Protocols/Squid.Protocols.csproj',
  'backend/src/Squid.Service/SquidServiceHost.cs',
  'docs/sdd/ideas/BACKLOG.md',
  'docs/sdd/plan/plan.md',
  'docs/sdd/requirements/REQ-0001-squid-mvp.md',
  'backend/src/Squid.Api/Auth/AuthAccount.cs',
  'backend/src/Squid.Api/Auth/AuthEndpoints.cs',
  'backend/src/Squid.Api/Auth/AuthErrors.cs',
  'backend/src/Squid.Api/Auth/AuthSessionMiddleware.cs',
  'backend/src/Squid.Api/Auth/AuthSessions.cs',
  'backend/src/Squid.Api/Auth/IAuthStore.cs',
  'backend/src/Squid.Api/Auth/Mnemonic.cs',
  'backend/src/Squid.Api/Auth/PasswordHasher.cs',
  'backend/src/Squid.Api/Crypto/AesGcmBox.cs',
  'backend/src/Squid.Api/Crypto/KeyDerivation.cs',
  'backend/src/Squid.Api/Crypto/KeyState.cs',
  'backend/src/Squid.Api/Crypto/RootKey.cs',
  'backend/src/Squid.Api/Crypto/RootKeyCipher.cs',
  'backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs',
  'backend/src/Squid.Api/Recovery/IRecoveryStore.cs',
  'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs',
  'backend/src/Squid.Api/Recovery/RecoveryWarnings.cs',
  'backend/src/Squid.Grains/Actors/ActorErrors.cs',
  'backend/src/Squid.Grains/Actors/ActorEventStream.cs',
  'backend/src/Squid.Grains/Actors/ActorEvents.cs',
  'backend/src/Squid.Grains/Actors/ActorGrain.cs',
  'backend/src/Squid.Grains/Actors/ActorHandle.cs',
  'backend/src/Squid.Grains/Actors/ActorState.cs',
  'backend/src/Squid.Grains/Actors/IActorEventStore.cs',
  'backend/src/Squid.Protocols/ActivityPub/ActorProfile.cs',
  'backend/src/Squid.Protocols/ActivityPub/Base58.cs',
  'backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs',
  'backend/src/Squid.Service/SqliteActorEventStore.cs',
  'backend/src/Squid.Service/SqliteAuthStore.cs',
  'backend/src/Squid.Service/SqliteRecoveryStore.cs',
  'backend/tests/Squid.Tests/Actors/ActorGrainTests.cs',
  'backend/tests/Squid.Tests/Actors/ActorPersistedStateTests.cs',
  'backend/tests/Squid.Tests/Actors/ActorTestData.cs',
  'backend/tests/Squid.Tests/Actors/SqliteActorEventStore.cs',
  'backend/tests/Squid.Tests/Auth/AuthHost.cs',
  'backend/tests/Squid.Tests/Auth/AuthSessionsTests.cs',
  'backend/tests/Squid.Tests/Auth/MnemonicTests.cs',
  'backend/tests/Squid.Tests/Auth/PasswordHasherTests.cs',
  'backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs',
  'backend/tests/Squid.Tests/Auth/RegisterLoginSessionTests.cs',
  'backend/tests/Squid.Tests/Crypto/KeyPrimitivesTests.cs',
  'backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs',
  'backend/tests/Squid.Tests/Discovery/WebFingerProfileTests.cs',
  'backend/tests/Squid.Tests/Recovery/RecoveryImportTests.cs',
  'backend/tests/Squid.Tests/Recovery/RecoveryKitTests.cs',
  'backend/tests/Squid.Tests/Recovery/RecoveryWarningsTests.cs',
  'docs/sdd/qa/QA-0011-actor-grain-event-sourced.md',
  'docs/sdd/qa/QA-0012-primitivas-de-chaves.md',
  'docs/sdd/qa/QA-0013-registro-login-sessao.md',
  'docs/sdd/qa/QA-0014-webfinger-perfil-jsonld.md',
  'docs/sdd/qa/qa-check-task-0012.ps1',
  'docs/sdd/qa/qa-check-task-0012.sh',
  'docs/sdd/qa/qa-check-task-0014.ps1',
  'docs/sdd/qa/qa-check-task-0014.sh',
  'docs/sdd/qa/qa-check-task-0015.ps1',
  'docs/sdd/qa/qa-check-task-0015.sh',
  'docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md',
  'docs/sdd/tasks/TASK-0012-primitivas-de-chaves.md',
  'docs/sdd/tasks/TASK-0013-actor-grain-event-sourced.md',
  'docs/sdd/tasks/TASK-0014-registro-login-sessao.md',
  'docs/sdd/tasks/TASK-0015-webfinger-perfil-jsonld.md',
  'docs/sdd/tasks/TASK-0016-kit-recuperacao.md'
)

$allowedNew = @(
  'backend/tests/Squid.Tests/Recovery/Qa0015RecoveryAcceptanceTests.cs',
  'docs/sdd/qa/QA-0015-kit-recuperacao.md',
  'docs/sdd/qa/qa-check-task-0016.ps1',
  'docs/sdd/qa/qa-check-task-0016.sh'
)

# --- GATE-NEWEST-EXIT / GATE-NEWEST-SUMMARY (S-22: the log classifies, not the console) ---
$testRuns = Join-Path $repo 'docs\sdd\runs\test-runs'
$gate = Get-ChildItem $testRuns -Filter '*-test-gate.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$gateText = if ($gate) { Get-Content -Raw -LiteralPath $gate.FullName } else { '' }
Check 'GATE-NEWEST-EXIT' ([bool]($gateText -match '(?m)^exit:\s+0\s*$')) $(if ($gate) { $gate.Name } else { 'no gate log found' })

$gateFalha = -1
$gateAprov = -1
if ($gateText -match 'Com falha:\s+(\d+)') { $gateFalha = [int]$Matches[1] }
if ($gateText -match 'Aprovado:\s+(\d+)') { $gateAprov = [int]$Matches[1] }
Check 'GATE-NEWEST-SUMMARY' (($gateFalha -eq 0) -and ($gateAprov -ge 163)) "Com falha=$gateFalha Aprovado=$gateAprov (need falha=0 and aprovado>=163)"

# --- FILTER-NEWEST-EXIT / FILTER-NEWEST-SUMMARY: the Recovery filter QA runs after restores ---
$filter = Get-ChildItem $testRuns -Filter '*-test-filter.log' -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
$filterText = if ($filter) { Get-Content -Raw -LiteralPath $filter.FullName } else { '' }
Check 'FILTER-NEWEST-EXIT' ([bool]($filterText -match '(?m)^exit:\s+0\s*$')) $(if ($filter) { $filter.Name } else { 'no filter log found' })

$filterFalha = -1
$filterAprov = -1
if ($filterText -match 'Com falha:\s+(\d+)') { $filterFalha = [int]$Matches[1] }
if ($filterText -match 'Aprovado:\s+(\d+)') { $filterAprov = [int]$Matches[1] }
Check 'FILTER-NEWEST-SUMMARY' (($filterFalha -eq 0) -and ($filterAprov -ge 11)) "Com falha=$filterFalha Aprovado=$filterAprov (need falha=0 and aprovado>=11)"

# --- TOUCHES-NO-OUTSIDE (AC-05): baseline kept, only the four QA artifacts may be new ---
$porcelain = & git status --porcelain -uall
$gitOk = ($LASTEXITCODE -eq 0)
$current = @()
if ($gitOk) {
  foreach ($line in $porcelain) { if ("$line".Length -gt 3) { $current += "$line".Substring(3) } }
}
$missing = @($baseline | Where-Object { $current -notcontains $_ })
$extras = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -notcontains $_) })
$newAllowed = @($current | Where-Object { ($baseline -notcontains $_) -and ($allowedNew -contains $_) })
Check 'TOUCHES-NO-OUTSIDE' ($gitOk -and ($missing.Count -eq 0) -and ($extras.Count -eq 0)) "git=$gitOk missing=$($missing.Count) out-of-scope=$($extras.Count) new-allowed=$($newAllowed.Count)/4"

# --- ROUTES-STATIC: direct filesystem read (S-24: git grep on untracked paths is vacuous) ---
$recovery = Get-Content -Raw 'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs'
$auth = Get-Content -Raw 'backend/src/Squid.Api/Auth/AuthEndpoints.cs'
$routesOk = $recovery.Contains('app.MapPost("/api/recovery/kit", KitAsync);') -and $recovery.Contains('app.MapPost("/api/recovery/kit/verify", VerifyAsync);') -and $recovery.Contains('app.MapPost("/api/recovery/import", ImportAsync);') -and $auth.Contains('app.MapGet("/api/auth/me", MeAsync);')
Check 'ROUTES-STATIC' $routesOk '3 recovery MapPost + GET /api/auth/me read from the filesystem'

# --- RESTORE-SHA256 (S-16): the mutated product file is byte-identical to its PRE hash ---
$restoreHash = (Get-FileHash 'backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs' -Algorithm SHA256).Hash
Check 'RESTORE-SHA256' ($restoreHash -eq '1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E') "SHA256=$restoreHash"

Write-Output "SUMMARY total=7 fails=$($script:failures)"
if ($script:failures -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'; exit 1
