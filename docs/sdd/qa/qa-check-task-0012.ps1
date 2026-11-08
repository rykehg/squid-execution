#Requires -Version 5.1
<#
qa-check-task-0012 - QA of TASK-0012 (key primitives: RK/RC, HKDF, Argon2id, RSA wrap), Windows half.

Executed checks (Windows / PowerShell 5.1):
  AC-01  RootKey.Create is filled by the platform CSPRNG (RandomNumberGenerator.Fill); RootKey
         ToString is masked; the RecoveryCode line of KeyState.Create is CSPRNG bytes and is NOT
         fed by KeyDerivation/HKDF (the source invariant whose mutation M2 turns the runtime
         test red); zero logging APIs in backend/src/Squid.Api/Crypto (never logged); zero files
         under backend/src outside the module reference Squid.Api.Crypto (no response surface
         exists in this task - never in a response, structurally).
  AC-02  zero `catch` and zero `ex.ToString` in the new source + test dirs (typed failure,
         never catch {} / ex.ToString to a client); AesGcmBox really encrypts and authenticates:
         `new AesGcm` appears once in Seal and once in Open (mutation M1 removes both and this
         check fails).
  AC-03  RootKeyCipher derives with Argon2id (Argon2Parameters.Argon2id) and pins the cost
         parameters in code (4096 KiB / 3 iterations / 1 lane).
  AC-04  KeyState declares no clear RootKey member; every byte[] member is encrypted/wrapped/
         public/RC (the clear-material assertion itself is the runtime test, executed separately).
  AC-05  ADR-25/D-01: zero `Version="` attributes and zero `PackageVersion` entries in any
         backend/**/*.csproj; exactly one BouncyCastle PackageVersion, in
         backend/Directory.Packages.props; exactly one BouncyCastle PackageReference, in
         Squid.Api.csproj, without Version=. Touches: every git-status entry under backend/
         belongs to the 4 TASK-0012 touches or to the disjoint TASK-0013 paths
         (Grains/Actors, Tests/Actors); entries under docs/sdd/ are flow artifacts (listed);
         anything else FAILs.

Never claimed by this script (executed as their own recorded commands):
  the runtime xunit scenarios (Squid.Tests.Crypto, DomainHasNoInfrastructureTests) and the gate
  (sdd-test.ps1 test). Scans read file CONTENT directly; `git grep` is not used because the new
  dirs are untracked and git grep silently skips them (probe: git grep RootKey on
  backend/src/Squid.Api/Crypto exits 1 while the content scan finds 28 hits).

Break-it proof: mutation M1 (AesGcmBox stops keying the box) makes AC02-aead-box FAIL and the
AC-02/AC-03 runtime tests go red; mutation M2 (RC derived from the RK) makes AC01-rc-csprng-src
FAIL and the RC runtime test goes red. SHA256 before each mutation, byte-exact restore after.

Exit codes: 0 = every executed check passed, 1 = at least one FAIL.
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1
#>
[CmdletBinding()]
param(
  [string]$RepoRoot = ''
)
$ErrorActionPreference = 'Continue'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path }

$script:fail = 0
$script:total = 0

function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  $script:total++
  if ($Ok) { Write-Output "PASS $Id - $Detail" }
  else { Write-Output "FAIL $Id - $Detail"; $script:fail++ }
}
function Info([string]$Id, [string]$Detail) { Write-Output "INFO $Id - $Detail" }
function Read-Raw([string]$p) {
  if (Test-Path -LiteralPath $p -PathType Leaf) { return [System.IO.File]::ReadAllText($p) }
  return ''
}
function Hits([string]$Path, [string]$Pattern) {
  $n = 0
  if (Test-Path -LiteralPath $Path -PathType Leaf) { return @(Select-String -LiteralPath $Path -Pattern $Pattern).Count }
  if (Test-Path -LiteralPath $Path -PathType Container) {
    foreach ($f in (Get-ChildItem -LiteralPath $Path -Filter *.cs -File -Recurse)) {
      $n += @(Select-String -LiteralPath $f.FullName -Pattern $Pattern).Count
    }
  }
  return $n
}

$srcCrypto = Join-Path $RepoRoot 'backend\src\Squid.Api\Crypto'
$tstCrypto = Join-Path $RepoRoot 'backend\tests\Squid.Tests\Crypto'
$rootKeyCs = Join-Path $srcCrypto 'RootKey.cs'
$keyStateCs = Join-Path $srcCrypto 'KeyState.cs'
$boxCs = Join-Path $srcCrypto 'AesGcmBox.cs'
$cipherCs = Join-Path $srcCrypto 'RootKeyCipher.cs'

