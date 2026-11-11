#Requires -Version 5.1
<#
qa-mutate-task-0027.ps1 — can-fail driver of QA-0026/QA-0027 (TASK-0027, upload de mídia com SkiaSharp).

Round 2 (QA-0027): 13 cycles — the original M1..M10+M12 plus M13/M14, which mutate the
QA-0026 BLOCKER fix itself (body headroom + BadHttpRequestException catch), so the corrected
AC-02 sub-case is proven can-fail against the current tree.

One cycle, exactly as the flow demands:
  S-16 : SHA256 PRE of the product file, recorded before anything is written
  apply: ONE unique pattern (count must be exactly 1 or the cycle is REFUSED, nothing written),
         inside the task's touches only, carrying the QA-MUTATION marker in the text
  red  : sdd-test.ps1 test -TestFilter <the AC under test>; classified by the `exit:` line and
         the summary INSIDE the log file (S-22), never by the wrapper's console exit code alone
  S-23 : byte-exact restore from the backup, mtime bump, SHA256 must equal PRE again
  green: same filtered run, exit 0 read inside its log

Nothing outside the task's touches (Squid.Api/Media, Squid.Api/Posts, Squid.Service,
Squid.Domain/Objects, tests/Squid.Tests/Media) is ever written, and no mutation outlives its
own cycle (restore runs in a finally block).
Usage: powershell -NoProfile -ExecutionPolicy Bypass -File qa-mutate-task-0027.ps1 [-Only M3]
#>
[CmdletBinding()]
param([string]$Only = '')

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$sddTest = Join-Path $repo 'docs\sdd\_tools\sdd-test.ps1'
$utf8 = New-Object System.Text.UTF8Encoding($false)

