<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0029 — Link unfurl com guarda SSRF

```yaml
runId: R-20261109-01
taskId: TASK-0029
status: DONE
agent: odev
phase: P-04
sequence: 2
createdAt: 2026-11-09 11:22:36
updatedAt: 2026-11-10 18:13:24
parent: SPEC-0009
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Unfurl/**"]
blocks: ["TASK-0030"]
blockedBy: ["TASK-0028"]
inputArtifacts: ["docs/sdd/specs/SPEC-0009-publicacao-feed-postagem.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Posts/**", "backend/src/Squid.Service/**"]
interviewRounds: 0/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0029 PASSED (5/5 ACs, gate 411/411, 6 mutacoes); proxima entrega TASK-0030
evidence: []
memoryBankFlags: []
```

## Intent

Fecha o item "texto, link" do Sprint 3: post cujo conteúdo é uma URL ganha
`Attachment.Kind: Link` com metadados OpenGraph buscados de forma **síncrona com timeout 3 s,
fail-open** e guarda SSRF completa — e nenhum teste do gate toca rede real.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /posts` com uma URL no conteúdo publica com um `Attachment.Kind: Link` contendo
  `og:title`, `og:description` e `og:image` extraídos (o teste usa `HttpMessageHandler` falso
  servindo HTML com OpenGraph) — teste.
- AC-02: fetch falha/estoura timeout → o post é criado mesmo assim, só com a URL (fail-open,
  201) — teste com handler que nunca responde (timeout curto configurável).
- AC-03: guarda SSRF: URL apontando para loopback/privado/link-local/CGNAT (`127.0.0.1`,
  `10.x`, `192.168.x`, `169.254.x`, `100.64.x`, `[::1]`) ou esquema fora de `http/https` →
  **nenhum fetch é feito** (handler nunca é chamado) e o post sai com só a URL — teste; o mesmo
  vale quando o **redirecionamento** desvia para IP privado (revalidação por salto) — teste.
- AC-04: resposta acima de 1 MB não é lida além do limite (fetch aborta) e o post segue
  (fail-open) — teste com corpo grande no handler.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; nenhum arquivo alterado fora dos `touches` — e nenhum teste desta task usa rede real
  (só handlers falsos).

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Posts/**` (unfurl no create + parsing OpenGraph)
- `backend/src/Squid.Service/**` (registro do cliente HTTP com resolução validada + config
  `Unfurl:TimeoutMs` default 3000, `Unfurl:MaxBytes` default 1 MB)