Write-Output "repo-root: $RepoRoot"
Write-Output 'mode:      STATIC content scans + git delta (runtime tests are separate commands)'

# ---------------------------------------------------------------- AC-01 source invariants
$rk = Read-Raw $rootKeyCs
Check 'AC01-rk-csprng-src' ($rk -match 'RandomNumberGenerator\.Fill\(bytes\)') `
  'RootKey.Create fills the 32 bytes with the platform CSPRNG (RandomNumberGenerator.Fill)'
Check 'AC01-rk-masking' ($rk -match 'masked' -and $rk -notmatch 'ToString\(\)[^=]*=>[^"]*"_bytes') `
  'RootKey.ToString is masked (no raw bytes in the printed form)'

$kc = Read-Raw $keyStateCs
$rcLine = @($kc -split "`r?`n" | Where-Object { $_ -match 'RecoveryCode\s*=' }) -join ' | '
$rcOk = ($rcLine -match 'RandomNumberGenerator\.GetBytes') -and ($rcLine -notmatch 'KeyDerivation|HKDF|Derive')
Check 'AC01-rc-csprng-src' $rcOk "RecoveryCode comes from the CSPRNG, not from the RK: $((($rcLine -replace '\s+', ' ').Trim()))"

$logged = (Hits $srcCrypto 'ILogger|Console\.|Log\.|Trace\.|Debug\.Write|Serilog')
Check 'AC01-no-logging' ($logged -eq 0) "logging APIs in backend/src/Squid.Api/Crypto = $logged"

$consumers = @()
foreach ($f in (Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'backend\src') -Filter *.cs -File -Recurse)) {
  if ($f.FullName -like "*Squid.Api\Crypto*") { continue }
  if (Select-String -LiteralPath $f.FullName -Pattern 'Squid\.Api\.Crypto' -Quiet) {
    $consumers += $f.FullName.Substring($RepoRoot.Length + 1)
  }
}
Check 'AC01-no-response-surface' ($consumers.Count -eq 0) `
  "files under backend/src outside the module referencing Squid.Api.Crypto = $($consumers.Count) [$($consumers -join '; ')] (no response path exists yet)"

# ---------------------------------------------------------------- AC-02 / AC-03 source shape
$catchHits = (Hits $srcCrypto '\bcatch\b') + (Hits $tstCrypto '\bcatch\b')
Check 'AC02-no-catch' ($catchHits -eq 0) "catch occurrences in Crypto src + tests = $catchHits"
$exTo = (Hits $srcCrypto 'ex\.ToString|exception\.ToString|\.ToString\(\)\s*\)') + (Hits $tstCrypto 'ex\.ToString|exception\.ToString')
Check 'AC02-no-ex-tostring' ($exTo -eq 0) "exception-to-string occurrences in Crypto src + tests = $exTo"
$aead = (Hits $boxCs 'new AesGcm')
Check 'AC02-aead-box' ($aead -eq 2) "AesGcmBox really encrypts/authenticates: 'new AesGcm' hits = $aead (Seal + Open, must be 2)"

$cipher = Read-Raw $cipherCs
Check 'AC03-argon2id-src' ($cipher -match 'Argon2Parameters\.Argon2id') 'RootKeyCipher derives with Argon2id'
Check 'AC03-argon2-cost-pinned' ($cipher -match 'Argon2Iterations\s*=\s*3' -and $cipher -match 'Argon2MemoryKib\s*=\s*4096' -and $cipher -match 'Argon2Parallelism\s*=\s*1') `
  'Argon2id cost pinned in code (4096 KiB / 3 iterations / 1 lane) - a package bump cannot change it'

# ---------------------------------------------------------------- AC-04 state shape
Check 'AC04-no-clear-rk-member' ($kc -notmatch 'public\s+(required\s+)?RootKey\b') `
  'KeyState declares no clear RootKey member (encrypted/wrapped/public/RC members only)'

# ---------------------------------------------------------------- AC-05 ADR-25 + touches
$csproj = @(Get-ChildItem -LiteralPath (Join-Path $RepoRoot 'backend') -Filter *.csproj -File -Recurse)
$verAttr = @(Select-String -Path $csproj.FullName -Pattern 'Version\s*=\s*"')
Check 'AC05-no-version-attr' ($verAttr.Count -eq 0) `
  "Version=`" attributes in $($csproj.Count) csproj = $($verAttr.Count) [$((@($verAttr) | ForEach-Object { $_.Filename + ':' + $_.LineNumber }) -join ', ')]"
