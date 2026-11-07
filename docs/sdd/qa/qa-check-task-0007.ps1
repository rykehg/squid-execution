#Requires -Version 5.1
<#
qa-check-task-0007 - static half of TASK-0007's acceptance criteria. Nothing here starts a host:
  1. GAP-12: enum names in backend/src/Squid.Domain/ContainerPolicy.cs match data-model section 2.3
     EXACTLY - the expected lists are parsed from specs/001-squid-mvp/data-model.md, never remembered.
  2. GAP-11: IPolicyEvaluator.Evaluate takes the role (ContainerRole), not an Actor aggregate.
  3. AC-04: PolicyEvaluatorTests.cs asserts exactly 5 roles x 8 actions = 40 unique matrix cells and
     every expected value equals an independent oracle built from data-model section 3.2 hierarchy,
     section 2.4 (Guest never writes whatever the policy says) and FR-014 (join is not a write).
  4. AC-06 static: Squid.Domain sources and csproj carry no Orleans / JSON-LD / I/O reference.
  5. AC-01/AC-02 static: the record carries the six fields, and the constructor rejects
     SinglePublisher with empty PublisherIds (the executed proof of AC-02 is the mutation run).
  6. Gate logs: DEV's claimed log and QA's newest gate log both exit 0 with total 97 (42+55).

Usage:
  powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0007.ps1
Exit: 0 = all checks green - 1 = at least one check failed
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$failed = 0

function Check([bool]$Ok, [string]$Name, [string]$Detail) {
  if ($Ok) { Write-Output "PASS | $Name | $Detail" }
  else { Write-Output "FAIL | $Name | $Detail"; $script:failed++ }
}

$policyPath = Join-Path $repo 'backend\src\Squid.Domain\ContainerPolicy.cs'
$evalPath   = Join-Path $repo 'backend\src\Squid.Domain\IPolicyEvaluator.cs'
$testsPath  = Join-Path $repo 'backend\tests\Squid.Tests\Domain\PolicyEvaluatorTests.cs'
$modelPath  = Join-Path $repo 'specs\001-squid-mvp\data-model.md'

$policySrc  = Get-Content -LiteralPath $policyPath -Raw
$evalSrc    = Get-Content -LiteralPath $evalPath -Raw
$testsSrc   = Get-Content -LiteralPath $testsPath -Raw
$model      = Get-Content -LiteralPath $modelPath -Raw

