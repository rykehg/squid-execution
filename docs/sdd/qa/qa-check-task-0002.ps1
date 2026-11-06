#Requires -Version 5.1
<#
qa-check-task-0002 - independent proof of AC-02 / AC-03 / AC-04 for TASK-0002.

DEV shipped Squid.Tests.Architecture.DependencyRuleTests as a self-check, but a suite that
proves itself can be green for the wrong reason. This script derives the truth from the backend
as it is on disk and asserts against that:

  1. reads the dependency-rule schema out of backend/Directory.Build.targets (the layer of each
     project and the names each layer may not reference);
  2. probes the graph (A1) and every edge (A2: 10 x 10 minus own project, injected one at a
     time) in a copy of backend/ under $env:TEMP - never in the repository;
  3. for each probe compares what the BUILD did with what the SCHEMA says:
       schema allows -> the build must succeed             (anything else : false red)
       schema bans   -> the build must fail with SQUID0001  (anything else : hole)
  4. evasion probes B1..B5, including the AC-02 claim: does an Orleans PackageReference on
     Squid.Domain survive the build unaided?
  5. cost probe (C) and clean-up of every temp folder in the same run (D).

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0002.ps1 [-Keep]

Exit: 0 every probe behaved as the schema says ; 1 at least one hole or false red ; 3 environment.

shortcut: the schema is read by regex from Directory.Build.targets instead of being evaluated by
MSBuild. If the rule file is refactored away from the
`<SquidLayer Condition="'$(MSBuildProjectName)' == 'Squid.X'">layer</SquidLayer>` and the
`<SquidForbidden Include="Y" ...>` shape, this reader must grow with it, otherwise probes stop
being classified and would be reported as probes, not as proof.
#>
[CmdletBinding()]
param(
  [switch]$Keep,
  [int]$TimeoutSeconds = 240
)

$ErrorActionPreference = 'Stop'

$ScriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot      = (Resolve-Path -LiteralPath (Join-Path (Join-Path $ScriptDir '..') '..\..')).Path
$BackendSource = Join-Path $RepoRoot 'backend'
$RuleFile      = Join-Path $BackendSource 'Directory.Build.targets'
$ErrorCode     = 'SQUID0001'
$Projects      = @('Squid.Api', 'Squid.Domain', 'Squid.Grains', 'Squid.PeerClient', 'Squid.Persistence', 'Squid.Protocols', 'Squid.ReadModels', 'Squid.Service', 'Squid.Worker', 'Squid.Tests')

$Rule = Get-Content -LiteralPath $RuleFile -Raw

function Get-LayerOf {
  param([string]$ProjectName)
  $found = 'unlisted'
  foreach ($raw in ($Rule -split "`r?`n")) {
    if ($raw -notmatch '<SquidLayer\s+Condition=') { continue }
    $open = [regex]::Match($raw, '>([a-z]+)</SquidLayer>')
    if (-not $open.Success) { continue }
    foreach ($name in [regex]::Matches($raw, "==\s*'(Squid\.[A-Za-z]+)'")) {
      if ($name.Groups[1].Value -eq $ProjectName) { $found = $open.Groups[1].Value }
    }
  }
  return $found
}

function Get-BannedNames {
  param([string]$Layer)
  $banned = @()
  foreach ($group in [regex]::Matches($Rule, '(?s)<ItemGroup\b[^>]*>.*?</ItemGroup>')) {
    $head = [regex]::Match($group.Value, '^<ItemGroup\b[^>]*>').Value
    if ($head -notmatch 'SquidLayer') { continue }
    $wanted = $false
    if ($head.Contains("== '" + $Layer + "'")) { $wanted = $true }
    if ($head.Contains("'" + $Layer + "' ==")) { $wanted = $true }
    if (-not $wanted) { continue }
    foreach ($inc in [regex]::Matches($group.Value, '<SquidForbidden\s+Include="([^"]+)"')) { $banned += $inc.Groups[1].Value }
  }
  return @($banned)
}

function Test-SchemaBans {
  param([string]$From, [string]$To)
  $banned = Get-BannedNames (Get-LayerOf $From)
  if ($banned -contains '*') { return $true }
  if ($banned -contains $To) { return $true }
  return $false
}

