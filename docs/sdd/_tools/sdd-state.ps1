#Requires -Version 5.1
<#
sdd-state — the only writer of the SDD canonical task state (Windows / PowerShell 5.1+).

Why this exists: hand-editing a task file with string replace fails in four ways that all
look like success — a backtick escape turning text into a control character, an anchor built
from the wrong timestamp, log lines appended after the evidence table, and a partial write
after a failed replacement. This tool owns exactly those four and refuses to guess.

Run it like this on Windows (execution policy blocks a bare `& script.ps1`):
  powershell -NoProfile -ExecutionPolicy Bypass -File sdd-state.ps1 <command> ...

Usage:
  sdd-state.ps1 append-log      <task> <role> [-Text "line"]     # one line per input line
  sdd-state.ps1 append-evidence <task> <role> <command> <exit> <artifact>
  sdd-state.ps1 set             <task> <field> <value>           # inside the yaml block only
  sdd-state.ps1 check           <task>                           # exit 3 if any check fails

Text for append-log comes from -Text or from stdin (preferred: no shell escaping at all).

Rules enforced, not suggested:
  - real timestamp from this machine, never typed by a caller
  - writes land in the right section (`## Log` before `## Evidence`), never at the end of file
  - atomic write (temp + move), UTF-8 without BOM, so a failure cannot leave a half file
  - control characters (tab, form feed, the `\t`/`\f` escape family) and pipe-breaking
    arguments are rejected instead of written
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true, Position = 0)][ValidateSet('append-log', 'append-evidence', 'set', 'check')][string]$Command,
  [Parameter(Mandatory = $true, Position = 1)][string]$Task,
  [Parameter(Position = 2)][string]$A1,
  [Parameter(Position = 3)][string]$A2,
  [Parameter(Position = 4)][string]$A3,
  [Parameter(Position = 5)][string]$A4,
  [string]$Text
)

$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Fail([string]$Message, [int]$Code = 3) {
  [Console]::Error.WriteLine("sdd-state: $Message")
  exit $Code
}

if (-not (Test-Path -LiteralPath $Task)) { Fail "task file not found: $Task" 2 }
$taskPath = (Resolve-Path -LiteralPath $Task).Path
$lines = [System.IO.File]::ReadAllLines($taskPath, [System.Text.Encoding]::UTF8)

function Write-Atomic([string[]]$Content) {
  $tmp = "$taskPath.sddtmp"
  [System.IO.File]::WriteAllLines($tmp, $Content, $utf8)
  Move-Item -LiteralPath $tmp -Destination $taskPath -Force
}

# any C0 control character is refused: that is what an unescaped backtick produces
function Assert-Clean([string]$Value, [string]$What) {
  if ($Value -match '[\x00-\x1F]') {
    $code = [int][char]([regex]::Match($Value, '[\x00-\x1F]').Value)
    Fail "$What contains control character 0x$($code.ToString('X2')) (tab=09, form feed=0C, line feed=0A). In PowerShell a backtick before a letter produces one; pass the text through stdin or -Text and drop the backtick." 3
  }
}

function Assert-NoPipe([string]$Value, [string]$What) {
  Assert-Clean $Value $What
  if ($Value -match '\|') { Fail "$What contains '|', which would break the evidence table. Reword it." 3 }
}

function Get-YamlRange {
  $start = -1; $end = -1
  for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($start -lt 0 -and $lines[$i] -match '^```yaml\s*$') { $start = $i + 1; continue }
    if ($start -ge 0 -and $lines[$i] -match '^```\s*$') { $end = $i - 1; break }
  }
  if ($start -lt 0 -or $end -lt $start) { Fail 'no ```yaml ... ``` control block found' 2 }
  return @($start, $end)
}

function Get-SectionIndex([string]$Header) {
  for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].Trim() -eq $Header) { return $i } }
  return -1
}

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

