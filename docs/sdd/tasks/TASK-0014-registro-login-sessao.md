<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0014 — Registro + login/sessão (cookie, CSRF, logout)

```yaml
runId: R-20261107-02
taskId: TASK-0014
status: DONE
agent: odev
phase: P-02
sequence: 2
createdAt: 2026-11-07 16:19:35
updatedAt: 2026-11-07 16:19:35
parent: SPEC-0007
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Auth/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Auth/**"]
blocks: ["TASK-0015", "TASK-0016"]
blockedBy: ["TASK-0012", "TASK-0013"]
inputArtifacts: ["docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Auth/**"]
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
nextAction: DONE - QA-0013 PASSED (6/6 ACs + facet RK, qaAttempt 1/2); TASK-0014 encerrada
evidence: []
memoryBankFlags: [PADRAO_NOVO: varredura de PII/chave sobre JSON persistido ou resposta HTTP precisa ler o texto decodificado - System.Text.Json escapa + de base64 como u002B, prova medida 124/200 cru vs 0/200 decodificado - source: backend/tests/Squid.Tests/Auth/Qa0013AuthAcceptanceTests.cs] [MUDANCA_RELEVANTE: CONTRATO - superficie de auth confirmada contra os fatos adjudicados (exempt exatamente register+login, 401 byte-identico sem traceId com dummy hash sempre verificado, cookies HttpOnly/Secure/Lax e csrf nao-HttpOnly, kit unico no formato do contrato §3, auth/account por handle sem migracao, /api/auth/me ausente) - source: backend/src/Squid.Api/Auth/AuthEndpoints.cs:130] [MUDANCA_RELEVANTE: SEGURANCA - senha e RK ausentes de 126 logs de execucao e de 7 corpos de resposta reais, varredura crua + decodificada - source: docs/sdd/qa/QA-0013-registro-login-sessao.md]
```

## Intent

