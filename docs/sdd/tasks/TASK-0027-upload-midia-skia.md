<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0027 — Upload de mídia com SkiaSharp, EXIF e blurHash

```yaml
runId: R-20261109-01
taskId: TASK-0027
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-10 13:11:06
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Media/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Domain/Objects/**", "backend/src/Squid.Service/**", "backend/Directory.Packages.props", "backend/tests/Squid.Tests/Media/**"]
blocks: ["TASK-0028"]
blockedBy: ["TASK-0026"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Media/**", "backend/src/Squid.Api/Posts/**", "backend/src/Squid.Domain/Objects/**", "backend/Directory.Packages.props"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 1/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-09
  scope: SkiaSharp aprovado como pacote unico de imagem, versao so em Directory.Packages.props (ADR-25) - decisao da entrevista de abertura da P-04
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0027 PASSED na tentativa 2/2 (6/6 ACs, gate 337/337, 13 mutacoes); proxima entrega TASK-0028 com AC-06 natives
evidence: []
memoryBankFlags: []
```

## Intent

Fecha FR-020 (mídia) + FR-021 (EXIF) + FR-022 (alt): `POST /api/media` multipart com
decodificação SkiaSharp, remoção de EXIF antes de blurHash, blob content-addressed, rejeições
`415/413/400`, `GET /api/media/{contentHash}` protegido e anexos no post (≤ 10).

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/media` com imagem válida + `alt` devolve `{ contentHash, mimeType, width,
  height, blurHash, sizeBytes, durationSeconds: null }` e o blob é gravado por SHA-256 **sem**
  nenhuma tag EXIF/GPS (varredura byte a byte do arquivo servido) — teste com imagem sintética
  carregada de EXIF real.
- AC-02: `alt` vazio em `Image|Video|Gif` → `400`; conteúdo executável disfarçado de imagem →
  `415` (magic bytes, §9.5); acima do limite por tipo → `413` — testes.
- AC-03: anexar no post: `POST /posts` com `contentHashes` (≤ 10) publica com `attachments`
  (kind derivado do mime, `alt` herdado do upload); hash inexistente → `400`; mais de 10 → `400` —
  teste.
- AC-04: `GET /api/media/{contentHash}`: sem sessão → 401; logado que é uploader **ou** vê ao
  menos um post que referencia o hash → 200; hash só em post invisível ao leitor → `404`
  byte-idêntico (GAP-25, SC-005) — teste.
- AC-05: ordem invariante §2.6: EXIF é removido **antes** de calcular o `blurHash` — o blurHash
  calculado sobre o arquivo servido é igual ao calculado sobre o blob sem metadados; `Image`/`Gif`
  sempre com `blurHash` — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches` (SkiaSharp entra só em
  `Directory.Packages.props` + csproj consumidor).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Media/**` (novo: upload, rota de serviço, interfaces)
- `backend/src/Squid.Api/Posts/**` (campo `contentHashes` no create/`PostView`)
- `backend/src/Squid.Domain/Objects/**` (registro `Attachment`, validação de alt/limite)
- `backend/src/Squid.Service/**` (blob store content-addressed + registro)
- `backend/Directory.Packages.props` (versão do SkiaSharp — gate concedido)
- `backend/tests/Squid.Tests/Media/**` (novo)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-21 | no | thumbnail de vídeo/áudio fora (placeholder) — tarefa da TASK-0032 só para Image/Gif |
| GAP-22 | no | `durationSeconds` só MP4/MOV (box `mvhd`); demais `null` |
| GAP-25 | no | `GET /api/media/{contentHash}`: sessão + "vê algum post que referencia o hash ou é uploader"; rota não listada no contrato §5 — registrada aqui |
| — | no | limites por tipo configuráveis (`Media:MaxBytes:*`, default 10 MiB imagem/50 MiB vídeo — doc 05 não dá número, assumido e configurável) |
| — | no | armazenamento em `Media:BlobRoot` (volume nomeado por config); nenhum serviço externo |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:56 | odev | task created from SPEC-0009 (entrega 5 de 10; blockedBy TASK-0026; gate do SkiaSharp registrado no humanGate)
- 2026-11-09 22:27:39 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0026 resolve (DONE com QA-0025 PASSED, gate 310/310); baseline do gate e 310/310; gate do SkiaSharp ja registrado no humanGate
- 2026-11-09 23:34:34 | dev | baseline do gate no dispatch: sdd-test.ps1 test exit 0, 310/310 (docs/sdd/runs/test-runs/20261109-223233-test-gate.log)
- 2026-11-09 23:34:34 | dev | viabilidade fechada em 1 rodada: D-01 resolvida (natives do SkiaSharp sao TRANSITORIOS do proprio nupkg - Win32 e macOS so), touches confirmados, nenhum teste pre-existente em risco - PostView e payload de atividade mudam simetricos para o comparador byte a byte da QA-0026
- 2026-11-09 23:34:34 | dev | implementado POST /api/media e GET /api/media/{contentHash} (Squid.Api/Media), Attachment + MediaSignature no dominio, blob content-addressed em Media:BlobRoot e pipeline SkiaSharp com strip de EXIF ANTES de blurHash em Squid.Service, contentHashes/attachments no publish (hash inexistente e maior que 10 -> 400) e indice reverso post x hash no IObjectStore (GAP-25)
- 2026-11-09 23:34:34 | dev | SkiaSharp 4.155.0 com versao so em Directory.Packages.props e PackageReference so em Squid.Service.csproj; nenhum outro pacote novo; cota/507 ficou para a TASK-0028
- 2026-11-09 23:34:34 | dev | iteracao de teste: primeiro filtro 16/20 com 4 falhas (constante do blurHash fS->fQ, tamanho do box mvhd nos builders, alt herdado de bytes identicos entre testes) - causas corrigidas, reexecucao 20/20 exit 0
- 2026-11-09 23:34:34 | dev | gate final: sdd-test.ps1 test exit 0, 330/330 (docs/sdd/runs/test-runs/20261109-233010-test-gate.log); build final da arvore entregue exit 0, 0 avisos/0 erros; escopo provado contra o baseline da QA-0025
- 2026-11-09 23:34:38 | odev | ODEV carimbou as 6 logLines e as 8 evidenceRows do DEV com o relogio do fluxo (DEV nao tocou o arquivo da task)
- 2026-11-09 23:34:39 | odev | ODEV verificou antes de transicionar: gate final 20261109-233010-test-gate.log lido no arquivo (exit: 0, Aprovado 330, Com falha 0 - S-22); residuos QA-MUTATION = 0; step result DONE aceito - D-01 (natives transitorios) fechada em 1 rodada; ordem EXIF antes de blurHash provada no proprio pipeline
- 2026-11-09 23:34:40 | odev | parked idea CRITICA registrada: SkiaSharp traz natives SO de Win32+macOS - o decode em Linux (CI ubuntu e compose) falhara sem o pacote SkiaSharp.NativeAssets.Linux, que exige gate humano novo; pergunta ao humano aberta nesta mesma rodada
- 2026-11-09 23:34:40 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 5 de 10 da P-04
- 2026-11-10 09:39:21 | qa | baseline do gate: sdd-test.ps1 test exit 0, 330/330 (docs/sdd/runs/test-runs/20261110-081456-test-gate.log, exit: lido DENTRO do log - S-22); git baseline capturado antes de a QA escrever (QA-0026-git-baseline.txt, 110 entradas, 08:17:05)
- 2026-11-10 09:39:21 | qa | 6 ACs executados com 5 cenarios proprios em backend/tests/Squid.Tests/Media/QaTask0027Tests.cs (fixtures construidos pela QA: JPEG real com EXIF/GPS sintetico e canario proprio percorrido antes do upload, GIF89a montado byte a byte, esqueleto MP4/mvhd, encoder de blurHash independente do algoritmo oficial) - classe 5/5 exit 0 no 20261110-092543 e gate final 335/335 exit 0 no 20261110-092954
- 2026-11-10 09:39:21 | qa | can-fail: 11 ciclos M1..M10+M12 (SHA256 PRE antes - S-16, vermelho classificado dentro do proprio log gravado - S-22, restore byte-exact com bump de mtime - S-23, verde em seguida) com SUMMARY cycles=11 failed=0 residue=0 temp-backups=0; a 1a execucao teve M5 RECUSADO (padrao com acento mangled por powershell.exe lendo .ps1 sem BOM - pattern-count=0, nada escrito) e foi corrigido para padrao ASCII; M11 (troca do decode para os bytes COM EXIF) sondado a parte e voltou UNPROVEN = mutante equivalente neste build (SkiaSharp nao aplica orientacao EXIF: decodeOriginal 16x9 == decodeServido 16x9, igualOriginal=True) - registrado em QA-0026-mutation-probe-m11.log
- 2026-11-10 09:39:21 | qa | ACHADO BLOCKER novo (nao existe no baseline 310): video/audio acima do limite por tipo (default 50 MiB == teto de corpo MaxRequestBytes) nao entregam 413 payload_too_large com envelope §1 - o servidor manda HTTP/1.1 413 Payload Too Large com Content-Length: 0 (sem envelope, §1 http-api.md:12) e o cliente .NET de streaming ve so connection reset sem status (QA-0026-ceiling-probe.log, exit 0); causa em MediaEndpoints.cs:61 + MediaOptions.cs:35, e o comentario de MediaEndpoints.cs:55-57 afirma o contrario - achado 1 da QA-0026
- 2026-11-10 09:39:21 | qa | AC-06 escopo limpo: git delta vs baseline = 10 entradas novas, todas em docs/sdd/qa/ + o teste novo em tests/Squid.Tests/Media/ (fora do permitido 0); mtime desde 08:00:00 sem bin/obj = 5 arquivos, todos nos touches; residuo QA-MUTATION em backend = 0; SHA256 final = PRE nos 4 arquivos mutados (14013D3A737A, D8F40FF0BC68, 5EBD7DF5E601, 229107B71CD9); SkiaSharp 4.155.0 com versao so em Directory.Packages.props e PackageReference so em Squid.Service.csproj; backups qa0026 no TEMP = 0
- 2026-11-10 09:39:21 | qa | plataforma Linux NAO executada (WSL podman-machine sem dotnet - instalar exige gate; gap estacionado: natives do SkiaSharp so Win32/macOS) = unverified; gormeos bash executados (sdd-test.sh probe/test = exit 127 por .slnx ausente, achado pre-existente; qa-check-task-0027.sh); verdicto FAILED com 1 achado BLOCKER (AC-02 video acima de 50 MiB) e 1 pre-existente re-confirmado; baseline 330/330 sem falhas pre-existentes
- 2026-11-10 09:41:45 | qa | checkers qa-check-task-0027.ps1 e qa-check-task-0027.sh executados nos dois sistemas - SUMMARY total=21 fails=0 RESULT: PASS nos dois (docs/sdd/qa/QA-0026-check.log runs 1-2); evidencias do checker anexadas e re-execucao final de confirmacao logo em seguida
- 2026-11-10 09:46:42 | odev | ODEV verificou antes da transicao: QA-0026-upload-midia-skia.md com verdicto FAILED; gate final 20261110-092954-test-gate.log lido no arquivo (exit: 0, Aprovado 335, Com falha 0 - S-22, 330 baseline + 5 da QA); residuos QA-MUTATION = 0 (11 ciclos restaurados byte-exact, 4/4 SHA256 = PRE); check exit 0 (log 19, evidencias 26); veredito aceito - 5/6 ACs provados, 1 BLOCKER executado
- 2026-11-10 09:46:43 | odev | BLOCKER do AC-02 adjudicado (escada: contrato publicado): arquivo de video/acima do limite por tipo (default 50 MiB == teto MaxRequestBytes) esbarrar no limite de corpo do Kestrel ANTES do handler e responder 413 raw com Content-Length 0, violando http-api.md 1 (erros sempre no mesmo envelope) e 5/8 (payload_too_large); o comentario de MediaEndpoints.cs:55-57 afirma o contrario so para imagem (limite 10 MiB < teto); correcao passada ao DEV: fazer o arquivo acima-do-tipo CHEGAR ao handler e responder o envelope 413 da secao 1/5 (folga no teto de corpo e/ou captura do BadHttpRequestException escrevendo o envelope; o teto absoluto pode continuar como rede de seguranca)
- 2026-11-10 09:46:43 | odev | achados restantes: pre-existente gemeo sdd-test.sh (S-11/S-12 + flag DIVERGENCIA); M11 mutante equivalente UNPROVEN documentado (decodeOriginal == decodeServido; ordem coberta por M1/M2/M12 + caminho do codigo); flag GAP contracts.md nao cobre rotas de midia - consolidacao do fecho da P-04; transicao AWAITING_QA -> QA_FAILED -> IN_CORRECTION, correctionAttempts 1/2, redispatch so com os findings
- 2026-11-10 11:01:02 | dev | baseline do gate na correcao: sdd-test.ps1 test exit 0, 335/335 lido dentro do log (docs/sdd/runs/test-runs/20261110-100241-test-gate.log)
- 2026-11-10 11:01:02 | dev | viabilidade fechada em 1 rodada: achado QA-0026 reproduzido no mecanismo (BadHttpRequestException de Http1ContentLengthMessageBody.OnReadStarting saindo do ReadFormAsync do handler, prova em QA-0026-ceiling-probe.log:74), touches confirmados, nenhum teste pre-existente em risco, 6 patterns do qa-mutate-task-0027.ps1 verificados com count=1 - sem D-0N
- 2026-11-10 11:01:02 | dev | correcao do achado 1: MediaOptions.MaxRequestBytes ganhou folga BodyHeadroomBytes = 16 MiB sobre o maior limite por tipo (teto 66 MiB nos defaults) - video de 50 MiB (no limite, valido) e de 51 MiB (acima) chegam ao handler; o acima responde 413 com o envelope da secao 1 error.code=payload_too_large pelo proprio HttpClient
- 2026-11-10 11:01:02 | dev | reserva absoluta: captura de Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException com StatusCode 413 no ReadFormAsync devolve o mesmo envelope em vez do 413 vazio do Kestrel; comentario de MediaEndpoints.cs:55-57 reescrito para descrever o comportamento real (antes so valia para imagem)
- 2026-11-10 11:01:02 | dev | testes novos em backend/tests/Squid.Tests/Media/MediaCeilingTests.cs (2 facts - no limite sobe, acima responde envelope, corpo declarado de 1 GiB sem bytes pela rota crua da QA-0026 responde o mesmo envelope) e MediaHarness.Mp4WithDuration com minSize opcional; Media:MaxBytes:* intactos
- 2026-11-10 11:01:02 | dev | gate final: sdd-test.ps1 test exit 0, 337/337 (docs/sdd/runs/test-runs/20261110-105251-test-gate.log, exit lido dentro do log); classe da QA 5/5 exit 0 apos a correcao; build exit 0; escopo: 4 arquivos, todos nos touches (git delta vs QA-0026-git-baseline = 1 entrada nova, o teste novo)
- 2026-11-10 11:01:08 | odev | ODEV carimbou as 6 logLines e as 12 evidenceRows do DEV da correcao com o relogio do fluxo
- 2026-11-10 11:01:08 | odev | ODEV verificou antes de transicionar: gate final 20261110-105251-test-gate.log lido no arquivo (exit: 0, Aprovado 337, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0; step result DONE aceito - correcao do BLOCKER entregue com folga de 16 MiB no teto + captura do BadHttpRequestException devolvendo o envelope, 2 testes novos, classe da QA 5/5 verde
- 2026-11-10 11:01:09 | odev | dois avisos de premissa repassados ao dispatch da QA 2/2: (1) o probe cru de CL agora precisa de corpo acima do teto NOVO (69206016 bytes - 53000000 esta DENTRO e o servidor espera o corpo, comportamento correto); o probe de streaming de 51 MiB agora DEVE receber o envelope 413; (2) o checker qa-check-task-0027 tem numeros do round 1 (Aprovado 335, PRE do MediaEndpoints) - a QA atualiza as expectativas na rodada 2
- 2026-11-10 11:01:09 | odev | transicao IN_CORRECTION -> AWAITING_QA; qaAttempts 0/2 -> 1/2 (tentativa 2/2 da re-verificacao)
- 2026-11-10 13:04:51 | qa | baseline round-2: sdd-test.ps1 test exit 0, 337/337 lido dentro do log (docs/sdd/runs/test-runs/20261110-113636-test-gate.log, S-22); git baseline QA-0027-git-baseline.txt capturado 11:36 com 125 entradas antes de a QA escrever
- 2026-11-10 13:04:52 | qa | 6 ACs re-verificados com a premissa corrigida: classe QaTask0027Tests 5/5 (20261110-125307) e MediaCeilingTests do DEV 2/2 (20261110-121708); sonda propria Qa0027CeilingProbe 3/3 exit 0 (QA-0027-ceiling-probe.log) - streaming 51 MiB RECEBE 413 com envelope payload_too_large (sub-caso que falhou na QA-0026), cru CL 69206017 (1 byte acima do teto 69206016) recebe o envelope de 180 bytes, cru CL 53000000 (dentro do teto) e esperado pelo servidor por 5s - premissa 1 confirmada; arquivo da sonda removido apos a execucao (gate segue 337, precedente QA-0026)
- 2026-11-10 13:04:52 | qa | can-fail round 2: driver ampliado para 13 ciclos (M1..M10+M12 + M13 headroom=0 + M14 captura 413 desligada, mutando o proprio fix da QA-0026) - SHA256 PRE antes (S-16), vermelho classificado dentro do proprio log (S-22: M13 wrapper=3 mas log exit=1 com Com falha: 1 - falha real de produto), restore byte-exact com bump de mtime (S-23), verde em seguida; SUMMARY cycles=13 failed=0 residue=0 temp-backups=0 (QA-0027-mutations.log); M13 red reproduz o bloqueio original (upload de 51 MiB morre no Kestrel) e M14 red = 413 vazio sem envelope
- 2026-11-10 13:04:53 | qa | AC-06 escopo: correcao em janela 09:46:44-11:36:00 tocou so 4 arquivos, todos nos touches (MediaEndpoints, MediaOptions, MediaHost, MediaCeilingTests); Directory.Packages.props intocado pela correcao (mtime 2026-11-09 23:00:01); delta git vs baseline QA-0027 = 3 entradas novas, todas em docs/sdd/qa/; mtime desde 08:00 = 8 arquivos fora dos toques 0; residuo QA-MUTATION = 0; SHA256 final = PRE nos 5 arquivos mutados (14013D3A737A, 449032345F9E, 5EBD7DF5E601, 229107B71CD9, 3BA462816D85); SkiaSharp 4.155.0 so em Directory.Packages.props + Squid.Service.csproj (0 outros); backups TEMP = 0
- 2026-11-10 13:04:53 | qa | checkers qa-check-task-0027.ps1 e .sh ATUALIZADOS para round 2 conforme a premissa do dispatch (baseline 330->337 no log 20261110-113636, gate final 335->337, 11->13 ciclos com PRE re-registrado pelo re-run do driver, artefatos QA-0026-* -> QA-0027-*, checks novos CEILING-PROBE-3 e verdicto PASSED) e executados nos dois sistemas - SUMMARY total=22 fails=0 RESULT: PASS nos dois (QA-0027-check.log runs 1-2)
- 2026-11-10 13:04:54 | qa | plataforma Linux NAO executada (WSL sem dotnet - instalar exige gate; gap de natives SkiaSharp aprovado pelo humano 2026-11-10 mas o pacote entra na TASK-0028) = unverified; gêmeo bash sdd-test.sh probe/test rodado (exit 127 com dotnet command not found, achado pre-existente MINOR re-confirmado); verdicto PASSED com 6/6 ACs, 13 ciclos can-fail, 1 achado pre-existente e nenhum BLOCKER - baseline 337/337 sem falhas pre-existentes
- 2026-11-10 13:11:08 | odev | ODEV verificou antes de transicionar: QA-0027-upload-midia-skia.md com verdicto PASSED; gate final 20261110-125712-test-gate.log lido no arquivo (exit: 0, Aprovado 337, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 38, evidencias 53); veredito aceito - 6/6 ACs, 13 ciclos can-fail (M13/M14 mutam o proprio fix), sonda 3/3 do sub-caso corrigido (streaming 51 MiB com envelope, cru acima do teto com envelope, cru dentro do teto esperado), checkers round-2 22/22 nos dois sistemas
- 2026-11-10 13:11:08 | odev | nota de contadores: qaAttempts permanece 1/2 - o incremento ocorreu na transicao IN_CORRECTION -> AWAITING_QA (tabela do fluxo); a QA rotulou a si mesma de 2/2, mas 2/2 seria o limite atingido, e o veredito passou; nenhuma correcao restante; achado unico restante e o pre-existente sdd-test.sh (S-11/S-12) + flag DIVERGENCIA ja registrada; flag [GAP] contracts.md sem rotas de midia vai para a consolidacao do fecho
- 2026-11-10 13:11:09 | odev | transicao AWAITING_QA -> DONE; TASK-0027 = entrega 5 de 10 da P-04 encerrada (QA-0027 PASSED, 1 BLOCKER corrigido no caminho); cadeia libera a TASK-0028

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-09 23:34:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-223233-test-gate.log - baseline 310/310 |
| 2026-11-09 23:34:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0023 StringValues em MediaEndpoints.cs - corrigido (leitura crua do header da parte multipart) |
| 2026-11-09 23:34:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0266 ulong->long em Mp4Duration + CS0246 SKException ausente no SkiaSharp 4.155 - corrigidos (cast explicito; Decode devolve null, sem catch) |
| 2026-11-09 23:34:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media | 1 | docs/sdd/runs/test-runs/20261109-232148-test-filter.log - 16/20, 4 falhas dos proprios testes |
| 2026-11-09 23:34:36 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media | 0 | docs/sdd/runs/test-runs/20261109-232532-test-filter.log - 20/20 |
| 2026-11-09 23:34:37 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261109-233010-test-gate.log - gate final 330/330, suite inteira com BuildProof |
| 2026-11-09 23:34:37 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | arvore final: 0 Aviso(s)/0 Erro(s) |
| 2026-11-09 23:34:38 | dev | git status --porcelain -uall + diff contra docs/sdd/qa/QA-0025-git-baseline.txt | 0 | delta = so arquivos dentro dos touches (Packages.props, Service.csproj, SquidServiceHost + Media/Posts/Domain novos) |
| 2026-11-09 23:34:42 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261109-233010-test-gate.log exit: 0 / Aprovado 330 / Com falha 0; residuos 0; DEV log 6/evidence 8 carimbados pelo ODEV |
| 2026-11-10 09:40:19 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-081456-test-gate.log - baseline 330/330, exit lido dentro do log (S-22) |
| 2026-11-10 09:40:20 | qa | git status --porcelain -uall para docs/sdd/qa/QA-0026-git-baseline.txt | 0 | docs/sdd/qa/QA-0026-git-baseline.txt - 110 entradas em 08:17:05, antes de a QA escrever |
| 2026-11-10 09:40:20 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests | 1 | docs/sdd/runs/test-runs/20261110-084206-test-filter.log - 1a execucao 4/5, bug na validadora EXIF da propria QA (corrigido) |
| 2026-11-10 09:40:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests | 0 | docs/sdd/runs/test-runs/20261110-084341-test-filter.log - 5/5 apos a correcao |
| 2026-11-10 09:40:21 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~QaTask0027Tests --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0026-observations.log - as 5 observacoes executadas |
| 2026-11-10 09:40:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1 -Only M11 | 1 | docs/sdd/qa/QA-0026-mutation-probe-m11.log - UNPROVEN mutante equivalente da ordem, restore HASH=PRE, residuo 0 |
| 2026-11-10 09:40:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1 (1a execucao) | 1 | docs/sdd/qa/QA-0026-mutations-run1.log - 10 PROVEN + M5 RECUSADO pattern-count=0 (padrao com acento, nada escrito) |
| 2026-11-10 09:40:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1 -Only M5 | 0 | M5 PROVEN apos padrao ASCII - red docs/sdd/runs/test-runs/20261110-090118-test-filter.log |
| 2026-11-10 09:40:23 | qa | bash docs/sdd/_tools/sdd-test.sh probe | 0 | docs/sdd/qa/QA-0026-sh-twin.log - dotnet command not found no WSL |
| 2026-11-10 09:40:23 | qa | bash docs/sdd/_tools/sdd-test.sh test backend/Squid.slnx | 1 | docs/sdd/qa/QA-0026-sh-twin.log - exit 127, plataforma Linux registrada como unverified |
| 2026-11-10 09:40:24 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1 (execucao final) | 0 | docs/sdd/qa/QA-0026-mutations.log - 11 ciclos PROVEN, SUMMARY cycles=11 failed=0 residue=0 temp-backups=0 |
| 2026-11-10 09:40:24 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Qa0026CeilingProbe --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0026-ceiling-probe.log - video 51 MiB: cliente streaming connection reset, raw socket 413 com Content-Length 0 |
| 2026-11-10 09:40:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests (final) | 0 | docs/sdd/runs/test-runs/20261110-092543-test-filter.log - 5/5 depois de todos os edits |
| 2026-11-10 09:40:25 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261110-092954-test-gate.log - 335/335, exit lido dentro do log (S-22) |
| 2026-11-10 09:40:25 | qa | varredura AC-06: git status + mtime + residuo QA-MUTATION + SHA256 + SkiaSharp + TEMP | 0 | docs/sdd/qa/QA-0026-scope.log - fora dos toques 0, residuo 0, 4/4 PRE, SkiaSharp so em props + Squid.Service.csproj |
| 2026-11-10 09:41:45 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0027.ps1 | 0 | docs/sdd/qa/QA-0026-check.log run1 - SUMMARY total=21 fails=0 RESULT: PASS |
| 2026-11-10 09:41:46 | qa | bash docs/sdd/qa/qa-check-task-0027.sh (WSL) | 0 | docs/sdd/qa/QA-0026-check.log run2 - SUMMARY total=21 fails=0 RESULT: PASS no Linux |
| 2026-11-10 09:46:44 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc da QA + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-092954-test-gate.log exit: 0 / Aprovado 335 / Com falha 0; residuos 0; verdicto FAILED no QA-0026-upload-midia-skia.md; check OK log 19 evidencias 26 |
| 2026-11-10 11:01:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-100241-test-gate.log - baseline pre-edicao: exit 0 e 335/335 lidos dentro do log |
| 2026-11-10 11:01:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0104 BadHttpRequestException ambiguo entre Microsoft.AspNetCore.Http e Kestrel.Core - corrigido com o tipo totalmente qualificado |
| 2026-11-10 11:01:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 1 | CS0118 Stream resolvia para o namespace Squid.Tests.Stream e CS0534 do override - corrigido com System.IO.Stream |
| 2026-11-10 11:01:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | 0 Erro(s)/1 aviso (CS0675 pre-existente em QaTask0027Tests.cs:687) |
| 2026-11-10 11:01:05 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaCeilingTests | 1 | docs/sdd/runs/test-runs/20261110-102629-test-filter.log - HttpClient recusou o Content-Length falso do proprio teste; teste reescrito com socket cru |
| 2026-11-10 11:01:05 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaCeilingTests | 1 | docs/sdd/runs/test-runs/20261110-103055/103817/104131-test-filter.log - parser de chunk errado; dump cru do 104131 provou o envelope 413 no wire |
| 2026-11-10 11:01:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaCeilingTests | 0 | docs/sdd/runs/test-runs/20261110-104804-test-filter.log - 2/2 na classe nova |
| 2026-11-10 11:01:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests | 0 | docs/sdd/runs/test-runs/20261110-104840-test-filter.log - 5/5 da QA verdes depois da correcao |
| 2026-11-10 11:01:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-105251-test-gate.log - gate final: exit 0 e 337/337 (335 + 2 novos) lidos dentro do log |
| 2026-11-10 11:01:07 | dev | git status --porcelain -uall contra docs/sdd/qa/QA-0026-git-baseline.txt + mtime desde 10:00:00 sem bin/obj | 0 | delta = 1 entrada nova (MediaCeilingTests.cs) e 4 arquivos com mtime, todos dentro dos touches |
| 2026-11-10 11:01:07 | dev | contagem dos 6 patterns do docs/sdd/qa/qa-mutate-task-0027.ps1 em MediaEndpoints.cs | 0 | count=1 nos 6 (M2/M3/M4/M5/M9/M10) - o driver da QA segue aplicavel |
| 2026-11-10 11:01:12 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-105251-test-gate.log exit: 0 / Aprovado 337 / Com falha 0; residuos 0; DEV log 6/evidence 12 carimbados |
| 2026-11-10 13:05:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261110-113636-test-gate.log - baseline round-2: exit 0 e 337/337 lidos dentro do log (S-22) |
| 2026-11-10 13:05:54 | qa | git status --porcelain -uall > docs/sdd/qa/QA-0027-git-baseline.txt | 0 | docs/sdd/qa/QA-0027-git-baseline.txt - 125 entradas em 11:36, antes de a QA escrever |
| 2026-11-10 13:05:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests | 0 | docs/sdd/runs/test-runs/20261110-121653-test-filter.log - classe da QA 5/5 no round 2 |
| 2026-11-10 13:05:55 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~MediaCeilingTests | 0 | docs/sdd/runs/test-runs/20261110-121708-test-filter.log - testes do DEV 2/2 re-executados |
| 2026-11-10 13:05:56 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~Qa0027CeilingProbe --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0027-ceiling-probe.log - sonda 3/3: streaming 51 MiB recebeu envelope 413, cru 69206017 envelope, cru 53000000 esperado (arquivo da sonda removido depois) |
| 2026-11-10 13:05:56 | qa | dotnet test backend/Squid.slnx -c Debug -m:1 --filter FullyQualifiedName~QaTask0027Tests --logger console;verbosity=detailed | 0 | docs/sdd/qa/QA-0027-observations.log - as 10 observacoes do round 2 |
| 2026-11-10 13:05:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0027.ps1 | 0 | docs/sdd/qa/QA-0027-mutations.log - 13 ciclos PROVEN (M13/M14 mutam o fix da QA-0026), SUMMARY cycles=13 failed=0 residue=0 temp-backups=0 |
| 2026-11-10 13:05:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~QaTask0027Tests (final) | 0 | docs/sdd/runs/test-runs/20261110-125307-test-filter.log - 5/5 depois da remocao da sonda |
| 2026-11-10 13:05:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-06) | 0 | docs/sdd/runs/test-runs/20261110-125712-test-gate.log - 337/337, exit lido dentro do log (S-22) |
| 2026-11-10 13:05:58 | qa | varredura AC-06: git delta x2 + janela da correcao + props + residuo + SHA256 PRE + SkiaSharp + TEMP | 0 | docs/sdd/qa/QA-0027-scope.log - correcao 4 arquivos nos touches, props intocado, fora dos toques 0, residuo 0, 5/5 PRE |
| 2026-11-10 13:05:59 | qa | bash docs/sdd/_tools/sdd-test.sh probe / test backend/Squid.slnx (WSL) | 1 | docs/sdd/qa/QA-0027-sh-twin.log - probe exit 0 com dotnet command not found, test exit 127 - Linux registrado como unverified |
| 2026-11-10 13:05:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0027.ps1 | 0 | docs/sdd/qa/QA-0027-check.log run1 - SUMMARY total=22 fails=0 RESULT: PASS (expectativas atualizadas para round 2) |
| 2026-11-10 13:05:59 | qa | bash docs/sdd/qa/qa-check-task-0027.sh (WSL) | 0 | docs/sdd/qa/QA-0027-check.log run2 - SUMMARY total=22 fails=0 RESULT: PASS no Linux |
| 2026-11-10 13:06:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check + re-execucao final dos checkers apos os appends | 0 | docs/sdd/qa/QA-0027-check.log - confirmacao final: ps1 22/22 e sh 22/22 PASS, sdd-state check OK |
| 2026-11-10 13:11:09 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-125712-test-gate.log exit: 0 / Aprovado 337 / Com falha 0; residuos 0; verdicto PASSED no QA-0027-upload-midia-skia.md; check OK log 38 evidencias 53 |
