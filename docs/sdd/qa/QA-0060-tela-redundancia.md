<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-22 15:44:05

# QA-0060 — Tela de redundância: redundancy.score + página /admin/backup (TASK-0060)

- task: TASK-0060 · runId: R-20261120-01 · qaAttempt: **1/2** · verdict: **PASSED** (6/6 ACs)
- branch testada: `task/TASK-0060-tela-redundancia` @ `ba9d32a` (1 commit sobre `develop` @ `ec01d6c`)
- spec: SPEC-0012 §G (opção A) · doc 05 §8 · contrato backup-recovery §6 · fecha **S-52**
- gate: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`

## Mapa AC → cenário

| AC | Cenário executado | Harness | Resultado |
| --- | --- | --- | --- |
| AC-01 | O score é `targets com o envelope mais recente VERIFICADO ÷ targets configurados` no formato do doc 05 §8: `1/2 — sem recuperação de desastre` sem off-site verificado, `2/2` sem sufixo quando todos verificam, `1/1` local-only **com** sufixo, `0/2` honesto quando nada verificou; o numerador vem das linhas do registry — a janela do envelope verificado mais recente, com a regra que impede a verified STALE de reivindicar a janela cuja última tentativa falhou — e o denominador é o par configurado (o wiring do composition root só acrescenta o off-site quando `Backup:S3:*` está completo) | `RedundancyScoreTests` (Ac01_…format, The_score_counts…, Ac01_ac02…score, Ac05 1º assert) + mutações **M1, M2, M6, M7** | PASS (M1 4 vermelhos, M2 só o cenário de janela, M6/M7 o 2/2 e o 1/2) |
| AC-02 | A página `/admin/backup` devolve `text/html`, `<!doctype html>`, sem `src=` e sem `http` (inline), e mostra os valores **do estado atual lido do registry**: `last_success_at` byte-idêntico ao da rota JSON, `envelopes_count`, `bytes_total`, `verify_failures`, `redundancy.score` e os `restore.*` — gravar um drill muda a página na hora. Cenário novo da QA: o **instante** de `restore.last_drill_at` é comparado `<dd>` a `<dd>` com o valor que `GET /api/admin/backup/status` responde (o fato do DEV só cobre o label e o placeholder `—`, que é compartilhado com `drill_result`) | `RedundancyScoreTests.Ac01_ac02…` + `RedundancyScoreQaTests.Ac02_the_page_prints_the_drill_values…` + mutações **M3, M10** | PASS (M3 vermelho nos dois fatos HTTP; M10 vermelho **só** no cenário da QA) |
| AC-03 | Sem sessão: **401** `not_authenticated` nas DUAS rotas; sessão fora de `Instance:Operators`: **403** `policy_denied` nas DUAS rotas (mesma regra da P-03). Mutação M4 apaga o `Gate` só da rota HTML e o fato cai no primeiro assert de status 401 | `RedundancyScoreTests.Ac01_ac02_ac03_ac04…` + mutação **M4** | PASS (M4: `Assert.Equal Values differ` na rota da página; a rota JSON continua gated) |
| AC-04 | Metade inline: página servida pelo backend com markup embutido (acima) + mutação **M5** injeta `<script src=…>` e o `DoesNotContain("src=")` cai. Metade pacote: `qa-scope-task-0060.ps1` prova zero `.csproj`/`.props` tocado e zero linha `PackageReference`/`PackageVersion` adicionada; mutação **S2** escreve um `PackageVersion` no `Directory.Packages.props` e o escopo sai **1** | filtro + `qa-scope-task-0060.ps1` (ciclos S2) | PASS (M5 vermelho; S2 `SCOPE-EXPECT-OK exit=1 pkg-added=1`) |
| AC-05 | Com o **FakeS3** respondendo 500, o primeiro run dá `Redundância: 1/2 — sem recuperação de desastre`; o destino sarra e o segundo run dá `Redundância: 2/2` (sufixo some) e a página mostra o mesmo. M6 mata a metade off-site do score (2/2 impossível) e M7 apaga o denominador configurado (1/2 vira 1/1) — os dois derrubam o fato | `RedundancyScoreTests.Ac05_…` + mutações **M6, M7** | PASS (M6: `Strings differ` no 2/2; M7: `Strings differ` no 1/2) |
| AC-06 | Gate `sdd-test.ps1 test` **exit 0** (abertura 652/652; fechamento 653/653 com o cenário da QA); escopo: `git diff develop` = **7 arquivos, 0 fora dos touches** (6 fontes + o arquivo de task), nenhum `.csproj`, nenhum pacote, resíduo de mutação zero (blobs 6/6 idênticos ao HEAD, mtime delta 0). As mutações **S1** (fora do touches) e **S2** (pacote) deixam o escopo em exit 1 — o próprio scanner de escopo tem prova de que pode falhar | gate + `qa-scope-task-0060.ps1` (ciclos S1/S2 + execução avulsa) | PASS (escopo final `fails=0`) |
| S-52 (extra) | `Backup:S3:*` parcial (endpoint+bucket sem credencial) → **warning de boot** `off-site backup disabled: Backup:S3:* is present but unusable…`; boot padrão local-only → **silêncio** (nenhuma linha `off-site backup disabled`). As DUAS metades têm mutação própria: M8 inverte o guarda (presence cai: `Assert.Contains Sub-string not found`) e M9 faz `if (true)` (silêncio cai: `Assert.DoesNotContain Sub-string found`) | `RedundancyScoreTests.S52_…` + mutações **M8, M9** | PASS — **fecha S-52** |

## Cenário novo da QA (arquivo novo, dentro dos `touches` de teste)

`backend/tests/Squid.Tests/Backup/RedundancyScoreQaTests.cs` — um fato:

1. **`Ac02_the_page_prints_the_drill_values_the_json_route_answers`** — a metade do AC-02 que
   nenhum fato do DEV cobria: depois de `SqliteRestoreDrillLedger.RecordAsync("restore-roundtrip",
   "passed")`, o valor de `restore.last_drill_at` é lido da rota JSON e exigido **verbatim**
   como `<dd>{valor}</dd>` na página. Os fatos do DEV aceitam qualquer coisa aqui porque o
   placeholder `—` é o mesmo de `drill_result` (um drill gravado já derrubaria um placeholder
   fixo, mas um valor errado não-placeholder passaria). A mutação **M10** deixa só este cenário
   vermelho — é ele que prova que o campo é lido do registry.

## D-01 — leitura do sufixo conferida contra SPEC-0012 §G (concordo)

- SPEC-0012 §G (`SPEC-0012-backup-rotação-p07.md:149-150`): sufixo
  `— sem recuperação de desastre` **quando nenhum target off-site tem o envelope mais recente
  verificado**. Com só o local configurado (1/1) não existe off-site → a condição é
  verdadeira por vacuidade → **sufixo presente**. A leitura do DEV (D-01) é a que a spec
  autoriza; o `1/1` local-only com sufixo está correto e está testado nos dois níveis
  (`RedundancyScoreTests.Ac01_…format` puro e o fato HTTP com host local-only).
- O caso `2/2` (todos verificados, off-site entre eles) remove o sufixo — cumprindo também a
  palavra do AC-01 ("sem o sufixo quando todos estão"). Nenhuma divergência para marcar.
- Formato conferido contra o próprio doc 05 §8 (`05-arquitetura-consolidada.md:541`) e o
  contrato §6 (`backup-recovery.md:113`): `Redundância: 1/3 — sem recuperação de desastre` —
  o implementado é o mesmo literal, `BackupRedundancy.cs:39-41`.

## S-47 (honestidade) — de onde os valores vêm

Estático + executado: a página e o JSON leem só ports sobre o registry —
`AdminEndpoints.cs:479-486` (`IBackupStatusSource` + `IBackupRedundancySource`), a fonte do
score faz SELECT apenas em `backup_envelope` (`SqliteBackupRedundancySource.cs:65-66, 89-91`)
e a fonte do status idem (`SqliteBackupStatusSource.cs:25-27`). Nenhum caminho lê o header que
o destino guarda em `<id>.json`. Executado: M2 (ignorar a regra da verified stale) e M6
(nunca marcar off-site) derrubam exatamente os fatos que exigem o número do registry — o
score é a linha, nunca a palavra do destino.

## Execuções (comando — exit — artefato)

| # | Comando | Exit | Artefato |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (abertura) | 0 — **652/652** | `docs/sdd/runs/test-runs/20261122-143930-test-gate.log` |
| 2 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScore` (família: 5 DEV + 1 QA) | 0 — **6/6** | `docs/sdd/runs/test-runs/20261122-145642-test-filter.log` |
| 3 | `… sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScoreTests` (filtro do dispatch) | 0 — **5/5** | `docs/sdd/runs/test-runs/20261122-145832-test-filter.log` |
| 4 | `powershell -File docs/sdd/qa/qa-mutate-task-0060.ps1 -VerifyOnly` | 0 — 12/12 achados, ASCII 0 | `docs/sdd/qa/QA-0060-verify.log` |
| 5 | `powershell -File docs/sdd/qa/qa-mutate-task-0060.ps1` (1ª execução, 11 ciclos — sem M10) | 0 — `SUMMARY cycles=11 failed=0 green-aprovado=6` | `docs/sdd/qa/QA-0060-mutations-run1.log` |
| 6 | `powershell -File docs/sdd/qa/qa-mutate-task-0060.ps1` (ledger final com M10) | 0 — `SUMMARY cycles=12 failed=0 green-aprovado=6 elapsed-s=388` | `docs/sdd/qa/QA-0060-mutations.log` |
| 7 | `powershell -File docs/sdd/qa/qa-scope-task-0060.ps1` (AC-06) | 0 — `fails=0` | `docs/sdd/qa/QA-0060-scope.log` |
| 8 | `podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0060.sh` (Linux: build + `sdd-test.sh test` + gate completo com saída nome-a-nome + família) | 0 (família) — build 0; gate **51 falha / 602 pass / 653** exit 1 (pré-existente); família **6/6 exit 0** | `docs/sdd/qa/QA-0060-linux-build.log`, `QA-0060-linux-test-gate.log`, `QA-0060-linux-test-redundancy.log`, `docs/sdd/runs/test-runs/20261122-182251-test-linux-full.log` |
| 9 | `… sdd-test.ps1 test` (fechamento, com o cenário da QA) | 0 — **653/653** | `docs/sdd/runs/test-runs/20261122-154405-test-gate.log` |

