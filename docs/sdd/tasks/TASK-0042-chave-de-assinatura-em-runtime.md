<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0042 — Chave de assinatura em runtime (GAP-09) + assinar/verificar HTTP

```yaml
runId: R-20261113-01
taskId: TASK-0042
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-13 13:47:25
updatedAt: 2026-11-13 18:33:35
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Grains/Actors/**", "backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0043"]
blockedBy: ["TASK-0041"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: ["backend/src/Squid.Api/Crypto/**", "backend/src/Squid.Grains/Actors/**", "backend/src/Squid.Protocols/**", "backend/tests/Squid.Tests/**"]
interviewRounds: 2/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 1/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: NENHUM
  approvedDate: NENHUM
  scope: NENHUM
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0042 PASSED (6/6 ACs, 18 mutacoes, gate 503/503, commits 0814968 + 92276ab); merge em develop pelo ODEV e dispatch da TASK-0043
evidence: []
memoryBankFlags: []
```

## Intent

Entrega 2 de 9 da P-06: fecha o **GAP-09** (decisão 2 da SPEC-0011, humana) — `RK` como segredo
de ambiente no boot, desembrulho da RSA privada do ator só em memória no `ActorGrain` — e entrega
a biblioteca de assinatura HTTP: saída draft-cavage `rsa-sha256` (ADR-28, contrato §5) e verificação
de entrada `rsa-sha256`/`hs2019` + RFC 9421 (contrato §3, ordem fixa). Nenhuma rota de inbox ainda:
aqui é chave + biblioteca com round-trip provado.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: sem `Squid:RecoveryKey` a instância **sobe**, segue servindo leitura e escrita local, e
  registra aviso/métrica de que a saída federada está desabilitada — teste.
- AC-02: com `Squid:RecoveryKey` válida, o `ActorGrain` desembrulha a RSA privada do ator
  (`HKDF(RK, "fed/rsa-wrap")`) na ativação e a mantém só em memória: nenhuma rota, log ou
  persistência devolve material privado — teste.
- AC-03: `Squid:RecoveryKey` errada → falha auditável na ativação do grain, sem vazar material
  em mensagem, log ou resposta — teste.
- AC-04: requisição de saída assinada bate com o formato do contrato §5 — `keyId` terminando em
  `#main-key`, `algorithm="rsa-sha256"`, `headers="(request-target) host date digest"`,
  `Digest: SHA-256=<base64>`, `Date` em HTTP-date GMT, GET assinando `(request-target) host date`
  — e um verificador independente aceita o par assinar→verificar — teste.
- AC-05: a verificação de entrada aceita draft-cavage `rsa-sha256` e `hs2019` e RFC 9421 (RSA e
  Ed25519), e rejeita: `Date` fora de ±5 min, `Digest` divergente, assinatura de chave que não é a
  publicada no `keyId` — teste.