switch ($Command) {

  'append-log' {
    $role = $A1
    if ([string]::IsNullOrWhiteSpace($role)) { Fail 'append-log needs a role (odev|dev|qa|humano)' 1 }
    if ($role -notmatch '^[a-z_-]+$') { Fail "role must be a lowercase slug: got '$role'" 1 }

    if ([string]::IsNullOrWhiteSpace($Text)) {
      $Text = [Console]::In.ReadToEnd()
      if ([string]::IsNullOrWhiteSpace($Text)) { Fail 'append-log needs text via -Text or stdin' 1 }
    }

    $incoming = @($Text -split "\r?\n" | ForEach-Object { $_.TrimEnd() } | Where-Object { $_.Length -gt 0 })
    if ($incoming.Count -eq 0) { Fail 'append-log got no non-empty line' 1 }
    $new = @()
    foreach ($t in $incoming) {
      Assert-Clean $t 'log text'
      $new += "- $stamp | $role | $t"
    }

    $ev = Get-SectionIndex '## Evidence'
    $log = Get-SectionIndex '## Log'
    if ($ev -ge 0 -and $log -ge 0 -and $log -gt $ev) { Fail '## Log sits after ## Evidence; fix the section order by hand first' 2 }

    $out = New-Object System.Collections.Generic.List[string]
    if ($ev -ge 0) {
      $last = -1
      for ($i = 0; $i -lt $ev; $i++) { if ($lines[$i].Trim().Length -gt 0) { $last = $i } }
      if ($last -lt 0) { Fail '## Evidence found but nothing before it' 2 }
      for ($i = 0; $i -le $last; $i++) { $out.Add($lines[$i]) }
      foreach ($n in $new) { $out.Add($n) }
      $out.Add('')
      for ($i = $ev; $i -lt $lines.Count; $i++) { $out.Add($lines[$i]) }
    }
    else {
      foreach ($l in $lines) { $out.Add($l) }
      foreach ($n in $new) { $out.Add($n) }
    }
    Write-Atomic $out.ToArray()
    Write-Output "append-log: $($new.Count) line(s) -> $(Split-Path -Leaf $taskPath)"
  }

  'append-evidence' {
    $role = $A1; $cmd = $A2; $code = $A3; $artifact = $A4
    if ([string]::IsNullOrWhiteSpace($role) -or [string]::IsNullOrWhiteSpace($cmd) -or
        [string]::IsNullOrWhiteSpace($code) -or [string]::IsNullOrWhiteSpace($artifact)) {
      Fail 'append-evidence needs: <role> <command> <exit> <artifact>' 1
    }
    if ($role -notmatch '^[a-z_-]+$') { Fail "role must be a lowercase slug: got '$role'" 1 }
    if ($code -notmatch '^-?\d+$|^NAO_COLETADO$|^n/a$') { Fail "exit must be an integer or NAO_COLETADO: got '$code'" 1 }
    Assert-NoPipe $cmd 'command'
    Assert-NoPipe $artifact 'artifact'

    $ev = Get-SectionIndex '## Evidence'
    if ($ev -lt 0) { Fail 'no ## Evidence section' 2 }

    $row = "| $stamp | $role | $cmd | $code | $artifact |"
    $out = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -le $ev; $i++) { $out.Add($lines[$i]) }
    $lastRow = -1
    for ($i = $ev + 1; $i -lt $lines.Count; $i++) { if ($lines[$i].TrimStart().StartsWith('|')) { $lastRow = $i } }
    if ($lastRow -lt 0) { Fail '## Evidence has no table row to append after' 2 }
    for ($i = $ev + 1; $i -le $lastRow; $i++) { $out.Add($lines[$i]) }
    $out.Add($row)
    for ($i = $lastRow + 1; $i -lt $lines.Count; $i++) { $out.Add($lines[$i]) }
    Write-Atomic $out.ToArray()
    Write-Output "append-evidence: 1 row -> $(Split-Path -Leaf $taskPath)"
  }

  'set' {
    $field = $A1; $value = $A2
    if ([string]::IsNullOrWhiteSpace($field)) { Fail 'set needs <field> <value>' 1 }
    if ($null -eq $value) { $value = '' }
    Assert-Clean $value "value of '$field'"

    $range = Get-YamlRange
    $s = $range[0]; $e = $range[1]
    $pattern = '^(\s*' + [regex]::Escape($field) + ':[ \t]*)(.*)$'
    $hits = @()
    for ($i = $s; $i -le $e; $i++) { if ($lines[$i] -match $pattern) { $hits += $i } }
    if ($hits.Count -eq 0) { Fail "field '$field' not found inside the yaml block" 2 }
    if ($hits.Count -gt 1) { Fail "field '$field' appears $($hits.Count) times inside the yaml block; use a longer unique name" 2 }

    $i = $hits[0]
    $lines[$i] = [regex]::Replace($lines[$i], $pattern, { param($m) $m.Groups[1].Value + $value })
    Write-Atomic $lines
    Write-Output "set: $field = $value"
  }

  'check' {
    $problems = New-Object System.Collections.Generic.List[string]

    $range = @(-1, -1)
    try { $range = Get-YamlRange } catch { $problems.Add('no ```yaml control block') }
    $s = $range[0]; $e = $range[1]
    if ($s -ge 0) {
      foreach ($k in @('runId', 'taskId', 'status', 'sequence', 'updatedAt', 'nextAction')) {
        $n = 0
        for ($i = $s; $i -le $e; $i++) { if ($lines[$i] -match ('^\s*' + [regex]::Escape($k) + ':')) { $n++ } }
        if ($n -eq 0) { $problems.Add("missing required field: $k") }
        if ($n -gt 1) { $problems.Add("duplicated field inside yaml block: $k ($n)") }
      }
    }

    $logIdx = Get-SectionIndex '## Log'
    $evIdx = Get-SectionIndex '## Evidence'
    if ($logIdx -lt 0) { $problems.Add('no ## Log section') }
    if ($evIdx -lt 0) { $problems.Add('no ## Evidence section') }
    if ($logIdx -ge 0 -and $evIdx -ge 0 -and $logIdx -gt $evIdx) { $problems.Add('## Log comes after ## Evidence') }

    $logEnd = if ($evIdx -ge 0) { $evIdx } else { $lines.Count }
    $logLines = 0; $evRows = 0
    $entryPattern = '^- (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}|NAO_COLETADO) \| [a-z_-]+ \| \S'

    for ($i = 0; $i -lt $lines.Count; $i++) {
      if ($lines[$i] -match '[\x00-\x1F]') {
        $code = [int][char]([regex]::Match($lines[$i], '[\x00-\x1F]').Value)
        $problems.Add("control character 0x$($code.ToString('X2')) at line $($i + 1)")
      }
    }

    if ($logIdx -ge 0) {
      for ($i = $logIdx + 1; $i -lt $logEnd; $i++) {
        if ($lines[$i] -match '^- ') {
          if ($lines[$i] -match $entryPattern) { $logLines++ }
          else { $problems.Add("malformed log line $($i + 1): $($lines[$i].Substring(0, [Math]::Min(70, $lines[$i].Length)))") }
        }
      }
    }

    if ($evIdx -ge 0) {
      for ($i = $evIdx + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^- (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}|NAO_COLETADO) \| [a-z_-]+ \|') { $problems.Add("log entry after ## Evidence at line $($i + 1)") }
        if ($lines[$i].TrimStart().StartsWith('|')) {
          $cols = ($lines[$i] -split '\|').Count
          if ($cols -ne 7) { $problems.Add("evidence row at line $($i + 1) has $($cols - 2) column(s), expected 5") }
          if ($lines[$i] -match '^\|\s*(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}|NAO_COLETADO)\s*\|') { $evRows++ }
        }
      }
    }

    if ($problems.Count -eq 0) {
      Write-Output "check: OK - log entries $logLines, evidence rows $evRows"
      exit 0
    }
    foreach ($p in $problems) { Write-Output "check: FAIL - $p" }
    exit 3
  }
}