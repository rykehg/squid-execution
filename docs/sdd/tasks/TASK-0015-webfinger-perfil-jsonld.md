<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0015 — Webfinger + perfil JSON-LD com round-trip

```yaml
runId: R-20261107-02
taskId: TASK-0015
status: DONE
agent: odev
phase: P-02
sequence: 2
createdAt: 2026-11-07 16:19:35
updatedAt: 2026-11-07 16:19:35
parent: SPEC-0007
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Discovery/**"]
blocks: []
blockedBy: ["TASK-0014"]
inputArtifacts: ["docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Discovery/**", "backend/src/Squid.Protocols/**"]
interviewRounds: 0/6
feasibilityRounds: 1/2
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
nextAction: DONE - QA-0014 PASSED (5/5 ACs, qaAttempt 1/2); TASK-0015 encerrada
evidence: []
memoryBankFlags: [DIVERGENCIA adjudicada: activitypub contract §1/§1.1 estado final (rel=profile, inbox/outbox/followers) nao servido - B-09/B-10 nao exigem, rotas inexistentes - virou S-26 no backlog, P-06/P-08 - source: specs/001-squid-mvp/contracts/activitypub.md:12,14 vs backend/src/Squid.Api/Discovery/DiscoveryEndpoints.cs:96] [GAP: caminho Linux nativo nao executavel - WSL sem dotnet SDK; twin .sh 12/12, CI ubuntu e o dono do run Linux]
```

## Intent

Metade da prova de descoberta do DoD do Sprint 1 (SPEC-0007 B-09..B-11, FR-003): webfinger
resolve o ator local e `/users/{name}` serve o perfil em JSON-LD com as chaves públicas, sem sessão.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `GET /.well-known/webfinger?resource=acct:<nome>@<domínio-da-instância>` para ator
  existente → 200 com `subject` `acct:...` e `link rel=self` de `application/activity+json`
  apontando para `/users/<nome>` — provado por execução HTTP.
- AC-02: webfinger para handle desconhecido, malformado ou de **outro** domínio → 404 (sem proxy
  de domínio estrangeiro) — teste com os três casos.
- AC-03: `GET /users/<nome>` sem sessão → 200 com JSON-LD do perfil contendo `id`, `preferredUsername`
  e as chaves públicas RSA-2048 (`#main-key`) + Ed25519 — teste; sem `RK`/privada em claro no
  documento (asserção).
- AC-04: round-trip canônico ↔ JSON-LD: serializar o perfil do ator para JSON-LD e materializar de
  volta produz os mesmos campos (nada se perde) — teste que falharia se algum campo fosse dropado.
