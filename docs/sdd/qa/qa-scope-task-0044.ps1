# QA-0044 scope scan (AC-08): proves that nothing outside the task's touches changed and
# that every product file the mutation driver touched is byte-identical to HEAD again.
# - blob-identity: git hash-object (worktree) vs git rev-parse HEAD:<path> for the 7 product
#   files the cycles mutated - S-16/S-23 restored byte-exact.
# - mtime since the baseline run (the pre-write filter log): exactly the 7 restored files
#   plus the QA's own new test file, nothing else in backend/**.cs (bin/obj excluded).
# - residue: the mutant tokens of the 18 substitutions that leave a literal behind.
# - git status: backend has exactly 1 entry (the new QA test, inside touches); every other
#   changed path is a pre-existing ODEV work-in-progress from the baseline or a QA artifact.
# ASCII-only on purpose (Windows PowerShell 5.1 without a UTF-8 BOM).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0044.ps1
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$outPath = Join-Path $PSScriptRoot 'QA-0044-scope.log'
$lines = New-Object System.Collections.Generic.List[string]

# The baseline: this QA's own pre-write run of the federation filter.
$baselineLog = Join-Path $repo 'docs\sdd\runs\test-runs\20261113-225032-test-filter.log'
$baseline = (Get-Item -LiteralPath $baselineLog).LastWriteTime
$lines.Add("baseline=$($baseline.ToString('yyyy-MM-dd HH:mm:ss')) ($([System.IO.Path]::GetFileName($baselineLog)))")

# 1. blob-identity of every product file the cycles mutated.
$mutated = @(
    'backend\src\Squid.Service\InboxPipeline.cs',
    'backend\src\Squid.Service\FederationRateLimiter.cs',
    'backend\src\Squid.Service\SqliteLikeStore.cs',
    'backend\src\Squid.Api\Federation\FederationEndpoints.cs',
    'backend\src\Squid.Api\Posts\RemoteActivities.cs',
    'backend\src\Squid.Api\Posts\RemoteMediaClient.cs',
    'backend\src\Squid.Service\SqliteInboxWindowStore.cs'
)
foreach ($rel in $mutated) {
    $work = (git -C $repo hash-object (Join-Path $repo $rel) | Out-String).Trim()
    $head = (git -C $repo rev-parse ("HEAD:" + ($rel -replace '\\', '/')) | Out-String).Trim()
    $lines.Add("blob work=$work head=$head match=$($work -eq $head) file=$rel")
}

# 2. mtime since the baseline: the restored 7 plus the QA test, nothing else in the sources.
$csFiles = Get-ChildItem -LiteralPath (Join-Path $repo 'backend') -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $baseline }
foreach ($f in $csFiles) {
    $rel = $f.FullName.Substring($repo.Length + 1)
    $lines.Add("mtime $rel = $($f.LastWriteTime.ToString('HH:mm:ss'))")
}
$lines.Add("mtime-count=$($csFiles.Count) (esperado 8: 7 mutados/restaurados + QaTask0044InboxGapTests.cs)")

# 3. Residue of the mutant tokens that leave a literal behind.
$tokens = @(
    'if (false) references.AddRange',
    'if (await _inbox.RememberAsync(Recipient(target), activity.Id!))',
    'Status400BadRequest, "digest_invalid"',
    '6_291_456',
    'Status406NotAcceptable, "payload_too_deep"',
    '_ => AuthErrors.Write(context, StatusCodes.Status400BadRequest,',
    'window.Count >= int.MaxValue',
    'DefaultLimit = 600',
    '// QA-0044 T9',
    '// QA-0044 T11',
    '// QA-0044 T12',
    'case "flag_disabled"',
    'MembersOnly && await MemberRecipientAsync',
    '// QA-0044 T16',
    '// QA-0044 T17',
    'qa0044-never-matches',
    'IPAddress.TryParse(host, out var literal)) return true;',
    'SearchAsync("Comunidade", 1, 0)'
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

$touches = '^(backend/src/Squid\.Grains/Inbox/|backend/src/Squid\.Api/Federation/|backend/src/Squid\.Api/Posts/|backend/src/Squid\.Protocols/|backend/src/Squid\.Service/|backend/tests/Squid\.Tests/)'
# The ODEV work-in-progress already present in the baseline, before any QA write.
$baselineWip = @(
    'docs/sdd/ideas/BACKLOG.md',
    'docs/sdd/plan/plan.md',
    'docs/sdd/specs/SPEC-0011-federacao-p06.md',
    'docs/sdd/tasks/TASK-0044-inbox-verificacao-e-aplicacao.md',
    'docs/sdd/tasks/TASK-0045-saida-fan-out-e-follow.md',
    'docs/sdd/tasks/TASK-0050-pares-confiaveis-ssrf.md'
)
$all = @(git -C $repo status --porcelain -uall)
$outside = 0
foreach ($e in $all) {
    $path = ($e -replace '^.{3}', '').Trim('"')
    $role =
        if ($path -match $touches) { 'touches' }
        elseif ($baselineWip -contains $path) { 'baseline-odev-wip' }
        elseif ($path -match '^docs/sdd/(qa/(QA-0044|qa-[a-z]+-task-0044)|runs/test-runs/)') { 'qa-artifact' }
        else { $outside++; 'OUTSIDE' }
    $lines.Add("git-all [$role] $e")
}
$lines.Add("fora-dos-touches=$outside (0 = so touches, wip do ODEV da baseline e artefatos desta QA)")
$lines.Add("baseline-odev-wip-count=$(@($all | Where-Object { $p = ($_ -replace '^.{3}', '').Trim('"'); $baselineWip -contains $p }).Count) (esperado 6)")

$text = ($lines -join "`n")
[System.IO.File]::WriteAllText($outPath, $text, (New-Object System.Text.UTF8Encoding($false)))
$text
Write-Output "scope: $outPath"
