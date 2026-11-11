<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 09:35

# QA-0026 — Upload de mídia com SkiaSharp, EXIF e blurHash

- task: TASK-0027
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: FAILED
- qaAttempt: 1/2

Leitura normativa usada: SPEC-0009 seção E + `Gaps` (GAP-21/22/25), `data-model.md` §2.6 (EXIF
removido **antes** de blurHash e persist; `Alt` obrigatório e não vazio para Image/Video/Gif;
`BlurHash` obrigatório para Image/Gif; ContentHash SHA-256; magic bytes), contrato `http-api.md`
§1 (erros **sempre** no envelope), §5 (resposta de upload e rejeições `415`/`413 payload_too_large`)
e §9.5. Baseline do DEV **330/330** (`20261110-081456`, `exit: 0` lido **dentro** do log — S-22) foi
a porta de entrada. A QA executou **5 cenários próprios** (`QaTask0027Tests`, com fixtures que a
própria QA constrói), **11 ciclos de mutação can-fail + 1 sonda de ordem**, a sonda de teto de
corpo, o gêmeo bash do gate no WSL e o gate final — nada foi apenas lido.

**Verdicto: FAILED** — 5 dos 6 ACs provados; **AC-02 falha num sub-caso executado**: vídeo/áudio
acima do seu limite por tipo (50 MiB default) **não entregam** `413 payload_too_large` com o
envelope §1 (achado 1, BLOCKER). Os outros cinco ACs estão verdes com evidência executada.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | `Ac01_UploadDeJpegComExifReal_RespondeOContratoEServeBlobSemMetadados` — JPEG real (Skia-encoded) com APP1 EXIF/GPS **construído pela QA** (TIFF little-endian com Make canário `QA0026-GPS-CANARY-42`, Orientation e ponteiro GPSInfo → GPS IFD com refs/rationais; o teste **anda a estrutura** antes de subir, então o fixture é EXIF de verdade, não bytes com as palavras certas). Resposta = exatamente os **7 campos** do §5, `durationSeconds: null`; servido: `SHA256(bytes) == contentHash` ≠ `SHA256(original)` (strip **antes** do hash), `sizeBytes == 533→366`; varredura byte a byte do servido e de **todo o volume** (nomes `^[0-9a-f]{64}$`): sem `Exif\0\0`, sem canário, sem `FF E1`, sem cabeçalho TIFF LE/BE. Can-fail **M1** (strip desligado) e **M2** (hash antes do strip): vermelhos `20261110-090221` e `20261110-090246` | `backend/tests/Squid.Tests/Media/QaTask0027Tests.cs` · `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` | PASS |
| AC-02 | `Ac02_Rejeicoes_AlternoVazio400_Executavel415_AcimaDoLimite413_PorTipo` — `alt` vazio em **Image/Gif/Video** → `400 validation_failed` (`[400,400,400]`); ELF e MZ disfarçados → `415 unsupported_media_type`; JPEG declarado `image/png` → `415` (§9.5); imagem de **11 MiB** > 10 MiB → `413 payload_too_large` com envelope; **por tipo**: vídeo de 12 MiB (acima do limite de imagem, abaixo do de vídeo) → `200` com `durationSeconds=10`. Can-fail **M3** (alt off), **M4** (limite off), **M5** (415→400): vermelhos `090310`/`090337`/`090402`. **PORÉM** o sub-caso "vídeo acima do seu limite" (51 MiB > 50 MiB == teto de corpo) **falha**: ver achado 1 — raw socket recebe `413 Payload Too Large` com `Content-Length: 0` (sem envelope §1, sem `payload_too_large`) e o cliente .NET de streaming vê só **connection reset**, sem status (sonda `QA-0026-ceiling-probe.log`, exit 0) | mesmo arquivo · filtros `.Ac02` · sonda `dotnet test --filter Qa0026CeilingProbe --logger console;verbosity=detailed` (arquivo da sonda removido depois) | **FAIL** |
| AC-03 | `Ac03_AnexosNoPost_KindDoMimeAltHerdado_DesconhecidoEOnzeSao400` — upload jpeg/gif/vídeo → publicação com `contentHashes` devolve `attachments` com `kind` derivado do mime (`image`/`gif`/`video`), `alt` herdado do upload, `blurHash` presente em image/gif e nulo em vídeo, `durationSeconds=10`, `url` terminando em `/api/media/{hash}`; re-upload dos **mesmos bytes** com outro alt → o registro guarda o **primeiro** (atalho documentado) e o anexo herda o primeiro alt; round trip pelo `GET /posts`; hash inexistente → `400`; **11** anexos → `400`; **10** → `201` com 10 anexos. Can-fail **M6** (kind fixo `image`), **M7** (alt não herdado), **M8** (teto 10 desligado): vermelhos `090429`/`090459`/`090531` | mesmo arquivo · filtro `.Ac03` | PASS |
| AC-04 | `Ac04_GetMedia_SessaoUploaderLeitor_E404IdenticoForaDeVista` — anônimo → `401 not_authenticated` **tanto para hash ausente quanto para existente** (a sessão vem antes de qualquer existência); logado sem post → `404`; **uploader** → `200` com `SHA256(bytes) == hash`; leitor que vê o post que referencia → `200` byte-a-byte igual; contêiner vira **privado** → leitor `404` **byte-idêntico** ao hash ausente (comparação de bytes crus) e **sem `traceId`** nos dois; uploader continua `200`. Can-fail **M9** (regra do uploader off) e **M10** (404 fora de vista ganha traceId): vermelhos `090559` e `090627` | mesmo arquivo · filtro `.Ac04` | PASS |
| AC-05 | `Ac05_Ordem_ExifRemovidoAntesDoBlurHash_BlurHashDoServidoConfere` — a resposta do upload é **recomputada por um encoder independente** escrito pela QA no próprio arquivo a partir do algoritmo oficial (não chama o `BlurHashEncoder` do produto): `igualServido=True` (`LdFFalOqJN6\|xUb9W?ShQAjLbIjE` sobre os pixels do **servido**); o servido não carrega canário nem `Exif\0\0`; Gif também vem com `blurHash`. A ordem §2.6 fica coberta por M1 (strip antes de persistir), M2 (strip antes do hash), M12 (a igualdade do blurHash fica vermelha com constante: `090656`) e o caminho do código (`SkiaMediaProcessor.cs:23` remove EXIF na primeira linha, antes do decode). **Sonda M11** (trocar o decode para os bytes COM EXIF): `UNPROVEN` = **mutante equivalente** neste build — o SkiaSharp não aplica orientação EXIF (`decodeOriginal 16x9 == decodeServido 16x9`, `igualOriginal=True`), logo nenhuma entrada distingue a ordem pelo valor do blurHash (`QA-0026-mutation-probe-m11.log`, restore `HASH=PRE`, residuo 0) | mesmo arquivo · filtro `.Ac05` · driver `-Only M11` | PASS |
| AC-06 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **`20261110-092954-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 335, Total: 335`** (330 + 5 da QA), lido **dentro** do log (S-22); baseline `20261110-081456` = 330/330 `exit: 0`. Escopo: `git status --porcelain -uall` vs `QA-0026-git-baseline.txt` → **10 entradas novas, todas** em `docs/sdd/qa/` + o teste novo em `tests/Squid.Tests/Media/` (fora do permitido = **0**); mtime desde 08:00:00 (sem bin/obj) → **5 arquivos, todos nos `touches`** (4 mutados/restaurados + o teste da QA); resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 final = **PRE** nos 4 arquivos mutados (`14013D3A737A`, `D8F40FF0BC68`, `5EBD7DF5E601`, `229107B71CD9`); backups `qa0026-*` no TEMP = 0; SkiaSharp: `<PackageVersion Include="SkiaSharp" Version="4.155.0" />` **só** em `Directory.Packages.props` e `<PackageReference>` **só** em `Squid.Service.csproj` | gate + varreduras (`QA-0026-scope.log`) + `qa-check-task-0027.ps1` / `.sh` | PASS |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Media/QaTask0027Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 5 cenários próprios (AC-01 a AC-05); fixtures construídos aqui: JPEG+EXIF/GPS com canário próprio, GIF89a montado byte a byte, esqueleto MP4/mvhd, e o encoder de blurHash independente |
| `docs/sdd/qa/qa-mutate-task-0027.ps1` | pwsh (gate `sdd-test.ps1`) | 11 ciclos can-fail: SHA256 PRE (S-16) → 1 mutação `QA-MUTATION` com pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact + bump de mtime (S-23) → hash=PRE → verde |
| `docs/sdd/qa/QA-0026-observations.log` | artefato (`--logger console;verbosity=detailed`) | as 5 observações: hashes/servido, status do AC-02, kinds/alts do AC-03, status do AC-04, igualdades do AC-05 |
| `docs/sdd/qa/QA-0026-mutations.log` | artefato | saída dos 11 ciclos (M1..M10, M12) + `SUMMARY cycles=11 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0026-mutations-run1.log` | artefato | 1ª execução: 10 PROVEN + **M5 RECUSADO** (`pattern-count=0` — padrão com acento mangled por `powershell.exe` lendo `.ps1` sem BOM; nada foi escrito; corrigido para padrão ASCII) |
| `docs/sdd/qa/QA-0026-mutation-probe-m11.log` | artefato | sonda da ordem EXIF→blurHash: `UNPROVEN` (mutante equivalente), restore `HASH=PRE` |
| `docs/sdd/qa/QA-0026-ceiling-probe.log` | artefato (sonda temporária, arquivo de teste removido) | vídeo 51 MiB: cliente .NET → connection reset; raw socket → `HTTP/1.1 413 Payload Too Large` + `Content-Length: 0` |
| `docs/sdd/qa/QA-0026-sh-twin.log` | artefato (WSL bash) | execução do gêmeo `sdd-test.sh` (probe/test/test com alvo) no Linux |
| `docs/sdd/qa/QA-0026-scope.log` | artefato | varredura AC-06: logs S-22, git delta, mtime, resíduo, hashes, SkiaSharp, TEMP |
| `docs/sdd/qa/qa-check-task-0027.ps1` | pwsh | 21 checks de evidência (logs S-22, hashes, resíduo, escopo, git, sdd-state, doc) |
| `docs/sdd/qa/qa-check-task-0027.sh` | bash (WSL) | gêmeo com os mesmos checks (exceto `sdd-state`, ferramenta PowerShell), executado no Linux |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution` (nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~QaTask0027Tests"   # esperado: exit 0, Aprovado 5, Total 5
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1                                                 # esperado: exit 0, SUMMARY cycles=11 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                   # esperado: exit 0, Com falha 0 / Aprovado 335 / Total 335
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0027.ps1                                                  # esperado: RESULT: PASS, exit 0
bash docs/sdd/qa/qa-check-task-0027.sh                                                                                                 # esperado: RESULT: PASS, exit 0 (WSL)
```