$mutations = @(
  # AC-01 — a remoção de EXIF some: o servido volta a carregar APP1/canário (e o blob não encolhe)
  @{ Label = 'M1-AC01-strip-off'; File = 'backend\src\Squid.Service\ExifStripper.cs'
     Old = '"image/jpeg" => StripJpeg(original),'
     New = '"image/jpeg" => original, // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac01' },
  # AC-01 — o contentHash passa a ser calculado ANTES da remoção (o id deixa de ser o SHA-256 do servido)
  @{ Label = 'M2-AC01-hash-before-strip'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'var contentHash = Convert.ToHexStringLower(SHA256.HashData(processed.Bytes));'
     New = 'var contentHash = Convert.ToHexStringLower(SHA256.HashData(original)); // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac01' },
  # AC-02 — a checagem do alt vazio é desligada (FR-022 deixa de recusar)
  @{ Label = 'M3-AC02-alt-check-off'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'if (Attachment.NeedsAlt(kind) && alt.Length == 0)'
     New = 'if (false && Attachment.NeedsAlt(kind) && alt.Length == 0) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac02' },
  # AC-02 — o limite por tipo é desligado (11 MiB de imagem deixa de ser 413)
  @{ Label = 'M4-AC02-limit-off'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'if (original.LongLength > limit)'
     New = 'if (false && original.LongLength > limit) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac02' },
  # AC-02 — a recusa 415 (§9.5) vira 400: o executável disfarçado deixa de ser rejeitado como
  # unsupported_media_type. Padrão ASCII de propósito: powershell.exe lê .ps1 sem BOM como ANSI e
  # any accented pattern would be mangled (run 1 refused M5 with pattern-count=0 - QA-0026-mutations-run1.log)
  @{ Label = 'M5-AC02-415-becomes-400'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'StatusCodes.Status415UnsupportedMediaType,'
     New = 'StatusCodes.Status400BadRequest, // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac02' },
  # AC-03 — o kind do anexo deixa de derivar do mime (tudo vira image)
  @{ Label = 'M6-AC03-kind-fixed-image'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'var kind = Attachment.KindOf(record.MimeType);'
     New = 'var kind = AttachmentKind.Image; // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac03' },
  # AC-03 — o alt do anexo deixa de ser herdado do upload
  @{ Label = 'M7-AC03-alt-not-inherited'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'Alt = record.Alt,'
     New = 'Alt = "alt trocado", // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac03' },
  # AC-03 — o teto de 10 anexos por post é desligado (11 publica)
  @{ Label = 'M8-AC03-cap-off'; File = 'backend\src\Squid.Api\Posts\PostEndpoints.cs'
     Old = 'if (contentHashes.Count > Attachment.MaxPerPost)'
     New = 'if (false && contentHashes.Count > Attachment.MaxPerPost) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac03' },
  # AC-04 — o uploader deixa de ler a própria mídia
  @{ Label = 'M9-AC04-uploader-rule-off'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'if (record.UploaderHandle == current.Handle)'
     New = 'if (false && record.UploaderHandle == current.Handle) // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac04' },
  # AC-04 — o 404 de hash fora de vista ganha traceId: deixa de ser byte-idêntico ao hash ausente
  @{ Label = 'M10-AC04-hidden-404-gains-traceId'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'return HiddenMedia(context);'
     New = 'return AuthErrors.Write(context, StatusCodes.Status404NotFound, "not_found", "qa-mut"); // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac04' },
  # M11-AC05-order-swap (decode recebe os bytes COM EXIF) foi executado como sondagem e voltou
  # UNPROVEN: mutante equivalente neste build - o SkiaSharp não aplica orientação EXIF no decode
  # (decodeOriginal 16x9 == decodeServido 16x9, igualOriginal=True em QA-0026-observations.log),
  # então a troca de ordem não muda NENHUM valor observável. Sondagem registrada em
  # QA-0026-mutation-probe-m11.log; a ordem §2.6 fica coberta por M1 (strip antes de persistir),
  # M2 (strip antes do hash) e M12 (a igualdade do blurHash do AC-05). Não é ciclo desta rodada.
  # AC-05 — o blurHash calculado é trocado por uma constante (a igualdade do AC-05 quebra)
  @{ Label = 'M12-AC05-blurhash-constant'; File = 'backend\src\Squid.Service\SkiaMediaProcessor.cs'
     Old = 'blurHash = BlurHashEncoder.Encode(decoded);'
     New = 'blurHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAA"; // QA-MUTATION'
     Filter = 'FullyQualifiedName~QaTask0027Tests.Ac05' },
  # QA-0027 round 2 — AC-02 corrigido (achado BLOCKER da QA-0026): a folga do teto some e o
  # arquivo no limite volta a bater no Kestrel ANTES do handler (teto == limite por tipo)
  @{ Label = 'M13-AC02-headroom-off'; File = 'backend\src\Squid.Api\Media\MediaOptions.cs'
     Old = 'public const long BodyHeadroomBytes = 16 * 1024 * 1024;'
     New = 'public const long BodyHeadroomBytes = 0; // QA-MUTATION'
     Filter = 'FullyQualifiedName~MediaCeilingTests' },
  # QA-0027 round 2 — a captura do BadHttpRequestException some: corpo acima do teto volta a
  # responder o 413 VAZIO do Kestrel (Content-Length: 0, sem envelope §1)
  @{ Label = 'M14-AC02-envelope-catch-off'; File = 'backend\src\Squid.Api\Media\MediaEndpoints.cs'
     Old = 'when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)'
     New = 'when (false && ex.StatusCode == StatusCodes.Status413PayloadTooLarge) // QA-MUTATION'
     Filter = 'FullyQualifiedName~MediaCeilingTests' }
)

# Runs one filtered gate and returns what the LOG FILE says (S-22), not what the console says.
function Invoke-FilterRun([string]$Filter) {
  $console = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $sddTest test -TestFilter $Filter 2>&1 | Out-String
  $wrapperExit = $LASTEXITCODE
  $logPath = [regex]::Match($console, '(?m)^log:\s+(.+)$').Groups[1].Value.Trim()
  if ([string]::IsNullOrWhiteSpace($logPath) -or -not (Test-Path -LiteralPath $logPath)) {
    return @{ WrapperExit = $wrapperExit; LogExit = 'no-log'; Summary = ''; Fails = @(); LogPath = ''; CompileError = $false }
  }

  $text = [System.IO.File]::ReadAllText($logPath)
  $logExit = [regex]::Match($text, '(?m)^exit:\s+(\d+)').Groups[1].Value
  $summary = [regex]::Match($text, '(?m)^(Aprovado!.*|Falhou!.*|Failed!.*)$').Groups[1].Value
  $fails = @([regex]::Matches($text, '(?m)^.*\b([\w.]*QaTask0027Tests\.[\w]+)\s+\[FAIL\]') |
    ForEach-Object { $_.Groups[1].Value })
  return @{
    WrapperExit  = $wrapperExit
    LogExit      = $logExit
    Summary      = $summary.Trim()
    Fails        = $fails
    LogPath      = $logPath
    CompileError = ($text -match 'error CS\d+')
  }
}