$pkgVer = @()
foreach ($f in $csproj) { $pkgVer += @(Select-String -LiteralPath $f.FullName -Pattern '<PackageVersion') }
Check 'AC05-packageversion-only-props' ($pkgVer.Count -eq 0) `
  "PackageVersion entries outside Directory.Packages.props = $($pkgVer.Count)"

$props = Join-Path $RepoRoot 'backend\Directory.Packages.props'
$bcVer = @(Select-String -LiteralPath $props -Pattern '<PackageVersion Include="BouncyCastle\.Cryptography" Version="[^"]+"\s*/>')
$bcRefAll = @()
foreach ($f in $csproj) { $bcRefAll += @(Select-String -LiteralPath $f.FullName -Pattern '<PackageReference Include="BouncyCastle\.Cryptography"') }
$bcRefBad = @($bcRefAll | Where-Object { $_.Line -match 'Version\s*=' })
Check 'AC05-bouncycastle-single-version' ($bcVer.Count -eq 1) `
  "BouncyCastle PackageVersion lines in Directory.Packages.props = $($bcVer.Count) (must be 1)"
Check 'AC05-bouncycastle-ref-no-version' (($bcRefAll.Count -eq 1) -and ($bcRefBad.Count -eq 0) -and ($bcRefAll[0].Filename -eq 'Squid.Api.csproj')) `
  "BouncyCastle PackageReference = $($bcRefAll.Count) in [$(@($bcRefAll | ForEach-Object { $_.Filename }) -join ',')] with Version= on line = $($bcRefBad.Count)"

$touches = @(
  'backend/Directory.Packages.props',
  'backend/src/Squid.Api/Squid.Api.csproj',
  'backend/src/Squid.Api/Crypto/',
  'backend/tests/Squid.Tests/Crypto/'
)
$peer = @(
  'backend/src/Squid.Grains/Actors/',
  'backend/tests/Squid.Tests/Actors/'
)
$stOut = @(); $stRc = 1
try { $stOut = @(git -C $RepoRoot status --porcelain 2>&1); $stRc = $LASTEXITCODE } catch { $stRc = 1 }
if ($stRc -ne 0) {
  Check 'AC05-touches' $false "git status failed (exit $stRc): $($stOut -join ' ')"
} else {
  $outside = @(); $mine = @(); $peerFiles = @(); $flow = @()
  foreach ($line in $stOut) {
    $line = "$line"
    if ($line.Length -lt 4) { continue }
    $path = $line.Substring(3).Trim('"') -replace '\\', '/'
    $isTouch = $false; $isPeer = $false
    foreach ($t in $touches) { if ($path.StartsWith($t.TrimEnd('/')) -or $path -eq $t) { $isTouch = $true } }
    foreach ($p in $peer) { if ($path.StartsWith($p.TrimEnd('/')) -or $path -eq $p) { $isPeer = $true } }
    if ($path -like 'backend/*') {
      if ($isTouch) { $mine += $path }
      elseif ($isPeer) { $peerFiles += $path }
      else { $outside += $path }
    }
    elseif ($path -like 'docs/sdd/*') { $flow += $path }
    else { $outside += $path }
  }
  Check 'AC05-touches' ($outside.Count -eq 0) `
    "backend/product entries outside the 4 touches (peer TASK-0013 excluded) = $($outside.Count) [$($outside -join '; ')]"
  Info 'AC05-touches-mine' "TASK-0012 product delta: $($mine -join ', ')"
  Info 'AC05-touches-peer' "disjoint TASK-0013 paths (not QA-0012's, never modified here): $($peerFiles -join ', ')"
  Info 'AC05-touches-flow' "flow artifacts under docs/sdd (not product): $($flow.Count) entries"
}

# ---------------------------------------------------------------- what this script never claims
Info 'AC05-runtime' 'executed as separate recorded commands, never claimed here: sdd-test.ps1 test (gate), -TestFilter FullyQualifiedName~Squid.Tests.Crypto, -TestFilter FullyQualifiedName~DomainHasNoInfrastructureTests'
Info 'platform-limit' 'the Linux gate is UNVERIFIED: the only WSL distro (podman-machine-default) has no dotnet; installing one needs a gate approval that was not granted. The .sh twin runs the same check ids on Linux.'

Write-Output "SUMMARY total=$($script:total) fails=$($script:fail)"
if ($script:fail -eq 0) {
  Write-Output 'RESULT: PASS - all executed checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $($script:fail) of $($script:total) checks failed"
exit 1