### Ledger de mutações (12 ciclos, todos restaurados byte-exact + mtime bump S-23)

Cada ciclo classifica o vermelho pelo **log gravado** (S-22 — no ciclo M1 o wrapper imprimiu
exit 3 e o cabeçalho do log dizia `exit: 1` com resumo real; o driver usa o log) e registra a
primeira linha da assertiva que caiu, que é o que distingue mutações com o mesmo fato vermelho.

| Mutação | AC | Resultado (fato vermelho :: assertiva) |
| --- | --- | --- |
| M1 sufixo removido do `Score` (`BackupRedundancy.cs`) | AC-01 | EXPECT-OK — 4 vermelhos (format/source/status/Ac05) `Assert.Equal Strings differ`; verdes: S-52 e o cenário da QA |
| M2 `lastAttemptFailedThere = false` (verified stale conta) (`SqliteBackupRedundancySource.cs`) | AC-01 | EXPECT-OK — **só** `The_score_counts…` vermelho (run C esperava 1/2); 5 verdes |
| M3 score da página virou constante `"0/0"` (`BackupPage.cs`) | AC-02 | EXPECT-OK — status + Ac05 vermelhos `Assert.Contains Sub-string not found`; 4 verdes |
| M4 `Gate` apagado só do `/admin/backup` (`AdminEndpoints.cs`) | AC-03 | EXPECT-OK — **só** o fato HTTP vermelho `Assert.Equal Values differ` (401→200); 5 verdes |
| M5 `<script src="/app.js">` injetado na página (`BackupPage.cs`) | AC-04 | EXPECT-OK — **só** o fato HTTP vermelho `Assert.DoesNotContain Sub-string found`; 5 verdes |
| M6 `offsiteVerified` forçado `false` (`SqliteBackupRedundancySource.cs`) | AC-05 | EXPECT-OK — Ac05 + fonte vermelhos `Strings differ` (2/2 impossível); o local-only segue verde |
| M7 wiring do off-site virou `null` (`SquidServiceHost.cs`) | AC-01 | EXPECT-OK — **só** Ac05 vermelho `Strings differ` (1/2 virou 1/1); 5 verdes |
| M8 guarda do warning invertida `is not null` (`SquidServiceHost.cs`) | S-52 presença | EXPECT-OK — S-52 vermelho `Assert.Contains Sub-string not found` |
| M9 warning incondicional `if (true)` (`SquidServiceHost.cs`) | S-52 silêncio | EXPECT-OK — S-52 vermelho `Assert.DoesNotContain Sub-string found` (o boot padrão passou a avisar) |
| M10 drill_at da página virou `"-"` (`BackupPage.cs`) | AC-02 | EXPECT-OK — **só** `RedundancyScoreQaTests.Ac02_…` vermelho `Assert.Contains Sub-string not found`; os fatos do DEV seguem verdes (placeholder compartilhado) |
| S1 `SquidApi.cs` editado fora do touches | AC-06 | SCOPE-EXPECT-OK — `qa-scope` exit 1 (`outside-touches=1 residue=1 fails=3`) |
| S2 `PackageVersion` novo no `Directory.Packages.props` | AC-04/06 | SCOPE-EXPECT-OK — `qa-scope` exit 1 (`pkg-added=1 fails=3`) |

