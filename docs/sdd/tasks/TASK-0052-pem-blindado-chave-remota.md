<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0052 — PEM blindado: chave remota resolve de verdade (S-41)

```yaml
runId: R-20261113-01
taskId: TASK-0052
status: DONE
agent: odev
phase: P-06
sequence: 2
createdAt: 2026-11-14 10:15:19
updatedAt: 2026-11-14 11:51:09
parent: SPEC-0011
authority: SPEC
parallel: false
touches: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
blocks: ["TASK-0047"]
blockedBy: ["TASK-0051"]
inputArtifacts: ["docs/sdd/specs/SPEC-0011-federacao-p06.md", "docs/sdd/plan/plan.md", "specs/001-squid-mvp/contracts/activitypub.md", "docs/sdd/ideas/BACKLOG.md"]
outputArtifacts: ["backend/src/Squid.Protocols/**", "backend/src/Squid.Service/**", "backend/tests/Squid.Tests/**"]
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
nextAction: DONE - QA-0052 PASSED (4/4 ACs, mutacao S-41 reproduzida, gate 578/578, commits 47c905e + 8f04a89); merge em develop pelo ODEV e dispatch da TASK-0047
evidence: []
memoryBankFlags: []
```

## Intent

Correção focada de um defeito **provado empiricamente** na TASK-0051 e registrado como S-41:
`ProfileJsonLd.TryDecodePublicKeyPem` não decodifica PEM **blindado** multi-linha (o `Compact`
remove os espaços do armor antes do `Replace` do armor, então o base64 nunca valida) e
`HttpFederatedActorClient.KeyOf` depende dele — na inbox real, a chave pública de Mastodon/
Friendica não resolve e toda atividade assinada por elas cairia. Sem esta correção, a TASK-0047
(dois silos por HTTP real) e a TASK-0049 (interop) tropeçam no caminho de verificação de entrada.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: a decodificação aceita o PEM **blindado multi-linha** do contrato §1.1 (armor
  `-----BEGIN PUBLIC KEY-----`/`-----END PUBLIC KEY-----` com quebras de linha, como Mastodon e
  Friendica publicam) e devolve uma chave RSA utilizável — round-trip assinar→verificar com a
  chave decodificada — teste.
- AC-02: `HttpFederatedActorClient.KeyOf` resolve a chave RSA de um **documento de ator remoto
  realista** (fixture byte-realista do formato §1.1, com `publicKey.id`/`owner`/`publicKeyPem`
  blindado) e a verificação de entrada aceita a atividade assinada com ela (`202`) — teste.
- AC-03: regressões preservadas: PEM de **linha única** continua aceito e `squid:ed25519Key`
  (`publicKeyMultibase`) continua resolvendo — teste.
- AC-04: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1
  test` exit 0; nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Protocols/**` — leitor de PEM (ordem do armor × compactação) e parsing do
  documento de ator
- `backend/src/Squid.Service/**` — `HttpFederatedActorClient.KeyOf` se precisar de ajuste de
  fiação (mínimo)