function Get-ProjectFile {
  param([string]$BackendRoot, [string]$Name)
  foreach ($root in 'src', 'tests') {
    $candidate = Join-Path (Join-Path (Join-Path $BackendRoot $root) $Name) ($Name + '.csproj')
    if (Test-Path -LiteralPath $candidate) { return $candidate }
  }
  throw ("project " + $Name + " not found under " + $BackendRoot)
}

function Copy-Tree {
  param([string]$From, [string]$To)
  New-Item -ItemType Directory -Force -Path $To | Out-Null
  foreach ($dir in Get-ChildItem -LiteralPath $From -Directory) {
    # bin/ and obj/ hold absolute paths of the original folder and would be reused, not rebuilt.
    if ($dir.Name -eq 'bin' -or $dir.Name -eq 'obj') { continue }
    Copy-Tree $dir.FullName (Join-Path $To $dir.Name)
  }
  foreach ($file in Get-ChildItem -LiteralPath $From -File) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $To $file.Name) -Force
  }
}

function Copy-Backend {
  param([string]$Destination)
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  foreach ($root in 'src', 'tests') {
    $from = Join-Path $BackendSource $root
    if (Test-Path -LiteralPath $from) { Copy-Tree $from (Join-Path $Destination $root) }
  }
  foreach ($file in Get-ChildItem -LiteralPath $BackendSource -File) {
    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $Destination $file.Name) -Force
  }
}

function Get-EdgeBlock {
  param([string]$To)
  $head = "  <ItemGroup>" + "`r`n"
  $item = "    <ProjectReference Include=`"..\" + $To + "\" + $To + ".csproj`" />" + "`r`n"
  return $head + $item + "  </ItemGroup>`r`n`r`n</Project>"
}

function Add-ProjectReference {
  param([string]$BackendRoot, [string]$From, [string]$To)
  $file = Get-ProjectFile $BackendRoot $From
  $text = [System.IO.File]::ReadAllText($file)
  if ($text.Contains("..\" + $To + "\" + $To + ".csproj")) { return 'already-present' }
  $modified = $text.Replace('</Project>', (Get-EdgeBlock $To))
  if ($modified -eq $text) { throw ("could not inject into " + $file) }
  [System.IO.File]::WriteAllText($file, $modified, (New-Object System.Text.UTF8Encoding($false)))
  return 'injected'
}

function Remove-ProjectReference {
  param([string]$BackendRoot, [string]$From, [string]$To)
  $file = Get-ProjectFile $BackendRoot $From
  $text = [System.IO.File]::ReadAllText($file)
  [System.IO.File]::WriteAllText($file, $text.Replace((Get-EdgeBlock $To), '</Project>'), (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-Dotnet {
  param([string]$WorkingDir, [string[]]$DotnetArgs)
  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName               = 'dotnet'
  # PowerShell 5.1 binds ProcessStartInfo through a compatibility shim that has no ArgumentList
  # member; Arguments (properly quoted) is the only way to pass them on this machine.
  $psi.Arguments              = (($DotnetArgs | ForEach-Object { '"' + $_ + '"' }) -join ' ')
  $psi.WorkingDirectory       = $WorkingDir
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError  = $true
  $psi.UseShellExecute        = $false
  if ([string]::IsNullOrWhiteSpace($psi.EnvironmentVariables['DOTNET_CLI_HOME'])) {
    $home = Join-Path $WorkingDir '.dotnet-home'
    New-Item -ItemType Directory -Force -Path $home | Out-Null
    $psi.EnvironmentVariables['DOTNET_CLI_HOME'] = $home
    $psi.EnvironmentVariables['NUGET_PACKAGES']  = Join-Path $home 'nuget'
  }

  $watch = [System.Diagnostics.Stopwatch]::StartNew()
  $process = [System.Diagnostics.Process]::Start($psi)
  $outTask = $process.StandardOutput.ReadToEndAsync()
  $errTask = $process.StandardError.ReadToEndAsync()
  $finished = $process.WaitForExit($TimeoutSeconds * 1000)
  if (-not $finished) { try { $process.Kill() } catch { } }
  $process.WaitForExit()
  $watch.Stop()
  return [pscustomobject]@{
    ExitCode = if ($finished) { $process.ExitCode } else { -999 }
    TimedOut = -not $finished
    Output   = $outTask.Result + $errTask.Result
    Seconds  = [math]::Round($watch.Elapsed.TotalSeconds, 1)
  }
}

function Invoke-Build {
  param([string]$BackendRoot, [string]$Project)
  $file = Get-ProjectFile $BackendRoot $Project
  return Invoke-Dotnet -WorkingDir $BackendRoot -Arguments @('build', $file, '-nologo', '-v:m', '-m:1', '-nodeReuse:false', '-p:UseSharedCompilation=false')
}

function Get-Cause {
  param([string]$Output)
  $lines = $Output -split "`n" | Where-Object { $_ -match 'SQUID0001|MSB4006|MSB3245|NU1101|error|erro ' } | Select-Object -First 2
  return ($lines -join ' | ').Trim()
}