$failed = 0
$ran = 0
foreach ($m in $mutations) {
  if ($Only -and $m.Label -notlike "$Only*") { continue }
  $ran++

  $path = Join-Path $repo $m.File
  $pre = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
  $text = [System.IO.File]::ReadAllText($path)
  $count = ([regex]::Matches($text, [regex]::Escape($m.Old))).Count
  if ($count -ne 1) {
    Write-Output "CYCLE $($m.Label): REFUSED pattern-count=$count (nothing written) - fix the driver pattern"
    $failed++
    continue
  }

  $backup = Join-Path $env:TEMP ('qa0026-' + [guid]::NewGuid().ToString('N') + '.bak')
  [System.IO.File]::Copy($path, $backup, $true)
  try {
    [System.IO.File]::WriteAllText($path, $text.Replace($m.Old, $m.New), $utf8)
    $residue = ([regex]::Matches([System.IO.File]::ReadAllText($path), 'QA-MUTATION')).Count
    $red = Invoke-FilterRun $m.Filter

    # restore FIRST (byte-exact), then bump the mtime, then prove the hash (S-23)
    [System.IO.File]::Copy($backup, $path, $true)
    (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
    $post = (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash
    $restored = ($post -eq $pre)

    $green = Invoke-FilterRun $m.Filter

    $redOk = ($red.LogExit -eq '1')
    $greenOk = ($green.LogExit -eq '0')
    $proven = $redOk -and $restored -and $greenOk -and ($residue -gt 0) -and (-not $red.CompileError)
    if (-not $proven) { $failed++ }

    $line = 'CYCLE {0}: {1} PRE={2} red(log exit={3} wrapper={4} fails=[{5}] compileErr={6}) restore={7} green(log exit={8} wrapper={9} {10}) => {11}' -f `
      $m.Label,
      $(if ($redOk) { 'PROVEN' } else { 'UNPROVEN' }),
      $pre.Substring(0, 12),
      $red.LogExit, $red.WrapperExit, ($red.Fails -join ', '), $red.CompileError,
      $(if ($restored) { 'HASH=PRE' } else { 'HASH-MISMATCH' }),
      $green.LogExit, $green.WrapperExit, $green.Summary,
      $(if ($proven) { 'RED classified in ' + $red.LogPath + ' | GREEN ' + $green.LogPath } else { 'CHECK MANUALLY: ' + $red.LogPath })
    Write-Output $line
  }
  finally {
    if (Test-Path -LiteralPath $backup) {
      [System.IO.File]::Copy($backup, $path, $true)
      (Get-Item -LiteralPath $path).LastWriteTime = Get-Date
      Remove-Item -LiteralPath $backup -Force
    }
  }
}

$leftover = @(Get-ChildItem -Path $env:TEMP -Filter 'qa0026-*.bak' -ErrorAction SilentlyContinue)
$residue = 0
Get-ChildItem -Path (Join-Path $repo 'backend') -Recurse -Include *.cs -File |
  Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
  ForEach-Object { if ((Get-Content -Raw -LiteralPath $_.FullName) -match 'QA-MUTATION') { $residue++ } }

Write-Output "SUMMARY cycles=$ran failed=$failed residue-QA-MUTATION=$residue temp-backups=$($leftover.Count)"
if ($failed -gt 0 -or $residue -gt 0 -or $leftover.Count -gt 0) { exit 1 }
exit 0
