# QA-0056 runbook spot-check (TASK-0056 AC-07): executes the EXECUTABLE sections of
# docs/runbooks/restaurar-backup.md myself - section A (session, backup/run, status), section B
# (restore by the binary: success exit 0 AND the documented wrong-key refusal exit 1) and the
# Verifications 1, 2 and 4 - against a throwaway service on 127.0.0.1:8080 with a fresh RK, a
# fresh database and a fresh volume at the repository root, all created and removed by this
# script. Section C (podman cycle, P-01) is NOT run here: it needs the container stack and is
# outside the "geracao/restore" stretch AC-07 names; it stays as the DEV left it.
# ASCII-only on purpose: runs under Windows PowerShell 5.1 without a UTF-8 BOM.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-runbook-task-0056.ps1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Continue'

$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
$ledgerPath = Join-Path $PSScriptRoot 'QA-0056-runbook.log'
$serviceDll = Join-Path $repo 'backend\src\Squid.Service\bin\Debug\net10.0\Squid.Service.dll'
$dbFile = Join-Path $repo 'squid-grainstate.db'
$backupDir = Join-Path $repo 'backups'
$serviceOut = Join-Path $env:TEMP 'qa0056-service-out.log'
$serviceErr = Join-Path $env:TEMP 'qa0056-service-err.log'
$handle = 'operador'
$Password = 'qa-runbook-2026'   # test credential, never a real one

$script:lines = New-Object System.Collections.Generic.List[string]
$script:fails = 0
$script:service = $null

function Log([string]$Line) {
    $script:lines.Add($Line)
    [System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Output $Line
}

function Check([string]$What, [bool]$Ok, [string]$Detail) {
    if ($Ok) { Log ("OK   {0} :: {1}" -f $What, $Detail) }
    else {
        Log ("FAIL {0} :: {1}" -f $What, $Detail)
        $script:fails++
    }
}

function Stop-Service {
    if ($script:service -and -not $script:service.HasExited) {
        try { $script:service.Kill($true) } catch { try { $script:service.Kill() } catch { } }
        $script:service.WaitForExit(15000) | Out-Null
        Log 'service parado'
    }
    $script:service = $null
}

Log ('=== QA-0056 runbook spot-check - {0} - cwd {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $repo)

# ---------------------------------------------------------------- preconditions
Check 'pre-condicao binario' (Test-Path -LiteralPath $serviceDll) $serviceDll
if (-not (Test-Path -LiteralPath $serviceDll)) { Log 'SEM BINARIO - abortando'; [System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false))); exit 1 }

$dirtyDb = Test-Path -LiteralPath $dbFile
$dirtyBk = Test-Path -LiteralPath $backupDir
Check 'estado limpo na raiz' ((-not $dirtyDb) -and (-not $dirtyBk)) ("db-existe={0} backups-existe={1} (esperado False/False; nada aqui foi criado por mim - aborto, nao apago)" -f $dirtyDb, $dirtyBk)
if ($dirtyDb -or $dirtyBk) { [System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false))); exit 1 }

$listener = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
Check 'porta 8080 livre' ($null -eq $listener) ('listeners={0}' -f $(if ($listener) { @($listener).Count } else { 0 }))
if ($listener) { [System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false))); exit 1 }

# Fresh RK: 32 bytes from the platform CSPRNG, base64 the way Squid:RecoveryKey is configured.
$rkBytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($rkBytes)
$env:SQUID_RK = [Convert]::ToBase64String($rkBytes)
$wrongBytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($wrongBytes)
$env:QA_WRONG_RK = [Convert]::ToBase64String($wrongBytes)
Log 'RK gerada (32 bytes CSPRNG, valor NUNCA registrado aqui) e RK errada de controle gerada'

# ---------------------------------------------------------------- start the service (A.0)
# $opArg is computed BEFORE the array: inside @() the comma binds tighter than '+', so
# '--x=' + $h, would split into two arguments and hand the service an EMPTY Instance:Operators
# (attempt 1 of this script hit exactly that - QA-0056-runbook-attempt1.log).
$opArg = '--Instance:Operators=' + $handle
$env:ASPNETCORE_URLS = 'http://127.0.0.1:8080'
$env:Squid__RecoveryKey = $env:SQUID_RK
$script:service = Start-Process -FilePath 'dotnet' -ArgumentList @(
    'backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll',
    '--Storage:Path=./squid-grainstate.db',
    '--Backup:Local:Path=./backups',
    $opArg,
    '--Backup:IntervalMinutes=0'
) -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $serviceOut -RedirectStandardError $serviceErr
Log ('servico subiu PID={0} (ASPNETCORE_URLS=http://127.0.0.1:8080, Instance:Operators={1})' -f $script:service.Id, $handle)

