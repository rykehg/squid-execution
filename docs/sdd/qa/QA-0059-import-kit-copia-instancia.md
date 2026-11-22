<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-22 12:37:00

# QA-0059 — Import de kit com cópia de instância (TASK-0059)

- task: TASK-0059 · runId: R-20261120-01 · qaAttempt: **1/2** · verdict: **PASSED** (5/5 ACs)
- branch testada: `task/TASK-0059-import-kit-copia-instancia` @ `77d1a44` (1 commit sobre `develop` @ `895b658`)
- spec: SPEC-0012 §F (fecha S-42) · SPEC-0011 §B (duplo embrulho, regra do registro)
- gate: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`

## Mapa AC → cenário

| AC | Cenário executado | Harness | Resultado |
| --- | --- | --- | --- |
| AC-01 | com `Squid:RecoveryKey`: o import nasce a linha `0009`, ela desembrulha **só** com o segredo de boot e o SPKI obtido bate com a chave pública RSA que o perfil publica; em seguida o pipeline de saída entrega um follow (`Sent`) e o **verificador independente** `HttpSignatureVerifier` aceita a assinatura `rsa-sha256` com `keyId …#main-key` — sem nenhum registro (import não emite sessão; o ator nasce só do kit). + cenário novo da QA para a metade "nunca no caminho 409": kit estrangeiro no handle já tomado → **409 `handle_taken`**, `COUNT 1` e a linha **byte-idêntica** à de antes (o kit recusado nunca escreve) | `ImportInstanceCopyTests.Ac01_…` + `ImportInstanceCopyQaTests.Conflict_path_…` | PASS (M1 e M3 vermelhos) |
| AC-02 | sem `Squid:RecoveryKey` no boot: 201, corpo exato `{"handle":"…","displayName":null}`, `COUNT 0` em `0009`, perfil 200. **Prova do byte-idêntico (M4)**: o endpoint é trocado pela versão do `develop` (o comportamento pré-mudança, blob `47cf2f8e`) e o cenário do AC-02 continua **VERDE** — os bytes esperados são o artefato real de antes, não uma lembrança | `ImportInstanceCopyTests.Ac02_…` + M4 | PASS (M4: Ac02 verde; M5 vermelho) |
| AC-03 | varredura de ausência sobre resposta do import, perfil, `/api/auth/me`, rota do kit, linha do ator e log capturado do boot: PKCS8 e RSA privada em **base64 e hex** + a própria `RK` de boot ausentes; o valor **embrulhado** da cópia não aparece em nenhuma resposta. Estático: `wrapped_signing_key` só é lido pelo anel (ativação do ator) e pela rotação — **nenhuma rota o serializa** | `ImportInstanceCopyTests.Ac03_…` + grep estático | PASS (M5 vermelho) |
| AC-04 | mesmo kit importado duas vezes: 201 nas duas, **corpo idêntico byte a byte** e `COUNT 1` (upsert `INSERT OR REPLACE` com PK `handle`, B-14) | `ImportInstanceCopyTests.Ac04_…` | PASS (M2 vermelho) |
| AC-05 | gate PowerShell **exit 0** (abertura 645/645, fechamento 647/647 = 645 + 2 cenários novos da QA); `git diff develop...HEAD` = 2 arquivos, **2/2 nos `touches`**; zero `.csproj`/`.props`, zero linha `PackageReference`+; sem resíduo de mutação (blobs 2/2 idênticos ao HEAD) | gate + `qa-scope-task-0059.ps1` | PASS |

## Cenários novos da QA (arquivo novo, dentro dos `touches` de teste)

`backend/tests/Squid.Tests/Recovery/ImportInstanceCopyQaTests.cs` — dois fatos:

1. **`Conflict_path_a_foreign_kit_409s_and_never_writes_its_copy`** (metade do AC-01 que nenhum
   AC cobria): importar um kit diferente no mesmo handle recusa com 409 e a linha `0009` fica
   **byte-idêntica** (não é só `COUNT 1` — o conteúdo não muda), ainda desembrulhável na chave
   pública do perfil. A mutação M3 (gravar **antes** de `CreateAsync`) deixa só este cenário
   vermelho: sem ele, um kit recusado sobrescreveria a cópia do ator legítimo em produção.
2. **`Obs01_after_the_in_memory_rotation_an_import_wraps_with_the_current_key`** — ver
   OBS-01 abaixo.

## OBS-01 — rotação (TASK-0058) × import (observação, não defeito)

Nenhum AC cobre "depois de uma rotação, o import embrulha com a `Squid:RecoveryKey` ATUAL".
Verificado com cenário executado + leitura do código:

- Executado: `ImportInstanceCopyQaTests.Obs01_…` — import com K1 → `ring.RotateTo(K2)` na
  memória (o mesmo singleton, `SquidServiceHost.cs:345`, que o `KeyRotationService.cs:111`
  troca) → import de outro kit → a cópia nova **abre só com K2** (K1 recusa no AEAD) e entrega
  o SPKI do perfil; a cópia anterior continua abrindo com K1 (R-20).
- Código: `RecoveryEndpoints.cs:234` chama `instanceKeys.WrapInstanceCopy` no anel resolvido do
  DI; `RuntimeSigningKeyRing.RotateTo` (`RuntimeSigningKeyRing.cs:46-50`) troca `_rootKey` no
  próprio objeto, sem trocar a instância → o import pós-rotação embrulha com a chave corrente.
- Resultado: **comportamento correto**, sem defeito. Limitação: o cenário chama `RotateTo`
  direto em vez da rota `POST /api/admin/recovery/rotate` (sessão + gate de operador já
  cobertos pela QA-0058); a troca invocada é exatamente o método que a rota aciona.

## Execuções (comando — exit — artefato)

| # | Comando | Exit | Artefato |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (abertura) | 0 — 645/645 | `docs/sdd/runs/test-runs/20261122-114334-test-gate.log` |
| 2 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~ImportInstanceCopy` (4 DEV + 2 da QA) | 0 — 6/6 | `docs/sdd/runs/test-runs/20261122-114602-test-filter.log` |
| 3 | `powershell -File docs/sdd/qa/qa-mutate-task-0059.ps1 -VerifyOnly` | 0 — 5/5 achados, ASCII 0 | `docs/sdd/qa/QA-0059-verify.log` |
| 4 | `powershell -File docs/sdd/qa/qa-mutate-task-0059.ps1` (1ª execução) | 1 — 5/5 EXPECT-OK na substância, **rótulo GREEN invertido no próprio driver** (ferramenta da QA) | `docs/sdd/qa/QA-0059-mutations-run1.log` |
| 5 | `powershell -File docs/sdd/qa/qa-mutate-task-0059.ps1` (ledger corrigido) | 0 — `SUMMARY cycles=5 failed=0 green-aprovado=6` | `docs/sdd/qa/QA-0059-mutations.log` |
| 6 | `powershell -File docs/sdd/qa/qa-scope-task-0059.ps1` (AC-05) | 0 — `fails=0` | `docs/sdd/qa/QA-0059-scope.log` |
| 7 | `podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0059.sh` (Linux: build + filtro) | 0 — build 0, **6/6**; a etapa de gate completo saiu 1 (ver "Linux" abaixo) | `docs/sdd/qa/QA-0059-linux-build.log`, `QA-0059-linux-test-importcopy.log`, `QA-0059-linux-test-gate.log` |
| 8 | `podman run … bash docs/sdd/qa/qa-linux2-task-0059.sh` (Linux: saída COMPLETA do gate para classificação + filtro da família) | 0 — build 0, gate 51 falhas / 596 pass / 647 total (exit 1, pré-existente), família **6/6 exit 0** | `docs/sdd/runs/test-runs/20261122-152625-test-linux-full.log`, `docs/sdd/qa/QA-0059-linux2-build.log`, `QA-0059-linux2-test-importcopy.log` |
| 9 | `… sdd-test.ps1 test` (fechamento, com os 2 cenários novos) | 0 — **647/647** | `docs/sdd/runs/test-runs/20261122-122607-test-gate.log` |

### Linux — 51 vermelhos, todos pré-existentes de ambiente (não são desta task)

- Contagem idêntica à baseline: `QA-0054` rodou a suíte completa no mesmo contêiner e teve
  **51** (`QA-0054-linux-test.log`); desta vez também **51** sobre 647 (+40 testes desde
  então, todos verdes: `556→596` pass, `51→51` falha).
- **Comparação de conjuntos, nome a nome** (baseline `20261121-154231-test-linux-full.log`
  pré-task × `20261122-152625-test-linux-full.log`): 40 únicos × 40 únicos,
  `only-now=0`, `only-baseline=0` — **nenhuma falha nova**.
- Classes (as mesmas da QA-0054 e do memory bank `build-run-test.md:42`): 37 nomes com
  `DllNotFoundException … libfontconfig.so.1` (SkiaSharp na imagem; `libfontconfig1` exige
  gate humano, I-10/S-44) + 13 casos `DependencyRuleTests` que spawnam `cmd.exe` (Windows-only,
  S-44) + 1 `Assert` a jusante (`ThreadAttachmentQuotaTests` → 500 do mesmo processador Skia).
- **Zero falha em `Recovery` / `ImportInstanceCopy` / `KeyRotation` / `DoubleWrap`** no
  conjunto; a família desta task passa **6/6 exit 0** no Linux
  (`QA-0059-linux2-test-importcopy.log`).

### Ledger de mutações (5 ciclos, todos restaurados byte-exact + mtime bump S-23)

| Mutação | AC | Resultado |
| --- | --- | --- |
| M1 `instanceCopy = WrapInstanceCopy(rsa)` → `null` (`RecoveryEndpoints.cs:234`) | AC-01 | EXPECT-OK — 5 vermelhos (Ac01/Ac03/Ac04/Conflict/Obs01); **Ac02 verde** (sem config nada muda) |
| M2 `INSERT OR REPLACE` → `INSERT` (`SqliteInstanceSigningKeyStore.cs:32`) | AC-04 | EXPECT-OK — só `Ac04` vermelho (2ª importação morre na PK; `COUNT 1`/resposta iguais são o alvo) |
| M3 save movido **antes** de `CreateAsync` (o caminho 409 passa a gravar) | AC-01 | EXPECT-OK — só `Conflict_path` vermelho (a linha do ator legítimo é sobrescrita pelo kit recusado) |
| M4 endpoint **revertido para o `develop`** (blob `47cf2f8e`, o comportamento pré-mudança) | AC-02 | EXPECT-OK — 4 cenários que exigem cópia vermelhos + **`Ac02` VERDE** (`GREEN-EXPECT-OK`): a resposta esperada é byte-idêntica à de antes |
| M5 cópia vazando no corpo da resposta (`copy = …`) | AC-03 | EXPECT-OK — `Ac02` + `Ac03` vermelhos (`Ac04` também, porque o nonce do AEAD muda a cada embrulho) |

## Achados

| # | Severidade | Onde | O que quebra | Evidência |
| --- | --- | --- | --- | --- |
| 1 | MINOR (ferramenta da QA, não produto) | `docs/sdd/qa/qa-mutate-task-0059.ps1` (predicado GREEN) | Na execução 1 o rótulo `GREEN-EXPECT-MISS` estava invertido: classificou como falha um M4 **correto** (o `Ac02` estava verde, como o log cru prova) e o driver saiu com exit 1 apesar de 5/5 EXPECT-OK. Corrigido antes do ledger fechar; execução 1 preservada em `QA-0059-mutations-run1.log`, execução 2 (`failed=0`) é a válida | `QA-0059-mutations-run1.log` vs `QA-0059-mutations.log` |

**Nenhum achado de produto.** Nenhum cenário de AC falhou.

## Pre-existing failures

- **Windows (plataforma primária, o gate do dispatch): NENHUM** — abertura 645/645 exit 0
  (641 baseline da TASK-0058 + 4 da TASK-0059) e fechamento **647/647 exit 0** (+2 da QA).
- **Linux (contêiner): 51**, todos pré-existentes de ambiente e **idênticos ao conjunto da
  baseline pré-task** (comparação nome a nome acima: `only-now=0`) — SkiaSharp sem
  `libfontconfig.so.1` na imagem + `DependencyRuleTests` spawnando `cmd.exe` (S-44). Instalar
  `libfontconfig1` exige gate humano (I-10), indisponível nesta sessão. Nenhum vermelho
  bloqueia o veredito: o gate do AC-05 é o `.ps1` no Windows (exit 0) e a família desta task
  está verde nas duas plataformas.

## Não coberto / unverified

- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest)**: `unverified` — a imagem sdk
  não traz `pwsh` e instalar exige gate; executei o gêmeo `sdd-test.sh` (o runner Linux
  disponível) com o mesmo alvo, build exit 0 e a família 6/6. Risco: baixo.
- **Servidor real em execução** (HTTP de verdade fora do host em processo): `unverified` —
  exigiria subir um serviço real (gate: pedir aprovação ao ODEV). Risco: baixo — todos os
  ACs rodam no host completo do próprio repo, com as mesmas rotas.
- **Rota `POST /api/admin/recovery/rotate` dentro do cenário OBS-01**: o cenário chama
  `RotateTo` direto; o gate da rota (sessão + `Instance:Operators`) é da QA-0058 e está
  coberto lá. Risco: baixo — o método invocado é o mesmo (`KeyRotationService.cs:111`).
- Demais ACs: todos os cenários executados nas duas plataformas (Windows e Linux).

## Reprodução

```powershell
# pré-requisito: .NET 10 SDK, repo na branch task/TASK-0059-import-kit-copia-instancia
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~ImportInstanceCopy   # esperado: 6/6, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0059.ps1                                                   # esperado: SUMMARY failed=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0059.ps1                                                    # esperado: SUMMARY fails=0, exit 0
# Linux:
podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0059.sh   # esperado: build 0 + 6/6 + gate exit 0
```