- AC-06: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Crypto/**` — carga de `Squid:RecoveryKey`, desembrulho de `RK`/RSA
- `backend/src/Squid.Grains/Actors/**` — desembrulho na ativação, chave só em memória
- `backend/src/Squid.Protocols/**` — assinar (saída) e verificar (entrada) draft-cavage + RFC 9421
- `backend/src/Squid.Service/**` — config/encanamento de `Squid:RecoveryKey`, aviso de saída desabilitada
- `backend/tests/Squid.Tests/**` — testes das ACs
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-09 | no | RESOLVED (HUMAN) — decisão 2 da SPEC-0011 (2026-11-13 11:48:04): `Squid:RecoveryKey` no boot, desembrulho em memória, sem rota de reidratação, kit = recuperação, produção = gate humano |
| — | no | sem `RK` a saída fica desabilitada e a instância continua funcional localmente (AC-01) — nunca é motivo de queda |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | qual a forma do valor de `Squid:RecoveryKey`? | SPEC-0011 §B (nível especificação, escrita pelo ODEV 2026-11-13 18:02:25) | **base64 dos 32 bytes** — mesma forma serializada do kit e dos estados; runbook da TASK-0048 documenta lendo o código; não bloqueante (nenhum artefato externo fixa o formato); código do DEV coerente (não muda) |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-13 13:47:25 | odev | task created from SPEC-0011 (entrega 2 de 9, fecha GAP-09 por decisao humana; blockedBy TASK-0041)
- 2026-11-13 16:52:11 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0041 resolve (DONE com QA-0041 PASSED 5/5, gate 484/484, merge ad2c36e em develop + docs 004b1be); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0042-chave-de-assinatura-em-runtime desde develop
- 2026-11-13 17:56:24 | dev | feasibilidade round 1/2: unit ActorGrain, callers (Auth/Recovery/Discovery) e CompositionRoot mapeados; touches confirmado sem acrescimo; baseline gate 484/484 exit 0 em runs/test-runs/20261113-170856-test-gate.log; formato do valor de Squid:RecoveryKey decidido como base64 dos 32 bytes (mesma forma serializada do repositorio) por nao ser duvida bloqueante; nenhuma duvida bloqueante
- 2026-11-13 17:56:24 | dev | implementacao: port IActorSigningKeyRing + ActorSigner + ActivateSigningKeyAsync no ActorGrain (chave privada so em memoria, SigningKeyActivationException auditavel); RuntimeSigningKeyRing le Squid:RecoveryKey no boot e o host avisa saida federada desabilitada; Squid.Protocols ganha HttpSignatureSigner (draft-cavage rsa-sha256 do contrato 5) e HttpSignatureVerifier (rsa-sha256/hs2019 + RFC 9421 RSA/Ed25519 na ordem do contrato 3); BouncyCastle ja pinado referenciado pelo Squid.Protocols
- 2026-11-13 18:00:35 | dev | gate AC-06: sdd-test.ps1 test exit 0 com 503/503 (baseline 484 + 19 casos novos) em runs/test-runs/20261113-180003-test-gate.log; unico vermelho do caminho foi a captura de console do proprio teste (StringWriter x log do host em thread de fundo), corrigida com writer travado e re-executada; nenhum arquivo de produto alterado fora dos touches
- 2026-11-13 18:03:13 | odev | ODEV verificou antes de transicionar: gate 20261113-180003-test-gate.log lido no arquivo (exit: 0, Aprovado 503, Com falha 0, 5m42s); commit 0814968 conferido via git show --stat (13 arquivos: 9 produto nos touches - Crypto, Grains/Actors, Protocols+csproj, Service - 3 testes, 1 task file so com Log/Evidence); branch task/TASK-0042-chave-de-assinatura-em-runtime sobre develop 004b1be; git status = so TASK-0041 (linha de merge que escrevi apos o commit de docs, meu); sdd-state check OK (log 5, evidence 4); PackageReference BouncyCastle = pin 2.7.0 ja aprovado pelo gate 2026-11-07 (sem dependencia nova nem mudanca em Directory.Packages.props); step result DONE aceito
- 2026-11-13 18:03:13 | odev | adjudicacao D-01 pelo ODEV no nivel especificacao: valor de Squid:RecoveryKey = base64 dos 32 bytes (mesma forma serializada do kit e dos estados) - clausula escrita na SPEC-0011 secao B; nao e gate humano (config interna, nenhum contrato publicado); runbook da TASK-0048 documenta lendo o codigo
- 2026-11-13 18:03:15 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-13 18:34:14 | qa | baseline antes de escrever: HEAD 0814968 na branch task/TASK-0042-chave-de-assinatura-em-runtime, 13 arquivos do commit todos nos touches, dotnet ausente no WSL (suite Linux unverified); filtro das 3 classes novas 19/19 exit 0 (20261113-180909-test-filter.log)
- 2026-11-13 18:34:15 | qa | QA-0042 mutacoes: -VerifyOnly 18/18 padroes OK e run cheio com 18 ciclos can-fail vermelhos na AC alvo (T16 falha=2), restore byte-exact 18/18, GREEN 19/19, SUMMARY cycles=18 failed=0 (docs/sdd/qa/QA-0042-mutations.log)
- 2026-11-13 18:34:15 | qa | gate AC-06 sdd-test.ps1 test exit 0 com 503/503 lido no log (20261113-182619-test-gate.log); escopo blob 5/5, mtime=5, residuo=0, fora-dos-touches=0, git status backend=0 (docs/sdd/qa/QA-0042-scope.log)
- 2026-11-13 18:34:16 | qa | checkers ps1 54/54 e gemelo sh 50/50 exit 0 (run1 do sh exit 1 por CRLF no proprio script, corrigido e re-executado; QA-0042-check-sh-run1.log preservado); verdict PASSED, doc docs/sdd/qa/QA-0042-chave-de-assinatura-em-runtime.md; commit 92276ab na branch da task
- 2026-11-13 18:34:16 | qa | QA attempt 1/2 fechada PASSED 6/6 ACs; logLines evidenceRows devolvidos sem relogio para o ODEV carimbar (QA nao gravou a task file - comportamento permitido do fluxo)
- 2026-11-13 18:34:44 | odev | ODEV verificou antes de transicionar: QA-0042-chave-de-assinatura-em-runtime.md com verdict PASSED lido no arquivo; gate 20261113-182619-test-gate.log exit: 0 / Aprovado 503 / Com falha 0 / 6m24s (lido no arquivo); mutations SUMMARY cycles=18 failed=0 residue=0 lido no log; commit QA 92276ab conferido via git show --stat (11 arquivos so docs/sdd/qa/, pai 0814968, sem push, main intacta); logLines (5) e evidenceRows (9) da QA carimbados pelo ODEV via sdd-state - reescrita da linha 2 do filtro sem o caractere de pipe, comando literal preservado no log citado
- 2026-11-13 18:34:45 | odev | QA-0042 PASSED attempt 1/2 (PRODUTO+TESTES): 6/6 ACs, 18 ciclos can-fail contra o PRODUTO todos vermelhos na AC alvo (RED-AC), restore byte-exact 18/18, GREEN 19/19; gate 503/503 re-executado pela QA; unverified registrados (suite Linux/WSL, created/expires RFC 9421 fora da janela Date, interop real, §3 passos 4-7 = TASK-0044/0046)
- 2026-11-13 18:34:45 | odev | transicao AWAITING_QA -> DONE; entrega 2 de 9 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0043 a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-13 17:57:20 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-170856-test-gate.log (baseline pre-mudanca 484/484) |
| 2026-11-13 17:57:21 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (3 classes novas: Crypto + Protocols + FederationRuntimeKeyTests; OR com pipes, comando exato no log) | 1 | docs/sdd/runs/test-runs/20261113-174934-test-filter.log (24/25; captura de console x log concorrente em AC-03, corrigida) |
| 2026-11-13 17:57:21 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (3 classes novas: Crypto + Protocols + FederationRuntimeKeyTests; OR com pipes, comando exato no log) | 0 | docs/sdd/runs/test-runs/20261113-175324-test-filter.log (25/25, exit 0) |
| 2026-11-13 18:00:35 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-180003-test-gate.log (gate AC-06, 503/503) |
| 2026-11-13 18:34:16 | qa | git rev-parse HEAD + branch + status --porcelain -uall + show --name-only 0814968 + wsl command -v dotnet | 0 | docs/sdd/qa/QA-0042-git-baseline.txt |
| 2026-11-13 18:34:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter (OR: FullyQualifiedName~FederationRuntimeKeyTests, ~HttpSignatureTests, ~RuntimeSigningKeyRingTests - comando literal no log) | 0 | docs/sdd/runs/test-runs/20261113-180909-test-filter.log (baseline QA 19/19) |
| 2026-11-13 18:34:39 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0042.ps1 -VerifyOnly | 0 | docs/sdd/qa/QA-0042-verify.log (VERIFY total=18 bad=0) |
| 2026-11-13 18:34:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0042.ps1 | 0 | docs/sdd/qa/QA-0042-mutations.log (18 vermelhos + GREEN 19/19 + SUMMARY cycles=18 failed=0) |
| 2026-11-13 18:34:40 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261113-182619-test-gate.log (gate AC-06, 503/503) |
| 2026-11-13 18:34:40 | qa | pwsh varredura de escopo (blob-identity vs rev-parse, mtime, residuo, git status vs baseline) | 0 | docs/sdd/qa/QA-0042-scope.log (blob 5/5, mtime-count=5, residue=0, fora-dos-touches=0, git-backend-entries=0) |
| 2026-11-13 18:34:41 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0042.ps1 | 0 | docs/sdd/qa/QA-0042-check.log (SUMMARY total=54 fails=0) |
| 2026-11-13 18:34:41 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0042.sh | 0 | docs/sdd/qa/QA-0042-check-sh.log (SUMMARY total=50 fails=0; run1 exit 1 em QA-0042-check-sh-run1.log, bug do script corrigido) |
| 2026-11-13 18:34:42 | qa | git add <11 artefatos QA-0042> && git commit TASK-0042 QA-0042: PASSED 6/6 ACs, 18 mutacoes, gate 503/503 | 0 | commit 92276ab em task/TASK-0042-chave-de-assinatura-em-runtime |
