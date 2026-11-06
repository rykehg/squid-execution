# Verifica os criterios de aceite de TASK-0001 sem tocar em arquivo versionado.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/check-task-0001.ps1
# Idempotente: cria probes temporarios, confere e remove; segunda execucao tem o mesmo efeito.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Push-Location $root
$fail = 0
function Check([string]$Nome, [scriptblock]$Corpo) {
  try { if (& $Corpo) { Write-Host "PASS $Nome" } else { Write-Host "FAIL $Nome"; $script:fail++ } }
  catch { Write-Host "FAIL $Nome ($($_.Exception.Message))"; $script:fail++ }
}

$probes = @('backend/bin/probe.dll', 'backend/obj/probe.txt', 'web/node_modules/probe/i.js',
  'probe.db', 'probe.db-wal', 'probe.db-shm', '.env', 'ideias-references/probe/a.rb')
$tempDirs = @('backend/bin', 'backend/obj', 'web/node_modules', 'ideias-references')

Check 'AC-01 ignores' {
  foreach ($p in $probes) {
    $full = Join-Path $root $p
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
    Set-Content -LiteralPath $full -Value 'probe' -Encoding utf8
  }
  $ignored = git status --porcelain --ignored | Where-Object { $_ -like '!!*' }
  $untracked = git status --porcelain | Where-Object { $_ -match 'probe|bin/|obj/|node_modules|ideias-references' }
  $ok = ($untracked -eq $null) -and ($ignored -match 'probe\.db$').Count -eq 1 -and
        ($ignored -match '\.env$').Count -eq 1 -and ($ignored -match 'backend/bin/$').Count -eq 1
  foreach ($p in $probes) { Remove-Item -LiteralPath (Join-Path $root $p) -Force -ErrorAction SilentlyContinue }
  foreach ($d in $tempDirs) { Remove-Item -Recurse -Force (Join-Path $root $d) -ErrorAction SilentlyContinue }
  return $ok
}

Check 'AC-02 pastas reservadas' {
  $out = git add --dry-run backend/.gitkeep web/.gitkeep infra/.gitkeep 2>&1
  ($out | Where-Object { $_ -match "add '(backend|web|infra)/\.gitkeep'" }).Count -eq 3
}

Check 'AC-03 gitattributes' {
  $png = git check-attr text -- x.png
  $md = git check-attr eol -- x.md
  $ps = git check-attr eol -- x.ps1
  ($png -match 'text: unset') -and ($md -match 'eol: lf') -and ($ps -match 'eol: crlf')
}

Check 'AC-04 README sem caminho externo' {
  $readme = Get-Content (Join-Path $root 'README.md') -Raw
  ($readme.Length -gt 300) -and -not ($readme -match 'squid-specs|ideias-references')
}

Pop-Location
Write-Host "$($probes.Count) probes limpos; $fail falha(s)."
if ($fail -gt 0) { exit 1 }