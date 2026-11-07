#Requires -Version 5.1
<#
qa-check-task-0009 - QA of TASK-0009 (GitHub Actions build+test workflow), static half.

Executed checks (Windows / PowerShell 5.1):
  AC-01  ci.yml parses with PyYAML (tool, not visual), push branches == develop+main,
         pull_request trigger present, permissions == contents: read only.
  AC-02  run steps are EXACTLY the two local-gate commands (same script, same args);
         0 continue-on-error; 0 secret-pattern hits; local gate is the same script.
  AC-03  setup-dotnet version is an exact x.y.z pin on .NET 10 (no range, no latest);
         actions/cache key derives from hashFiles('**/Directory.Packages.props') and
         that file exists in the repo.
  AC-05  git status + mtime: no file changed after the task window (2026-11-06 23:18:15)
         outside .github/ and outside the flow artifacts of ODEV/QA (listed in $allow).
  AC-06  HEAD == origin/develop, left-right 0 0, no [ahead], no commit since the task
         was created - i.e. nothing was pushed by this task.
  AC-04  NOT here: the gate (sdd-test.ps1 build + test) and the standalone PyYAML
         validation are executed as separate commands and recorded as evidence.

Not an AC, printed as INFO only: runs-on (ubuntu-only is the human decision of the
humanGate, listed in the spec, not in the AC list).

Exit codes: 0 = every executed check passed, 1 = at least one FAIL.
Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0009.ps1 -CiPath <copy.yml>
  (the second form is the mutation proof: the same checks must FAIL on a broken copy)
#>
[CmdletBinding()]
param(
  [string]$CiPath = '',
  [string]$RepoRoot = ''
)
$ErrorActionPreference = 'Continue'

if (-not $RepoRoot) { $RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path }
if (-not $CiPath) { $CiPath = Join-Path $RepoRoot '.github\workflows\ci.yml' }

$script:fail = 0
$script:total = 0

function Check([string]$Id, [bool]$Ok, [string]$Detail) {
  $script:total++
  if ($Ok) { Write-Output "PASS $Id - $Detail" }
  else { Write-Output "FAIL $Id - $Detail"; $script:fail++ }
}
function Info([string]$Id, [string]$Detail) { Write-Output "INFO $Id - $Detail" }

Write-Output "repo-root: $RepoRoot"
Write-Output "ci-path:   $CiPath"

# ---------------------------------------------------------------- AC-01/02/03 via PyYAML
# The python body is written to %TEMP% at run time (ASCII only) and executed with the
# same interpreter DEV validated: py -V:Astral\CPython3.13.18 (PyYAML 6.0.3).
$pyBody = @'
import os, re, sys
import yaml

ci, root = sys.argv[1], sys.argv[2]
state = {"fail": 0, "total": 0}

def check(cid, ok, detail):
    state["total"] += 1
    print(("PASS " if ok else "FAIL ") + cid + " - " + detail)
    if not ok:
        state["fail"] += 1

raw = open(ci, "r", encoding="utf-8").read()
try:
    doc = yaml.safe_load(raw)
except Exception as e:
    print("FAIL AC01-parse - YAML invalid: " + " ".join(str(e).split())[:200])
    sys.exit(1)
check("AC01-parse", isinstance(doc, dict), "PyYAML %s parsed %s without error" % (yaml.__version__, os.path.basename(ci)))

# YAML 1.1: a bare `on` key is read as boolean True.
on = doc.get("on", doc.get(True)) if isinstance(doc, dict) else None
push = on.get("push") if isinstance(on, dict) else None
branches = (push or {}).get("branches") if isinstance(push, dict) else None
check("AC01-push", sorted(branches or []) == ["develop", "main"], "push branches = %s (need exactly develop+main)" % (branches,))
has_pr = isinstance(on, dict) and "pull_request" in on
check("AC01-pr", has_pr, "pull_request trigger present = %s" % has_pr)
perms = doc.get("permissions") if isinstance(doc, dict) else None
check("AC01-perms", perms == {"contents": "read"}, "permissions = %s (need contents: read only)" % (perms,))

jobs = doc.get("jobs") if isinstance(doc, dict) else None
jobs = jobs if isinstance(jobs, dict) else {}
steps = []
runs_on = []
for job in jobs.values():
    if isinstance(job, dict):
        runs_on.append(str(job.get("runs-on")))
        for s in (job.get("steps") or []):
            if isinstance(s, dict):
                steps.append(s)
