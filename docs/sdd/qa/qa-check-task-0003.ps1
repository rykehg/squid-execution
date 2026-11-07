#Requires -Version 5.1
<#
qa-check-task-0003 - independent proof of AC-06 (store name never a literal), of ADR-25
(no Version= on any PackageReference) and of the AC-07 leftover rule for TASK-0003.

DEV shipped StoreNameLiteralTests as a self-check; a suite that proves itself can be green for
the wrong reason. This script derives every expectation from what is on disk right now:

  1. AC-06 positive : grep the real backend/src/Squid.Persistence sources -> hits must be 0.
  2. AC-06 negative : run the SAME scanner against a fixture folder that does contain a store
                      literal -> it must find it. A scanner that cannot fail proves nothing.
  3. ADR-25         : every <PackageReference> in every backend .csproj must carry no Version=,
                      and the pinned versions are printed from Directory.Packages.props so the
                      reader compares them against the artifact, not against memory.
  4. GAP-04 static  : the UPDATE and the DELETE of SqliteGrainStorage.cs must compare
                      `Version = @Version` (the exact-comparison half of the gap assumption).
  5. AC-07 leftovers: no squid-grainstate-* file may survive in %TEMP%.

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0003.ps1

Exit: 0 every check behaved as specified ; 1 at least one check failed ; 3 environment.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$ScriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot      = (Resolve-Path -LiteralPath (Join-Path (Join-Path $ScriptDir '..') '..\..')).Path
$Persistence   = Join-Path $RepoRoot 'backend\src\Squid.Persistence'
$StorageSrc    = Join-Path $Persistence 'SqliteGrainStorage.cs'
$BackendDir    = Join-Path $RepoRoot 'backend'
$PackagesProps = Join-Path $BackendDir 'Directory.Packages.props'

# Same shape as StoreNameLiteralTests: word boundary, case insensitive.
$StorePattern  = '\b(?:actors?|containers?)\b'

$failures = @()

function Get-StoreNameHits {
    param([string]$Root)
    $hits = @()
    foreach ($file in (Get-ChildItem -LiteralPath $Root -Recurse -File -Filter *.cs -ErrorAction Stop)) {
        $relative = $file.FullName.Substring($Root.Length).TrimStart('\')
        if ($relative -match '\\(bin|obj)\\') { continue }
        $lineNumber = 0
        foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
            $lineNumber++
            if ($line -imatch $StorePattern) { $hits += ('{0}:{1}: {2}' -f $relative, $lineNumber, $line.Trim()) }
        }
    }
    return ,$hits
}

function Fail {
    param([string]$Check, [string]$Detail)
    $script:failures += ('{0} :: {1}' -f $Check, $Detail)
    Write-Output ('FAIL  {0} - {1}' -f $Check, $Detail)
}

function Pass {
    param([string]$Check, [string]$Detail)
    Write-Output ('ok    {0} - {1}' -f $Check, $Detail)
}

