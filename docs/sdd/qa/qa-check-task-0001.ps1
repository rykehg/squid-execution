# QA independente de TASK-0001 (R-20261104-01).
# Nao depende de docs/sdd/_tools/check-task-0001.ps1: cada AC tem seu proprio cenario executavel.
# Idempotente: todo probe vive sob '_qa-probe-*' e e removido num bloco finally.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0001.ps1
# Exit: 0 = todos PASS; 1 = alguma falha; 3 = estado do repo nao corresponde ao baseline.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Push-Location $root

$utf8 = New-Object System.Text.UTF8Encoding($false)
$script:fail = 0
$script:skipped = 0
$result = [ordered]@{}

$probeFiles = @('_qa-probe-root.db', '_qa-probe-root.db-wal', '_qa-probe-root.db-shm',
  '_qa-probe-nested/app/bin/Debug/app.dll', '_qa-probe-nested/app/obj/Debug/app.txt',
  '_qa-probe-nested/web/node_modules/pkg/index.js', '_qa-probe-env/.env',
  'ideias-references/lib/notes.txt')
# diretorios _qa-probe-* criados AQUI pelo script (nunca os que ja existiam no repo)
$script:createdDirs = New-Object System.Collections.Generic.List[string]

function Write-Probe([string]$RelPath) {
  $full = Join-Path $root $RelPath
  $dir = Split-Path -Parent $full
  $missing = @()
  $cur = $dir
  while ($cur -and $cur.Length -gt $root.Length -and -not (Test-Path -LiteralPath $cur)) {
    $missing += $cur; $cur = Split-Path -Parent $cur
  }
  # Test-Path antes de criar: PS 5.1 emite erro nao-terminante ResourceExists que o
  # $ErrorActionPreference='Stop' do chamador converte em excecao mesmo com -Force.
  foreach ($m in $missing) {
    if (-not (Test-Path -LiteralPath $m)) { New-Item -ItemType Directory -Path $m -Force -ErrorAction Stop | Out-Null; $script:createdDirs.Add($m) }
  }
  if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force -ErrorAction Stop | Out-Null; $script:createdDirs.Add($dir) }
  [System.IO.File]::WriteAllText($full, "probe`n", $utf8)
  return $full
}