# ---------------------------------------------------------------- 1. GAP-12 enum names
# Expected values come from the section 2.3 table of the data model (the data contract prevails
# over doc 05's lowercase table, per GAP-12).
$expectedEnums = @{}
foreach ($field in 'Posting', 'Visibility', 'Join') {
  $rowPattern = '(?m)^\|\s*`' + $field + '`\s*\|\s*enum\s*\|\s*(?<vals>.*)\|\s*$'
  $row = [regex]::Match($model, $rowPattern)
  if (-not $row.Success) { Check $false "data-model 2.3 row for $field" 'row not found in data-model.md'; continue }
  $cell = $row.Groups['vals'].Value
  $backticked = [regex]::Match($cell, '`(?<v>[^`]+)`')
  $raw = if ($backticked.Success) { $backticked.Groups['v'].Value } else { $cell }
  $values = @($raw -split '\\\|' | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
  $expectedEnums[$field] = $values
}

$declaredEnums = @{}
foreach ($m in [regex]::Matches($policySrc, 'public\s+enum\s+(?<n>\w+)\s*\{(?<b>[^}]*)\}')) {
  $members = @($m.Groups['b'].Value -split ',' | ForEach-Object { ($_ -replace '//.*$', '').Trim() } |
    Where-Object { $_ -match '^\w+$' })
  $declaredEnums[$m.Groups['n'].Value] = $members
}

$nameMap = @{ Posting = 'PostingPolicy'; Visibility = 'VisibilityPolicy'; Join = 'JoinPolicy' }
foreach ($field in 'Posting', 'Visibility', 'Join') {
  if (-not $expectedEnums.ContainsKey($field)) { continue }
  $typeName = $nameMap[$field]
  $expected = $expectedEnums[$field]
  if (-not $declaredEnums.ContainsKey($typeName)) { Check $false "GAP-12 $typeName declared" "$typeName not found in ContainerPolicy.cs"; continue }
  $actual = $declaredEnums[$typeName]
  $same = ($expected.Count -eq $actual.Count)
  if ($same) { for ($i = 0; $i -lt $expected.Count; $i++) { if ($expected[$i] -cne $actual[$i]) { $same = $false } } }
  Check $same "GAP-12 $typeName equals data-model 2.3" ("expected=[{0}] actual=[{1}]" -f ($expected -join ','), ($actual -join ','))
}

# ---------------------------------------------------------------- 2. GAP-11 signature
$signatureOk = $evalSrc -match 'PolicyDecision\s+Evaluate\s*\(\s*PolicyAction\s+action\s*,\s*ContainerRole\s+role\s*,\s*ContainerPolicy\s+policy\s*\)'
Check $signatureOk 'GAP-11 Evaluate(action, role, policy)' 'IPolicyEvaluator.cs declares Evaluate(PolicyAction, ContainerRole, ContainerPolicy)'
Check (-not ($evalSrc -match '\bActor\s+\w+\s*[,)]')) 'GAP-11 no Actor parameter in Domain' 'no "Actor <name>" parameter appears in IPolicyEvaluator.cs'

# ---------------------------------------------------------------- 3. AC-04 matrix oracle
$rank = @{ Guest = 0; Member = 1; Moderator = 2; Admin = 3; Owner = 4 }
function Get-OracleExpected([string]$role, [string]$action) {
  switch ($action) {
    { $_ -in 'Post', 'Comment', 'React', 'Share', 'Attach' } {
      # permissive policy (Everyone gate => Member rank) AND data-model 2.4: Guest never writes.
      return (($rank[$role] -ge 1) -and ($role -ne 'Guest'))
    }
    'Join'     { return $true }   # FR-014: Join=Open admits entry; entering is not a write action
    'Moderate' { return ($rank[$role] -ge 2) }   # data-model 3.2: Moderator and above
    'Admin'    { return ($rank[$role] -ge 3) }   # data-model 3.2: Admin and above
  }
  return $null
}

$cells = @()
foreach ($m in [regex]::Matches($testsSrc, 'InlineData\(ContainerRole\.(?<r>\w+)\s*,\s*PolicyAction\.(?<a>\w+)\s*,\s*(?<e>true|false)\s*\)')) {
  $cells += [pscustomobject]@{ Role = $m.Groups['r'].Value; Action = $m.Groups['a'].Value; Expected = ($m.Groups['e'].Value -eq 'true') }
}
Check ($cells.Count -eq 40) 'AC-04 exactly 40 matrix cells' ("cells={0} (5 roles x 8 actions)" -f $cells.Count)

$unique = @($cells | ForEach-Object { "$($_.Role)/$($_.Action)" } | Sort-Object -Unique)
Check ($unique.Count -eq 40) 'AC-04 40 unique role/action cells' ("unique={0}" -f $unique.Count)

$roles    = @($cells | ForEach-Object { $_.Role } | Sort-Object -Unique)
$actions  = @($cells | ForEach-Object { $_.Action } | Sort-Object -Unique)
Check (($roles -join ',') -eq 'Admin,Guest,Member,Moderator,Owner') 'AC-04 the five roles' ($roles -join ',')
Check ($actions.Count -eq 8 -and $actions -contains 'Join' -and $actions -contains 'Moderate' -and $actions -contains 'Admin') 'AC-04 the eight actions' ($actions -join ',')

# The matrix runs under the permissive policy: prove it from the source instead of assuming it.
$permissive = [regex]::Match($testsSrc, 'private\s+static\s+ContainerPolicy\s+Permissive\s*\([^)]*\)\s*=>\s*new\s*\((?<b>.*?)\);', 'Singleline')
$permissiveOk = $permissive.Success
if ($permissiveOk) {
  $body = $permissive.Groups['b'].Value
  $everyoneCount = ([regex]::Matches($body, 'PostingPolicy\.Everyone')).Count
  $permissiveOk = ($everyoneCount -ge 5 -and $body -match 'JoinPolicy\.Open')
  Check $permissiveOk 'AC-04 matrix runs under a permissive policy' ("Everyone gates={0}, Join=Open present={1}" -f $everyoneCount, ($body -match 'JoinPolicy\.Open'))
} else {
  Check $false 'AC-04 matrix runs under a permissive policy' 'Permissive() helper not found'
}

$mismatch = 0
foreach ($c in $cells) {
  $oracle = Get-OracleExpected $c.Role $c.Action
  if ($null -eq $oracle -or $oracle -ne $c.Expected) {
    $mismatch++
    Write-Output "       mismatch: $($c.Role)/$($c.Action) test=$($c.Expected) oracle=$oracle"
  }
}
Check ($mismatch -eq 0) 'AC-04 every cell equals the hierarchy oracle' ("mismatches={0}" -f $mismatch)

# ---------------------------------------------------------------- 4. AC-06 static
# Product sources only: obj/ and bin/ hold SDK-generated files (GlobalUsings.g.cs lists the
# implicit usings of the SDK, including System.IO) - a reference lives in what we write.
$domainFiles = @(Get-ChildItem -Path (Join-Path $repo 'backend\src\Squid.Domain') -Recurse -Filter *.cs -File |
  Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })
$forbidden = 'Orleans', 'JsonLd', 'JSON-LD', 'Newtonsoft', 'System\.IO', 'HttpClient', '\bFileStream\b', '\bStreamReader\b', '\bStreamWriter\b', 'Directory\.', 'File\.(Read|Write|Open|Create|Delete)', '\bPath\.(Combine|GetFullPath|GetDirectoryName)'
$hits = @()
foreach ($f in $domainFiles) {
  $lines = Get-Content -LiteralPath $f.FullName
  for ($i = 0; $i -lt $lines.Count; $i++) {
    foreach ($p in $forbidden) { if ($lines[$i] -match $p) { $hits += "$($f.Name):$($i + 1) [$p]" } }
  }
}
Check ($hits.Count -eq 0) 'AC-06 Domain sources free of Orleans/JSON-LD/I-O' ("files={0} hits={1}" -f $domainFiles.Count, $hits.Count)
foreach ($h in $hits) { Write-Output "       hit: $h" }

$csprojPath = Join-Path $repo 'backend\src\Squid.Domain\Squid.Domain.csproj'
$csprojHits = @(Select-String -LiteralPath $csprojPath -Pattern '<PackageReference|<ProjectReference')
Check ($csprojHits.Count -eq 0) 'AC-06 Domain csproj declares no references' ("hits={0} (XML elements, comments ignored)" -f $csprojHits.Count)

# ---------------------------------------------------------------- 5. AC-01 / AC-02 static shape
foreach ($prop in 'Posting', 'Visibility', 'Join', 'Interaction', 'PublisherIds', 'Version') {
  $present = $policySrc -match "public\s+\w+(<[^>]+>)?\s+$prop\s*\{\s*get;"
  Check $present "AC-01 field $prop exists on ContainerPolicy" 'property with getter only'
}
Check ($policySrc -match 'static\s+ContainerPolicy\s+Group\s*\(') 'AC-01 Group preset exists' 'ContainerPolicy.Group()'
Check ($policySrc -match 'static\s+ContainerPolicy\s+Community\s*\(') 'AC-01 Community preset exists' 'ContainerPolicy.Community()'
Check ($policySrc -match 'posting\s*==\s*PostingPolicy\.SinglePublisher\s*&&\s*publisherIds\.Count\s*==\s*0' -and $policySrc -match 'throw new ArgumentException\("SinglePublisher') `
  'AC-02 constructor rejects empty SinglePublisher' 'ContainerPolicy.cs ctor: SinglePublisher && Count==0 -> throw'

# ---------------------------------------------------------------- 6. gate logs
$runFolder = Join-Path $repo 'docs\sdd\runs\test-runs'
$gateLogs = @(Get-ChildItem -LiteralPath $runFolder -Filter '*-test-gate.log' -File | Sort-Object Name -Descending)
Check ($gateLogs.Count -gt 0) 'a gate log exists' ("count={0}" -f $gateLogs.Count)
if ($gateLogs.Count -gt 0) {
  $newest = $gateLogs[0]
  $text = Get-Content -LiteralPath $newest.FullName -Raw
  $exit = if ($text -match '(?m)^exit:\s+(-?\d+)') { [int]$Matches[1] } else { $null }
  $total = if ($text -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
  Check ($exit -eq 0 -and $total -eq 97) 'newest gate log exit 0 with 97/97' ("{0} exit={1} aprovado={2}" -f $newest.Name, $exit, $total)
}
$devClaim = Join-Path $runFolder '20261106-174304-test-gate.log'
if (Test-Path -LiteralPath $devClaim) {
  $text = Get-Content -LiteralPath $devClaim -Raw
  $exit = if ($text -match '(?m)^exit:\s+(-?\d+)') { [int]$Matches[1] } else { $null }
  $total = if ($text -match 'Aprovado:\s+(\d+)') { [int]$Matches[1] } else { $null }
  Check ($exit -eq 0 -and $total -eq 97) 'DEV claimed gate log 20261106-174304 exit 0 with 97/97' ("exit={0} aprovado={1}" -f $exit, $total)
} else {
  Check $false 'DEV claimed gate log exists' 'docs/sdd/runs/test-runs/20261106-174304-test-gate.log missing'
}

if ($failed -eq 0) {
  Write-Output 'RESULT: PASS - all checks green'
  exit 0
}
Write-Output "RESULT: FAIL - $failed check(s) failed"
exit 1