function Get-Outcome {
  param([string]$From, [string]$To, $Build)
  if ($Build.TimedOut) { return 'timeout' }
  if ($Build.ExitCode -eq 0) { return 'compiled' }
  if ($Build.Output -match 'MSB4006') { return 'refused-cycle' }
  if ($Build.Output.Contains($ErrorCode)) {
    if ($Build.Output.Contains($From) -and $Build.Output.Contains($To)) { return 'refused-SQUID0001' }
    return 'refused-SQUID0001-unnamed'
  }
  return 'refused-other'
}

$WorkRoot  = Join-Path $env:TEMP ('squid-qa0002-' + [guid]::NewGuid().ToString('N'))
$probes    = @()
$findings  = @()
$exitCode  = 0
$steps     = @()

function Record {
  param([string]$Edge, [string]$Expected, [string]$Actual, [string]$Cause, [double]$Seconds, [bool]$Ok)
  $script:probes += [pscustomobject]@{ Edge = $Edge; Expected = $Expected; Actual = $Actual; Cause = $Cause; Seconds = $Seconds; Ok = $Ok }
  $flag = 'ok  '
  if (-not $Ok) { $flag = 'FAIL' }
  Write-Output ("{0} {1,-44} schema={2,-20} build={3,-26} {4,6}s  {5}" -f $flag, $Edge, $Expected, $Actual, $Seconds, $Cause)
}

function Record-Edge {
  param([string]$BackendRoot, [string]$From, [string]$To)
  $injected = Add-ProjectReference $BackendRoot $From $To
  if ($injected -eq 'already-present') { return }
  try {
    $build = Invoke-Build -BackendRoot $BackendRoot -Project $From
    $actual = Get-Outcome -From $From -To $To -Build $build
    $expected = 'refused-SQUID0001'
    if (-not (Test-SchemaBans $From $To)) { $expected = 'compiled' }
    $ok = ($expected -eq $actual)
    if ($expected -eq 'compiled' -and $actual -eq 'refused-cycle') { $ok = $true }
    if (-not $ok) {
      $script:exitCode = 1
      $script:findings += ("edge " + $From + " -> " + $To + ": schema says " + $expected + ", build did " + $actual + ". " + (Get-Cause $build.Output))
    }
    Record -Edge ($From + ' -> ' + $To) -Expected $expected -Actual $actual -Cause (Get-Cause $build.Output) -Seconds $build.Seconds -Ok $ok
  }
  finally {
    Remove-ProjectReference $BackendRoot $From $To
  }
}