try {
    Write-Output ('repo      : ' + $RepoRoot)
    Write-Output ('dotnet    : ' + (& dotnet --version))
    Write-Output ('')

    # ---------------------------------------------------------------- 1. AC-06 positive
    if (-not (Test-Path -LiteralPath $Persistence)) { throw "missing $Persistence" }
    $hits = Get-StoreNameHits -Root $Persistence
    if ($hits.Count -eq 0) {
        Pass 'AC-06 positive' ('0 store-name literal in ' + $Persistence)
    }
    else {
        Fail 'AC-06 positive' ($hits.Count + " literal(s): " + ($hits -join ' | '))
    }

    # ---------------------------------------------------------------- 2. AC-06 negative probe
    $fixture = Join-Path $env:TEMP ('squid-qa0003-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Force -Path $fixture | Out-Null
        $fixtureFile = Join-Path $fixture 'Injected.cs'
        [System.IO.File]::WriteAllText($fixtureFile,
            "namespace Fixture;`r`npublic static class Injected { public const string Store = `"actors`"; }`r`n",
            (New-Object System.Text.UTF8Encoding($false)))
        $fixtureHits = Get-StoreNameHits -Root $fixture
        if ($fixtureHits.Count -ge 1) {
            Pass 'AC-06 can-fail' ('the same scanner found the injected literal: ' + $fixtureHits[0])
        }
        else {
            Fail 'AC-06 can-fail' 'the scanner found nothing in a folder that contains a store literal'
        }
    }
    finally {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
    }

    # ---------------------------------------------------------------- 3. ADR-25
    $csprojs = Get-ChildItem -LiteralPath $BackendDir -Recurse -File -Filter *.csproj |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    $versionAttrs = @()
    foreach ($proj in $csprojs) {
        foreach ($line in [System.IO.File]::ReadAllLines($proj.FullName)) {
            if ($line -match '<PackageReference' -and $line -imatch '\sVersion\s*=') {
                $versionAttrs += ($proj.Name + ': ' + $line.Trim())
            }
        }
    }
    if ($versionAttrs.Count -eq 0) {
        Pass 'ADR-25' ($csprojs.Count.ToString() + ' csproj scanned, 0 PackageReference carries Version=')
    }
    else {
        Fail 'ADR-25' ($versionAttrs -join ' | ')
    }

    $pinned = @([regex]::Matches([System.IO.File]::ReadAllText($PackagesProps),
        '<PackageVersion\s+Include="([^"]+)"\s+Version="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value + ' ' + $_.Groups[2].Value })
    Write-Output ('      pinned in Directory.Packages.props: ' + ($pinned -join ', '))
    foreach ($wanted in 'Microsoft.Data.Sqlite 10.0.14', 'Microsoft.Orleans.Core 10.2.2') {
        if ($pinned -contains $wanted) { Pass 'ADR-25 pinned' $wanted }
        else { Fail 'ADR-25 pinned' ('expected ' + $wanted + ', artifact has: ' + ($pinned -join ', ')) }
    }

    # ---------------------------------------------------------------- 4. GAP-04 static
    $storageText = [System.IO.File]::ReadAllText($StorageSrc)
    foreach ($statement in 'UPDATE GrainState', 'DELETE FROM GrainState') {
        $start = $storageText.IndexOf($statement)
        if ($start -lt 0) { Fail 'GAP-04 SQL' ('statement not found: ' + $statement); continue }
        $window = $storageText.Substring($start, [math]::Min(400, $storageText.Length - $start))
        if ($window -match 'Version\s*=\s*@Version') {
            Pass 'GAP-04 SQL' ($statement + ' compares Version = @Version')
        }
        else {
            Fail 'GAP-04 SQL' ($statement + ' has no Version = @Version comparison')
        }
    }

    # ---------------------------------------------------------------- 5. AC-07 leftovers
    $leftovers = @(Get-ChildItem -Path $env:TEMP -Filter 'squid-grainstate-*' -File -ErrorAction SilentlyContinue)
    if ($leftovers.Count -eq 0) { Pass 'AC-07 leftovers' '0 squid-grainstate-* file in %TEMP%' }
    else {
        Fail 'AC-07 leftovers' ($leftovers.Count.ToString() + ' left behind: ' +
            (($leftovers | ForEach-Object { $_.Name }) -join ', '))
        foreach ($f in $leftovers) { Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue }
    }
}
catch {
    Write-Output ('environment: ' + $_.Exception.Message)
    Write-Output 'exit: 3'
    exit 3
}

Write-Output ''
if ($failures.Count -eq 0) {
    Write-Output 'summary: all checks behaved as specified - exit 0'
    exit 0
}
Write-Output ('summary: ' + $failures.Count + ' check(s) failed - exit 1')
foreach ($f in $failures) { Write-Output ('  ' + $f) }
exit 1