Fecho do driver: `GREEN … aprovado=6 falha=0` + `residue-backend=0` + `rogue-untracked=0`
(`QA-0060-mutations.log`).

## Achados

| # | Severidade | Onde | O que quebra | Evidência |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado de produto.** Nenhum cenário de AC falhou | — |

Observações (não bloqueiam, nenhuma contradiz o contrato):

1. **Gap de cenário preenchido pela QA (não é defeito):** os fatos do DEV cobrem
   `restore.last_drill_at` por label e por placeholder — o **valor** só é exigido pelo cenário
   novo (`RedundancyScoreQaTests`), cuja mutação M10 prova. Risco residual: nenhum, preenchido.
2. **S-22 pré-existente observado de novo:** no ciclo M1 o wrapper `sdd-test.ps1` imprimiu
   `exit 3` (assinatura de ambiente) enquanto o log gravado diz `exit: 1` com resumo real — o
   driver classifica pelo log, como o memory bank `build-run-test.md:54` manda. Já está no
   backlog (S-22); nenhum achado novo.
3. **AC-01 palavra x SPEC:** a frase do AC ("sem o sufixo quando todos estão") lida à risca
   num host 1/1 local-only diria "sem sufixo", mas a autoridade é a SPEC-0012 §G (condição
   off-site) — seção "D-01" acima. Adjudicado a favor da spec, como o ODEV já registrou.

