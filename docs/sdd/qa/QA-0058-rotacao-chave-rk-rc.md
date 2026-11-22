# QA-0058 — Rotação de chave RK/RC da instância + kit reemitido (TASK-0058)

- runId: R-20261120-01 · qaAttempt: 1/2 · verdict: **PASSED** (8/8 ACs)
- branch testada: `task/TASK-0058-rotacao-chave-rk-rc` @ `e3ec208` (1 commit sobre `develop`)
- autoridade: SPEC-0012 §E · `specs/001-squid-mvp/research.md` R-20 · `docs/features/05-arquitetura-consolidada.md` §5.1
- gate: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`

## Mapa AC → cenário

| AC | Cenário executado | Harness | Resultado |
| --- | --- | --- | --- |
| AC-01 | rota `POST /api/admin/recovery/rotate`: 401 sem sessão, 403 fora de `Instance:Operators` (`policy_denied`), `keyEpoch=2`, ledger com `RetiredAt` da época 1 + fingerprint `sha256:` distinto, backup pós-rotação carimba época 2 | `KeyRotationRouteTests.Ac01_…` | PASS (M1/M2 vermelhos) |
| AC-02 | cópia `0009` reembrulhada abre só com a chave nova (antiga recusa); DK da época 2 abre só com a chave nova; **restore da época 1 depois da rotação** com o keyring do kit reemitido — 2 envelopes, 7 eventos, `Succeeded` | `KeyRotationChainTests.Ac02_ac03_ac05_…` | PASS (M3 vermelho) |
| AC-03 | SHA-256 de **todo** o volume de backup antes e depois da rotação — idêntico (nenhum byte movido, R-20); mutação M8 esvazia o baseline e o cenário fica vermelho (prova de que a comparação é real e não-vacua) | idem + M8 | PASS (M8 vermelho) |
| AC-04 | RC anterior → **401** na 2ª rotação; RC nova → **200**; kit reemitido restaura a `RK` (`RootKeyCipher.Decrypt`) que abre a cópia `0009` reembrulhada numa RSA que **assina e verifica** um desafio; chave do kit == `IBackupKeySource.Resolve()`; conta B-13 (sem corpo) intacta | `KeyRotationRouteTests.Ac04_…` | PASS (M4/M6 vermelhos) |
| AC-05 | cadeia derivada nova (Ed25519 difere, `rsa-wrap` novo abre a cópia e o antigo recusa, `backup-kek` novo abre o DK), `OutputEnabled` sem reinício; **+ cenário novo da QA**: o `RuntimeSigningKeyRing` trocado em place abre a cópia reembrulhada e assina, e o wrap pós-rotação concorda com a `IBackupKeySource` trocada | `KeyRotationChainTests` + **`KeyRotationRingSwapTests.Ac05_…` (novo, QA-0058)** | PASS (M5/M9 vermelhos) |
| AC-06 | kit servido uma única vez: 2ª rotação devolve material diferente e a RC antiga morre; rotas `/api/recovery/kit`, `/api/auth/me`, `/api/admin/backup/status` varridas para todas as formas do material (credencial, `encryptedRootKey`, chaves do keyring, mnemônico em 3 formas JSON) — ausência; log do host capturado desde o 1º write contém "instance key rotated" e **nenhuma** semente (raw + `\uXXXX` decodificado) | `KeyRotationRouteTests.Ac06_…` + M7/M7b | PASS (M7 vermelho; M7b probe documenta a defesa em profundidade) |
| AC-07 | revisão do runbook (0 valores de chave: 0 base64 ≥40, 0 hex64, 0 `RC-XXXX-…`, 0 `rk_…`, 0 mnemônico, 0 placeholder com formato real; 16 EXECUTÁVEL, 0 PENDENTE) + índice `docs/runbooks/README.md` = EXECUTÁVEL apontando o log real + spot-check do log `20261122-071147-runbook-rotacionar-chave.log` | scripts estáticos + leitura do log | PASS (finding F2) |
| AC-08 | gate abertura 640/640 e fechamento **641/641** exit 0; escopo `git diff develop...HEAD` = 19 arquivos, **19/19 nos `touches`** (D-01 `RestorePipeline.cs` = 9+/6−, só seleção por keyring + fallback legado); zero `.csproj`/`.props`, zero linha `PackageReference`+; sem resíduo de mutação (blobs 6/6 idênticos) | gate + `qa-scope-task-0058.ps1` | PASS (M-independent; scope `fails=0`) |

## Cenário novo da QA (fecha o gap do M9)

A mutação M9 (`ring.RotateTo(newKey)` removido) deixou o **gate completo de 640 testes verdes** —
nenhum cenário do DEV podia falhar na troca em place do anel de assinatura (rotas de
assinatura pós-rotação quebrariam em produção sem nenhum vermelho). A QA escreveu
`backend/tests/Squid.Tests/Recovery/KeyRotationRingSwapTests.cs` (arquivo novo, dentro dos
`touches` de testes): após a rotação o mesmo singleton deve abrir a cópia `0009` reembrulhada
com a chave NOVA (sem a troca isso lança `CryptographicException`), assinar um desafio, e o
wrap de um registro novo deve concordar com a `IBackupKeySource` trocada.
Filtro com o cenário novo: **8/8 verdes**; M9 sobre o cenário novo: **vermelho** (ledger).

## Execuções (comando — exit — artefato)

| # | Comando | Exit | Artefato |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (abertura) | 0 — 640/640 | `docs/sdd/runs/test-runs/20261122-080230-test-gate.log` |
| 2 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation` (7 testes do DEV) | 0 — 7/7 | `docs/sdd/runs/test-runs/20261122-080617-test-filter.log` |
| 3 | `powershell -File docs/sdd/qa/qa-mutate-task-0058.ps1 -VerifyOnly` | 0 — 10/10 finds, ASCII 0 | `docs/sdd/qa/QA-0058-verify.log` |
| 4 | `powershell -File docs/sdd/qa/qa-mutate-task-0058.ps1` (ledger: 10 ciclos) | 0 — `SUMMARY cycles=10 failed=0 green-aprovado=8` | `docs/sdd/qa/QA-0058-mutations.log` (1ª execução preservada em `QA-0058-mutations-run1.log`) |
| 5 | `powershell -File docs/sdd/qa/qa-scope-task-0058.ps1` (AC-08) | 0 — `fails=0` | `docs/sdd/qa/QA-0058-scope.log` |
| 6 | gate completo **sob a mutação M9** (640 verdes — prova do gap de cobertura) | 0 — 640/640 | `docs/sdd/runs/test-runs/20261122-093526-test-gate.log` |
| 7 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation` (com o cenário novo) | 0 — 8/8 | `docs/sdd/runs/test-runs/20261122-094705-test-filter.log` |
| 8 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~Ac06_…` sob M7 (diagnóstico: dump do log capturado) | 1 — falha real esperada | `docs/sdd/runs/test-runs/20261122-091021-test-filter.log` |
| 9 | `podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0058.sh` (Linux: build + filtro) | 0 — build 0, 8/8 | `docs/sdd/qa/QA-0058-linux-build.log`, `docs/sdd/qa/QA-0058-linux-test-keyrotation.log` |
| 10 | `… sdd-test.ps1 test` (fechamento, com o cenário novo) | 0 — **641/641** | `docs/sdd/runs/test-runs/20261122-101034-test-gate.log` |