$base = 'http://127.0.0.1:8080'
$healthOk = $false
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline -and -not $healthOk) {
    Start-Sleep -Milliseconds 1500
    $h = & curl.exe -fsS "$base/health" 2>$null
    if ($LASTEXITCODE -eq 0) { $healthOk = $true }
}
Check 'health do servico' $healthOk ("curl -fsS http://localhost:8080/health exit={0}" -f $LASTEXITCODE)
if (-not $healthOk) { Stop-Service; [System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false))); exit 1 }

# ---------------------------------------------------------------- runbook A.1 - session
# The runbook block verbatim, except the password source: Read-Host cannot be automated, so
# the value comes from this script (the runbook itself says it never appears in the document).
$body = @{ handle = $handle; password = $Password } | ConvertTo-Json
$register = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base/api/auth/register" `
    -ContentType 'application/json' -Body $body -ErrorAction SilentlyContinue
$registerCode = if ($register) { [int]$register.StatusCode } else { 0 }
$header = ($register.Headers['Set-Cookie'] -join ',')
$session = [regex]::Match($header, 'squid_session=([^;,\s]+)').Groups[1].Value
$csrf = [regex]::Match($header, 'squid_csrf=([^;,\s]+)').Groups[1].Value
$cookie = "squid_session=$session; squid_csrf=$csrf"
Check 'A.1 register' (($registerCode -eq 201) -and $session -and $csrf) ("register={0} session-existe={1} csrf-existe={2}" -f $registerCode, [bool]$session, [bool]$csrf)

# ---------------------------------------------------------------- runbook A.2 - backup/run
$runBody = & curl.exe -sS -X POST "$base/api/admin/backup/run" -H "Cookie: $cookie" -H "X-CSRF-Token: $csrf" 2>&1 | Out-String
$runExit = $LASTEXITCODE
Check 'A.2 backup/run' (($runExit -eq 0) -and ($runBody -match '"status":"Verified"') -and ($runBody -match '"seqFrom":')) ("exit={0} Verified={1} seqFrom={2}" -f $runExit, ($runBody -match '"status":"Verified"'), ($runBody -match '"seqFrom":'))
Log ('A.2 body={0}' -f $runBody.Trim())

# ---------------------------------------------------------------- runbook A.3 - status
$statusBody = & curl.exe -sS "$base/api/admin/backup/status" -H "Cookie: $cookie" 2>&1 | Out-String
$statusExit = $LASTEXITCODE
Check 'A.3 status' (($statusExit -eq 0) -and ($statusBody -match '"last_success_at"') -and ($statusBody -match '"envelopes_count"')) ("exit={0} last_success_at={1} envelopes_count={2}" -f $statusExit, ($statusBody -match '"last_success_at"'), ($statusBody -match '"envelopes_count"'))
Log ('A.3 body={0}' -f $statusBody.Trim())

# ---------------------------------------------------------------- runbook B.1 - stop
Stop-Service
Check 'B.1 parar o servico' ($true) ('PID encerrado; o arquivo de destino fica livre')

# ---------------------------------------------------------------- runbook B.2 - remove
Remove-Item ./squid-grainstate.db -ErrorAction SilentlyContinue
Check 'B.2 Remove-Item' (-not (Test-Path -LiteralPath $dbFile)) ('arquivo ausente depois do B.2: {0}' -f (-not (Test-Path -LiteralPath $dbFile)))

# ---------------------------------------------------------------- runbook B.3 - restore, exit 0
$env:Squid__RecoveryKey = $env:SQUID_RK
$b3 = & dotnet backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll --restore `
    --Storage:Path=./squid-grainstate.db `
    --Backup:Local:Path=./backups 2>&1 | Out-String
$b3Exit = $LASTEXITCODE
Check 'B.3 restore (RK do kit)' (($b3Exit -eq 0) -and ($b3 -match 'restore: ok')) ("exit={0} stdout={1}" -f $b3Exit, ($b3.Trim() -replace '\s+', ' '))
Log ('B.3 out={0}' -f ($b3.Trim() -replace '\s+', ' '))