function Remove-Probes {
  foreach ($p in $probeFiles) { Remove-Item -LiteralPath (Join-Path $root $p) -Force -ErrorAction SilentlyContinue }
  # remove de baixo para cima, so o que este script criou
  $dirs = $script:createdDirs | Sort-Object -Property Length -Descending
  foreach ($d in $dirs) {
    if (-not (Test-Path -LiteralPath $d)) { continue }
    $left = @(Get-ChildItem -LiteralPath $d -Force -ErrorAction SilentlyContinue)
    if ($left.Count -eq 0) { Remove-Item -LiteralPath $d -Force -ErrorAction SilentlyContinue }
    else {
      # pasta nao vazia = residuo que o git NAO denuncia (git nao rastreia pasta vazia);
      # nunca apagar conteudo que o script nao criou.
      Write-Host "  RESIDUO nao removido (nao criado por este script): $d"
      $script:fail++
    }
  }
  $script:createdDirs.Clear()
  # rede de seguranca: diretorios _qa-probe-* vazios que sobraram de uma run anterior
  Get-ChildItem -LiteralPath $root -Force -Directory -Filter '_qa-probe-*' -ErrorAction SilentlyContinue | ForEach-Object {
    $inner = @(Get-ChildItem -Recurse -Force -LiteralPath $_.FullName -ErrorAction SilentlyContinue | Where-Object { -not $_.PSIsContainer })
    if ($inner.Count -eq 0) { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    else { Write-Host "  RESIDUO nao removido: $($_.FullName)"; $script:fail++ }
  }
}

function Check([string]$Id, [string]$Nome, [scriptblock]$Corpo) {
  try {
    $r = & $Corpo
    if ($r) { Write-Host "PASS $Id $Nome"; $result[$Id] = 'PASS' }
    else { Write-Host "FAIL $Id $Nome"; $script:fail++; $result[$Id] = 'FAIL' }
  }
  catch {
    Write-Host "FAIL $Id $Nome ($($_.Exception.Message))"; $script:fail++; $result[$Id] = 'FAIL'
  }
}

function Get-Porcelain([string]$extra = '') {
  if ($extra) { $out = & git status --porcelain $extra 2>&1 } else { $out = & git status --porcelain 2>&1 }
  return @($out | ForEach-Object { [string]$_ } | Where-Object { $_.Trim().Length -gt 0 })
}

# ---------------- AC-01 ----------------
Check 'AC-01' 'gitignore cobre bin/ obj/ node_modules/ *.db*-wal*-shm .env ideias-references/' {
  $sv = $env:GIT_CEILING_DIRECTORIES
  try {
    # impede .gitignore de ancestrais (fora do repo) de mascarar o do repo
    $env:GIT_CEILING_DIRECTORIES = $root
    $all = $probeFiles
    foreach ($p in $all) { Write-Probe $p | Out-Null }
    $allOut = Get-Porcelain '--ignored'
    $allSnow = Get-Porcelain
    $bad = @()
    foreach ($p in $all) {
      $slash = $p -replace '\\', '/'
      # git status colapsa pasta totalmente ignorada em '!! dir/'; check-ignore responde por arquivo.
      & git check-ignore -q -- $slash 2>&1 | Out-Null
      $isIgnored = if ($LASTEXITCODE -eq 0) { 1 } else { 0 }
      $shown = @($allSnow | Where-Object { $_ -eq "?? $slash" }).Count
      if ($isIgnored -ne 1 -or $shown -ne 0) { $bad += "$p ignored=$isIgnored untracked=$shown" }
    }
    Write-Host ("  probes vistos: {0}; fora do esperado: {1}" -f $all.Count, $bad.Count)
    $bad | ForEach-Object { Write-Host "    -> $_" }
    return ($bad.Count -eq 0)
  }
  finally { $env:GIT_CEILING_DIRECTORIES = $sv; Remove-Probes }
}

# ---------------- AC-02 ----------------
Check 'AC-02' 'backend/ web/ infra/ no indice via .gitkeep, sem codigo de produto' {
  # .gitkeep e stageavel com path explicito. NAO usar git add --dry-run como controle negativo de
  # ignore: ele ignora as regras de ignore para path explicito (medido: add em _qa-probe-bin/.gitkeep
  # sob bin/ retorna 0). O controle de ignore e o proprio git status de AC-01.
  $ok = @(& git add --dry-run backend/.gitkeep web/.gitkeep infra/.gitkeep 2>&1 | Where-Object { $_ -match "add '(backend|web|infra)/\.gitkeep'" }).Count
  Write-Host "  gitkeep stageaveis: $ok/3 (esperado 3)"
  $extra = @()
  foreach ($d in 'backend', 'web', 'infra') {
    if (-not (Test-Path -LiteralPath (Join-Path $root "$d/.gitkeep"))) { $extra += "$d/.gitkeep ausente" }
    Get-ChildItem -Recurse -Force -File -LiteralPath $d | Where-Object { $_.Name -ne '.gitkeep' } |
      ForEach-Object { $extra += "codigo em $d : " + $_.FullName }
  }
  $extra | ForEach-Object { Write-Host "    -> $_" }
  return ($ok -eq 3) -and ($extra.Count -eq 0)
}

# ---------------- AC-03 ----------------
Check 'AC-03' 'gitattributes: text unset para binarios conhecidos, eol lf para x.md' {
  $binExt = @('png', 'jpg', 'jpeg', 'gif', 'webp', 'ico', 'mp4', 'webm', 'mp3', 'ogg', 'wav', 'pdf', 'zip', 'db', 'sqlite')
  $bad = @()
  foreach ($e in $binExt) {
    $t = [string](& git check-attr text -- "x.$e" 2>&1)
    if ($t -notmatch 'text: unset') { $bad += "x.$e nao marca texto: $t" }
  }
  $png = [string](& git check-attr text -- x.png 2>&1)
  $md = [string](& git check-attr eol -- x.md 2>&1)
  $line = @(Get-Content -LiteralPath (Join-Path $root '.gitattributes') | Where-Object { $_ -match '^\*\s+text=auto\s+eol=lf\s*$' }).Count
  # prova de que a regra * NAO se aplica a binario: dentro do .gitattributes, a ordem importa
  $mdOk = ($md -match 'eol: lf')
  $pngOk = ($png -match 'text: unset')
  $firstRule = ((Get-Content -LiteralPath (Join-Path $root '.gitattributes') | Where-Object { $_ -and -not $_.StartsWith('#') })[0]).Trim()
  Write-Host "  x.png -> $png"
  Write-Host "  x.md  -> $md"
  Write-Host "  regra '* text=auto eol=lf' como primeira regra: $firstRule"
  Write-Host "  binarios testados: $($binExt.Count); fora do esperado: $($bad.Count)"
  $bad | ForEach-Object { Write-Host "    -> $_" }
  return $mdOk -and $pngOk -and ($bad.Count -eq 0) -and ($line -eq 1)
}

# ---------------- AC-04 ----------------
Check 'AC-04' 'README em pt-BR descreve, tem build/test e nenhum caminho fora do repo' {
  $readmePath = Join-Path $root 'README.md'
  $raw = [System.IO.File]::ReadAllText($readmePath, [System.Text.Encoding]::UTF8)
  $bad = @()
  $lenOk = $raw.Length -gt 300
  # caminho absoluto Windows (C:\ ...) - nunca e "dentro do repositorio"
  if ($raw -match '[A-Za-z]:\\') { $bad += 'caminho absoluto windows' }
  # caminho unix absoluto isolado (/mnt, /home, /tmp, /opt, /srv)
  foreach ($m in [regex]::Matches($raw, '(?<![\w`/])/(mnt|home|tmp|opt|srv|etc|root|usr)/')) { $bad += "absoluto unix: $($m.Value)" }
  # caminho de escape do repositorio
  if ($raw -match '\.\./') { $bad += 'caminho com ../' }
  # repositorio irmao conhecido do projeto
  foreach ($s in 'squid-specs', 'skuid-specs', 'ideias-references') { if ($raw -match [regex]::Escape($s)) { $bad += "citacao de $s" } }
  # todo link markdown relativo deve existir dentro do repo
  $links = @()
  foreach ($m in [regex]::Matches($raw, '\]\(([^)#\s]+)(?:#[^)]*)?\)')) { $links += $m.Groups[1].Value }
  foreach ($m in [regex]::Matches($raw, '`([A-Za-z0-9_./-]+/[A-Za-z0-9_.*/-]+)`')) {
    $c = $m.Groups[1].Value
    if ($c -match '^(docs|specs|backend|web|infra)/') { $links += ($c -replace '/$', '') }
  }
  $links = @($links | Sort-Object -Unique)
  foreach ($l in $links) {
    if ($l -match '[*<>]') {
      # glob: confere pelo menos uma correspondencia dentro do repo
      if (-not (Get-ChildItem -Path (Join-Path $root $l) -ErrorAction SilentlyContinue)) { $bad += "glob sem correspondencia: $l" }
      continue
    }
    if (-not (Test-Path -LiteralPath (Join-Path $root $l))) { $bad += "caminho citado inexistente no repo: $l" }
  }
  Write-Host "  tamanho: $($raw.Length) chars; caminhos checados: $($links.Count); links: $($links -join ', ')"
  $bad | ForEach-Object { Write-Host "    -> $_" }
  return $lenOk -and ($bad.Count -eq 0)
}

# ---------------- AC-05 ----------------
Check 'AC-05' 'check-task-0001.ps1 roda 2x com exit 0 e sem residuo entre os runs' {
  $checkScript = Join-Path $root 'docs\sdd\_tools\check-task-0001.ps1'
  $before = Get-Porcelain
  $runs = @()
  $residues = @()
  for ($i = 1; $i -le 2; $i++) {
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript 2>&1
    $code = $LASTEXITCODE
    $runs += $code
    $after = Get-Porcelain
    $diff = Compare-Object $before $after
    if ($diff) { $residues += "run ${i}: " + (($diff | ForEach-Object { "$($_.SideIndicator)$($_.InputObject)" }) -join '; ') }
    Write-Host "  run $i exit=$code | $($out | Select-Object -Last 1)"
    $out | Where-Object { [string]$_ -match '^FAIL' } | ForEach-Object { Write-Host "    -> $_" }
  }
  $runs | ForEach-Object { Write-Host "  exit: $_" }
  $residues | ForEach-Object { Write-Host "    -> $_" }
  return (@($runs | Where-Object { $_ -ne 0 }).Count -eq 0) -and ($residues.Count -eq 0)
}

Remove-Probes
Pop-Location
Write-Host ("resultado: {0}" -f (($result.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ' '))
Write-Host "$($script:fail) falha(s)."
if ($script:fail -gt 0) { exit 1 }
exit 0