Achado 1, reprodução (o arquivo da sonda foi removido; recrie `Qa0026CeilingProbe.cs` — as duas
facts estão descritas em `QA-0026-ceiling-probe.log` — ou use TCP cru contra um host da suite):

```
dotnet test backend/Squid.slnx -c Debug --filter "FullyQualifiedName~Qa0026CeilingProbe" --logger "console;verbosity=detailed"
# observado: (1) cliente .NET enviando 51 MiB -> HttpRequestException "connection reset", sem status;
#            (2) só os cabeçalhos com Content-Length: 53000000 -> HTTP/1.1 413 Payload Too Large, Content-Length: 0 (sem envelope)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 08:14:56 | `sdd-test.ps1 test` (gate de baseline da QA) | 0 | `20261110-081456-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 330, Total: 330` |
| 2026-11-10 08:17:05 | `git status --porcelain -uall > QA-0026-git-baseline.txt` | 0 | baseline de 110 entradas capturada antes de a QA escrever |
| 2026-11-10 08:42:06 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` (1ª versão) | 1 | `20261110-084206-test-filter.log` → 4/5: falha na **validadora EXIF da própria QA** (slice incluía o prefixo `Exif\0\0` como se fosse TIFF) — corrigida, não é defeito do produto |
| 2026-11-10 08:43:41 | mesma (após correção) | 0 | `20261110-084341-test-filter.log` → `Com falha: 0, Aprovado: 5, Total: 5` |
| 2026-11-10 ~08:46 | `dotnet test ... --logger console;verbosity=detailed` (1ª coleta) | 0 | artefato substituído pela coleta final abaixo |
| 2026-11-10 08:49:24 | `qa-mutate-task-0027.ps1 -Only M11` (sonda da ordem) | 1 | `QA-0026-mutation-probe-m11.log` → `UNPROVEN` (mutante equivalente), `restore=HASH=PRE`, `residue=0` |
| 2026-11-10 08:51–08:55 | `qa-mutate-task-0027.ps1` (1ª execução, 11 ciclos) | 1 | `QA-0026-mutations-run1.log` → 10× PROVEN + **M5 REFUSED pattern-count=0** (padrão com acento) |
| 2026-11-10 09:01:18 | `qa-mutate-task-0027.ps1 -Only M5` (padrão ASCII corrigido) | 0 | M5 `PROVEN`, red `090118` |
| 2026-11-10 09:04:56 | `bash docs/sdd/_tools/sdd-test.sh probe / test / test backend/Squid.slnx` | 0 / 1 / 1 | `QA-0026-sh-twin.log` → `dotnet: command not found` · `no *.sln found within depth 3` · `test: exit 127` (Linux = `unverified`) |
| 2026-11-10 09:02–09:07 | `qa-mutate-task-0027.ps1` (**execução final, 11 ciclos**) | 0 | `QA-0026-mutations.log` → M1..M10+M12 `PROVEN` · reds `090221..090656` · `SUMMARY cycles=11 failed=0 residue=0 temp-backups=0` |
| 2026-11-10 ~09:11 | `dotnet test --filter Qa0026CeilingProbe --logger console;verbosity=detailed` (sonda do teto) | 0 | `QA-0026-ceiling-probe.log` → streaming: `HttpRequestException` connection reset · raw: `HTTP/1.1 413 Payload Too Large` + `Content-Length: 0` |
| 2026-11-10 ~09:24 | `dotnet test ... --logger console;verbosity=detailed` (coleta final das observações) | 0 | `QA-0026-observations.log` → `igualServido=True` · `404IdenticoAoAusente=True` · `video12MiB=200` |
| 2026-11-10 09:25:43 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests` (final, pós-todos os edits) | 0 | `20261110-092543-test-filter.log` → `Com falha: 0, Aprovado: 5, Total: 5` |
| 2026-11-10 09:29:54 | `sdd-test.ps1 test` (gate final AC-06) | 0 | `20261110-092954-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 335, Total: 335` (330 + 5 da QA) |
| 2026-11-10 09:30:49 | varredura de escopo (git + mtime + resíduo + hashes + SkiaSharp + TEMP) | 0 | `QA-0026-scope.log` → fora dos toques 0 · resíduo 0 · 4/4 `PRE` · SkiaSharp ok · backups 0 |
| 2026-11-10 ~09:40 | `qa-check-task-0027.ps1` + `bash qa-check-task-0027.sh` | 0 / 0 | `QA-0026-check.log` → `SUMMARY ... fails=0` · `RESULT: PASS` nos dois sistemas (execuções detalhadas abaixo) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| 1 | **BLOCKER** (novo — não existe no baseline 310) | `backend/src/Squid.Api/Media/MediaEndpoints.cs:55-62` (teto = `options.MaxRequestBytes` e o comentário que promete o contrário) + `backend/src/Squid.Api/Media/MediaOptions.cs:35` (`MaxRequestBytes` = o **maior** limite por tipo, sem folga) | "acima do limite por tipo → 413" **falha para vídeo/áudio** com os defaults (limite 50 MiB **==** teto de corpo): o corpo acima do teto nunca chega ao handler que escreve o envelope — o Kestrel responde `413 Payload Too Large` com `Content-Length: 0` (**violando §1 "Erros sempre no mesmo envelope"** e o `payload_too_large` do §5/§8) e um cliente de streaming padrão (.NET `HttpClient`) só vê **connection reset**, sem status algum. O comentário de `MediaEndpoints.cs:55-57` afirma que "an oversized file … still reaches the handler and gets the contract's 413 envelope instead of an empty connection reset" — verdade só para imagem (limite 10 MiB < teto), falso para o tipo que **detém** o teto. Correção natural (área do DEV): dar folga ao teto sobre o maior limite, para o arquivo acima do limite do próprio tipo alcançar o handler | `QA-0026-ceiling-probe.log` (as duas observações, exit 0); contrato `http-api.md:12` (§1), `:113` (§5), `:183` (§8); SPEC-0009 E `limite por tipo (413)`; reprodução no bloco acima |
| 2 | MINOR (pré-existente, re-confirmado) | `docs/sdd/_tools/sdd-test.sh:20-21` (e `:64`) | O gêmeo Linux do gate só procura `*.sln` enquanto o solution é `*.slnx`, e **não tem a guarda de ambiente** do `.ps1`: sem `dotnet` no WSL ele imprime `test: exit 127 (real test failure)` — classifica ferramenta ausente como falha real (S-22 no traje de Linux). Mesmo achado das QA-0023/0024/0025 | `QA-0026-sh-twin.log` (probe/test/test-com-alvo, exits 0/1/1) |

Notas (não são defeitos): o **atalho do primeiro registro** (re-upload de bytes idênticos mantém o
primeiro `alt`/uploader) está documentado no próprio código (`SqliteMediaStore.cs:87-91`) e
verificado no AC-03; a **cota `507`** é da TASK-0028 e sua ausência aqui não é achado; a sonda
M11 (ordem decode/strip do blurHash) é um **mutante equivalente** neste build e foi registrada como
tal, não como falha.

**Pre-existing failures on baseline 330: NENHUM** — o gate de baseline desta QA rodou 330/330
`exit: 0` (`20261110-081456`), lido dentro do log, e nada regrediu depois (gate final 335/335).

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina (`podman-machine-default`) não tem `dotnet`
  (instalar exige gate humano; aprovações estão desativadas nesta sessão) → `unverified`, nunca
  assumido igual ao Windows que passou. Além disso o gap estacionado pelo ODEV diz que o SkiaSharp
  4.155.0 traz natives só de Win32/macOS: o decode em Linux falharia sem
  `SkiaSharp.NativeAssets.Linux` (gate humano novo). O que **foi** executado no Linux: os gêmeos
  bash (`sdd-test.sh` — achado 2 — e `qa-check-task-0027.sh`).
- **`qa-mutate-task-0027.ps1` em Linux** — o driver orquestra `sdd-test.ps1`/`dotnet`,
  indisponíveis no WSL → gêmeo `.sh` não faz sentido e não foi escrito (mesmo precedente das
  QA-0024/0025/0026); a restauração byte-exact (S-16/S-23) é provada no Windows e conferida de
  novo pelos checkers nos dois sistemas.
- **Áudio acima do seu limite (51 MiB)** — não executado à parte: o mecanismo do teto é
  independente do tipo (`MaxRequestBytes` cobre qualquer corpo > 50 MiB) e a sonda do vídeo prova
  a classe de comportamento; o braço `LimitFor` do áudio é o mesmo `switch` exercitado pelo vídeo.
- **`Media:MaxBytes:*` com limites customizados** (abaixo do teto) — não varrido; com limite
  customizado abaixo de 50 MiB o handler volta a entregar o envelope (é o caminho da imagem), mas
  a configuração default é a do contrato e é a que falha (achado 1).
- **`durationSeconds` de MOV/QuickTime real** (`moov` sem `ftyp`) — o parser `FindBox` trata o caso
  (`Mp4Duration.cs:44-47`) e o esqueleto MP4 cobre `mvhd` v0/via `ftyp`; um QuickTime real de
  amostra não foi subido (fixture sintético com `ftyp isom` foi o executado).
- **Decodificação de vídeo/thumbnail** — fora do escopo por GAP-21/22 (thumbnail é TASK-0032).

## Memory bank (rota `qa-integracao`)

- Rota lida: `build-run-test.md#Local tests without gate` — **S-22 e S-23 re-confirmados ao vivo**
  nesta execução (todo vermelho classificado dentro do `exit:` do log; todo restore com bump de
  mtime e hash = PRE); `integrations.md` (rascunho, sem mídia) e `contracts.md` (sem menção de mídia).
- `[GAP]` `docs/memory-bank/contracts.md` — não cobre as rotas de mídia (`POST /api/media`,
  `GET /api/media/{contentHash}`); a rota GET é GAP-25 e nem no contrato §5 está. Proposta de
  consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador.