# ---------------------------------------------------------------- documented refusal - wrong RK, exit 1
$env:Squid__RecoveryKey = $env:QA_WRONG_RK
$wk = & dotnet backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll --restore `
    --Storage:Path=./squid-grainstate.db `
    --Backup:Local:Path=./backups 2>&1 | Out-String
$wkExit = $LASTEXITCODE
Check 'B.3 recusa com RK errada' (($wkExit -eq 1) -and ($wk -match 'restore: falhou') -and ($wk -match 'chave do kit')) ("exit={0} stderr={1}" -f $wkExit, ($wk.Trim() -replace '\s+', ' '))
Log ('B.3 wrong-key out={0}' -f ($wk.Trim() -replace '\s+', ' '))
$env:Squid__RecoveryKey = $env:SQUID_RK

# ---------------------------------------------------------------- Verificacao 1/2 - service back
$script:service = Start-Process -FilePath 'dotnet' -ArgumentList @(
    'backend/src/Squid.Service/bin/Debug/net10.0/Squid.Service.dll',
    '--Storage:Path=./squid-grainstate.db',
    '--Backup:Local:Path=./backups',
    $opArg,
    '--Backup:IntervalMinutes=0'
) -WorkingDirectory $repo -PassThru -WindowStyle Hidden `
    -RedirectStandardOutput $serviceOut -RedirectStandardError $serviceErr
Log ('servico (2) subiu PID={0} sobre o banco restaurado' -f $script:service.Id)

$healthOk = $false
$readyOk = $false
$deadline = (Get-Date).AddSeconds(60)
while ((Get-Date) -lt $deadline -and -not $healthOk) {
    Start-Sleep -Milliseconds 1500
    & curl.exe -fsS "$base/health" > $null 2>&1
    if ($LASTEXITCODE -eq 0) { $healthOk = $true }
}
Check 'V.1 health' $healthOk ('curl -fsS http://localhost:8080/health exit={0}' -f $LASTEXITCODE)
& curl.exe -fsS "$base/ready" > $null 2>&1
Check 'V.2 ready' ($LASTEXITCODE -eq 0) ('curl -fsS http://localhost:8080/ready exit={0} (sobre o banco restaurado)' -f $LASTEXITCODE)

# ---------------------------------------------------------------- Verificacao 4 - new session + status
$register2 = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base/api/auth/register" `
    -ContentType 'application/json' -Body $body -ErrorAction SilentlyContinue
$register2Code = if ($register2) { [int]$register2.StatusCode } else { 0 }
$header2 = ($register2.Headers['Set-Cookie'] -join ',')
$session2 = [regex]::Match($header2, 'squid_session=([^;,\s]+)').Groups[1].Value
$csrf2 = [regex]::Match($header2, 'squid_csrf=([^;,\s]+)').Groups[1].Value
$cookie2 = "squid_session=$session2; squid_csrf=$csrf2"
Check 'V.4 sessao nova apos o restore' (($register2Code -eq 201) -and $session2) ("register={0}" -f $register2Code)

$status2 = & curl.exe -sS "$base/api/admin/backup/status" -H "Cookie: $cookie2" 2>&1 | Out-String
$status2Exit = $LASTEXITCODE
Check 'V.4 status restore: passed' (($status2Exit -eq 0) -and ($status2 -match '"last_drill_result":"restore: passed"') -and ($status2 -notmatch '"last_drill_at":null')) ("exit={0} passed={1} drill-at-preenchido={2}" -f $status2Exit, ($status2 -match 'restore: passed'), ($status2 -notmatch '"last_drill_at":null'))
Log ('V.4 body={0}' -f $status2.Trim())

# ---------------------------------------------------------------- teardown (my own files only)
Stop-Service
Remove-Item -LiteralPath $dbFile -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
$leftoverDb = Test-Path -LiteralPath $dbFile
$leftoverBk = Test-Path -LiteralPath $backupDir
Check 'teardown limpo' ((-not $leftoverDb) -and (-not $leftoverBk)) ('db={0} backups={1} (esperado False/False)' -f $leftoverDb, $leftoverBk)

Log ('SUMMARY fails={0} (secao C - ciclo podman P-01 - fora do escopo AC-07: nao executada por esta QA)' -f $script:fails)
[System.IO.File]::WriteAllLines($ledgerPath, $script:lines, (New-Object System.Text.UTF8Encoding($false)))
if ($script:fails -gt 0) { exit 1 }
exit 0