runs = [s.get("run") for s in steps if isinstance(s.get("run"), str)]
expected = [
    "pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 build",
    "pwsh -NoProfile -File docs/sdd/_tools/sdd-test.ps1 test",
]
check("AC02-commands", runs == expected, "run steps = %s (need exactly the local gate build+test)" % (runs,))
coe = len(re.findall(r"continue-on-error", raw, re.I))
check("AC02-no-coe", coe == 0, "continue-on-error occurrences = %d" % coe)
pats = [
    ("secrets-ref", r"secrets\."),
    ("github-token", r"GITHUB_TOKEN"),
    ("gh-token", r"\bgh[pousr]_[A-Za-z0-9]{10,}"),
    ("aws-key", r"\bAKIA[0-9A-Z]{16}\b"),
    ("private-key", r"-----BEGIN [A-Z ]*PRIVATE KEY"),
    ("password", r"\b(password|passwd)\b"),
    ("api-key", r"\b(api[_-]?key|apikey)\b"),
    ("token", r"\btoken\b"),
    ("credential", r"credential"),
]
hits = []
for nm, pat in pats:
    for m in re.finditer(pat, raw, re.I):
        hits.append(nm + ":L%d" % (raw[:m.start()].count("\n") + 1))
check("AC02-no-secret", len(hits) == 0, "secret-pattern hits = %d %s" % (len(hits), hits[:6]))

sd = [s for s in steps if str(s.get("uses", "")).startswith("actions/setup-dotnet")]
ver = None
if sd:
    ver = (sd[0].get("with") or {}).get("dotnet-version")
pin = isinstance(ver, str) and re.fullmatch(r"\d+\.\d+\.\d+", ver) is not None and ver.split(".")[0] == "10"
check("AC03-pin", pin, "dotnet-version = %r (exact x.y.z pin, .NET 10)" % (ver,))

cache = [s for s in steps if str(s.get("uses", "")).startswith("actions/cache")]
ckey = None
if cache:
    ckey = (cache[0].get("with") or {}).get("key")
pkg = os.path.join(root, "backend", "Directory.Packages.props")
deriv = isinstance(ckey, str) and "hashFiles('**/Directory.Packages.props')" in ckey
check("AC03-cache", deriv and os.path.isfile(pkg), "cache key = %r ; Directory.Packages.props exists = %s" % (ckey, os.path.isfile(pkg)))

print("INFO runner - runs-on = %s (ubuntu-only is the humanGate decision, not an AC)" % (runs_on,))
print("SUMMARY py-total=%d py-fails=%d" % (state["total"], state["fail"]))
sys.exit(1 if state["fail"] else 0)
'@

$pyFile = Join-Path $env:TEMP 'qa0009-yaml-check.py'
[System.IO.File]::WriteAllText($pyFile, $pyBody, (New-Object System.Text.UTF8Encoding($false)))
$pyOut = & py '-V:Astral\CPython3.13.18' $pyFile $CiPath $RepoRoot 2>&1
$pyExit = $LASTEXITCODE
foreach ($l in $pyOut) { Write-Output "$l" }
$pyFailCount = @($pyOut | Where-Object { "$_" -like 'FAIL *' }).Count
$pyPassCount = @($pyOut | Where-Object { "$_" -like 'PASS *' }).Count
$script:total += ($pyFailCount + $pyPassCount)
$script:fail += $pyFailCount
if ($pyExit -ne 0 -and $pyFailCount -eq 0) {
  Check 'AC01-parse' $false "PyYAML runner exited $pyExit without a PASS/FAIL line"
}
Info 'AC04-pyyaml' "standalone validation executed as a separate command (see evidence); this run's py exit = $pyExit"