- `backend/tests/Squid.Tests/**` — testes das ACs (fixtures realistas do contrato §1.1)
- produto **fora** dos `touches` desta task: qualquer lacuna levantada como dúvida, não corrigida aqui

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| S-41 | no | defeito de produto pre-existente (nasceu na TASK-0044, provado na 0051) — esta task é a correção; a RCA é registrada no backlog, nenhuma AC anterior se abre |
| — | no | o import de kit sem cópia de instância (parked da 0051) é **S-42**, fora daqui |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`
- 2026-11-14 10:15:19 | odev | task created from SPEC-0011 (correcao S-41, entrega 9 de 12 da P-06; blockedBy TASK-0051, blocks TASK-0047; defeito provado pelo DEV da 0051 fora dos touches dele)
- 2026-11-14 11:05:48 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; runId R-20261113-01; blockedBy TASK-0051 resolve (DONE com QA-0051 PASSED 6/6, gate 573/573, merge 1d46f61 em develop + docs 4b2f209); defeito provado pelo DEV da 0051 e confirmado pela NOTA-3 da QA-0051 (ProfileJsonLd.cs:78 / HttpFederatedActorClient.cs:172); autorizacao de commit gate PHASE 2026-11-11 20:42:28 (modelo C) incluida - nova branch task/TASK-0052-pem-blindado-chave-remota desde develop; logLines e evidenceRows sem relogio para o ODEV carimbar (sem pipe em comandos)
- 2026-11-14 11:35:37 | dev | feasibility 1/2: unidade ProfileJsonLd.TryDecodePublicKeyPem e chamador unico HttpFederatedActorClient.KeyOf conferidos contra SPEC-0011 (secoes C/E), FR-039/FR-040 e contrato activitypub 1.1/3; touches confirmado integralmente (Squid.Service nao precisou de ajuste de fiacao); baseline gate 573/573 exit 0; sem duvida bloqueante - a linha unica da AC-03 coberta nas duas grafias (base64 puro e armor em linha unica)
- 2026-11-14 11:35:38 | dev | testes das AC-01..AC-03 escritos ANTES do fix e executados com o filtro FullyQualifiedName~Armored: exit 1 com 4 vermelhos (TryDecodePublicKeyPem nulo no PEM blindado, ResolveKeyAsync nulo no documento realista, inbox 401 no lugar do 202) e 1 verde (ed25519) - defeito S-41 provado empiricamente; log docs/sdd/runs/test-runs/20261114-112355-test-filter.log
- 2026-11-14 11:35:38 | dev | fix em ProfileJsonLd.cs: armor removido ANTES do Compact pelo novo PemBody privado, compartilhado com FromPem (a ordem inversa era a causa do base64 nunca validar); nenhum outro arquivo de produto alterado
- 2026-11-14 11:35:39 | dev | apos o fix: mesmo filtro 5/5 exit 0 (log docs/sdd/runs/test-runs/20261114-112536-test-filter.log) e gate completo 578/578 exit 0 (log docs/sdd/runs/test-runs/20261114-113405-test-gate.log)
- 2026-11-14 11:35:40 | dev | commit unico autorizado 47c905e no branch task/TASK-0052-pem-blindado-chave-remota criado desde develop 4b2f209, com so os 3 arquivos de touches; a edicao do ODEV no arquivo da task ficou fora do commit e intacta no working tree; push, rebase e main nao tocados
- 2026-11-14 11:35:43 | odev | ODEV verificou antes de transicionar: gate 20261114-113405-test-gate.log lido no arquivo (exit: 0, Aprovado 578, Com falha 0, 7m34s) e a prova vermelha pre-fix 20261114-112355-test-filter.log (exit: 1, 4 falhas reais do defeito S-41) lida no arquivo; commit 47c905e conferido via git show --stat (3 arquivos: 1 produto em Squid.Protocols + 2 testes, todos nos touches); git status -- backend = 0; logLines (5) e evidenceRows (6) carimbados via sdd-state; step result DONE aceito
- 2026-11-14 11:35:45 | odev | transicao IN_DEVELOPMENT -> AWAITING_QA (attempt 1/2) - dispatch QA a seguir
- 2026-11-14 11:51:47 | qa | baseline do filtro FullyQualifiedName~Armored antes de qualquer mutacao: 5/5 aprovados exit 0, AC-01 AC-02 e AC-03 verdes (log docs/sdd/runs/test-runs/20261114-114012-test-filter.log)
- 2026-11-14 11:51:47 | qa | mutacao can-fail T1: PemBody de ProfileJsonLd.cs:121 revertido a Compact antes do Replace do armor, a regressao S-41 exata - mesmo filtro exit 1 com Com falha 4 e Aprovado 1, o ed25519 unico verde (log docs/sdd/runs/test-runs/20261114-114048-test-filter.log)
- 2026-11-14 11:51:48 | qa | restore byte-exact da T1 com git restore e bump de mtime S-23: git hash-object 2f267df1ca86df0a5c00a05612501609c4ed42f3 identico ao blob de 47c905e, git status backend vazio
- 2026-11-14 11:51:48 | qa | escopo AC-04 em Windows e em shell Linux WSL: commit 47c905e com 3 arquivos nos touches, HEAD na branch da task, working tree so com o carimbo do ODEV na task file nao committado; WSL sem dotnet, suite Linux unverified
- 2026-11-14 11:51:49 | qa | gate AC-04 re-executado pela QA: exit 0 com Com falha 0 Aprovado 578 Total 578 em 7m51s lidos dentro do log docs/sdd/runs/test-runs/20261114-114911-test-gate.log
- 2026-11-14 11:51:50 | qa | veredito PASSED em 4 de 4 ACs; QA doc docs/sdd/qa/QA-0052-pem-blindado-chave-remota.md; commit unico autorizado 8f04a89 so com o artefato da QA, o carimbo do ODEV ficou fora, nenhum push e main intocada
- 2026-11-14 11:51:57 | odev | ODEV verificou antes de transicionar: QA-0052-pem-blindado-chave-remota.md com verdict PASSED lido no arquivo; gate 20261114-114911-test-gate.log exit: 0 / Aprovado 578 / Com falha 0 / 7m51s (lido no arquivo); mutacao T1 (reintroducao do S-41) exit 1 com 4 falhas lida no log; commit 8f04a89 conferido (1 arquivo docs/sdd/qa); git status -- backend = 0; logLines (6) e evidenceRows (8) carimbados via sdd-state - veredito aceito
- 2026-11-14 11:51:58 | odev | QA-0052 PASSED attempt 1/2 (PRODUTO+TESTES): 4/4 ACs, mutacao unica que reproduz a regressao exata e volta verde apos restore byte-exact, gate 578/578; 0 BLOCKER/MAJOR; S-41 FECHADO - caminho de chave remota (KeyOf) sao agora utilizaveis para a inbox real
- 2026-11-14 11:51:59 | odev | transicao AWAITING_QA -> DONE; entrega 9 de 12 da P-06 encerrada - merge em develop + docs commitados pelo ODEV, dispatch da TASK-0047 (facet de dois silos) a seguir

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-14 11:35:40 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-112028-test-gate.log (baseline antes das mudancas, 573/573) |
| 2026-11-14 11:35:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored | 1 | docs/sdd/runs/test-runs/20261114-112355-test-filter.log (vermelho antes do fix: Com falha 4, Aprovado 1 - defeito S-41 provado) |
| 2026-11-14 11:35:41 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored | 0 | docs/sdd/runs/test-runs/20261114-112536-test-filter.log (5/5 apos o fix) |
| 2026-11-14 11:35:42 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-113405-test-gate.log (gate final 578/578, exit 0) |
| 2026-11-14 11:35:42 | dev | git switch -c task/TASK-0052-pem-blindado-chave-remota | 0 | branch criada desde develop 4b2f209 |
| 2026-11-14 11:35:43 | dev | git commit -m TASK-0052 leitor de PEM blindado no ProfileJsonLd (S-41) | 0 | commit 47c905e (ProfileJsonLd.cs + ArmoredPemDecodeTests.cs + ArmoredRemoteKeyTests.cs, 3 files, +321/-19) |
| 2026-11-14 11:51:50 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored | 0 | docs/sdd/runs/test-runs/20261114-114012-test-filter.log baseline 5/5 |
| 2026-11-14 11:51:51 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored | 1 | docs/sdd/runs/test-runs/20261114-114048-test-filter.log mutacao T1 S-41 Com falha 4 Aprovado 1 |
| 2026-11-14 11:51:51 | qa | git restore -- backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs | 0 | blob 2f267df1ca86df0a5c00a05612501609c4ed42f3 identico ao de 47c905e, git status backend vazio, mtime bump S-23 |
| 2026-11-14 11:51:52 | qa | git status --porcelain | 0 | so o carimbo do ODEV na task file e o artefato desta QA, residuo em backend zero |
| 2026-11-14 11:51:53 | qa | git diff --name-only 47c905e~1 47c905e | 0 | exatamente 3 arquivos, todos nos touches, 1 produto em Squid.Protocols e 2 testes |
| 2026-11-14 11:51:53 | qa | wsl -e bash com git status e git diff 47c905e~1 47c905e no repo | 0 | mesma varredura em shell Linux, dotnet-ABSENT no WSL |
| 2026-11-14 11:51:54 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261114-114911-test-gate.log exit 0 no cabecalho, Com falha 0, Aprovado 578, Total 578, 7m51s |
| 2026-11-14 11:51:54 | qa | git commit -m TASK-0052 QA-0052 PASSED AC-01..AC-04 provadas | 0 | commit 8f04a89 so com docs/sdd/qa/QA-0052-pem-blindado-chave-remota.md, carimbo do ODEV fora |