- AC-05: gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Discovery/**` (novo diretório)
- `backend/src/Squid.Protocols/**` (mapeamento canônico ↔ JSON-LD do perfil)
- `backend/src/Squid.Service/**` (registro das rotas)
- `backend/tests/Squid.Tests/Discovery/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| — | no | o JSON-LD do perfil segue o vocabulário ActivityPub (`Person`); `@context` fixo URL do padrão — nenhum I/O de rede em teste (contexto embutido no código) |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | Qual é a fonte do domínio da instância (webfinger `subject`, `link.href`, `id` JSON-LD): `Host` da requisição ou chave de config? | SPEC-0007 B-09a (nível spec, sobre requirement: `spec.md` §Dependencies torna o domínio público dependência de implantação) | **`Host` da requisição**, origem = scheme do request + `Host`, sem chave de configuração — quem responde já está no domínio certo; domínio estrangeiro → 404 sem regra nova; `X-Forwarded-*` parked como I-11 (proxy/fase P-06) |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 16:19:35 | odev | task created from SPEC-0007 (blockedBy TASK-0014; toca Squid.Api junto com TASK-0014/0016, por isso serial)
- 2026-11-07 21:00:05 | odev | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0014 DONE (QA-0013 PASSED 6/6); baseline do gate 147/147
- 2026-11-07 21:22:56 | odev | odev | step result DOUBT recebido (D-01: fonte do dominio da instancia); zero codigo escrito; baseline do DEV 147/147 verificada no log; status IN_DEVELOPMENT -> DOUBT_ESCALATED
- 2026-11-07 21:22:56 | odev | odev | D-01 adjudicada no nivel da escada em que a resposta existe: spec (SPEC-0007 B-09a nova) apoiada no requirement - specs/001-squid-mvp/spec.md §Dependencies torna o dominio publico dependencia de implantacao (dominio aponta para a instancia), logo quem responde ja esta no dominio certo; resposta = Host da requisicao (origem = scheme + Host), sem chave de config surpresa; X-Forwarded-* parked como I-11; subject/link/id derivam da mesma origem; o mesmo DEV reabre feasibility r2
- 2026-11-07 21:22:56 | odev | odev | status DOUBT_ESCALATED -> IN_DEVELOPMENT, feasibilityRounds 0/2 -> 1/2
- 2026-11-07 21:48:48 | odev | odev | ODEV verificou antes de transicionar: gate 20261107-214525-test-gate.log exit 0 com Aprovado 151 / Com falha 0 lido no log (S-22); suite Discovery 4/4 no filtro; git delta dentro dos 4 toques - as 6 linhas do git diff em Squid.Api.csproj sao do TASK-0012 (PackageReference BouncyCastle, diff acumulado, nada da P-02 commitado), nao desta task; ProjectReference Protocols -> Grains e legal (adapter bane so Api/Service/Worker; SQUID0001 rodou no gate verde); check exit 0; step result DONE aceito (feasibility 2/2, logLines/evidenceRows sem timestamps)
- 2026-11-07 21:48:48 | odev | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-07 21:49:38 | dev | feasibility r1: task lida contra o codigo - unit = endpoints novos em Squid.Api/Discovery (webfinger + /users/{name}), mapeamento canonico <-> JSON-LD em Squid.Protocols, registro na composition root SquidServiceHost; chamada existente ActorGrain.GetStateAsync entrega ActorState com PublicKeys (RSA-2048 #main-key, Ed25519 #ed25519); GET sem sessao passa pelo AuthSessionMiddleware (so mutations sao gateadas) - toques confirmados, viaveis sem tocar Squid.Api.csproj: o mapper chega ao endpoint via delegate da composition root (Squid.Service, dentro dos touches), porque edge->adapter e permitido na regra de build mas o csproj fica fora dos touches e AC-05 proibe; serial com TASK-0016 (Squid.Service compartilhado) segue obrigatorio
- 2026-11-07 21:49:38 | dev | feasibility r1: baseline do gate green - sdd-test.ps1 test exit 0 com 147/147 (Com falha 0), classificado pelo resumo dentro do log (S-22), artefato docs/sdd/runs/test-runs/20261107-211413-test-gate.log
- 2026-11-07 21:49:38 | dev | feasibility r1: D-01 aberto e retornado em DOUBT - nenhuma fonte de dominio da instancia existe no codigo nem nos documentos de governancia; suposicao proposta = Host da requisicao; nenhum arquivo tocado, nenhum arquivo fora dos touches
- 2026-11-07 21:49:38 | dev | feasibility r2: D-01 fechada pela spec (B-09a: dominio = Host da requisicao, origem = scheme + Host, X-Forwarded-* fora como I-11); ponytail full - mapeamento JSON-LD em Squid.Protocols com ProjectReference para Squid.Grains dentro dos touches, endpoint em Squid.Api/Discovery recebendo o mapper por delegate da composition root, Squid.Api.csproj intocado zero vezes
- 2026-11-07 21:49:38 | dev | implementado AC-01..AC-04: webfinger responde 200 com subject acct e link rel=self application/activity+json para /users/{nome} e 404 nos tres casos (desconhecido, malformado, dominio estrangeiro incluindo host sem porta); /users/{nome} sem sessao serve JSON-LD com @context fixo embutido, id, preferredUsername, RSA #main-key em PEM e Ed25519 em multibase, sem WrappedSigningKey nem RK no documento (varredura raw + decodificada conforme S-25); round-trip canonico <-> JSON-LD devolve registro igual e id -> JSON-LD -> id igual
- 2026-11-07 21:49:38 | dev | gate: primeira execucao vermelha por erro de compilacao no teste novo (RootKey para byte[]), corrigido com AsSpan() no mesmo padrao do teste PII do TASK-0014; reexecucao verde 151/151 (147 + 4 novos) e suite Discovery isolada 4/4, ambos classificados pelo resumo dentro do log gravado (S-22)
- 2026-11-07 21:49:38 | dev | escopo (AC-05): git status mostra apenas os quatro toques da task - Discovery em Squid.Api, Protocols (csproj + ActivityPub), SquidServiceHost em Squid.Service e tests/Discovery; nenhum arquivo fora dos touches, nenhuma mutacao ou restore feita (S-23 nao aplicavel)
- 2026-11-07 22:45:29 | qa | QA-0014 dispatched (qaAttempt 1/2): task lido, AC-01..AC-05 mapeados contra SPEC-0007 B-09..B-11 + B-09a (D-01); baseline confirmada no log 20261107-214525 (exit 0, 151/151, S-22); git status baseline capturado com 58 entradas
- 2026-11-07 22:45:29 | qa | QA escreveu 5 cenarios proprios em backend/tests/Squid.Tests/Discovery/Qa0014WebfingerAcceptanceTests.cs (HTTP real, cliente sem cookies, PEM RSA parseado como SPKI de 2048 bits, Ed25519 decodificado por base58 proprio, round-trip byte a byte, origem vem do Host da requisicao com host forjado e X-Forwarded ignorado) e os checkers docs/sdd/qa/qa-check-task-0015.ps1 e .sh (12 checks, twin Linux)
- 2026-11-07 22:45:29 | qa | 1a execucao do cenario QA AC-03 vermelha por defeito do proprio teste (linha 138: linha do ator lida como ActorState, mas ator novo so existe no log de eventos com Snapshot null) - corrigido com a leitura tipada do produto (SqliteActorEventStore.ReadAsync), retry unico; Discovery 9/9 no log 20261107-222243 exit 0; checkers tiveram 3 e 2 execucoes por defeitos proprios ate SUMMARY total=12 fails=0 nas duas plataformas
- 2026-11-07 22:45:29 | qa | mutacao M1 (can-fail AC-02): guarda de dominio estrangeiro removida em DiscoveryEndpoints.cs - suite Discovery vermelha 3 falhas (log 20261107-223450 exit 1 Com falha 3; console do wrapper imprimiu exit 3, S-22 reproduzido ao vivo) + checker 3 FAILs; restore SHA256 byte-exact 34D4CC50 com mtime bump (S-23) e verde 9/9
- 2026-11-07 22:45:29 | qa | mutacao M2 (can-fail AC-04): name descartado no ToDocument de ProfileJsonLd.cs - vermelho 2 falhas (log 20261107-223724 exit 1, Name perdido no round-trip nas duas suites) + restore byte-exact C1CB00BA com mtime bump e verde 9/9; backups em %TEMP% removidos apos as duas verificacoes
- 2026-11-07 22:45:29 | qa | gate final sdd-test.ps1 test exit 0 (log 20261107-223928: Aprovado 156, Com falha 0 = 151 baseline + 5 QA, S-22); checker final 12/12 no ps1 e no sh (WSL); git delta sem nada fora dos toques + artefatos QA (AC-05); QA-0014 escrita em docs/sdd/qa/QA-0014-webfinger-perfil-jsonld.md
- 2026-11-07 22:45:29 | qa | findings: 0 BLOCKER, 3 MINOR - (1) harness sdd-test.ps1:80-81 imprimiu exit 3 para um vermelho real (S-22, reproduzido); (2) activitypub.md:12,14 rel=profile e inbox/outbox/followers ainda nao servidos - escopo futuro nao exigido por B-09/B-10, proposto como DIVERGENCIA para ODEV; (3) defeitos proprios da QA corrigidos antes do veredito
- 2026-11-07 22:49:57 | odev | odev | ODEV verificou antes de transicionar: gate 20261107-223928-test-gate.log exit 0 com Aprovado 156 / Com falha 0 lido no log (S-22); mutacao M1 20261107-223450-test-filter.log com exit: 1 e Com falha: 3 no log (wrapper imprimiu 3 = S-22 de novo); SHA256 atuais DiscoveryEndpoints.cs = 34D4CC507FD70477235EF38F3027DE6DF0BB0190F1F1C18FBB15D9F6D849E1A9 e ProfileJsonLd.cs = C1CB00BAA7CA7170A6B657C37A8CC900A26452F58BE0BC6A75E1DE550F553342 = PRE/POST da QA; QA-0014 existe; check exit 0 (21 logs, 29 evidencias)
- 2026-11-07 22:49:57 | odev | odev | finding [DIVERGENCIA] adjudicado: contrato activitypub §1/§1.1 lista rel=profile e inbox/outbox/followers como ESTADO FINAL do MVP, nao exigido por B-09/B-10 (autoridade da fase) e sem rotas existentes - nao e falha desta task; virou S-26 no backlog com ponteiro P-06 (rotas) e P-08 (rel=profile com front), e a linha foi escrita no Out of scope da SPEC-0007; nao toca contrato publicado em contraditoria com AC testada, portanto sem gate humano
- 2026-11-07 22:49:57 | odev | odev | verdicto QA-0014 adjudicado: PASSED com 5/5 ACs provadas por execucao propria (9/9 filtrado com 5 cenarios independentes da QA, B-09a verificada em codigo e por HTTP com host forjado, 2 mutacoes can-fail vermelhas restauradas byte-exact com bump mtime, checkers 12/12 nas duas plataformas, gate 156/156); unverified aceito (Linux nativo - CI ubuntu e o dono); 0 BLOCKER; transicao AWAITING_QA -> DONE

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 21:49:38 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-211413-test-gate.log - feasibility r1: exit 0, Aprovado 147, Com falha 0 (S-22) |
| 2026-11-07 21:49:39 | dev | Select-String de Configuration[...] sobre backend/src + contagem de appsettings*.json | 0 | 1 unica chave de configuracao lida (SquidServiceHost.cs:68 Storage:Path), 0 appsettings - nao existe chave de dominio (base de D-01) |
| 2026-11-07 21:49:39 | dev | Select-String de padrao de config de dominio em 7 documentos de governancia | 0 | 0 ocorrencias de fonte de dominio; 21 ocorrencias de webfinger/users com placeholder (base de D-01) |
| 2026-11-07 21:49:40 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-214525-test-gate.log - exit 0, Aprovado 151, Com falha 0 = 147 + 4 novos (S-22) |
| 2026-11-07 21:49:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery | 0 | docs/sdd/runs/test-runs/20261107-214600-test-filter.log - exit 0, Aprovado 4, Com falha 0 |
| 2026-11-07 21:49:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (1a execucao apos escrita) | 1 | docs/sdd/runs/test-runs/20261107-214338-test-gate.log - CS1503 no teste novo (RootKey para byte[]); causa corrigida, retry unico |
| 2026-11-07 21:49:42 | dev | git status --porcelain | 0 | apenas Discovery (novo), Protocols (csproj + ActivityPub/), SquidServiceHost.cs, tests/Discovery - dentro dos touches (AC-05) |
| 2026-11-07 22:45:56 | qa | read do log 20261107-214525-test-gate.log (exit e resumo, S-22) | 0 | baseline do gate confirmada: exit 0, Aprovado 151, Com falha 0 |
| 2026-11-07 22:45:57 | qa | git status --porcelain -uall (inicio da QA-0014) | 0 | baseline de 58 entradas capturada e embutida nos dois checkers (AC-05) |
| 2026-11-07 22:45:57 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery | 1 | docs/sdd/runs/test-runs/20261107-221756-test-filter.log - 1a execucao: defeito do proprio teste da QA na linha 138, Com falha 1 |
| 2026-11-07 22:45:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery | 0 | docs/sdd/runs/test-runs/20261107-222243-test-filter.log - exit 0, Com falha 0, Aprovado 9 (DEV 4 + QA 5) |
| 2026-11-07 22:45:58 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1 (1a e 2a execucoes) | 1 | defeitos proprios do script corrigidos: variaveis reservadas host e profile, depois prefixo do porcelain |
| 2026-11-07 22:45:59 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1 (3a execucao) | 0 | SUMMARY total=12 fails=0, RESULT PASS |
| 2026-11-07 22:45:59 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0015.sh (1a execucao) | 1 | defeito proprio do twin (argumento posicional de min-approved) corrigido |
| 2026-11-07 22:46:00 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0015.sh (2a execucao) | 0 | SUMMARY total=12 fails=0, RESULT PASS no Linux |
| 2026-11-07 22:46:01 | qa | Get-FileHash SHA256 + copia para TEMP (backup M1) | 0 | DiscoveryEndpoints.cs PRE 34D4CC507FD70477235EF38F3027DE6DF0BB0190F1F1C18FBB15D9F6D849E1A9 |
| 2026-11-07 22:46:02 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery (mutacao M1) | 1 | docs/sdd/runs/test-runs/20261107-223450-test-filter.log - exit 1, Com falha 3, Aprovado 6 (S-22: console imprimiu exit 3) |
| 2026-11-07 22:46:02 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1 (sob M1) | 1 | SUMMARY total=12 fails=3 - FAIL AC01-single-origin-code, AC02-foreign-404-rule, AC05-filter-log-green |
| 2026-11-07 22:46:03 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M1, S-23) | 0 | BYTE_EXACT True, PRE igual a POST 34D4CC50, mtime 2026-11-08 01:35:35 UTC, sem marcador de mutacao |
| 2026-11-07 22:46:03 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery (apos restore M1) | 0 | docs/sdd/runs/test-runs/20261107-223608-test-filter.log - exit 0, Com falha 0, Aprovado 9 |
| 2026-11-07 22:46:04 | qa | Get-FileHash SHA256 + copia para TEMP (backup M2) | 0 | ProfileJsonLd.cs PRE C1CB00BAA7CA7170A6B657C37A8CC900A26452F58BE0BC6A75E1DE550F553342 |
| 2026-11-07 22:46:05 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery (mutacao M2) | 1 | docs/sdd/runs/test-runs/20261107-223724-test-filter.log - exit 1, Com falha 2: Name perdido no round-trip |
| 2026-11-07 22:46:05 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M2, S-23) | 0 | BYTE_EXACT True, PRE igual a POST C1CB00BA, mtime 2026-11-08 01:37:39 UTC |
| 2026-11-07 22:46:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Discovery (apos restore M2) | 0 | docs/sdd/runs/test-runs/20261107-223816-test-filter.log - exit 0, Com falha 0, Aprovado 9 |
| 2026-11-07 22:46:06 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final AC-05) | 0 | docs/sdd/runs/test-runs/20261107-223928-test-gate.log - exit 0, Aprovado 156, Com falha 0 = 151 baseline + 5 QA (S-22) |
| 2026-11-07 22:46:07 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0015.ps1 (final com o doc QA-0014) | 0 | SUMMARY total=12 fails=0; touches baseline 58 current 62 missing 0 out-of-scope 0 |
| 2026-11-07 22:46:07 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0015.sh (final) | 0 | SUMMARY total=12 fails=0 - os mesmos 12 check ids passam no Linux |
| 2026-11-07 22:46:08 | qa | Remove-Item dos backups TEMP qa0015 apos as verificacoes byte-exact | 0 | 0 arquivos qa0015 restantes no TEMP (sem efeitos colaterais) |
| 2026-11-07 22:46:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check TASK-0015 | 0 | check: OK - log entries 21, evidence rows 28 |