### Ledger de mutações (10 ciclos, todos restaurados byte-exact + mtime bump S-23)

| Mutação | AC | Resultado |
| --- | --- | --- |
| M1 gate do operador falha aberto (`AdminEndpoints.cs:534`) | AC-01 | EXPECT-OK — só `Ac01` vermelho |
| M2 `RetiredAt` nunca escrito (INSERT NULL + UPDATE morto, `SqliteKeyEpochStore.cs:59-71`) | AC-01 | EXPECT-OK — `Ac01` + chain vermelhos |
| M3 época aposentada fora do keyring do kit (`KeyRotationService.cs` `keyring[currentEpoch]`) | AC-02 | EXPECT-OK — só o chain vermelho (restore recusa época 1) |
| M4 `0009` reembrulhada com a chave ANTIGA (`KeyRotationService.cs:58`) | AC-04 | EXPECT-OK — `Ac04`/`Ac06`/chain/novo vermelhos |
| M5 fonte de backup fica na chave antiga (`keys.RotateTo(oldKey,…)`) | AC-05 | EXPECT-OK — `Ac04`/`Ac06`/chain/novo vermelhos |
| M6 comparação da RC desligada (`RecoveryEndpoints.cs:158`) | AC-04 | EXPECT-OK — `Ac04` + `Ac06` vermelhos |
| M7 RC concatenada NO template do log (`AdminEndpoints.cs:484`) | AC-06 | EXPECT-OK — só `Ac06` vermelho (a prova de ausência pode falhar) |
| M7b RC como propriedade NOMEADA `{Credential}` (probe) | AC-06 | PROBE-GREEN — `PiiSanitizingEnricher` mascara para `[MASKED]` antes do sink (defesa em profundidade; ver diagnóstico #8) |
| M8 baseline do volume esvazio (quebra de expectativa, teste) | AC-03 | EXPECT-OK — chain vermelho |
| M9 `ring.RotateTo(newKey)` removido (`KeyRotationService.cs:111`) | AC-05 | EXPECT-OK — só o cenário novo vermelho (antes do cenário novo: gate 640 inteiro verde, #6) |

## AC-07 — runbook e índice (revisão + evidência de execução)

- `docs/runbooks/rotacionar-chave.md`: status **EXECUTÁVEL**, 16 marcações EXECUTÁVEL, 0 `PENDENTE`.
- Varredura de material: 0 base64 ≥40 chars, 0 hex64, 0 `RC-XXXX-…`, 0 `rk_…`, 0 mnemônico,
  0 placeholder com formato real. **Nenhum valor de chave no documento.**
- Índice `docs/runbooks/README.md` (linha ODEV, fora dos toques do DEV): rotacionar-chave =
  **EXECUTÁVEL** (P-07, TASK-0058 — 2 rotações reais, aponta o log
  `20261122-071147-runbook-rotacionar-chave.log`, que existe) e a prosa do topo saiu de
  "esboço com ponteiro de fase" para EXECUTÁVEL — **confere com o status real do runbook**.
- Spot-check do log de execução (37 linhas, `EXECUCAO OK`): diagnósticos 1–3 (health/ready/ledger),
  A registro 201, B backup `epoch=1`, C rotação `epoch=2` (corpo nunca impresso), D keyring de 2
  épocas só em variável, reload 1, E.1 exit 0, E.2 sem saída `arquivos-alterados=0`, E.4 `epoch=2`;
  2ª rotação `epoch=3`, reload 2, E.2 `arquivos-alterados=0`, E.3 RC antiga **401** / nova **200**,
  E.4 `epoch=3`; verificação 3 = ledger com as 3 épocas (1 e 2 com `retired_at`, 3 sem), varredura
  do log do serviço **10 sementes, 0 ocorrências**, fim com serviço parado.
- Nenhuma forma de material no log da execução (0 `RC-…`, 0 base64 44 chars; só fingerprints
  `sha256:` hex64 — o `public_fingerprint` por design: identifica sem expor).

## Achados

1. **[F1 — cobertura, fechada pela própria QA]** `backend/src/Squid.Api/Crypto/KeyRotationService.cs:111`
   — a troca em place do anel de assinatura (`ring.RotateTo(newKey)`) não tinha cenário
   can-fail: a mutação M9 deixou o gate completo **640/640 verde** (`20261122-093526-test-gate.log`),
   ou seja, um regressão real (assinatura de saída quebrando pós-rotação na ativação seguinte)
   passaria sem nenhum vermelho. **Remediada nesta QA** com
   `backend/tests/Squid.Tests/Recovery/KeyRotationRingSwapTests.cs` (8/8 no filtro; M9 agora
   vermelha só esse cenário). Não bloqueante por já remediada.
2. **[F2 — MINOR, documentação]** `docs/runbooks/rotacionar-chave.md:8` afirma "todos os
   comandos abaixo foram executados na prova", mas a **verificação 7** (`rotacionar-chave.md:155`,
   restore do envelope da época antiga com o keyring) não tem execução nenhuma no log
   `20261122-071147-runbook-rotacionar-chave.log` (0 menções a restore/restaurar). O
   comportamento é coberto pelos testes (`KeyRotationChainTests` restaura a época 1 pós-rotação;
   o parse de `Squid:RecoveryKeyRing` é unit-testado), e o comando apontado
   (`restaurar-backup.md` item B) é EXECUTÁVEL desde a TASK-0056 — mas a claim de prova é
   overstated. Não bloqueante: o núcleo do procedimento (A–E + verificações 1–6) está todo logado.
   Pré-existente? **Não** — o runbook foi reescrito nesta task.

## Pre-existing failures

NENHUM — gate de abertura 640/640 e fechamento 641/641, exit 0, sem vermelho pré-existente.

## Não coberto / unverified

- **Execução live da verificação 7 do runbook** (restore da época antiga num serviço real com
  `Squid__RecoveryKeyRing`): `unverified` — exigiria subir um serviço real (gate: pedir aprovação
  ao ODEV); o comportamento equivalente está coberto pelos testes acima. Risco: o passo final do
  runbook tem prova de execução só indireta (ver F2).
- **Suíte completa no Linux**: `unverified` — no Linux (podman/sdk 10.0.403) rodaram build
  (exit 0) e o filtro KeyRotation (8/8, exit 0); a suíte inteira só no Windows (o gate do
  dispatch é o comando PowerShell). Risco: baixo — os testes da task são cross-platform e
  passaram nas duas plataformas.
- Demais ACs: todos os cenários executados nas duas plataformas relevantes.

## Reprodução

```powershell
# pré-requisito: .NET 10 SDK, repo na branch task/TASK-0058-rotacao-chave-rk-rc
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~KeyRotation   # esperado: 8/8, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0058.ps1                                            # esperado: SUMMARY failed=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0058.ps1                                              # esperado: SUMMARY fails=0, exit 0
# Linux:
podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0058.sh                     # esperado: build 0 + 8/8, exit 0
```
