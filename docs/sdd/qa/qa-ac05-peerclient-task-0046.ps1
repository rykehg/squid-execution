# QA-0046 AC-05 probe: the Squid.PeerClient CLI really EXPOSES the three failure scenarios
# (500, timeout, slowness) and really PLAYS them for a local client. Throwaway loopback servers
# started from the binary itself (dotnet run -- --scenario=...), every wait bounded, every
# process tree killed in the same run. This is the CLI half of AC-05; the in-process half
# (the tests feeding the queue through these scenarios) is proven by the Federation filter.
# Process handling, paid live (QA-0046-ac05-cli-run1.log and the run that followed):
#   - Start-Process -PassThru WITH -RedirectStandardOutput returns a handle-less Process on
#     this PowerShell 5.1 (ExitCode comes back empty) and its pipe makes -Wait hang on MSBuild
#     node-reuse workers;
#   - scriptblock delegates on OutputDataReceived run on the reader thread and crash the host;
# so this version starts every child through .NET ProcessStartInfo, drains stdout ONLY after a
# bounded WaitForExit (these outputs are far below the pipe buffer), discovers a server's port
# through its own listening socket, and disables MSBuild node reuse for the children.
# ASCII-only on purpose: runs under Windows PowerShell 5.1 without a UTF-8 BOM.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-ac05-peerclient-task-0046.ps1
$ErrorActionPreference = 'Stop'

$env:MSBUILDDISABLENODEREUSE = '1'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$proj = Join-Path $repo 'backend\src\Squid.PeerClient'
$logPath = Join-Path $PSScriptRoot 'QA-0046-ac05-cli.log'
$runStart = Get-Date

$script:lines = New-Object System.Collections.Generic.List[string]
$script:checks = 0
$script:fails = 0
$script:procs = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Out([string]$line) {
    $script:lines.Add($line)
    [System.IO.File]::WriteAllLines($logPath, $script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $line
}

function Check([string]$name, [bool]$ok, [string]$detail) {
    $script:checks++
    if ($ok) { Out ('CHECK {0} OK - {1}' -f $name, $detail) }
    else { $script:fails++; Out ('CHECK {0} FAIL - {1}' -f $name, $detail) }
}

function Stop-Tree([System.Diagnostics.Process]$proc) {
    if ($null -eq $proc) { return }
    try { taskkill /T /F /PID $proc.Id 2>$null | Out-Null } catch { }
    try { $null = $proc.WaitForExit(5000) } catch { }
}

# Start one child with stdout+stderr redirected (drained only after the bounded wait - every
# output here is tiny, far below the pipe buffer, so no reader thread is needed at all).
function Start-Tool([string]$fileName, [string]$arguments) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $fileName
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)
    $script:procs.Add($proc)
    return $proc
}

# Runs a command to completion: real exit code, captured output, timed-out flag. Bounded.
function Invoke-Tool([string]$fileName, [string]$arguments, [int]$seconds) {
    $proc = Start-Tool $fileName $arguments
    $done = $proc.WaitForExit($seconds * 1000)
    $stdout = ''
    $stderr = ''
    if ($done) {
        $stdout = $proc.StandardOutput.ReadToEnd()
        $stderr = $proc.StandardError.ReadToEnd()
    }
    else {
        Stop-Tree $proc
        try { $stdout = $proc.StandardOutput.ReadToEnd() } catch { }
        try { $stderr = $proc.StandardError.ReadToEnd() } catch { }
    }
    $exit = -1
    if ($done) { $exit = $proc.ExitCode }
    return @{ Exit = $exit; TimedOut = (-not $done); StdOut = $stdout; StdErr = $stderr;
        Text = (($stdout + ' ' + $stderr) -replace '\s+', ' ').Trim() }
}

# Starts one scenario server and waits (bounded) for ITS OWN listening socket: the CLI binds a
# random loopback port, so the port comes from the Squid.PeerClient child process, never from
# parsing output that no one is draining. The per-call marker makes sure a PREVIOUS scenario's
# server (run 2 died on exactly that: a still-listening 500 answered the timeout's probe) can
# never be picked as this call's port.
function Start-Server([string]$scenarioArgs) {
    $callStart = Get-Date
    $proc = Start-Tool 'dotnet' ('run --project "' + $proj + '" -- ' + $scenarioArgs)
    $port = $null
    $dead = $false
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-Date) -lt $deadline) {
        if ($proc.HasExited) { $dead = $true; break }
        $app = @(Get-Process -Name 'Squid.PeerClient' -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $callStart.AddSeconds(-2) })
        if ($app.Count -gt 0) {
            $ids = @($app | ForEach-Object { $_.Id })
            $listen = @(Get-NetTCPConnection -State Listen -LocalAddress '127.0.0.1' -ErrorAction SilentlyContinue |
                Where-Object { $ids -contains $_.OwningProcess })
            if ($listen.Count -gt 0) {
                $port = [int]($listen | Select-Object -First 1).LocalPort
                break
            }
        }
        Start-Sleep -Milliseconds 250
    }
    return @{ Proc = $proc; Port = $port; Dead = $dead }
}

