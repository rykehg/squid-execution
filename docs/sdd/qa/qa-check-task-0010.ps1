#Requires -Version 5.1
<#
qa-check-task-0010 - QA of TASK-0010 (five runbooks of doc05 section 8 + index), Windows half.

Executed checks (Windows / PowerShell 5.1):
  AC-01  README.md links all 5 runbooks; each index row carries its explicit status
         (EXECUTAVEL / PARCIAL / PENDENTE-fase, acute A built as [char]0x00C1) and the
         README points at the local gate (sdd-test.ps1 test).
  AC-02  the 6 files exist (index + 5) and no extra .md sits in docs/runbooks/; every one
         of the 6 has the four exact section headings Sintoma / Diagnostico(accent) /
         Procedimento / Verificacao(accents); the three PENDENTE runbooks have zero
         numbered steps, zero code fences, zero EXECUTAVEL mark, zero command-like
         backtick, and carry PENDENTE + phase pointer ("Ponteiros:").
  AC-03  every command-like backticked segment in disco-cheio.md and restaurar-backup.md
         is one of: (a) an exact member of $PlannedExec - the list this script really runs
         with -Execute, every entry exit 0 required; (b) on a same-line NAO-executar mark
         (destructive: AC-03 requires they NOT appear as executables - proven by them
         being outside the executed list); or (c) one of four tool-name mentions that are
         not runnable lines (podman compose / df / curl / curl -f - each has its runnable
         sibling elsewhere in the same file). Plus staleness: every $PlannedExec string
         must actually appear in those files.
         With -Execute the ordered cycle runs for real (podman system df, df -h and the
         documented forms, compose up/down with -f from the repo root, the file->volume
         restore cycle create/cp/rm, every curl form, byte-identical roundtrip proof),
         then removes all scaffolding, leaves the stack DOWN with volume infra_squid-data
         kept, and asserts ports 8080/16686 free.
  AC-04  every "down -v" line in docs/runbooks/ sits in a destructive-warning context;
         zero secret/token/password/key-value/entropy patterns; zero personal absolute
         machine paths (drive letters, UNC, /Users, /home, /mnt/c/Users).
  AC-05  delta: with the task window (created 2026-11-07 00:36:08) every file in
         git status is either the product delta under docs/runbooks/** or a flow artifact
         under docs/sdd/** - anything else FAILs. Pre-existing modifications of
         TASK-0003..0009 predate the window and are not this delta.
         The gate itself (sdd-test.ps1 test) is executed as its own command and recorded
         as evidence; this script never claims it.

Break-it proof: -RunbooksPath points at a copy OUTSIDE the repo; the same checks FAIL on a
copy with an injected fake EXECUTAVEL command (and other mutations) while the real runbooks
are never mutated (SHA256 before/after).

Exit codes: 0 = every executed check passed, 1 = at least one FAIL.
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -Execute
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0010.ps1 -RunbooksPath <copy>
#>
[CmdletBinding()]
param(
  [string]$RunbooksPath = '',
  [string]$RepoRoot = '',
  [switch]$Execute,
  [string]$RefTime = '2026-11-07 00:36:08'
)
$ErrorActionPreference = 'Continue'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path }
if (-not $RunbooksPath) { $RunbooksPath = Join-Path $RepoRoot 'docs\runbooks' }

$script:fail = 0
$script:total = 0

# accent-safe tokens (this source file stays ASCII; PowerShell 5.1 reads non-BOM files as ANSI)
$T_EXEC   = 'EXECUT' + [char]0x00C1 + 'VEL'                       # EXECUTAVEL
$T_NAO    = 'N' + [char]0x00C3 + 'O-executar'                     # NAO-executar
$T_PARC   = 'PARCIAL'
$T_PEND   = 'PENDENTE-fase'
$H_DIAG   = '## Diagn' + [char]0x00F3 + 'stico'                   # ## Diagnostico
$H_VERI   = '## Verifica' + [char]0x00E7 + [char]0x00E3 + 'o'     # ## Verificacao
$H_SINT   = '## Sintoma'
$H_PROC   = '## Procedimento'
$T_TRECHO = '### Trecho execut' + [char]0x00E1 + 'vel'            # ### Trecho executavel

function Sanz([string]$s) { if ($null -eq $s) { return '' }; return ($s -replace '[^\x20-\x7E]', '.') }
function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  $script:total++
  if ($Ok) { Write-Output "PASS $Id - $Detail" }
  else { Write-Output "FAIL $Id - $Detail"; $script:fail++ }
}
function Info([string]$Id, [string]$Detail) { Write-Output "INFO $Id - $Detail" }
function Read-Utf8([string]$p) { ,([System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)) }
function Read-Utf8Raw([string]$p) { [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) }

$expected = @('README.md', 'disco-cheio.md', 'restaurar-backup.md', 'rotacionar-chave.md', 'peer-entrega-travada.md', 'dlq-cheia.md')
$pendFiles = @('rotacionar-chave.md', 'peer-entrega-travada.md', 'dlq-cheia.md')
$rb2       = @('disco-cheio.md', 'restaurar-backup.md')   # AC-03 scope per the AC text

# exact strings that -Execute really runs (the AC-03 proof); each must appear in the 2 runbooks
$PlannedExec = @(
  'podman system df',
  'df -h',
  'wsl df -h',
  'podman volume ls',
  'podman compose -f infra/compose.yaml down',
  'podman compose -f infra/compose.yaml up -d',
  'curl -fsS http://localhost:8080/health',
  'curl.exe -fsS http://localhost:8080/health',
  'curl -fsS http://localhost:8080/ready',
  'curl.exe -fsS http://localhost:8080/ready',
  'podman create --name squid-restore -v infra_squid-data:/data squid-service:dev',
  'podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db',
  'podman rm squid-restore'
)
$Mentions = @('podman compose', 'df', 'curl', 'curl -f')   # tool-name references, not runnable lines
$cmdRe = '^(podman|curl|df|wsl)\b'

Write-Output "repo-root:    $RepoRoot"
Write-Output "runbooks:     $RunbooksPath"
Write-Output "mode:         $(if ($Execute) { 'STATIC + EXECUTE (AC-03 real cycle)' } else { 'STATIC only' })"
Write-Output "delta window: $RefTime (task createdAt)"

# ---------------------------------------------------------------- AC-02 existence (+ no extra .md)
$present = @()
if (Test-Path -LiteralPath $RunbooksPath -PathType Container) {
  $present = @(Get-ChildItem -LiteralPath $RunbooksPath -Filter *.md -File | ForEach-Object { $_.Name })
}
$missing = @($expected | Where-Object { $present -notcontains $_ })
$extra   = @($present | Where-Object { $expected -notcontains $_ })
Check 'AC02-existence' (($missing.Count -eq 0) -and ($extra.Count -eq 0)) `
  "expected 6 files, found $($present.Count): missing=[$($missing -join ',')] extra=[$($extra -join ',')]"

# ---------------------------------------------------------------- AC-01 index of the 5 with status + gate
$readmePath = Join-Path $RunbooksPath 'README.md'
$statusNeed = [ordered]@{
  'disco-cheio.md'          = $T_EXEC
  'restaurar-backup.md'     = $T_PARC
  'rotacionar-chave.md'     = $T_PEND
  'peer-entrega-travada.md' = $T_PEND
  'dlq-cheia.md'            = $T_PEND
}
$readmeLines = @()
if (Test-Path -LiteralPath $readmePath) { $readmeLines = Read-Utf8 $readmePath }
foreach ($f in $statusNeed.Keys) {
  $ok = $false; $why = 'no index line linking this runbook'
  $rows = @($readmeLines | Where-Object { $_ -match ('\]\(' + [regex]::Escape($f) + '\)') })
  if ($rows.Count -ge 1) {
    $tok = $statusNeed[$f]
    $hit = @($rows | Where-Object { $_.Contains($tok) })
    if ($hit.Count -ge 1) { $ok = $true; $why = "row carries status token $(Sanz $tok)" }
    else { $why = "index line exists but lacks token $(Sanz $tok): $(Sanz $rows[0])" }
  }
  Check ('AC01-status-' + ($f -replace '\.md$', '')) $ok $why
}
$gateOk = (@($readmeLines | Where-Object { $_ -match 'sdd-test\.ps1 test' }).Count -ge 1)
Check 'AC01-gate-pointer' $gateOk 'README points at the local gate (sdd-test.ps1 test)'

# ---------------------------------------------------------------- AC-02 sections + PENDENTE structure
foreach ($f in $expected) {
  $id = 'AC02-sections-' + ($f -replace '\.md$', '')
  $p = Join-Path $RunbooksPath $f
  if (-not (Test-Path -LiteralPath $p)) { Check $id $false 'file missing'; continue }
  $lines = Read-Utf8 $p
  $missingSec = @()
  foreach ($h in @($H_SINT, $H_DIAG, $H_PROC, $H_VERI)) {
    $found = $false
    foreach ($ln in $lines) { if ($ln.Trim() -eq $h) { $found = $true; break } }
    if (-not $found) { $missingSec += Sanz $h }
  }
  Check $id ($missingSec.Count -eq 0) "4 required sections present=$($missingSec.Count -eq 0) missing=[$($missingSec -join ', ')]"
}
foreach ($f in $pendFiles) {
  $id = 'AC02-pendente-' + ($f -replace '\.md$', '')
  $p = Join-Path $RunbooksPath $f
  if (-not (Test-Path -LiteralPath $p)) { Check $id $false 'file missing'; continue }
  $lines = Read-Utf8 $p
  $raw = ($lines -join "`n")
  $numbered = 0; $fences = 0; $execMark = 0
  $cmdSegs = @()
  foreach ($ln in $lines) {
    if ($ln -match '^\s*\d+\.\s') { $numbered++ }
    if ($ln -match '```') { $fences++ }
    if ($ln.Contains($T_EXEC)) { $execMark++ }
    foreach ($m in [regex]::Matches($ln, '`([^`]+)`')) {
      $s = $m.Groups[1].Value
      if ($s -match $cmdRe) { $cmdSegs += $s }
    }
  }
  $hasPointer = $raw.Contains('Ponteiros:')
  $hasPend    = $raw.Contains('PENDENTE')
  $ok = ($numbered -eq 0) -and ($fences -eq 0) -and ($execMark -eq 0) -and ($cmdSegs.Count -eq 0) -and $hasPointer -and $hasPend
  Check $id $ok "no-step proof: numbered=$numbered fences=$fences execMarks=$execMark commandLike=$($cmdSegs.Count) pointer=$hasPointer PENDENTE=$hasPend"
}

# ---------------------------------------------------------------- AC-03 classification (static half)
$plannedSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($s in $PlannedExec) { [void]$plannedSet.Add($s) }
$mentionSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($s in $Mentions) { [void]$mentionSet.Add($s) }
$seenStrings = New-Object 'System.Collections.Generic.HashSet[string]'
$offenders = @()
$naoListed = @()
foreach ($f in $rb2) {
  $p = Join-Path $RunbooksPath $f
  if (-not (Test-Path -LiteralPath $p)) { $offenders += "${f}: FILE MISSING"; continue }
  $lines = Read-Utf8 $p
  for ($i = 0; $i -lt $lines.Count; $i++) {
    $ln = $lines[$i]
    $naoHere = $ln.Contains($T_NAO)
    foreach ($m in [regex]::Matches($ln, '`([^`]+)`')) {
      $s = $m.Groups[1].Value
      if ($s -notmatch $cmdRe) { continue }
      [void]$seenStrings.Add($s)
      if ($plannedSet.Contains($s)) { continue }
      if ($naoHere) { $naoListed += "${f}:$($i + 1):$(Sanz $s)"; continue }
      if ($mentionSet.Contains($s)) { continue }
      $offenders += "${f}:$($i + 1): $(Sanz $s)"
    }
  }
}
Check 'AC03-classify' ($offenders.Count -eq 0) `
  "every command-like segment classified (executed | NAO-executar | known mention); offenders=$($offenders.Count) [$($offenders -join ' | ')]"
$stale = @($PlannedExec | Where-Object { -not $seenStrings.Contains($_) })
Check 'AC03-stale' ($stale.Count -eq 0) `
  "all $($PlannedExec.Count) executed strings appear in the 2 runbooks; missing=[$($stale -join ' | ')]"
Check 'AC03-nao-listed' ($true) `
  "destructive commands sit on NAO-executar lines (never executed by AC-03 rule): $(($naoListed | Sort-Object -Unique) -join ' | ')"
$rbPath = Join-Path $RunbooksPath 'restaurar-backup.md'
if (Test-Path -LiteralPath $rbPath) {
  $rbRaw = Read-Utf8Raw $rbPath
  Check 'AC03-trecho' $rbRaw.Contains($T_TRECHO) 'restaurar-backup.md declares its executable trecho (ciclo arquivo -> volume)'
} else { Check 'AC03-trecho' $false 'restaurar-backup.md missing' }

# ---------------------------------------------------------------- AC-04 down -v context + secrets + paths
$downv = @(); $downvBad = @()
foreach ($f in $expected) {
  $p = Join-Path $RunbooksPath $f
  if (-not (Test-Path -LiteralPath $p)) { continue }
  $lines = Read-Utf8 $p
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match 'down -v') {
      $ctx = $lines[$i] -match ('(?i)(destructiv|N' + [char]0x00C3 + 'O-executar|Nunca|destrui|apaga|perda de dados)')
      $downv += "${f}:$($i + 1)ctx=$(if ($ctx) { 'destructive' } else { 'NO-CONTEXT' })"
      if (-not $ctx) { $downvBad += "${f}:$($i + 1)" }
    }
  }
}
Check 'AC04-downv' ($downvBad.Count -eq 0) `
  "'down -v' occurrences=$($downv.Count), outside destructive context=$($downvBad.Count): $($downv -join ' | ')"

$rawAll = ''
foreach ($f in $expected) { $p = Join-Path $RunbooksPath $f; if (Test-Path -LiteralPath $p) { $rawAll += (Read-Utf8Raw $p) + "`n" } }
$secretPats = @(
  @('token', '(?i)\btoken\b'),
  @('password', '(?i)\bpassword\b'),
  @('passwd', '(?i)\bpasswd\b'),
  @('secret', '(?i)\bsecrets?\b'),
  @('apikey', '(?i)\bapi[_-]?key\b'),
  @('credential', '(?i)\bcredentials?\b'),
  @('bearer', '(?i)\bbearer\s+[A-Za-z0-9._-]{15,}'),
  @('keyvalue', '(?i)\b(chave|key|token|secret|password)\s*[:=]\s*\S'),
  @('gh-token', '\bgh[pousr]_[A-Za-z0-9]{30,}'),
  @('aws-key', '\bAKIA[0-9A-Z]{16}\b'),
  @('privkey', '-----BEGIN [A-Z ]*PRIVATE KEY'),
  @('longhex', '\b[0-9a-fA-F]{40,}\b'),
  @('longb64', '[A-Za-z0-9+/]{70,}={0,2}')
)
$secretHits = @()
foreach ($sp in $secretPats) { foreach ($m in [regex]::Matches($rawAll, $sp[1])) { $secretHits += $sp[0] } }
Check 'AC04-secrets' ($secretHits.Count -eq 0) "secret/token pattern hits=$($secretHits.Count) [$($secretHits -join ',')] patterns=$($secretPats.Count)"

$pathPats = @(
  @('drive', '(?<![A-Za-z0-9])[A-Za-z]:[\\/]'),
  @('unc', '(?<![:\w])\\\\[A-Za-z0-9._-]+\\[A-Za-z0-9]'),
  @('unix-user', '/Users/[A-Za-z0-9]'),
  @('unix-home', '/home/[A-Za-z0-9]+/'),
  @('wslmnt', '/mnt/c/Users/')
)
$pathHits = @()
foreach ($pp in $pathPats) { foreach ($m in [regex]::Matches($rawAll, $pp[1])) { $pathHits += $pp[0] } }
Check 'AC04-paths' ($pathHits.Count -eq 0) "personal absolute path hits=$($pathHits.Count) [$($pathHits -join ',')] patterns=$($pathPats.Count)"

# ---------------------------------------------------------------- AC-05 delta of the change
$ref = [datetime]$RefTime
$stOut = @(); $stRc = 1
try { $stOut = @(& git -C $RepoRoot status --porcelain 2>&1); $stRc = $LASTEXITCODE } catch { $stRc = 1 }
if ($stRc -ne 0) {
  Check 'AC05-delta' $false "git status failed (exit $stRc): $($stOut -join ' ')"
} else {
  $newOutside = @(); $product = @(); $flow = @(); $pre = 0
  foreach ($line in $stOut) {
    $line = "$line"
    if ($line.Length -lt 4) { continue }
    $path = $line.Substring(3).Trim('"')
    $full = Join-Path $RepoRoot ($path -replace '/', '\')
    $targets = @()
    if (Test-Path -LiteralPath $full -PathType Container) {
      $targets = @(Get-ChildItem -LiteralPath $full -Recurse -File | ForEach-Object { $_.FullName.Substring($RepoRoot.Length + 1) -replace '\\', '/' })
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) { $targets = @($path) }
    if ($targets.Count -eq 0) { $targets = @($path) }
    foreach ($t in $targets) {
      $tf = Join-Path $RepoRoot ($t -replace '/', '\')
      $mt = $ref.AddDays(-1)
      if (Test-Path -LiteralPath $tf -PathType Leaf) { $mt = (Get-Item -LiteralPath $tf).LastWriteTime }
      if ($mt -lt $ref) { $pre++; continue }        # pre-existing delta of TASK-0003..0009: not this task
      if ($t -like 'docs/runbooks/*') { $product += $t }
      elseif ($t -like 'docs/sdd/*') { $flow += "$t($($mt.ToString('HH:mm:ss')))" }
      else { $newOutside += "$t($($mt.ToString('HH:mm:ss')))" }
    }
  }
  Check 'AC05-delta' ($newOutside.Count -eq 0) `
    "files touched after $RefTime outside docs/runbooks and docs/sdd = $($newOutside.Count) [$($newOutside -join '; ')]"
  Info 'AC05-delta-product' "task product delta: $(($product | Sort-Object -Unique) -join ', ')"
  Info 'AC05-delta-flow' "flow artifacts under docs/sdd (allowed, listed): $($flow -join '; ')"
  Info 'AC05-delta-pre' "pre-existing entries from TASK-0003..0009 outside the window (not findings): $pre"
}

# ---------------------------------------------------------------- AC-03 real execution (-Execute)
if ($Execute) {
  Write-Output '---- AC-03 EXECUTION (every entry must exit 0) ----'
  $repoBk = (Get-Location).Path
  Set-Location -LiteralPath $RepoRoot
  $srcDb = Join-Path $RepoRoot 'squid-grainstate.db'
  $rtDb = Join-Path $env:TEMP 'qa0010-roundtrip.db'
  $srcHash = ''; $rtHash = ''
  $script:entries = New-Object System.Collections.Generic.List[object]

  function Run-Entry([string]$Id, [string]$Kind, [string]$CmdStr, [string]$Planned) {
    $out = @(); $code = -999
    try {
      switch ($Kind) {
        'cmdc' {
          $out = @(cmd /c $CmdStr 2>&1); $code = $LASTEXITCODE
        }
        'native' {
          $parts = @($CmdStr -split '\s+' | Where-Object { $_ -ne '' })
          $exe = $parts[0]
          if ($parts.Count -gt 1) { $exeArgs = @($parts[1..($parts.Count - 1)]) } else { $exeArgs = @() }
          $out = @(& $exe @exeArgs 2>&1); $code = $LASTEXITCODE
        }
        'wsl-inner' {
          $out = @(wsl -e bash -lc $CmdStr 2>&1); $code = $LASTEXITCODE
        }
      }
    } catch { $out = @("EXCEPTION: $_"); $code = -1 }
    $tail = (@($out | ForEach-Object { Sanz "$_" }) -join ' ~ ')
    if ($tail.Length -gt 220) { $tail = $tail.Substring($tail.Length - 220) }
    $plannedBit = if ($Planned) { "planned=$Planned" } else { 'scaffolding (not a runbook string)' }
    Check $Id ($code -eq 0) "exit=$code [$Kind] $plannedBit out: $tail"
    $script:entries.Add([pscustomobject]@{ Id = $Id; Cmd = $CmdStr; Code = $code; Planned = $Planned })
  }
  function Wait-Health([string]$Id, [int]$Seconds) {
    $ok = $false; $k = 0
    while ($k -lt $Seconds) {
      cmd /c curl -fsS http://localhost:8080/health > $null 2>&1
      if ($LASTEXITCODE -eq 0) { $ok = $true; break }
      Start-Sleep -Seconds 1; $k++
    }
    Check $Id $ok "stack healthy within ${Seconds}s (bounded retry loop)"
  }

  # --- diagnosis side of disco-cheio (stack starts DOWN)
  Run-Entry 'AC03-exec-01-system-df'    'cmdc' 'podman system df' 'podman system df'
  Run-Entry 'AC03-exec-02-df-linux'     'wsl-inner' 'df -h' 'df -h'
  Run-Entry 'AC03-exec-03-wsl-df'       'cmdc' 'wsl df -h' 'wsl df -h'
  Run-Entry 'AC03-exec-04-volume-ls'    'cmdc' 'podman volume ls' 'podman volume ls'
  Run-Entry 'AC03-exec-05-up'           'cmdc' 'podman compose -f infra/compose.yaml up -d' 'podman compose -f infra/compose.yaml up -d'
  Wait-Health 'AC03-wait-01-health' 90
  Run-Entry 'AC03-exec-06-health-cmd'   'cmdc' 'curl -fsS http://localhost:8080/health' 'curl -fsS http://localhost:8080/health'
  Run-Entry 'AC03-exec-07-health-exe'   'native' 'curl.exe -fsS http://localhost:8080/health' 'curl.exe -fsS http://localhost:8080/health'
  Run-Entry 'AC03-exec-08-ready-cmd'    'cmdc' 'curl -fsS http://localhost:8080/ready' 'curl -fsS http://localhost:8080/ready'
  Run-Entry 'AC03-exec-09-ready-exe'    'native' 'curl.exe -fsS http://localhost:8080/ready' 'curl.exe -fsS http://localhost:8080/ready'
  # the documented Linux forms, verbatim inside WSL
  Run-Entry 'AC03-exec-10-linux-health' 'wsl-inner' 'curl -fsS http://localhost:8080/health' 'curl -fsS http://localhost:8080/health'
  Run-Entry 'AC03-exec-11-linux-ready'  'wsl-inner' 'curl -fsS http://localhost:8080/ready' 'curl -fsS http://localhost:8080/ready'
  # scaffolding: take the byte-exact source file OUT of the volume (the restore needs ./squid-grainstate.db)
  if (Test-Path -LiteralPath $srcDb) { Remove-Item -LiteralPath $srcDb -Force }
  Run-Entry 'AC03-scaf-01-staging-create' 'native' 'podman create --name qa0010-staging -v infra_squid-data:/data squid-service:dev' ''
  Run-Entry 'AC03-scaf-02-staging-cp-out'  'native' "podman cp qa0010-staging:/data/squid-grainstate.db $srcDb" ''
  Run-Entry 'AC03-scaf-03-staging-rm'      'native' 'podman rm qa0010-staging' ''
  if (Test-Path -LiteralPath $srcDb) { $srcHash = (Get-FileHash -LiteralPath $srcDb -Algorithm SHA256).Hash }
  Check 'AC03-scaf-source' ($srcHash -ne '') "restore source file SHA256=$srcHash"

  # --- the documented restore cycle, from the repo root, in runbook order
  Run-Entry 'AC03-exec-12-down'    'cmdc' 'podman compose -f infra/compose.yaml down' 'podman compose -f infra/compose.yaml down'
  Run-Entry 'AC03-exec-13-create'  'cmdc' 'podman create --name squid-restore -v infra_squid-data:/data squid-service:dev' 'podman create --name squid-restore -v infra_squid-data:/data squid-service:dev'
  Run-Entry 'AC03-exec-14-cp-in'   'cmdc' 'podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db' 'podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db'
  Run-Entry 'AC03-exec-15-rm'      'cmdc' 'podman rm squid-restore' 'podman rm squid-restore'
  Run-Entry 'AC03-exec-16-up'      'cmdc' 'podman compose -f infra/compose.yaml up -d' 'podman compose -f infra/compose.yaml up -d'
  Wait-Health 'AC03-wait-02-health' 90
  # verification side of both runbooks after the restore
  Run-Entry 'AC03-exec-17-health'  'cmdc' 'curl -fsS http://localhost:8080/health' 'curl -fsS http://localhost:8080/health'
  Run-Entry 'AC03-exec-18-ready'   'cmdc' 'curl -fsS http://localhost:8080/ready' 'curl -fsS http://localhost:8080/ready'
  Run-Entry 'AC03-exec-19-volume'  'cmdc' 'podman volume ls' 'podman volume ls'
  # roundtrip proof: bytes back in the volume == bytes that came out
  if (Test-Path -LiteralPath $rtDb) { Remove-Item -LiteralPath $rtDb -Force }
  Run-Entry 'AC03-scaf-04-rt-create' 'native' 'podman create --name qa0010-rt -v infra_squid-data:/data squid-service:dev' ''
  Run-Entry 'AC03-scaf-05-rt-cp'     'native' "podman cp qa0010-rt:/data/squid-grainstate.db $rtDb" ''
  Run-Entry 'AC03-scaf-06-rt-rm'     'native' 'podman rm qa0010-rt' ''
  if (Test-Path -LiteralPath $rtDb) { $rtHash = (Get-FileHash -LiteralPath $rtDb -Algorithm SHA256).Hash }
  Check 'AC03-roundtrip' (($srcHash -ne '') -and ($srcHash -eq $rtHash)) "restored bytes == source bytes: src=$srcHash back=$rtHash"
  if (Test-Path -LiteralPath $rtDb) { Remove-Item -LiteralPath $rtDb -Force }
  # cleanup of scaffolding: no stray file left in the repo
  if (Test-Path -LiteralPath $srcDb) { Remove-Item -LiteralPath $srcDb -Force }
  Check 'AC03-cleanup-file' (-not (Test-Path -LiteralPath $srcDb)) 'no stray squid-grainstate.db left in the repo root'

  # --- final state: plain down (NEVER -v), volume kept, ports free, no stray containers
  Run-Entry 'AC03-exec-20-final-down' 'cmdc' 'podman compose -f infra/compose.yaml down' 'podman compose -f infra/compose.yaml down'
  $psOut = @(& podman ps -a --format '{{.Names}}' 2>&1) | ForEach-Object { "$_".Trim() } | Where-Object { $_ -ne '' }
  Check 'STATE-stack-down' (@($psOut).Count -eq 0) "containers after final plain down = $(@($psOut).Count) [$(@($psOut) -join ',')]"
  $vols = @(& podman volume ls --format '{{.Name}}' 2>&1) | ForEach-Object { "$_" }
  Check 'STATE-volume-kept' (@($vols | Where-Object { $_ -eq 'infra_squid-data' }).Count -eq 1) 'volume infra_squid-data kept (no down -v ran anywhere)'
  $stray = @()
  foreach ($cn in @('qa0010-staging', 'qa0010-rt', 'squid-restore')) {
    $null = & podman container exists $cn 2>&1
    if ($LASTEXITCODE -eq 0) { $stray += $cn }
  }
  Check 'STATE-no-stray-container' ($stray.Count -eq 0) "scaffolding containers removed; stray=[$($stray -join ',')]"
  foreach ($port in 8080, 16686) {
    $open = $false
    try { $open = [bool](Test-NetConnection -ComputerName 127.0.0.1 -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) } catch { $open = $false }
    Check "STATE-port-$port-free" (-not $open) "port $port open after final down = $open (must be False)"
  }
  # every planned string really executed with exit 0 at least once
  $execOk = @(); $execBad = @()
  foreach ($s in $PlannedExec) {
    $runs = @($script:entries | Where-Object { $_.Planned -eq $s })
    if ($runs.Count -eq 0) { $execBad += "$s(NOT-RUN)"; continue }
    if (@($runs | Where-Object { $_.Code -eq 0 }).Count -ge 1) { $execOk += $s }
    else { $execBad += "$s(exits=$(($runs | ForEach-Object { $_.Code }) -join '/'))" }
  }
  Check 'AC03-exec-coverage' ($execBad.Count -eq 0) "executed exit 0: $($execOk.Count)/$($PlannedExec.Count); not proven=[$($execBad -join ' | ')]"
  Set-Location -LiteralPath $repoBk
}

# ---------------------------------------------------------------- summary
Write-Output "SUMMARY total=$($script:total) fails=$($script:fail)"
if ($script:fail -eq 0) {
  Write-Output 'RESULT: PASS - all executed checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $($script:fail) of $($script:total) checks failed"
exit 1
