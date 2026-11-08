<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0016 — Kit de recuperação: gerar, verificar e importar

```yaml
runId: R-20261107-02
taskId: TASK-0016
status: DONE
agent: odev
phase: P-02
sequence: 2
createdAt: 2026-11-07 16:19:35
updatedAt: 2026-11-07 16:19:35
parent: SPEC-0007
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Auth/AuthEndpoints.cs", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/Recovery/**"]
blocks: []
blockedBy: ["TASK-0014"]
inputArtifacts: ["docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md", "specs/001-squid-mvp/data-model.md", "specs/001-squid-mvp/contracts/http-api.md"]
outputArtifacts: ["backend/src/Squid.Api/Recovery/**", "backend/src/Squid.Api/Auth/AuthEndpoints.cs"]
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
nextAction: DONE - QA-0015 PASSED (5/5 ACs + facet DoD, qaAttempt 1/2); ultima task da P-02 encerrada; fecho aguarda gate humano
evidence: []
memoryBankFlags: [DIVERGENCIA para fecho: data-model.md:272 lista RecoveryCredentialHash e KitRecord.RecoveryCode persiste a credencial em claro (AuthAccount.cs:13) - mnemonic derivavel da RC logo persistivel em claro derivadamente; pre-existente da TASK-0014, fora dos toques da 0016; comportamento de re-export identico exige RC derivavel - decisao humana no fecho: (a) aceitar e emendar o data-model ou (b) guardar so o hash e mudar o re-export] [MUDANCA_RELEVANTE: CONTRATO - kit de POST /api/recovery/kit = campos do contrato §3 + wrappedSigningKey (data-model 2.12, exigido pelo AC-03); import em campos planos + handle + password; senha errada = 401 recovery_failed unico com estado intacto - source: backend/src/Squid.Api/Recovery/RecoveryEndpoints.cs] [GAP: suite .NET no Linux nativa segue unverified - WSL sem SDK, sdd-test.sh so procura *.sln (S-11); twin .sh do checker executou 7/7 no WSL2; CI ubuntu e o executor Linux]
```

## Intent

Fecha FR-005/FR-006/FR-007 (SPEC-0007 B-12..B-15) e o terceiro ponto do DoD do Sprint 1:
gerar o kit exportável (mnemônico de 24 palavras uma vez), marcá-lo verificado e importá-lo em
instância limpa restabelecendo a **mesma** identidade — `webfinger` resolve e o kit restaura a `RK`.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `POST /api/recovery/kit` (autenticado) devolve 201 com `recoveryCredential`, `mnemonic`
  de **24 palavras** e `encryptedRootKey`; o mnemônico não é recuperável por nenhuma outra rota e
  `RK` em claro nunca aparece na resposta — teste (asserção de corpo + rota de leitura inexistente).
- AC-02: `POST /api/recovery/kit/verify` marca o kit verificado e `GET /api/auth/me` **deixa** de
  avisar; sem verificar (ou com kit > 12 meses) o aviso persiste — teste dos dois estados (FR-007).
- AC-03: `POST /api/recovery/import` em instância limpa (banco novo, sem o ator) com kit + senha
  correta restabelece a identidade: mesmo `handle`, mesma pública Ed25519 **e** mesma RSA — o ator
  re-importado resolve no webfinger e assina/verifica com as chaves antigas — teste de round-trip
  (FR-006; é o "kit baixado restaura a `RK`" do DoD).
- AC-04: import com senha errada → erro tipado e **nada** destruído: estado pré-existente byte a
  byte inalterado antes/depois (hash) — teste (invariante data-model §2.12).