try {
  Write-Output '=== 0. context ==='
  Write-Output ("repo       : " + $RepoRoot)
  Write-Output ("backend    : " + $BackendSource)
  Write-Output ("temp root  : " + $WorkRoot)
  Write-Output ("dotnet     : " + $(& dotnet --version))
  foreach ($p in $Projects) { Write-Output ("  {0,-16} layer={1,-9} bans={2}" -f $p, (Get-LayerOf $p), ((Get-BannedNames (Get-LayerOf $p)) -join ',')) }

  # ---------------------------------------------------------------- A1. graph recursion
  Write-Output ''
  Write-Output '=== A1. graph recursion: a broken lane inside the graph must fail a clean consumer ==='
  $copyA1 = Join-Path $WorkRoot 'a1'
  Copy-Backend $copyA1
  Add-ProjectReference $copyA1 'Squid.Persistence' 'Squid.Grains' | Out-Null
  $b = Invoke-Build -BackendRoot $copyA1 -Project 'Squid.Service'
  $a1Ok = ($b.ExitCode -ne 0) -and $b.Output.Contains($ErrorCode) -and $b.Output.Contains('Squid.Persistence') -and $b.Output.Contains('Squid.Grains')
  Record -Edge 'Squid.Service (clean, broken lane below)' -Expected 'refused-SQUID0001' -Actual (Get-Outcome 'Squid.Persistence' 'Squid.Grains' $b) -Cause (Get-Cause $b.Output) -Seconds $b.Seconds -Ok $a1Ok
  if (-not $a1Ok) { $exitCode = 1; $findings += 'A1: a clean consumer built despite a broken lane inside its graph' }

  # ---------------------------------------------------------------- A2. edge matrix
  Write-Output ''
  Write-Output '=== A2. exhaustive edge matrix over a temp copy of backend/ (schema vs real build) ==='
  $copyA2 = Join-Path $WorkRoot 'a2'
  Copy-Backend $copyA2
  $matrixStart = Get-Date
  foreach ($from in $Projects) {
    foreach ($to in $Projects) {
      if ($from -eq $to) { continue }
      Record-Edge -BackendRoot $copyA2 -From $from -To $to
    }
  }
  $matrixSeconds = [math]::Round(((Get-Date) - $matrixStart).TotalSeconds, 1)
  Write-Output ("matrix: {0} injected edge(s) in {1} s" -f $probes.Count, $matrixSeconds)

  # ---------------------------------------------------------------- B. evasion probes
  Write-Output ''
  Write-Output '=== B. evasion probes ==='
  $copyB = Join-Path $WorkRoot 'b'
  Copy-Backend $copyB

  # B1: AC-02 claim. A PackageReference is not a ProjectReference, so SQUID0001 never sees it.
  $domainFile = Get-ProjectFile $copyB 'Squid.Domain'
  $package = "  <ItemGroup>`r`n    <PackageReference Include=`"Microsoft.Orleans.Core`" Version=`"9.0.0`" />`r`n  </ItemGroup>`r`n`r`n</Project>"
  [System.IO.File]::WriteAllText($domainFile, ([System.IO.File]::ReadAllText($domainFile)).Replace('</Project>', $package))
  $b1 = Invoke-Build -BackendRoot $copyB -Project 'Squid.Domain'
  $b1Caught = ($b1.ExitCode -ne 0)
  Write-Output ("B1 PackageReference Microsoft.Orleans.Core on Squid.Domain -> build exit {0} ({1}s) :: {2}" -f $b1.ExitCode, $b1.Seconds, (Get-Cause $b1.Output))
  if ($b1Caught) { Write-Output '     the BUILD catches a package on Domain' }
  else { Write-Output '     the BUILD accepts a package on Domain: AC-02 rests on the test, not on SQUID0001' }
  $steps += ("B1 package-on-Domain caught by build = " + $b1Caught)

  # B2: control - a ProjectReference on Domain IS caught in the same copy (so B1 is about packages).
  $copyB2 = Join-Path $WorkRoot 'b2'
  Copy-Backend $copyB2
  Add-ProjectReference $copyB2 'Squid.Domain' 'Squid.Persistence' | Out-Null
  $b2 = Invoke-Build -BackendRoot $copyB2 -Project 'Squid.Domain'
  $b2Ok = ($b2.ExitCode -ne 0) -and $b2.Output.Contains($ErrorCode) -and $b2.Output.Contains('Squid.Domain') -and $b2.Output.Contains('Squid.Persistence')
  Write-Output ("B2 control Domain -> Persistence: exit {0}, SQUID0001 naming both = {1} :: {2}" -f $b2.ExitCode, $b2Ok, (Get-Cause $b2.Output))
  if (-not $b2Ok) { $exitCode = 1; $findings += 'B2 control: the copy cannot reproduce SQUID0001 on Domain -> Persistence' }

  # B3: a raw <Reference> is not a ProjectReference: the layer rule is blind to it by construction.
  $apiFile = Get-ProjectFile $copyB 'Squid.Api'
  $element = "  <ItemGroup>`r`n    <Reference Include=`"Squid.Persistence`">`r`n      <HintPath>..\Squid.Persistence\bin\Debug\net10.0\Squid.Persistence.dll</HintPath>`r`n    </Reference>`r`n  </ItemGroup>`r`n`r`n</Project>"
  [System.IO.File]::WriteAllText($apiFile, ([System.IO.File]::ReadAllText($apiFile)).Replace('</Project>', $element))
  $b3 = Invoke-Build -BackendRoot $copyB -Project 'Squid.Api'
  $b3Blind = -not $b3.Output.Contains($ErrorCode)
  Write-Output ("B3 raw <Reference> of Squid.Persistence from Squid.Api -> exit {0}, SQUID0001 emitted = {1} :: {2}" -f $b3.ExitCode, (-not $b3Blind), (Get-Cause $b3.Output))
  $steps += ("B3 raw-Reference blind = " + $b3Blind)

  # B4: Remove= of a legal edge is not a breach.
  $copyB4 = Join-Path $WorkRoot 'b4'
  Copy-Backend $copyB4
  $grainsFile = Get-ProjectFile $copyB4 'Squid.Grains'
  $remove = "  <ItemGroup>`r`n    <ProjectReference Remove=`"..\Squid.Domain\Squid.Domain.csproj`" />`r`n  </ItemGroup>`r`n`r`n</Project>"
  [System.IO.File]::WriteAllText($grainsFile, ([System.IO.File]::ReadAllText($grainsFile)).Replace('</Project>', $remove))
  $b4 = Invoke-Build -BackendRoot $copyB4 -Project 'Squid.Grains'
  Write-Output ("B4 <ProjectReference Remove> Grains drops Domain -> exit {0} (expected 0) :: {1}" -f $b4.ExitCode, (Get-Cause $b4.Output))
  if ($b4.ExitCode -ne 0) { $exitCode = 1; $findings += 'B4: Remove= of a legal edge broke the build' }

  # B5: dotnet msbuild (not `dotnet build`) still runs the rule.
  $copyB5 = Join-Path $WorkRoot 'b5'
  Copy-Backend $copyB5
  Add-ProjectReference $copyB5 'Squid.Grains' 'Squid.Persistence' | Out-Null
  $b5file = Get-ProjectFile $copyB5 'Squid.Grains'
  $b5 = Invoke-Dotnet -WorkingDir $copyB5 -Arguments @('msbuild', $b5file, '-t:Restore;Build', '-nologo', '-v:m', '-nodeReuse:false')
  $b5Ok = ($b5.ExitCode -ne 0) -and $b5.Output.Contains($ErrorCode)
  Write-Output ("B5 dotnet msbuild -t:Restore;Build Grains -> Persistence -> exit {0}, SQUID0001 = {1} :: {2}" -f $b5.ExitCode, $b5.Output.Contains($ErrorCode), (Get-Cause $b5.Output))
  if (-not $b5Ok) { $exitCode = 1; $findings += 'B5: dotnet msbuild bypasses the layer rule' }

  # ---------------------------------------------------------------- C. cost
  Write-Output ''
  Write-Output '=== C. cost of the recursive emitter ==='
  $copyC = Join-Path $WorkRoot 'c'
  Copy-Backend $copyC
  $c1 = Invoke-Build -BackendRoot $copyC -Project 'Squid.Service'
  $c2 = Invoke-Build -BackendRoot $copyC -Project 'Squid.Service'
  Write-Output ("C1 Squid.Service (6 ProjectReference) clean build : exit {0} in {1} s" -f $c1.ExitCode, $c1.Seconds)
  Write-Output ("C2 Squid.Service second build (no-op)             : exit {0} in {1} s" -f $c2.ExitCode, $c2.Seconds)
}
finally {
  Write-Output ''
  Write-Output '=== D. summary ==='
  $failed = @($probes | Where-Object { -not $_.Ok })
  Write-Output ("probes: {0} - agreed with schema: {1} - disagreed: {2}" -f $probes.Count, ($probes.Count - $failed.Count), $failed.Count)
  foreach ($p in $failed) { Write-Output ("  DISAGREE {0}: schema={1} build={2} :: {3}" -f $p.Edge, $p.Expected, $p.Actual, $p.Cause) }
  foreach ($f in $findings) { Write-Output ("  finding: {0}" -f $f) }
  foreach ($s in $steps) { Write-Output ("  step: {0}" -f $s) }

  if ($Keep) { Write-Output ("temp kept: " + $WorkRoot) }
  else {
    try {
      if (Test-Path -LiteralPath $WorkRoot) {
        Get-ChildItem -LiteralPath $WorkRoot -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Attributes = 'Normal' }
        Remove-Item -LiteralPath $WorkRoot -Recurse -Force -ErrorAction Stop
      }
      Write-Output ("temp removed: " + $WorkRoot)
    }
    catch { Write-Output ("temp NOT removed (" + $WorkRoot + "): " + $_.Exception.Message) }
  }

  Write-Output ("exit: {0}" -f $exitCode)
  exit $exitCode
}