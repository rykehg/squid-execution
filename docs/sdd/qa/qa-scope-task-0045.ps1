# QA-0045 scope scan (AC-07): proves that nothing outside the task's touches changed and
# that every product file the mutation driver touched is byte-identical to HEAD again.
# - blob-identity: git hash-object (worktree) vs git rev-parse HEAD:<path> for the 3 product
#   files the cycles mutated - S-16/S-23 restored byte-exact.
# - mtime since the baseline run (the pre-write filter log): exactly the 3 restored files
#   plus the QA's own new test file, nothing else in backend/**.cs (bin/obj excluded).
# - residue: the mutant tokens of every substitution that leaves a literal behind.
# - git status: backend has exactly 1 entry (the new QA test, inside touches); every other
#   changed path is the pre-existing ODEV task-file WIP from the baseline or a QA artifact.
# ASCII-only on purpose (Windows PowerShell 5.1 without a UTF-8 BOM).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0045.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outPath = Join-Path $PSScriptRoot 'QA-0045-scope.log'
$lines = New-Object System.Collections.Generic.List[string]

# The baseline: this QA's own pre-write run of the federation filter.
$baselineLog = Join-Path $repo 'docs\sdd\runs\test-runs\20261114-010209-test-filter.log'
$baseline = (Get-Item -LiteralPath $baselineLog).LastWriteTime
$lines.Add("baseline=$($baseline.ToString('yyyy-MM-dd HH:mm:ss')) ($([System.IO.Path]::GetFileName($baselineLog)))")

# 1. blob-identity of every product file the cycles mutated.
$mutated = @(
    'backend\src\Squid.Protocols\ActivityPub\ObjectJsonLd.cs',
    'backend\src\Squid.Service\OutboundPipeline.cs',
    'backend\src\Squid.Api\Federation\FederationEndpoints.cs'
)
foreach ($rel in $mutated) {
    $work = (git -C $repo hash-object (Join-Path $repo $rel) | Out-String).Trim()
    $head = (git -C $repo rev-parse ("HEAD:" + ($rel -replace '\\', '/')) | Out-String).Trim()
    $lines.Add("blob work=$work head=$head match=$($work -eq $head) file=$rel")
}

# 2. mtime since the baseline: the restored 3 plus the QA test, nothing else in the sources.
$csFiles = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $baseline }
foreach ($f in $csFiles) {
    $rel = $f.FullName.Substring($repo.Length + 1)
    $lines.Add("mtime $rel = $($f.LastWriteTime.ToString('HH:mm:ss'))")
}
$lines.Add("mtime-count=$($csFiles.Count) (esperado 4: 3 mutados/restaurados + QaTask0045FanOutGapTests.cs)")

# 3. Residue of the mutant tokens that leave a literal behind.
$tokens = @(
    'qa0045-broken',
    '// QA-0045 T2',
    '// QA-0045 T3',
    '// QA-0045 T4',
    '// QA-0045 T5',
    '// QA-0045 T6',
    '// QA-0045 T7',
    '// QA-0045 T8',
    '// QA-0045 T9',
    '// QA-0045 T10',
    '_ => followers,',
    '.Where(_ => true)',
    'VisibilityPolicy.Unlisted => [.. followers,',
    'VisibilityPolicy.Public => followers,'
)
$residue = 0
foreach ($token in $tokens) {
    $found = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
        Select-String -SimpleMatch -Pattern $token
    $residue += @($found).Count
}
$lines.Add("residue=$residue (tokens=$($tokens.Count), backend .cs fora de bin/obj)")

# 4. git status: backend, then the whole tree classified against the baseline.
$backend = @(git -C $repo status --porcelain -uall -- backend)
$lines.Add("git-backend-entries=$($backend.Count)")
foreach ($e in $backend) { $lines.Add("git-backend $e") }

# The eight glob sets of the TASK-0045 touches, verbatim from the task file.
$touches = '^(backend/src/Squid\.Persistence/schema/|backend/src/Squid\.Api/Posts/|backend/src/Squid\.Api/Federation/|backend/src/Squid\.Api/Containers/|backend/src/Squid\.ReadModels/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/)'
# The ODEV work-in-progress already present in the baseline, before any QA write.
$baselineWip = @(
    'docs/sdd/tasks/TASK-0045-saida-fan-out-e-follow.md'
)
$all = @(git -C $repo status --porcelain -uall)
$outside = 0
foreach ($e in $all) {
    $path = ($e -replace '^.{3}', '').Trim('"')
    $role =
        if ($path -match $touches) { 'touches' }
        elseif ($baselineWip -contains $path) { 'baseline-odev-wip' }
        elseif ($path -match '^docs/sdd/(qa/(QA-0045|qa-[a-z]+-task-0045)|runs/test-runs/)') { 'qa-artifact' }
        else { $outside++; 'OUTSIDE' }
    $lines.Add("git-all [$role] $e")
}
$lines.Add("fora-dos-touches=$outside (0 = so touches, wip do ODEV da baseline e artefatos desta QA)")
$lines.Add("baseline-odev-wip-count=$(@($all | Where-Object { $p = ($_ -replace '^.{3}', '').Trim('"'); $baselineWip -contains $p }).Count) (esperado 1)")

$text = ($lines -join "`n")
[System.IO.File]::WriteAllText($outPath, $text, (New-Object System.Text.UTF8Encoding($false)))
$text
Write-Output "scope: $outPath"
