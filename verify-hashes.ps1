# Confere MANIFEST.md (tamanho + SHA-256) contra os arquivos do pacote.
# Uso: powershell -NoProfile -ExecutionPolicy Bypass -File .\verify-hashes.ps1
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Join-Path $root 'MANIFEST.md'
if (-not (Test-Path -LiteralPath $manifest)) { Write-Host "MANIFEST.md nao encontrado"; exit 1 }

$fail = 0
$checked = 0
Get-Content $manifest | Where-Object { $_ -match '^\| `' } | ForEach-Object {
  $parts = $_ -split '\|' | ForEach-Object { $_.Trim() }
  $rel = $parts[1].Trim('`')
  $size = [int]$parts[2]
  $hash = $parts[3].Trim('`')
  $path = Join-Path $root $rel
  if (-not (Test-Path -LiteralPath $path)) { Write-Host "MISSING  $rel"; $script:fail++; return }
  $len = (Get-Item -LiteralPath $path).Length
  $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLower()
  if ($len -ne $size -or $h -ne $hash) { Write-Host "MISMATCH $rel"; $script:fail++ }
  else { $script:checked++ }
}
Write-Host "$checked artefato(s) conferido(s), $fail problema(s)."
if ($fail -gt 0) { exit 1 }