<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 13:02

# QA-0027 — Upload de mídia com SkiaSharp, EXIF e blurHash (re-verification)

- task: TASK-0027
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 2/2

Round 2 re-verifies the six ACs against the DEV correction of QA-0026's BLOCKER (body ceiling
headroom + `BadHttpRequestException` catch). Normative reading unchanged from QA-0026:
SPEC-0009 section E + `Gaps` (GAP-21/22/25), `data-model.md` §2.6, contrato `http-api.md`
§1 (erros sempre no envelope), §5 (`413 payload_too_large`), §8, §9.5. Round-1 artifacts
(`QaTask0027Tests`, `qa-mutate-task-0027.ps1`, checkers) were reused with round-2 expectation
refreshes (gate 337, 13 cycles, `QA-0027-*` names) — every refresh is listed in `## Log`.

**Premise shifts respected (not defects):** (1) a declared `Content-Length: 53000000` now sits
INSIDE the 69206016 ceiling — the server correctly **waits** for the body (proven, fact 3 of the
probe); an above-ceiling raw probe must declare **more than 69206016** (probe uses 69206017);
the 51 MiB streaming probe must now **receive** the envelope 413 (it does — that was the failed
round-1 sub-case). (2) `qa-check-task-0027.ps1/.sh` round-1 expectations (Aprovado 335, baseline
330, 11 cycles, round-1 PRE of `MediaEndpoints.cs` = `D8F40FF0BC68`) were refreshed to round 2
(337, 13 cycles, PRE `449032345F9E` re-registered by re-running the driver).