- AC-05: gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Recovery/**` (novo diretório)
- `backend/src/Squid.Api/Auth/AuthEndpoints.cs` (só para `GET /api/auth/me` — ampliação adjudicada; a 0014 já está DONE e esta task é serial)
- `backend/src/Squid.Service/**` (registro das rotas)
- `backend/tests/Squid.Tests/Recovery/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-09 | no | importar é o único caminho que reidrata `RK`/RSA nesta fase; o teste prova isso em instância limpa |
| — | no | "instância limpa" no teste = banco novo na mesma suíte (não é o restore drill de backup, que é P-07) |
| — | no | **toque ampliado pela adjudicação da D de touches da TASK-0014 (2026-11-07)**: `GET /api/auth/me` (aviso de kit, B-13/FR-007, AC-02 aqui) é criada em `backend/src/Squid.Api/Auth/AuthEndpoints.cs` — arquivo era toque da 0014, que já está DONE; a 0016 roda serial depois dela, então não há conflito de escrita. Reutilizar `Mnemonic.FromRecoveryCode` e `RecoveryCredential.Format` já existentes — não reimplementar |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 16:19:35 | odev | task created from SPEC-0007 (blockedBy TASK-0014; entrega que fecha o DoD do Sprint 1 junto com TASK-0015)
- 2026-11-07 19:36:48 | odev | odev | toques ampliados antes do dispatch: backend/src/Squid.Api/Auth/AuthEndpoints.cs entra para criar GET /api/auth/me (aviso de kit B-13/FR-007, exigido pelo AC-02 desta task); adjudicacao da conflito de toques levantado pelo DEV da TASK-0014 - a 0014 ficou com os ACs dela fechados, a rota e da entrega do kit, e esta task e serial apos a 0014 (sem paralelismo); reutilizar Mnemonic.FromRecoveryCode e RecoveryCredential.Format
- 2026-11-07 22:50:21 | odev | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; blockedBy TASK-0014 e TASK-0015 ambas DONE (QA-0013 e QA-0014 PASSED); baseline do gate 156/156; entrega final da P-02 - com esta, o DoD do Sprint 1 fica completo (webfinger ja provado, JSON-LD servido, falta o kit restaurando RK)
- 2026-11-07 23:48:05 | dev | baseline do gate antes de qualquer edicao: sdd-test.ps1 test -> exit 0, 156/156 (docs/sdd/runs/test-runs/20261107-230735-test-gate.log)
- 2026-11-07 23:48:05 | dev | viabilidade round 1/2 sem duvida bloqueante: toques confirmados e completos; D-01 (onde marcar VerifiedAt) fechada pelos proprios toques + AC-05 - KitRecord nao tem campo e Auth/** e fechado, entao a marcacao vira linha propria em Squid.Api/Recovery + SqliteRecoveryStore (Squid.Service/**)
- 2026-11-07 23:48:05 | dev | POST /api/recovery/kit (201, sessao + CSRF): recoveryCredential, mnemonic de 24 palavras re-derivado da RC do registro, encryptedRootKey e createdAt, mais wrappedSigningKey (data-model 2.12 - sem ele o AC-03 nao restaura a mesma RSA); nenhuma rota GET expoe mnemonic, RC ou RK
- 2026-11-07 23:48:05 | dev | POST /api/recovery/kit/verify grava VerifiedAt; GET /api/auth/me (unica edicao em AuthEndpoints.cs, adjudicada) devolve handle + displayName + warnings com kit_unverified e kit_stale dos 12 meses (FR-007)
- 2026-11-07 23:48:05 | dev | POST /api/recovery/import: Argon2id abre a RK antes de qualquer escrita - senha errada -> 401 recovery_failed com estado intacto; cria o ator com as mesmas chaves ou, se ja existe igual, responde a mesma identidade (idempotente, B-14) e marca o kit verificado (a posse do kit e a prova que FR-007 pede)
- 2026-11-07 23:48:05 | dev | testes novos em backend/tests/Squid.Tests/Recovery/**: 7 testes - 3 puros do aviso, AC-01 (corpo + rotas de leitura 404/405 + ausencia da RK em cru e decodificado), AC-02 (dois estados + kit de 13 meses envelhecido na linha), round-trip do AC-03 (banco novo, webfinger, assinaturas RSA e Ed25519 verificando contra as chaves publicadas, reexportacao identica do kit, import repetido idempotente) e AC-04 (SHA-256 de todas as linhas antes/depois)
- 2026-11-07 23:48:05 | dev | gate completo: sdd-test.ps1 test -> exit 0, 163/163 (156 + 7) em docs/sdd/runs/test-runs/20261107-234315-test-gate.log (exit: 0 no log, S-22); git status mostra nenhuma alteracao minha fora dos toques
- 2026-11-07 23:48:08 | odev | odev | ODEV verificou antes de transicionar: gate 20261107-234315-test-gate.log exit 0 com Aprovado 163 / Com falha 0 lido no log (S-22); rota GET /api/auth/me confirmada em AuthEndpoints.cs (unica edicao adjudicada nesse arquivo fechado); git delta dentro dos toques; check exit 0; step result DONE aceito (D-01 do VerifiedAt resolvida pelos proprios toques, sem reabrir Auth/**)
- 2026-11-07 23:48:08 | odev | odev | nota do DEV registrada para a QA: kit da rota = 4 campos do contrato §3 + wrappedSigningKey (exigido por data-model 2.12 e AC-03); import em campos planos + handle + password; senha errada = 401 recovery_failed unico; AC-04 deve tentar SEM sessao (request autenticado desliza a sessao por B-05 e mudaria a linha antes/depois)
- 2026-11-07 23:48:08 | odev | odev | status IN_DEVELOPMENT -> AWAITING_QA
- 2026-11-08 00:26:34 | odev | odev | QA-0015 subagent FALHOU antes de fechar (harness: chamou ferramenta inexistente psh code, sem step result, sem QA-0015 escrito); artefatos parciais dela na arvore: Qa0015RecoveryAcceptanceTests.cs, qa-check-task-0016.ps1/.sh
- 2026-11-08 00:26:34 | odev | odev | ODEV verificou a saude da arvore apos a falha: gate proprio 20261108-002605-test-gate.log exit 0 com Aprovado 167 / Com falha 0 (163 + 4 testes da QA); 0 residos de QA-MUTATION nos arquivos de produto; nenhum arquivo de produto alterado fora dos toques - a falha nao deixou dano
- 2026-11-08 00:26:34 | odev | odev | politica de falha: 1 redispatch da mesma task (subagente nova, qaAttempt segue 1/2); a nova QA recebe os artefatos parciais como ponto de partida, nao como veredito
- 2026-11-08 01:14:00 | qa | QA-0015 redispatch (qaAttempt 1/2) da TASK-0016: artefatos da QA anterior lidos e verificados contra os ACs; baseline gate 20261108-004409 exit 0 com Aprovado 167 Com falha 0 e filtro Recovery 11/11 (20261108-004513), classificados pelos exit:/summary dos logs (S-22); checkers ps1 e sh 7/7 nas duas plataformas
- 2026-11-08 01:14:01 | qa | 4 mutacoes can-fail (S-16), uma por AC, so em Squid.Api/Recovery/RecoveryEndpoints.cs: M1 cria GET /api/recovery/kit e Ac01 fica vermelha na linha 106 (MethodNotAllowed vs Created, checker ps1 tambem vermelho fails=3); M2 tira o MarkVerifiedAsync do verify e Ac02 fica vermelha na linha 177 (kit_unverified+kit_stale em vez de so kit_stale); M3 tira wrappedSigningKey do kit e a round-trip do Ac03 fica vermelha com KeyNotFound (mais Ac01 com 4 campos em vez de 5 e Ac04 - 6 falhas); M4 faz a senha errada gravar a linha antes do 401 e Ac04 fica vermelha na linha 296, exatamente no hash antes/depois (checker sh tambem vermelho fails=3)
- 2026-11-08 01:14:01 | qa | cada restauracao foi byte-exact SHA256 PRE=POST=1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E com bump de mtime (S-23) e suite verde 11/11 em seguida; residuo final grep QA-MUTATION sobre backend = 0 e nenhum backup qa0015 restante no temp
- 2026-11-08 01:14:02 | qa | S-22 reproduzido ao vivo no run da M3: console do wrapper imprimiu exit 3 (assinatura de sandbox pelo parametro String handle nos frames da pilha) enquanto o log 20261108-005410 diz exit 1 com Com falha 6 - toda classificacao veio dos logs
- 2026-11-08 01:14:02 | qa | gate final 20261108-010157 exit 0 com Aprovado 167 Com falha 0 na arvore restaurada; checkers finais 7/7 com new-allowed=4/4 nas duas plataformas; QA-0015-kit-recuperacao.md gravado com os 5 ACs PASS mais a facet DoD da P-02; veredito PASSED sem BLOCKER e sem falhas pre-existentes
- 2026-11-08 01:14:03 | qa | achado 3 para adjudicacao do ODEV (nao e AC desta task e o arquivo esta fora dos toques): data-model 2.12 lista RecoveryCredentialHash mas KitRecord guarda a credencial em claro (AuthAccount.cs:13) - flag DIVERGENCIA proposta; o comportamento aceito de re-export identico (RecoveryImportTests:84) exige RC derivavel
- 2026-11-08 01:19:57 | odev | odev | ODEV verificou antes de transicionar: gate final 20261108-010157-test-gate.log exit 0 com Aprovado 167 / Com falha 0 lido no log (S-22); SHA256 atual de RecoveryEndpoints.cs = 1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E = PRE/POST das 4 mutacoes; 0 residuos QA-MUTATION; QA-0015 existe; check exit 0 (22 logs, 27 evidencias); a QA re-executou os artefatos herdados da subagente morta e nenhum veredito depende dos logs dela
- 2026-11-08 01:19:57 | odev | odev | achado 3 CONFIRMADO na fonte: AuthAccount.cs:13 RecoveryCode em claro vs data-model.md:272 RecoveryCredentialHash; pre-existente da TASK-0014 (fora dos toques da 0016), ACs verdes porque nenhuma rota vaza a RC e a RK segue protegida pelo Argon2id; a contradicao toca o MODELO DE DADOS adotado - nao e minha de resolver: vira pergunta de fecho com default, e o comportamento de re-export identico (RecoveryImportTests:84) e o lado que decide se a linha do data-model muda ou o storage muda
- 2026-11-08 01:19:57 | odev | odev | verdicto QA-0015 adjudicado: PASSED com 5/5 ACs + facet DoD da P-02 (import em instancia limpa -> webfinger do mesmo ator com as mesmas chaves) provadas por execucao propria (11/11 no filtro, 4 mutacoes can-fail - uma por AC - vermelhas e restauradas byte-exact com bump mtime, checkers 7/7 verdes E vermelhos nas duas plataformas, gate 167/167); unverified aceito (suite .NET Linux nativa - CI ubuntu e o executor); 0 BLOCKER; transicao AWAITING_QA -> DONE
- 2026-11-08 01:19:57 | odev | odev | TAREFAS DA P-02: 0012 DONE, 0013 DONE, 0014 DONE, 0015 DONE, 0016 DONE - 5/5 com QA PASSED; proximo passo = fecho de fase (consolidacao + snapshot + authority CODE), GATE HUMANO

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 23:48:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-230735-test-gate.log - baseline 156/156 |
| 2026-11-07 23:48:06 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build | 0 | 0 Aviso(s), 0 Erro(s) |
| 2026-11-07 23:48:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261107-234145-test-filter.log - 7/7 novos |
| 2026-11-07 23:48:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-234315-test-gate.log - exit: 0 no log, 163/163 |
| 2026-11-07 23:48:08 | dev | git status --porcelain | 0 | deltas meus: Squid.Api/Recovery/**, Squid.Service/SqliteRecoveryStore.cs, SquidServiceHost.cs, Auth/AuthEndpoints.cs, tests/Squid.Tests/Recovery/** - fora dos toques: NENHUM |
| 2026-11-08 01:14:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-004409-test-gate.log - baseline exit 0 Aprovado 167 Com falha 0 |
| 2026-11-08 01:14:34 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261108-004513-test-filter.log - baseline exit 0 Aprovado 11 Com falha 0 |
| 2026-11-08 01:14:35 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0016.ps1 | 0 | SUMMARY total=7 fails=0 RESULT PASS (baseline) |
| 2026-11-08 01:14:35 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0016.sh | 0 | SUMMARY total=7 fails=0 RESULT PASS (baseline, WSL2) |
| 2026-11-08 01:14:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 1 | docs/sdd/runs/test-runs/20261108-004724-test-filter.log - M1 exit 1 Com falha 2 Ac01:106 Expected MethodNotAllowed Actual Created |
| 2026-11-08 01:14:36 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0016.ps1 (under M1) | 1 | SUMMARY total=7 fails=3 - FAIL FILTER-NEWEST-EXIT, FILTER-NEWEST-SUMMARY, RESTORE-SHA256 61816A88 |
| 2026-11-08 01:14:37 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M1, S-16 e S-23) | 0 | POST=PRE=1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E BYTE_EXACT True mtime 2026-11-08 03:47:58 UTC |
| 2026-11-08 01:14:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261108-004901-test-filter.log - pos-M1 exit 0 Aprovado 11 Com falha 0 |
| 2026-11-08 01:14:37 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 1 | docs/sdd/runs/test-runs/20261108-005108-test-filter.log - M2 exit 1 Com falha 2 Ac02:177 Expected kit_stale Actual kit_unverified mais kit_stale |
| 2026-11-08 01:14:38 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M2, S-16 e S-23) | 0 | POST=PRE=1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E BYTE_EXACT True mtime 2026-11-08 03:51:37 UTC |
| 2026-11-08 01:14:38 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261108-005241-test-filter.log - pos-M2 exit 0 Aprovado 11 Com falha 0 |
| 2026-11-08 01:14:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 1 | docs/sdd/runs/test-runs/20261108-005410-test-filter.log - M3 exit 1 Com falha 6 Ac01 5 campos vs 4 e Ac03 KeyNotFound na round-trip; console do wrapper exit 3 vs log exit 1 (S-22) |
| 2026-11-08 01:14:39 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M3, S-16 e S-23) | 0 | POST=PRE=1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E BYTE_EXACT True mtime 2026-11-08 03:54:49 UTC |
| 2026-11-08 01:14:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261108-005552-test-filter.log - pos-M3 exit 0 Aprovado 11 Com falha 0 |
| 2026-11-08 01:14:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 1 | docs/sdd/runs/test-runs/20261108-005738-test-filter.log - M4 exit 1 Com falha 2 Ac04:296 hash antes e depois divergente (1eeda497 vs 39e6678e) |
| 2026-11-08 01:14:41 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0016.sh (under M4) | 1 | SUMMARY total=7 fails=3 - FAIL FILTER-NEWEST-EXIT, FILTER-NEWEST-SUMMARY, RESTORE-SHA256 6D6AF0C0 |
| 2026-11-08 01:14:41 | qa | Copy-Item backup + Get-FileHash + mtime bump (restore M4, S-16 e S-23) | 0 | POST=PRE=1E14DA6AA26F6F9F647DB889822B5DA1789F153561348C1C0D6B23179EB21E9E BYTE_EXACT True mtime 2026-11-08 03:58:15 UTC |
| 2026-11-08 01:14:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Recovery | 0 | docs/sdd/runs/test-runs/20261108-010007-test-filter.log - pos-M4 exit 0 Aprovado 11 Com falha 0 |
| 2026-11-08 01:14:42 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261108-010157-test-gate.log - final exit 0 Aprovado 167 Com falha 0 |
| 2026-11-08 01:14:43 | qa | grep QA-MUTATION backend + git status --porcelain -uall + remove backups qa0015 do temp | 0 | 0 marcadores de mutacao; 72 entradas = baseline 69 + 3 artefatos QA na hora; 0 backups restantes |
| 2026-11-08 01:14:43 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0016.ps1 | 0 | SUMMARY total=7 fails=0 RESULT PASS (final) - gate 20261108-010157 167/167, filter 20261108-010007 11/11, new-allowed=4/4 |
| 2026-11-08 01:14:44 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0016.sh | 0 | SUMMARY total=7 fails=0 RESULT PASS (final, WSL2, new-allowed=4/4) |