# One bounded POST to the stand-in inbox: curl's exit code, the printed http_code, elapsed ms.
function Invoke-Post([int]$port, [int]$maxTimeSec) {
    $url = 'http://127.0.0.1:' + $port + '/users/alice/inbox'
    # Line breaks keep the operator at the END of the line: PowerShell 5.1 does not accept a
    # break after an operand with a leading '+' on the next line, even inside parentheses.
    $argStr = ('-s -o NUL -w "%{http_code}" --max-time ' + $maxTimeSec +
        ' -X POST -H "Content-Type: application/activity+json" --data {} ' + $url)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $run = Invoke-Tool 'curl.exe' $argStr ([Math]::Max(30, $maxTimeSec + 20))
    $sw.Stop()
    return @{ Exit = $run.Exit; TimedOut = $run.TimedOut; Code = ($run.StdOut.Trim())
        Ms = $sw.ElapsedMilliseconds }
}

try {
    Out ('probe: task=QA-0046 ac=AC-05 project=' + $proj)

    # 1. The CLI itself exposes the three scenario tokens (the "expoe cenarios" half).
    $help = Invoke-Tool 'dotnet' ('run --project "' + $proj + '" -- --help') 120
    Check 'HELP-EXIT' ((-not $help.TimedOut) -and $help.Exit -eq 0) `
        ('dotnet run -- --help ' + $(if ($help.TimedOut) { 'NAO SAIU em 120s' } else { 'exit=' + $help.Exit }))
    Check 'HELP-TOKENS' ($help.StdOut -match '500' -and $help.StdOut -match 'timeout' -and $help.StdOut -match 'slow') `
        ('usage declara os cenarios 500/timeout/slow: ' + $help.StdOut.Trim())

    # 2. Scenario 500 - the peer answers HTTP 500 to the inbox POST.
    $s500 = Start-Server '--scenario=500'
    if ($null -eq $s500.Port) {
        Check 'SCENARIO-500' $false ('o servidor 500 nao publicou a porta em 90s; morto=' + $s500.Dead)
    }
    else {
        $r500 = Invoke-Post -port $s500.Port -maxTimeSec 10
        Check 'SCENARIO-500' ($r500.TimedOut -eq $false -and $r500.Exit -eq 0 -and $r500.Code -eq '500') `
            ('porta {0}: http={1} curl-exit={2} elapsed={3}ms' -f $s500.Port, $r500.Code, $r500.Exit, $r500.Ms)
    }
    Stop-Tree $s500.Proc # one scenario at a time: the next one must own the next port

    # 3. Scenario timeout - the peer takes the request and never answers; curl gives up bounded.
    $stimeout = Start-Server '--scenario=timeout'
    if ($null -eq $stimeout.Port) {
        Check 'SCENARIO-TIMEOUT' $false ('o servidor timeout nao publicou a porta em 90s; morto=' + $stimeout.Dead)
    }
    else {
        $rto = Invoke-Post -port $stimeout.Port -maxTimeSec 2
        Check 'SCENARIO-TIMEOUT' ($rto.TimedOut -eq $false -and $rto.Exit -eq 28 -and $rto.Code -eq '000') `
            ('porta {0}: curl-exit={1} (28 = tempo esgotado sem resposta) http={2} elapsed={3}ms' -f `
                $stimeout.Port, $rto.Exit, $rto.Code, $rto.Ms)
    }
    Stop-Tree $stimeout.Proc

    # 4. Scenario slow - the answer comes AFTER the delay the CLI itself was given.
    $sslow = Start-Server '--scenario=slow --delay=1500'
    if ($null -eq $sslow.Port) {
        Check 'SCENARIO-SLOW' $false ('o servidor slow nao publicou a porta em 90s; morto=' + $sslow.Dead)
    }
    else {
        $rslow = Invoke-Post -port $sslow.Port -maxTimeSec 10
        Check 'SCENARIO-SLOW' ($rslow.TimedOut -eq $false -and $rslow.Exit -eq 0 -and
            $rslow.Code -eq '202' -and $rslow.Ms -ge 1200) `
            ('porta {0}: http={1} elapsed={2}ms (esperado >= 1200 pelo --delay=1500)' -f `
                $sslow.Port, $rslow.Code, $rslow.Ms)
    }
    Stop-Tree $sslow.Proc
}
finally {
    foreach ($proc in @($script:procs)) { Stop-Tree $proc }
    foreach ($app in @(Get-Process -Name 'Squid.PeerClient' -ErrorAction SilentlyContinue |
            Where-Object { $_.StartTime -ge $runStart.AddSeconds(-5) })) {
        try { taskkill /F /T /PID $app.Id 2>$null | Out-Null } catch { }
    }
    $alive = 0
    foreach ($proc in @($script:procs)) {
        try { if ($proc -and -not $proc.HasExited) { $alive++ } } catch { }
    }
    $strays = @(Get-Process -Name 'Squid.PeerClient' -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $runStart.AddSeconds(-5) }).Count
    Out ('CLEANUP processos-ancora restantes=' + $alive + ' stray-app=' + $strays +
        ' temporarios=' + @(Get-ChildItem -Path $env:TEMP -Filter 'qa0046-*' -File -ErrorAction SilentlyContinue).Count)
}

Out ('SUMMARY checks={0} fails={1}' -f $script:checks, $script:fails)
if ($script:fails -gt 0) { exit 1 }
exit 0