Fecha FR-001/FR-002/FR-004/FR-062 (SPEC-0007 B-01..B-07, B-16): `POST /api/auth/register` cria o
ator local via `ActorGrain` gerando `RK`/`RC`/RSA pelas primitivas da TASK-0012; login devolve
sessão cookie com CSRF dupla submissão; `logout`/`logout-all` revogam.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/auth/register` com `handle` novo e senha ≥ 10 cria o ator e devolve sessão
  (cookies `squid_session` HttpOnly/Secure/SameSite=Lax e `squid_csrf`); o kit aparece na resposta
  **uma vez** — provado por execução HTTP.
- AC-02: registro duplicado → 409 com mensagem clara; `handle` malformado ou senha < 10 → 422
  citando a regra; nos três casos nada é persistido — teste.
- AC-03: `POST /api/auth/login` certo → 200 + cookies; senha errada **e** handle inexistente → a
  mesma resposta 401 (corpo igual, não enumera conta) — teste comparando os dois corpos.
- AC-04: mutação sem `X-CSRF-Token` ou com valor divergente do cookie `squid_csrf` → 403; com o
  valor certo → executa — teste (dupla submissão R-09).
- AC-05: `logout` invalida a sessão atual e `logout-all` invalida todas: cookie antigo → 401 —
  teste; e a senha nunca aparece em claro em log/span/resposta (grep + asserção de PII) — FR-004/§4.7.
- AC-06: gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Auth/**` (novo diretório)
- `backend/src/Squid.Service/**` (registro das rotas mínimas em `Program`/`SquidServiceHost`)
- `backend/tests/Squid.Tests/Auth/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-07 | no | senha + sessão cookie Argon2id (decisão humana 2026-11-07); passkey/OIDC fora |
| — | no | `Squid.Api/Crypto/**` (TASK-0012) **sela a RK**, não hasheia senha de login (B-04): o helper Argon2id de senha vive dentro dos toques desta task (`Squid.Api/Auth/**`), usando o mesmo pacote BouncyCastle já referenciado no csproj |
| — | no | o adaptador de produção `IActorEventStore -> SqliteGrainStorage` hoje só existe na árvore de testes (flag GAP da TASK-0013, backlog S-21): registrá-lo em `Squid.Service` no wiring do registro — `Squid.Api` não pode referenciar `Squid.Persistence` (ADR-35) |
| — | no | persistência de sessão é escolha de implementação do DEV (stateless com token assinado ou storage), **desde** que `logout`/`logout-all` sejam observáveis; registrada no log |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 16:19:35 | odev | task created from SPEC-0007 (blockedBy TASK-0012+TASK-0013)
- 2026-11-07 18:11:59 | odev | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0012 e TASK-0013 ambas DONE (QA-0012 e QA-0011 PASSED); gaps de wiring preparados (helper de senha Argon2id em Auth/**, adaptador IActorEventStore em Squid.Service)
- 2026-11-07 18:42:21 | dev | feasibility 1/2: unit=Squid.Api/Auth (rotas, hash de senha Argon2id, sessao+CSRF); caller=SquidServiceHost (MapSquidAuth + DI de IActorEventStore/IAuthStore); dados=linha KV store auth key account/<handle> em GrainState + stream store actors do TASK-0013; touches confirmado sem acrescimos (SquidApi.cs e Crypto/** intocados); kit do registro fechado por FR-005 + SPEC-0007 (register out = sessao + kit uma vez); baseline gate 119/119 exit 0
- 2026-11-07 19:22:20 | dev | escrita: 8 arquivos em backend/src/Squid.Api/Auth/** (AuthEndpoints: register/login/logout/logout-all; AuthSessionMiddleware: resolucao de sessao, slide de 30 dias e CSRF de dupla submissao; PasswordHasher Argon2id PHC; Mnemonic de 24 palavras; IAuthStore + AuthAccount; AuthSessions; AuthErrors), 2 novos em backend/src/Squid.Service/** (SqliteActorEventStore de producao = backlog S-21, SqliteAuthStore sobre SqliteGrainStorage, store auth key account/<handle> sem migracao) e 5 em backend/tests/Squid.Tests/Auth/**; SquidServiceHost modificado so com as 3 DI e app.MapSquidAuth()
- 2026-11-07 19:28:30 | dev | gate final: sdd-test.ps1 test exit 0 (log 20261107-192322-test-gate.log com exit: 0) - Aprovado 141, Com falha 0, Total 141 = baseline 119 + 22 novos; AC-01..AC-06 verdes; nenhuma alteracao fora dos toques (git status delta = so os 3 toques de codigo)
- 2026-11-07 19:35:37 | odev | odev | ODEV verificou antes de transicionar: gate 20261107-193032-test-gate.log com exit: 0 e Aprovado 141 / Com falha 0 lido no log (S-22); git status delta = so os 3 toques de codigo; Squid.Api.csproj sem ProjectReference para Persistence (ADR-35 intacto); GET /api/auth/me confirmado ausente; check exit 0 (5 logs, 9 evidencias); step result DONE aceito sem timestamp inventado
- 2026-11-07 19:35:37 | odev | odev | conflito de toques adjudicado no nivel spec: GET /api/auth/me (contrato §2, exigida pelo AC-02 da TASK-0016 para o aviso de kit B-13/FR-007) NAO entra na TASK-0014 agora - seus ACs estao fechados e reabrir apos DONE mudaria a lista testada; a rota e da entrega do kit, TASK-0016 roda SERIAL apos a 0014, entao os toques da 0016 sao ampliados para backend/src/Squid.Api/Auth/AuthEndpoints.cs especifico; sem paralelismo envolvido (0015 e 0016 seriais entre si); TASK-0016 deve reutilizar Mnemonic.FromRecoveryCode e RecoveryCredential.Format ja existentes
- 2026-11-07 19:35:37 | odev | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-07 20:45:50 | qa | QA attempt 1: cenarios independentes AC-01..AC-06 + facet RK nunca em resposta HTTP escritos e EXECUTADOS - Qa0013AuthAcceptanceTests.cs (6 cenarios HTTP reais) + qa-check-task-0014.ps1 e .sh (16 checks por plataforma, ambos exit 0); DEV's 22 testes reexecutados no gate
- 2026-11-07 20:45:51 | qa | QA attempt 1: mutacoes M1 (portao CSRF removido) e M2 (401 com traceId propria) ficaram VERMELHAS em AC-04/AC-03 e no checker e foram restauradas byte-exact (SHA256 PRE=POST) com bump de mtime (S-23); gate final 20261107-203804-test-gate.log exit 0 com Aprovado 147 Com falha 0 (baseline 141 + 6 QA); o vermelho da gate 20261107-201948 era o proprio teste da QA (extracao do PHC do JSON cru, System.Text.Json escapa + como u002B) com causa raiz medida por dotnet fsi (cru 124/200 vs decodificada 0/200), produto sem defeito; veredito PASSED
- 2026-11-07 20:59:35 | odev | odev | ODEV verificou antes de transicionar: gate 20261107-203804-test-gate.log exit 0 com Aprovado 147 / Com falha 0 lido no log (S-22); mutacao M2 20261107-201705-test-fast.log com exit: 1 e Com falha: 2 no log (wrapper imprimiu 3 = S-22 disparou de novo, confirmado ao vivo); SHA256 atuais de AuthSessionMiddleware.cs = F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948 e AuthErrors.cs = 81BE69E8975F84D51A1E89B5558531BC9745DD7EF16544F8779ADC29B56FA1C7 = PRE/POST declarados; 0 residuo QA-MUTATION; QA-0013 existe; check exit 0
- 2026-11-07 20:59:35 | odev | odev | findings adjudicados: 2 da propria QA (script sem BOM cp1252; extracao crua do PHC com + escapado pelo System.Text.Json) corrigidos na causa pela QA antes de fechar - os dois sao defeito de medicao, nao de produto; o vermelho gate 201948 foi o teste da QA, nao o produto (login 200 na mesma execucao); finding 3 = lacura de cobertura do DEV (scan cru do RK pula base64 com +) registrada como S-25 no backlog com a facet da QA cobrindo a visao decodificada; S-22 ja backlogado segue se repetindo
- 2026-11-07 20:59:35 | odev | odev | verdicto QA-0013 adjudicado: PASSED com 6/6 ACs + facet RK-nunca-em-resposta provadas por execucao propria (6 cenarios HTTP independentes + checkers 16/16 nas duas plataformas, 2 mutacoes can-fail vermelhas restauradas byte-exact com bump de mtime S-23, gate 147/147); unverified aceitos (Linux sem SDK; PII em span/OTLP sem coletor - coberto por grep de 126 logs); 0 BLOCKER; transicao AWAITING_QA -> DONE

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 19:26:17 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (baseline antes de escrever) | 0 | docs/sdd/runs/test-runs/20261107-183331-test-gate.log - exit:0 do log, Com falha 0, Aprovado 119, Total 119 (lido pelo log e nao pelo wrapper, S-22) |
| 2026-11-07 19:26:18 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (1a passada) | 1 | 1 erro CS0117 Convert.ToHexStringUpper inexistente em backend/src/Squid.Api/Auth/AuthAccount.cs:40 - defeito do DEV, nao ambiente |
| 2026-11-07 19:26:18 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (apos correcao na causa) | 0 | backend/Squid.slnx - 0 avisos, 0 erros |
| 2026-11-07 19:26:19 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth (1a passada) | 0 | docs/sdd/runs/test-runs/20261107-191158-test-fast.log - Aprovado 22, Com falha 0 (7 testes HTTP de AC-01..AC-05 + 15 unit) |
| 2026-11-07 19:26:19 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate antes do slide de cookie) | 0 | docs/sdd/runs/test-runs/20261107-191321-test-gate.log - exit:0, Com falha 0, Aprovado 141, Total 141 |
| 2026-11-07 19:26:20 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth (apos Expires e re-emissao do cookie) | 0 | docs/sdd/runs/test-runs/20261107-191936-test-fast.log - Aprovado 22, Com falha 0 |
| 2026-11-07 19:26:20 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final) | 0 | docs/sdd/runs/test-runs/20261107-192322-test-gate.log - exit:0 do log, Com falha 0, Aprovado 141, Total 141 = baseline 119 + 22 novos (AC-06) |
| 2026-11-07 19:26:21 | dev | git status --porcelain | 0 | meus toques: SquidServiceHost.cs modificado, src/Squid.Api/Auth/, src/Squid.Service/SqliteActorEventStore.cs, src/Squid.Service/SqliteAuthStore.cs, tests/Squid.Tests/Auth/ - fora dos toques so pre-existentes de tarefas anteriores (Directory.Packages.props, Squid.Api.csproj, BACKLOG, plan, REQ-0001) e docs/sdd/qa da QA |
| 2026-11-07 19:30:49 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final apos o comentario shortcut) | 0 | docs/sdd/runs/test-runs/20261107-193032-test-gate.log - exit:0 do log, Com falha 0, Aprovado 141, Total 141 - arvore final verde (AC-06) |
| 2026-11-07 20:46:26 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-194741-test-gate.log - exit:0 do log, Aprovado 141 Com falha 0 - gate de referencia da QA antes de qualquer escrita (AC-06 baseline) |
| 2026-11-07 20:46:27 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth.Qa0013 | 1 | docs/sdd/runs/test-runs/20261107-195646-test-fast.log - erros CS1061 CS1503 CS0117 no arquivo de teste da QA - defeito da QA, nao do produto |
| 2026-11-07 20:46:27 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth.Qa0013 | 0 | docs/sdd/runs/test-runs/20261107-195801-test-fast.log - Aprovado 6 Com falha 0 - AC-01..AC-05 executados + facet RK em respostas HTTP |
| 2026-11-07 20:46:27 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 1 | 1a execucao do checker - FAIL AC02-validation-before-persist: script sem BOM lido como cp1252 pelo PowerShell 5.1, literais acentuados nao casavam - defeito da QA corrigido com BOM UTF-8 |
| 2026-11-07 20:46:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 0 | SUMMARY total=16 fails=0 RESULT PASS - Windows PowerShell 5.1, 16 checks |
| 2026-11-07 20:46:28 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 1 | docs/sdd/runs/test-runs/20261107-201134-test-fast.log - mutacao M1a, portao do CSRF passou a disparar sempre - exit:1 do log, Com falha 5, Aprovado 23 |
| 2026-11-07 20:46:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 1 | docs/sdd/runs/test-runs/20261107-201416-test-fast.log - mutacao M1b, portao CSRF removido - exit:1 do log, Com falha 2, AC-04 vermelho nos dois artefatos, Aprovado 26 |
| 2026-11-07 20:46:29 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 1 | checker sob M1b - FAIL AC04-csrf-gate, SUMMARY total=16 fails=1 |
| 2026-11-07 20:46:30 | qa | Copy-Item de backup + Get-FileHash em AuthSessionMiddleware.cs (restauracao M1) | 0 | PRE=POST F2F22F2269D011ED6DB70978382A0EFF515424492C6A8002F4966324A37EE948 BYTE_EXACT=True, bump de mtime apos restaurar (S-23) |
| 2026-11-07 20:46:30 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 0 | docs/sdd/runs/test-runs/20261107-201544-test-fast.log - Aprovado 28 Com falha 0 apos restaurar M1 |
| 2026-11-07 20:46:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 1 | docs/sdd/runs/test-runs/20261107-201705-test-fast.log - mutacao M2, 401 com traceId propria - exit:1 do log, Com falha 2, AC-03 vermelho nos dois artefatos; wrapper imprimiu exit 3 por S-22 (substring handle + resumo Com falha! fora da regex) e o log e quem manda |
| 2026-11-07 20:46:31 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 1 | checker sob M2 - FAIL AC03-single-401-shape tracelessShape=False, SUMMARY total=16 fails=1 |
| 2026-11-07 20:46:31 | qa | Copy-Item de backup + Get-FileHash em AuthErrors.cs (restauracao M2) | 0 | PRE=POST 81BE69E8975F84D51A1E89B5558531BC9745DD7EF16544F8779ADC29B56FA1C7 BYTE_EXACT=True, bump de mtime (S-23) |
| 2026-11-07 20:46:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 0 | docs/sdd/runs/test-runs/20261107-201838-test-fast.log - Aprovado 28 Com falha 0 apos restaurar M2 |
| 2026-11-07 20:46:32 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 1 | docs/sdd/runs/test-runs/20261107-201948-test-gate.log - exit:1 do log, Com falha 1 - Ac05 da QA extraiu o PHC do JSON cru e Verify devolveu falso (System.Text.Json escapa o + do base64) - defeito do teste da QA, nao do produto |
| 2026-11-07 20:46:33 | qa | dotnet fsi --exec %TEMP%/qa0013-phc.fsx (medicao da causa raiz) | 0 | extracao cru do JSON inutilizavel 124/200 vs decodificada 0/200 - causa raiz medida; login 200 na mesma execucao prova o produto |
| 2026-11-07 20:46:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 0 | docs/sdd/runs/test-runs/20261107-203658-test-fast.log - Aprovado 28 Com falha 0 apos a correcao do teste da QA |
| 2026-11-07 20:46:33 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-203804-test-gate.log - exit:0 do log, Aprovado 147 Com falha 0 = baseline 141 + 6 da QA (AC-06) |
| 2026-11-07 20:46:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 0 | SUMMARY total=16 fails=0 - 123 logs varridos, 4 marcadores de RK (12 formas), 0 hits de senha |
| 2026-11-07 20:46:34 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0014.sh | 0 | SUMMARY total=16 fails=0 - gemeo Linux em WSL2, 125 logs, 1 marcador de RK (3 formas), 0 hits de senha |
| 2026-11-07 20:46:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 0 | docs/sdd/runs/test-runs/20261107-204230-test-fast.log - Aprovado 28 Com falha 0, ultima execucao verde antes do fechamento |
| 2026-11-07 20:56:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -Fast -TestFilter FullyQualifiedName~Squid.Tests.Auth | 0 | docs/sdd/runs/test-runs/20261107-205425-test-fast.log - Aprovado 28 Com falha 0 - verde final com o doc QA-0013 ja presente na arvore |
| 2026-11-07 20:56:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0014.ps1 | 0 | SUMMARY total=16 fails=0 - execucao final com o doc QA-0013 presente: git-status 52 entradas vs baseline 48 com unexpected 0, 126 logs, 1 marcador de RK (3 formas), 0 hits |