- `backend/tests/Squid.Tests/Unfurl/**` (novo — handlers falsos)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-26 | no | síncrono, timeout 3 s, fail-open, SSRF por validação de IP resolvido (todas as respostas do resolver) + revalidação por salto de redireção; só http/https |
| — | no | unfurl roda só quando o conteúdo é uma URL única (1º token parseável como http/https); textos com URL embutida ficam sem attachment (MVP) |
| — | no | `og:image` é guardada como URL externa no attachment Link (sem baixar a imagem de terceiros) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-09 11:34:57 | odev | task created from SPEC-0009 (entrega 7 de 10; blockedBy TASK-0028; unfurl com guarda SSRF)
- 2026-11-10 15:27:20 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0028 resolve (DONE com QA-0028 PASSED, gate 348/348); baseline do gate e 348/348
- 2026-11-10 16:52:47 | dev | feasibility 1/2: task coerente contra o codigo existente (unfurl no create de POST /posts em PostEndpoints, registro na raiz SquidServiceHost, Attachment do Domain so ganha campos que ja existem); touches confirmados e completos - nenhum teste existente publica conteudo que e URL unica (grep em backend/tests); baseline do gate 348/348 exit 0; sem duvida bloqueante - mapeamento OG para as colunas fixas de 2.6 decidido nos campos livres do registro (url=og:image, alt=og:title, blurHash=og:description, contentHash vazio = sem blob)
- 2026-11-10 16:53:02 | dev | implementacao: LinkUnfurler sincrono fail-open em Squid.Api/Posts (regra de URL unica, so http/https, guarda SSRF de todas as respostas do resolver + revalidacao a cada salto, max 3 redirects, corpo cortado em Unfurl:MaxBytes, timeout total Unfurl:TimeoutMs=3000) + UnfurlOptions + parser OpenGraph; SquidServiceHost registra opcoes, DnsUnfurlResolver, HttpClient com AllowAutoRedirect=false e o unfurler; SqliteObjectStore pula contentHash vazio (Link sem blob); create de POST /posts anexa o card quando cabe no teto de 10
- 2026-11-10 16:53:30 | dev | testes: 50 novos em backend/tests/Squid.Tests/Unfurl (rota AC-01..AC-04 + unidades do guard, da regra de URL unica, do parser e das opcoes), todos com HttpMessageHandler/fake resolver e zero rede; run com filtro FullyQualifiedName~Squid.Tests.Unfurl: exit 0, 50/50
- 2026-11-10 16:55:37 | dev | gate apos a mudanca: powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test exit 0 - 398/398 (baseline 348/348 + 50 novos), log docs/sdd/runs/test-runs/20261110-165329-test-gate.log; escopo conferido por lista de arquivos da sessao: nenhum arquivo de codigo fora dos touches
- 2026-11-10 17:03:14 | odev | ODEV verificou antes de transicionar: gate final 20261110-165329-test-gate.log lido no arquivo (exit: 0, Aprovado 398, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 6, evidencias 4, carimbadas pelo DEV); step result DONE aceito - nenhum duvida, 50 testes novos todos com handler falso (AC-05 sem rede real)
- 2026-11-10 17:03:14 | odev | decisao de mapeamento OG adjudicada em nivel de especificacao: data-model 2.6 so prende alt/blurHash para Image|Video|Gif e contentHash/blob para nao-Link, entao os campos sao LIVRES para Kind: Link - url=og:image (externa, nunca baixada), alt=og:title, blurHash=og:description, contentHash vazio, mimeType=Content-Type; aceito, com o cheiro registrado (blurHash segurando texto de descricao) para contracts.md/consolidacao - se a QA levantar como MAJOR, escada ate humano
- 2026-11-10 17:03:15 | odev | parked ideas registradas: unfurl em replies (fora dos ACs) e pinning da conexao no IP validado (DNS-rebinding, shortcut declarado no AllowedAsync); flags MUDANCA_RELEVANTE SEGURANCA (guarda SSRF) e CONTRATO (card Link) para a consolidacao
- 2026-11-10 17:03:15 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA; entrega 7 de 10 da P-04
- 2026-11-10 18:08:43 | qa | QA-0029 attempt 1/2: 5 ACs executados - filtro ~Unfurl 63/63 (50 DEV + 13 QA) e gate final 411/411 exit 0 lido dentro do log (S-22); 6 mutacoes can-fail M1..M6 vermelhas classificadas no log + restore byte-exact hash=PRE (S-16/S-23); escopo final 37 arquivos 0 fora da allowlist, git +10 todas QA-0029 e 0 removidas, residuo QA-MUTATION 0; veredito PASSED; achados: 1 pre-existente (sdd-test.sh no Linux classifica dotnet ausente como falha real) e 1 MINOR de contrato para adjudicacao (data-model 2.6 diz Url = servido pela instancia, o card Link guarda og:image externo - junto do cheiro blurHash=prosa ja registrado); checkers ps1 e sh 10/10 exit 0; suite Linux segue unverified (sem dotnet no WSL)
- 2026-11-10 18:13:27 | odev | ODEV verificou antes de transicionar: QA-0029-unfurl-ssrf.md com verdicto PASSED; gate final 20261110-174218-test-gate.log lido no arquivo (exit: 0, Aprovado 411, Com falha 0 - S-22); residuos QA-MUTATION = 0; check exit 0 (log 11, evidencias 12); veredito aceito - 5/5 ACs, 6 ciclos can-fail, SSRF provado contra IP RESOLVIDO privado e revalidacao no meio da cadeia de redirects, fail-open em timeout/5xx/oversize/mais-de-3-saltos
- 2026-11-10 18:13:27 | odev | F2 de contrato adjudicado (escada: especificacao, NAO humano): o data-model 2.6 diz Url = Servido pela instancia, mas para Kind: Link a tabela nao define a coluna (Link nao tem blob, o GET /media indexa por hash vazio e um card de link TEM de apontar para fora) - leitura aceita: a nota de Url vale para os kinds com blob; o cheiro blurHash=prosa vai junto; virou S-38 no backlog (colunas OG proprias quando o Domain puder mudar) e vai para contracts.md na consolidacao; se o humano ler como MAJOR, eu reabro
- 2026-11-10 18:13:27 | odev | transicao AWAITING_QA -> DONE; TASK-0029 = entrega 7 de 10 da P-04 encerrada (QA-0029 PASSED); cadeia libera a TASK-0030

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-10 16:55:50 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes da mudanca) | 0 | docs/sdd/runs/test-runs/20261110-153351-test-gate.log (348/348) |
| 2026-11-10 16:56:10 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | stdout: build exit 0 (erros de compilacao das iteracoes 1-3 corrigidos; so warnings pre-existentes) |
| 2026-11-10 16:56:11 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Unfurl | 0 | docs/sdd/runs/test-runs/20261110-164847-test-filter.log (50/50 novos) |
| 2026-11-10 16:56:12 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate apos a mudanca) | 0 | docs/sdd/runs/test-runs/20261110-165329-test-gate.log (398/398) |
| 2026-11-10 17:03:16 | odev | Select-String do gate final + varredura QA-MUTATION em backend + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-165329-test-gate.log exit: 0 / Aprovado 398 / Com falha 0; residuos 0; check OK log 6 evidencias 4 |
| 2026-11-10 18:09:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline da QA) | 0 | docs/sdd/runs/test-runs/20261110-172026-test-gate.log (exit: 0, Aprovado 398, Total 398) |
| 2026-11-10 18:09:21 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Unfurl | 0 | docs/sdd/runs/test-runs/20261110-172550-test-filter.log (Com falha 0, Aprovado 63, Total 63) |
| 2026-11-10 18:09:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0029.ps1 | 0 | docs/sdd/qa/QA-0029-mutations.log (M1..M6 RED-IN-LOG + RESTORE-OK hash=PRE; SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0) |
| 2026-11-10 18:09:22 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final) | 0 | docs/sdd/runs/test-runs/20261110-174218-test-gate.log (exit: 0, Aprovado 411, Total 411) |
| 2026-11-10 18:09:22 | qa | varredura de escopo final (mtimes desde dispatch 15:27 + git vs QA-0029-git-baseline + residuo QA-MUTATION) | 0 | docs/sdd/qa/QA-0029-unfurl-ssrf.md (37 arquivos, 0 fora da allowlist; git +10 todas QA-0029, 0 removidas; residuo 0) |
| 2026-11-10 18:09:23 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0029.ps1 | 0 | docs/sdd/qa/QA-0029-check.log (SUMMARY total=10 fails=0) |
| 2026-11-10 18:09:23 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0029.sh (WSL bash) | 0 | docs/sdd/qa/QA-0029-check-sh.log (SUMMARY total=10 fails=0) + QA-0029-sh-twin.log (sdd-test.sh probe exit 0, test exit 1) |
| 2026-11-10 18:13:28 | odev | Select-String do gate final + varredura QA-MUTATION em backend + verdicto do doc + sdd-state check | 0 | docs/sdd/runs/test-runs/20261110-174218-test-gate.log exit: 0 / Aprovado 411 / Com falha 0; residuos 0; verdicto PASSED no QA-0029-unfurl-ssrf.md; check OK log 11 evidencias 12 |