## Pre-existing failures

- **Windows (plataforma primária, o gate do dispatch): NENHUM** — abertura **652/652 exit 0**
  (647 baseline + 5 da TASK-0060) e fechamento 653/653 exit 0 (+1 da QA).
- **Linux (contêiner): 51**, idênticos **nome a nome** à baseline pré-task — comparação de
  conjuntos entre `20261122-182251-test-linux-full.log` (esta task, 653 testes) e
  `20261122-152625-test-linux-full.log` (QA-0059, 647 testes): **mine=51 base=51 only-now=0
  only-baseline=0**. Classes: 13 `DependencyRuleTests` spawnando `cmd.exe` (Windows-only,
  S-44) + 38 SkiaSharp (`libfontconfig.so.1` ausente na imagem → `DllNotFoundException` e os
  500 a jusante em Quota/Threads/Unfurl — I-10/S-44). Instalar `libfontconfig1` exige gate
  humano, indisponível nesta sessão. **Nenhum vermelho novo** e a família desta task passa
  **6/6 exit 0** no Linux (`QA-0060-linux-test-redundancy.log`).

## Não coberto / unverified

- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest): `unverified`** — `pwsh` não
  existe nesta máquina nem na imagem sdk (instalar exige gate). Executei o gêmeo disponível,
  `sdd-test.sh` + `dotnet test` no contêiner, com build, gate completo e família.
  Risco: baixo — mesma suíte, mesmo SDK 10.
- **Servidor real em execução (HTTP fora do host em processo): `unverified`** — exigiria subir
  um serviço real (gate: pedir aprovação ao ODEV). Risco: baixo — todos os ACs rodam no host
  completo do próprio repo, com as mesmas rotas e o mesmo pipeline de auth.
- **Endpoint S3 real (não-FakeS3): `unverified`** — o AC-05 manda usar o FakeS3 e foi assim;
  o caminho real da rede só é exercitado em deploy (GAP-52 já conhecido). Risco: baixo.

## Reprodução

```powershell
# pré-requisito: .NET 10 SDK, repo na branch task/TASK-0060-tela-redundancia
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~RedundancyScore   # esperado: 6/6, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0060.ps1   # esperado: SUMMARY cycles=12 failed=0, exit 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0060.ps1    # esperado: SUMMARY fails=0, exit 0
# Linux:
podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0060.sh   # esperado: LINUX-FAMILY-EXIT=0
```