**Verdict: PASSED** — all six ACs proven by executed scenarios, no BLOCKER finding. The corrected
AC-02 sub-case is proven three ways (streaming client, raw above-ceiling, raw inside-ceiling) and
**can-fail**: M13 (headroom off) and M14 (catch off) both went red inside their own logs.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_UploadDeJpegComExifReal_RespondeOContratoEServeBlobSemMetadados` — JPEG real (Skia-encoded) com APP1 EXIF/GPS **construído pela QA** (TIFF LE, GPS IFD, canário `QA0026-GPS-CANARY-42`, estrutura validada antes do upload); resposta = exatamente os **7 campos** do §5 com `durationSeconds: null`; servido: `SHA256(bytes) == contentHash` ≠ `SHA256(original)` (strip antes do hash), `533→366` bytes; varredura byte a byte do servido **e de todo o volume** sem `Exif\0\0`/canário/`FF E1`/TIFF. Observação round-2: `contentHash=4f767448…0fd9a sha256Servido=4f767448…0fd9a` (`QA-0027-observations.log`, exit 0). Can-fail **M1** (strip off) e **M2** (hash antes do strip): vermelhos `124216`/`124249`, log exit 1 lido dentro do log | `backend/tests/Squid.Tests/Media/QaTask0027Tests.cs` · `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` → `20261110-125307` 5/5 exit 0 | PASS |
| AC-02 | `Ac02_Rejeicoes_AlternoVazio400_Executavel415_AcimaDoLimite413_PorTipo` — `alt` vazio em Image/Gif/Video → `[400,400,400]`; ELF/MZ/mismatch → `415`; imagem 11 MiB → `413 payload_too_large`; vídeo 12 MiB (abaixo do limite de vídeo) → `200` (por tipo). **Sub-caso corrigido (o que falhou na QA-0026), executado de 3 formas:** (a) **streaming 51 MiB** via .NET client → **recebe** `413` com envelope `error.code=payload_too_large` + traceId (`status=413 code=payload_too_large traceId=00-7b61d0de…`); (b) **raw `Content-Length: 69206017`** (1 byte acima do teto 69206016) → `HTTP/1.1 413` **com** envelope JSON de 180 bytes, nunca `Content-Length: 0`; (c) **raw `Content-Length: 53000000`** (dentro do teto) → servidor **espera** o corpo por 5 s sem responder (premissa 1 verificada). Sonda: `QA-0027-ceiling-probe.log` exit 0, 3/3 (arquivo da sonda removido após a execução, mesmo precedente da QA-0026; os 3 facts estão descritos aqui e no log). Testes do DEV re-executados: `MediaCeilingTests` **2/2** (`20261110-121708`, exit 0) — no limite sobe, acima responde envelope, cru 1 GiB responde envelope. Can-fail **M3/M4/M5** (alt/limite/415): vermelhos `124319`/`124348`/`124417`; **M13** (headroom = 0 → teto volta a igualar o limite): vermelho `124739` com `Com falha: 1, Total: 2` lido **dentro** do log (o upload de 51 MiB morre no Kestrel — o bloqueio da QA-0026 reproduzido); **M14** (captura do `BadHttpRequestException` desligada): vermelho `124813` `BodyAboveTheAbsoluteCeiling [FAIL]` (413 vazio sem envelope) | mesmo arquivo · filtros `.Ac02` · sonda `dotnet test --filter Qa0027CeilingProbe --logger console;verbosity=detailed` → `QA-0027-ceiling-probe.log` · `MediaCeilingTests` via `sdd-test.ps1` | **PASS** |
| AC-03 | `Ac03_AnexosNoPost_KindDoMimeAltHerdado_DesconhecidoEOnzeSao400` — jpeg/gif/vídeo → `attachments` com `kind` derivado do mime, `alt` herdado, `blurHash` presente em image/gif e nulo em vídeo, `durationSeconds=10`, `url` em `/api/media/{hash}`; re-upload de bytes iguais guarda o primeiro alt (atalho documentado); hash inexistente → `400`; **11** anexos → `400`; **10** → `201`. Observação round-2: `kinds=image/gif/video … desconhecido=400 onze=400 dez=201`. Can-fail **M6/M7/M8**: vermelhos `124446`/`124515`/`124543` | mesmo arquivo · filtro `.Ac03` | PASS |
| AC-04 | `Ac04_GetMedia_SessaoUploaderLeitor_E404IdenticoForaDeVista` — anônimo `401` (ausente e existente); logado sem post `404`; uploader `200`; leitor que vê o post `200` byte-idêntico; contêiner privado → leitor `404` **byte-idêntico** ao hash ausente, **sem `traceId`** nos dois. Observação round-2: `404IdenticoAoAusente=True traceIdPresente=False`. Can-fail **M9/M10**: vermelhos `124611`/`124640` | mesmo arquivo · filtro `.Ac04` | PASS |
| AC-05 | `Ac05_Ordem_ExifRemovidoAntesDoBlurHash_BlurHashDoServidoConfere` — resposta **recomputada por encoder independente** da QA sobre os pixels do servido: `igualServido=True` (`LdFFalOqJN6\|xUb9W?ShQAjLbIjE`); Gif também com `blurHash`. Ordem §2.6 coberta por M1 (strip antes de persistir), M2 (strip antes do hash), M12 (constante quebra a igualdade — vermelho `124709`) e o caminho do código (`SkiaMediaProcessor.cs` remove EXIF antes do decode). Sonda **M11** reutilizada da QA-0026 (`QA-0026-mutation-probe-m11.log`): `UNPROVEN` = mutante equivalente (SkiaSharp não aplica orientação EXIF neste build), restore `HASH=PRE`, residuo 0 — `SkiaMediaProcessor.cs` não foi tocado pela correção (SHA256 final `229107B71CD9` = PRE round-1), então a sonda segue válida | mesmo arquivo · filtro `.Ac05` · driver `-Only M11` (round 1) | PASS |
| AC-06 | **Gate:** `sdd-test.ps1 test` → **`20261110-125712-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 337, Total: 337`**, lido **dentro** do log (S-22); baseline round-2 `20261110-113636` = 337/337 `exit: 0`. **Escopo da correção:** mtime na janela 09:46:44–11:36:00 → **4 arquivos, todos nos `touches`** (`MediaEndpoints.cs` 10:24, `MediaOptions.cs` 10:20 — mtimes posteriores só por causa dos ciclos de mutação da própria QA, `MediaHost.cs` 10:21, `MediaCeilingTests.cs` 10:47); **`Directory.Packages.props` intocado pela correção** (mtime 2026-11-09 23:00:01, anterior à janela); delta git vs baseline QA-0027 (125 entradas capturadas 11:36) → **3 entradas novas, todas em `docs/sdd/qa/`**; mtime desde 08:00 → 8 arquivos, **0 fora dos touches**; resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 final = **PRE** nos 5 arquivos mutados (`14013D3A737A`, `449032345F9E`, `5EBD7DF5E601`, `229107B71CD9`, `3BA462816D85`); SkiaSharp `4.155.0` **só** em `Directory.Packages.props` + `Squid.Service.csproj` (0 outros csproj); backups `qa00*-*.bak` no TEMP = 0 | gate + `QA-0027-scope.log` + `qa-check-task-0027.ps1` / `.sh` | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Media/QaTask0027Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 5 cenários próprios (AC-01 a AC-05); fixtures construídos aqui: JPEG+EXIF/GPS com canário próprio, GIF89a, esqueleto MP4/mvhd, encoder de blurHash independente |
| `backend/tests/Squid.Tests/Media/Qa0027CeilingProbe.cs` | xunit (sonda temporária, **removida após a execução**) | 3 facts do sub-caso corrigido do AC-02: streaming 51 MiB recebe envelope 413; raw CL 69206017 (acima do teto) recebe envelope; raw CL 53000000 (dentro do teto) é esperado pelo servidor |
| `backend/tests/Squid.Tests/Media/MediaCeilingTests.cs` | xunit (testes do DEV, re-executados) | 2 facts: no limite sobe / acima responde envelope; cru 1 GiB responde envelope |
| `docs/sdd/qa/qa-mutate-task-0027.ps1` | pwsh (gate `sdd-test.ps1`) | **13** ciclos can-fail (round 2): M1..M10+M12 + **M13** (headroom = 0) + **M14** (captura 413 desligada) — SHA256 PRE (S-16) → mutação `QA-MUTATION` pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact + mtime bump (S-23) → hash=PRE → verde |
| `docs/sdd/qa/QA-0027-observations.log` | artefato (`--logger console;verbosity=detailed`) | as 10 observações round-2 (hashes/servido, status AC-02, kinds/AC-03, status AC-04, igualdades AC-05) |
| `docs/sdd/qa/QA-0027-ceiling-probe.log` | artefato (sonda temporária) | 3/3 exit 0: streaming `status=413 code=payload_too_large`, cru `CL=69206017 → HTTP/1.1 413` com envelope, cru `CL=53000000 → servidor esperou o corpo` |
| `docs/sdd/qa/QA-0027-mutations.log` | artefato | saída dos 13 ciclos + `SUMMARY cycles=13 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0027-sh-twin.log` | artefato (WSL bash) | gêmeo `sdd-test.sh` probe/test: `dotnet: command not found`, `test: exit 127` (Linux = `unverified`) |
| `docs/sdd/qa/QA-0027-scope.log` | artefato | varredura AC-06: git delta (2 baselines), janela da correção, props intocado, resíduo, hashes PRE, SkiaSharp, TEMP |
| `docs/sdd/qa/QA-0026-mutation-probe-m11.log` | artefato round 1, reutilizado | M11 `UNPROVEN` mutante equivalente — `SkiaMediaProcessor.cs` intocado pela correção |
| `docs/sdd/qa/qa-check-task-0027.ps1` | pwsh | 22 checks de evidência, **atualizados para round 2** (baseline 337, gate 337, 13 ciclos, artefatos `QA-0027-*`, sonda de teto, verdicto PASSED) |
| `docs/sdd/qa/qa-check-task-0027.sh` | bash (WSL) | gêmeo com os mesmos checks (exceto `sdd-state`), também atualizado para round 2 |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution`
(nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~QaTask0027Tests"   # esperado: exit 0, Aprovado 5, Total 5
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~MediaCeilingTests" # esperado: exit 0, Aprovado 2, Total 2
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1                                                 # esperado: exit 0, SUMMARY cycles=13 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                   # esperado: exit 0, Com falha 0 / Aprovado 337 / Total 337
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0027.ps1                                                  # esperado: RESULT: PASS, exit 0
bash docs/sdd/qa/qa-check-task-0027.sh                                                                                                 # esperado: RESULT: PASS, exit 0 (WSL)
```

A sonda de teto foi um arquivo temporário (`Qa0027CeilingProbe.cs`, removido após a execução —
mesmo precedente da QA-0026, para o gate continuar em 337). Os 3 facts estão descritos acima e
no artefato `QA-0027-ceiling-probe.log`; para recriar: classe `[Collection("ServiceHost")]` com
`MediaHostFixture`, um fact com `MediaHarness.Mp4WithDuration(600, 6000, 51 MiB)` enviado por
`MultipartFormDataContent`, e dois facts de socket cru com `Content-Length: 69206017` e
`53000000` (cookies de sessão + `X-CSRF-Token`, leitura com timeout limitado).

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 11:36:36 | `sdd-test.ps1 test` (baseline round-2) | 0 | `20261110-113636-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 337, Total: 337` |
| 2026-11-10 11:36 | `git status --porcelain -uall > QA-0027-git-baseline.txt` | 0 | baseline de 125 entradas capturada antes de a QA escrever |
| 2026-11-10 12:16:53 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` | 0 | `20261110-121653-test-filter.log` → `Aprovado: 5, Total: 5` |
| 2026-11-10 12:17:08 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaCeilingTests` | 0 | `20261110-121708-test-filter.log` → `Aprovado: 2, Total: 2` |
| 2026-11-10 ~12:30 | `dotnet test … --filter Qa0027CeilingProbe --logger console;verbosity=detailed` | 0 | `QA-0027-ceiling-probe.log` → 3/3; streaming `status=413 code=payload_too_large`; cru `69206017 → 413` com envelope de 180 bytes; cru `53000000 → servidor esperou` |
| 2026-11-10 ~12:35 | `dotnet test … --filter QaTask0027Tests --logger console;verbosity=detailed` | 0 | `QA-0027-observations.log` → as 10 observações round-2 |
| 2026-11-10 12:42–12:48 | `qa-mutate-task-0027.ps1` (13 ciclos) | 0 | `QA-0027-mutations.log` → M1..M10+M12+M13+M14 `PROVEN` · reds `124216..124813` · `SUMMARY cycles=13 failed=0 residue=0 temp-backups=0` |
| 2026-11-10 12:53:07 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` (final) | 0 | `20261110-125307-test-filter.log` → `Aprovado: 5, Total: 5` (sonda já removida) |
| 2026-11-10 12:57:12 | `sdd-test.ps1 test` (gate final AC-06) | 0 | `20261110-125712-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 337, Total: 337` |
| 2026-11-10 12:54:31 | varredura de escopo (git×2 + janela + props + resíduo + hashes + SkiaSharp + TEMP) | 0 | `QA-0027-scope.log` → correção 4 arquivos nos touches · props intocado · fora dos touches 0 · resíduo 0 · 5/5 `PRE` |
| 2026-11-10 12:56:15 | `bash docs/sdd/_tools/sdd-test.sh probe` / `test backend/Squid.slnx` (WSL) | 0 / 1 | `QA-0027-sh-twin.log` → `dotnet: command not found` · `test: exit 127` (Linux = `unverified`) |
| 2026-11-10 ~13:00 | `qa-check-task-0027.ps1` + `bash qa-check-task-0027.sh` | 0 / 0 | `QA-0027-check.log` → `SUMMARY … fails=0` · `RESULT: PASS` nos dois sistemas |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado round 2.** O BLOCKER da QA-0026 (413 vazio / connection reset em arquivo acima do limite por tipo) está corrigido e provado can-fail (M13/M14 vermelhos) | `QA-0027-ceiling-probe.log`, `QA-0027-mutations.log` |
| 1 | MINOR (pré-existente, re-confirmado) | `docs/sdd/_tools/sdd-test.sh:20-21` (e `:64`) | O gêmeo Linux do gate só procura `*.sln` enquanto o solution é `*.slnx`, e **não tem a guarda de ambiente** do `.ps1`: sem `dotnet` no WSL imprime `test: exit 127 (real test failure)` — classifica ferramenta ausente como falha real (S-22 no traje de Linux). Mesmo achado das QA-0023..0026 | `QA-0027-sh-twin.log` (probe/test, exits 0/1) |

Notas (não são defeitos): o **atalho do primeiro registro** (re-upload guarda o primeiro
`alt`/uploader) está documentado em `SqliteMediaStore.cs` e verificado no AC-03; a **cota `507`**
é da TASK-0028; a sonda M11 segue **mutante equivalente** documentado; o gap estacionado
(natives SkiaSharp só Win32/macOS) foi **aprovado pelo humano em 2026-11-10** mas o pacote
`SkiaSharp.NativeAssets.Linux` entra na TASK-0028 — não é achado desta QA.

**Pre-existing failures on baseline 337: NENHUM** — baseline round-2 `20261110-113636` rodou
337/337 `exit: 0` lido dentro do log, e o gate final `20261110-125712` idem.

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina não tem `dotnet` (instalar exige gate
  humano; aprovações desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows
  que passou. O gap de natives do SkiaSharp está aprovado mas o pacote só chega na TASK-0028, então
  nem aqui nem lá o decode Linux é afirmado. O que **foi** executado no Linux: os gêmeos bash
  (`sdd-test.sh` — achado 1 — e `qa-check-task-0027.sh`).
- **`qa-mutate-task-0027.ps1` em Linux** — o driver orquestra `sdd-test.ps1`/`dotnet`,
  indisponíveis no WSL → gêmeo `.sh` não faz sentido (mesmo precedente das QA-0024..0026); a
  restauração byte-exact (S-16/S-23) é provada no Windows e conferida de novo pelos checkers
  nos dois sistemas.
- **Áudio acima do seu limite (51 MiB)** — não executado à parte: o mecanismo do teto é
  independente do tipo (`MaxRequestBytes` cobre qualquer corpo acima de 50 MiB) e a sonda do
  vídeo prova a classe de comportamento; o braço `LimitFor` do áudio é o mesmo `switch`.
- **`Media:MaxBytes:*` com limites customizados** (abaixo do teto) — não varrido; com limite
  customizado abaixo de 50 MiB o handler entrega o envelope (caminho da imagem), e a configuração
  default é a do contrato.
- **`durationSeconds` de MOV/QuickTime real** (`moov` sem `ftyp`) — parser trata o caso
  (`Mp4Duration.cs`), fixture executado foi o esqueleto MP4 `ftyp isom`.
- **Decodificação de vídeo/thumbnail** — fora do escopo por GAP-21/22 (TASK-0032).

## Memory bank (rota `qa-integracao`)

- Rota lida: `build-run-test.md#Local tests without gate` — **S-22 e S-23 re-confirmados ao vivo**
  nesta rodada (M13: wrapper imprimiu exit 3 mas o log diz `exit: 1` com `Com falha: 1` — o
  vermelho é de produto, classificado dentro do log; todo restore com bump de mtime e hash = PRE);
  `contracts.md` re-verificado: segue **sem** menção das rotas de mídia.
- `[GAP]` `docs/memory-bank/contracts.md` — não cobre `POST /api/media` nem
  `GET /api/media/{contentHash}` (a GET é GAP-25 e nem no contrato §5 está). Proposta de
  consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador. (Re-levantado na
  QA-0026, continua aberto.)