# ---------------------------------------------------------------- AC-02 local-gate identity
$gatePath = Join-Path $RepoRoot 'docs\sdd\_tools\sdd-test.ps1'
$gateOk = Test-Path -LiteralPath $gatePath
$gateTxt = ''
if ($gateOk) { $gateTxt = Get-Content -LiteralPath $gatePath -Raw }
Check 'AC02-localgate' ($gateOk -and ($gateTxt -match "ValidateSet\('probe', 'build', 'test'")) `
  "local gate is the SAME script docs/sdd/_tools/sdd-test.ps1 with modes build and test"
Info 'AC02-shell' 'workflow shell is pwsh -NoProfile (GAP-17, ubuntu runner); local gate runs powershell 5.1 - identical script and identical build/test args, shell binary differs by platform decision'

# ---------------------------------------------------------------- AC-05 delta of the change
$ref = [datetime]'2026-11-06 23:18:15'   # task created (window start)
$allow = @(
  '^\.github/',
  '^docs/sdd/plan/plan\.md$',
  '^docs/sdd/tasks/TASK-0009',
  '^docs/sdd/specs/SPEC-0005',
  '^docs/sdd/qa/QA-0008',
  '^docs/sdd/qa/qa-check-task-0009'
)
$stLines = @()
$stRc = 0
$stOut = & git -C $RepoRoot status --porcelain 2>&1
$stRc = $LASTEXITCODE
if ($stRc -eq 0) { $stLines = @($stOut) }
$newOutside = New-Object System.Collections.Generic.List[string]
$flowArts = New-Object System.Collections.Generic.List[string]
if ($stRc -ne 0) {
  Check 'AC05-delta' $false "git status failed (exit $stRc): $stOut"
}
else {
  foreach ($line in $stLines) {
    $line = "$line"
    if ($line.Length -lt 4) { continue }
    $path = $line.Substring(3).Trim('"')
    $full = Join-Path $RepoRoot $path
    $targets = @()
    if (Test-Path -LiteralPath $full -PathType Container) {
      $targets = @(Get-ChildItem -LiteralPath $full -Recurse -File | ForEach-Object { $_.FullName.Substring($RepoRoot.Length + 1) -replace '\\', '/' })
    }
    elseif (Test-Path -LiteralPath $full -PathType Leaf) { $targets = @($path) }
    foreach ($t in $targets) {
      $tf = Join-Path $RepoRoot ($t -replace '/', '\')
      if (-not (Test-Path -LiteralPath $tf -PathType Leaf)) { continue }
      $mtime = (Get-Item -LiteralPath $tf).LastWriteTime
      if ($mtime -lt $ref) { continue }   # pre-existing change from an earlier task: not this delta
      $isAllow = $false
      foreach ($a in $allow) { if ($t -match $a) { $isAllow = $true; break } }
      $entry = '{0} ({1})' -f $t, $mtime.ToString('HH:mm:ss')
      if ($isAllow) { $flowArts.Add($entry) } else { $newOutside.Add($entry) }
    }
  }
  Check 'AC05-delta' ($newOutside.Count -eq 0) "files changed after 2026-11-06 23:18:15 outside touches = $($newOutside.Count) $($newOutside -join '; ')"
  Info 'AC05-flow' "in-window writes by ODEV/QA flow (allowed, listed): $($flowArts -join '; ')"
}

# ---------------------------------------------------------------- AC-06 no push
$head = ''
$odev = ''
$cnt = ''
$ahead = $false
$newCommits = @()
$headDate = ''
try {
  $head = (& git -C $RepoRoot rev-parse HEAD) -join ''
  $odev = (& git -C $RepoRoot rev-parse origin/develop) -join ''
  $cnt = ((& git -C $RepoRoot rev-list --left-right --count HEAD...origin/develop) -join ' ').Trim()
  $sb = ((& git -C $RepoRoot status -sb) -split "`n")[0]
  $ahead = ($sb -match '\[ahead ')
  $newCommits = @(& git -C $RepoRoot log --format=%H --since='2026-11-06 23:18:15')
  $headDate = ((& git -C $RepoRoot log -1 --format=%ci) -join '')
}
catch {
  Check 'AC06-sync' $false "git failed: $_"
}
if ($head) {
  Check 'AC06-sync' (($head -eq $odev) -and ($cnt -eq "0`t0") -and (-not $ahead)) `
    "HEAD=$($head.Substring(0, 7)) origin/develop=$($odev.Substring(0, 7)) left-right=$cnt ahead=$ahead status='$sb'"
  Check 'AC06-nopush' (($newCommits.Count -eq 0) -and ($headDate -lt '2026-11-06 23:18:15')) `
    "commits since task creation = $($newCommits.Count); HEAD commit = $headDate (predates the task = no push)"
}

Write-Output "SUMMARY total=$($script:total) fails=$($script:fail)"
if ($script:fail -eq 0) {
  Write-Output 'RESULT: PASS - all executed checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $($script:fail) of $($script:total) checks failed"
exit 1
