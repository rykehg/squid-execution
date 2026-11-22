<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: CONCLUIDO
- owner: qa
- lastVerified: 2026-11-22 04:52:00

# QA-0057 — S3CompatibleTarget off-site + drill restore-offsite

- task: `docs/sdd/tasks/TASK-0057-s3-offsite.md` (status `AWAITING_QA`, qaAttempt 0/2 → esta é a 1/2)
- runId: `R-20261120-01` · branch `task/TASK-0057-s3-offsite` (commit `90959a8` sobre `develop` `5d84ed3`, um commit só — testado exatamente nesse estado, sem push/merge, `main` intocado, nenhuma branch removida)
- critérios testados: a lista `## Acceptance criteria` da task como está (SPEC-0012 §B + §D; contrato `specs/001-squid-mvp/contracts/backup-recovery.md` §1/§5/§7; GAP-52 em vigor; cobertura adjudicada do restore = log de atividade + projeções + ledger de cota, S-49 estacionado)
- política: ponytail-qa `full` · memory bank: rota `qa-integracao` lida (`build-run-test.md#Local tests without gate`, `index.md`) — arquivos dentro do limite de 3/300 linhas
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows, plataforma primária) + gêmeo Linux `docs/sdd/qa/qa-linux-task-0057.sh` no contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` (10.0.403 = pin do `ci.yml`, imagem já presente — nada instalado) + mutações can-fail aplicadas à mão e restauradas com `git checkout` + SHA-256 pré/pós (byte-exact, bump de mtime implícito no checkout — S-23)

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `S3CompatibleTargetTests.Ac01_The_five_operations_run_over_the_configured_s3_destination` (`backend/tests/Squid.Tests/Backup/S3CompatibleTargetTests.cs:24`): `Backup:S3:*` lido só de configuração (seção incompleta → `From` devolve `null`, linha 41) → `TargetId = "s3-offsite"` → `PutAsync` (`.bin` + `.json`) / `GetAsync` bytes idênticos / `ListAsync` V2 paginado (4 objetos sobre fake com página 2 = o loop de `NextContinuationToken` é obrigatório) ordenado por `seqTo` e filtrando `fromSeq` / `ExistsAsync` / `DeleteAsync` idempotente → ≥10 requisições, **todas** com `Authorization AWS4-HMAC-SHA256` (linhas 98-100). Zero pacote NuGet: `git diff develop...HEAD` não toca `.csproj`/`Directory.Packages.props`/`Directory.Build.props` e o client usa só BCL (`System.Security.Cryptography`, `System.Text`) | PASS — gate `633/633 exit 0` + filtro `FullyQualifiedName~S3CompatibleTargetTests` exit 0 (2/2) e exit 0 no Linux; mutação **M3** (client assinando errado) → vermelho **o Ac01** na linha 52 (o primeiro `PutAsync` é recusado — "a client that signs wrong gets nothing done" é carga real) |
| AC-02 | `S3CompatibleTargetTests.Ac02_The_fake_s3_refuses_what_the_signature_does_not_cover` (`S3CompatibleTargetTests.cs:104`) — 4 casos de adulteração, cada um isolando exatamente o que a assinatura cobre: **(a)** sem assinatura → `403 SignatureDoesNotMatch` (linha 122-125); **(b)** um dígito hex da assinatura virado → `403` (linha 128-132; o replay nunca copia o `Authorization` original junto da adulterada — linha 178-192); **(c)** replay da assinatura sobre OUTRO path (PUT do `.json` reenviado para o `.bin`) → `403` (linha 134-135); **(d)** um byte do corpo virado com a mesma assinatura → `400 BadDigest` (linha 138-142) + **byte-identidade dos objetos guardados após os 4 recusos** (linha 145-147). Sem rede externa: Kestrel em `127.0.0.1:0` (`FakeS3.cs:119`) | PASS — filtros exit 0 (Windows e Linux); mutações **M1** (fake nunca recusa) → vermelho na linha 123 (caso a) e **M2** (checagem de payload neutra) → vermelho na linha 141 (caso d) — cada recuso é carga real |
| AC-03 | `RestoreDrillTests.Restore_offsite_brings_back_the_complete_state_without_the_local_disk` (`backend/tests/Squid.Tests/Backup/RestoreDrillTests.cs:234`, `Trait Category=RestoreDrill` na linha 34): seed de 5 eventos + blob de 1000 B + projeções + cota → backup local + off-site (2 linhas em `backup_envelope`, **ambas** `verified_at` não-nulo, `target_id` distinto `local-disk`/`s3-offsite`, bucket off-site não-vazio) → **apaga o disco local** (`DeleteVolume`, volume inexente) **e** o banco coberto → restore **do off-site** (`NewRestore(scenario.Offsite)`, linha 270) → `Succeeded`, `Events = 5`, `AggregateDigests.Matches` = 100% dos hashes por agregado (SC-009), projeções idênticas, `quota_ledger.used_bytes = 1000` idêntico → `GET /ready` **200** sobre o banco restaurado (linha 288-289). Cobertura = a adjudicada (log + projeções + cota; contas/sessões/atores ficam fora — S-49) | PASS — filtro `Category=RestoreDrill` exit 0 (**8/8** no Windows e no Linux); mutação **M5** (restore apontado para o disco local apagado) → vermelho **só esse drill** (linha 271) — o drill depende de verdade dos objetos off-site |
| AC-04 | `BackupOffsiteTests.Ac04` ×2 (`backend/tests/Squid.Tests/Backup/BackupOffsiteTests.cs:31` 500 e `:90` timeout): 500 permanente → run **succeede** com envelope local `Verified`/`local-disk`, linha local `verified_at IS NOT NULL` + linha off-site `verified_at IS NULL` (SQL linhas 59-62), warning logado contendo `s3-offsite` (linha 63); segunda janela: descoberta **só no primário** → cadeia local contígua `[0, 5]` (linhas 72-74) e bucket off-site **vazio** após `Heal` (linha 79) = backup local nunca corrompido. Timeout: `HangNext(2 s)` × `HttpClient` de 150 ms → mesma degradação (linhas 95-115) | PASS — exit 0 (Windows e Linux); mutação **M4** (catch de degradção removido, `when (false)`) → vermelho **os 3 fatos de BackupOffsiteTests** (Ac04 ×2 + Ac05) — a degradação é a única razão deles passarem |
| AC-05 | `BackupOffsiteTests.Ac05_The_s3_credential_comes_only_from_config_and_reaches_no_state_log_or_route` (`BackupOffsiteTests.cs:124`): host real com `--Backup:S3:Endpoint/Region/Bucket/AccessKey/SecretKey` (secret/acess-key únicos por execução) → rota `POST /api/admin/backup/run` degradada 200 + rota `GET /api/admin/backup/status` 200 + 2ª run saudável (bucket com ciphertext real) → varredura de `secret` E `accessKey` em: corpo das respostas das rotas, logs do console (Serilog), **bytes do estado do grain** (`*.db` + `-wal` + `-journal`) e **bytes de cada objeto do bucket** (linhas 184-205) → 4 linhas de envelope, exatamente 1 off-site com `verified_at IS NULL` (linhas 209-212). Leituras de `Backup:S3:*`: só `BackupS3Options.From` (grep) e `ToString()` nunca vaza credencial (`BackupS3Options.cs:89`) | PASS — exit 0 (Windows e Linux na família); mutação **M4** também derruba este fato (rota 500 sem a degradação) |
| AC-06 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → **exit 0** na abertura (`633/633`, log `20261122-042134-test-gate.log`, cabeçalho `exit: 0`) e no fechamento; escopo lido do git: `git diff --name-only develop...HEAD` = **exatamente 11 arquivos**, todos dentro dos `touches` (`Squid.Worker/Backup/**` ×7, `Squid.Service/SquidServiceHost.cs`, `Squid.Tests/Backup/**` ×3), **0** `.csproj`/`Directory.Packages.props`/`Directory.Build.props`; D-01 sem migração: nenhum arquivo de schema/migração no diff e `backup_envelope.target_id` já existia (`SqliteBackupEnvelopeStore.cs:31`, arquivo não alterado) | PASS — gates exit 0 + escopo conferido no git (linha de evidência própria) |

Nenhum AC ficou `NOT-COVERED`.

## Independência da validação SigV4 (mérito do AC-01/AC-02)

A prova de que a validação é **genuinamente independente**, não um acordo de código compartilhado:

1. **O fake nunca importa o client**: grep por `S3SigV4` no repo só encontra `S3SigV4.cs` (a classe) e `S3CompatibleTarget.cs` (o chamador) — zero referência em `backend/tests/**`. O `FakeS3.cs:9` importa `Squid.Worker.Backup` apenas para `BackupS3Options` (tipo de configuração).
2. **Re-derivacao própria**: `FakeS3.ValidateSignature` (`FakeS3.cs:323`) faz o parse do `Authorization` recebido, monta o canonical request **a partir do que chegou** (método/path/query/headers reais), com as suas próprias cópias das regras — `AwsEncode` (linha 415, declarado "an independent copy of the rule, on purpose"), `CanonicalPath` por segmento (linha 397), `CanonicalQuery` (linha 401), `DeriveKey` (linha 435), `HashHex` (linha 446) — e compara em tempo fixo (`FixedTimeEquals`, linha 382). O client tem as suas próprias (`Encode`/`DeriveSigningKey` em `S3SigV4.cs`), implementações separadas dos dois lados.
3. **Ordem real de um endpoint**: validação de assinatura → checagem do payload hash contra o corpo que chegou → falha roteirizada → despacho do objeto. Recusa acontece **antes** de tocar qualquer objeto (prova: byte-identidade pós-adulteração, linha 145-147).
4. **As 4 adulterações recusam de verdade** (tabela do AC-02) e as mutações de can-fail vermelham — ver próxima seção.

## Prova de que os testes podem falhar

5 mutações — um literal quebrado por ciclo, aplicado à mão —, cada uma rodada com o filtro do AC afetado, classificada **pelo log gravado** (`exit:` do cabeçalho, regra S-22), com o vermelho exigido no teste do próprio AC, e restaurada em seguida com `git checkout -- <arquivo>` + conferência SHA-256 pré/pós idêntico (byte-exact):

| Ciclo | Escopo | Literal quebrado | Vermelho obtido (saiu) |
| --- | --- | --- | --- |
| M1 | AC-02 | `FakeS3.cs:325` `ValidateSignature` → `return true;` (fake nunca recusa assinatura) | `Com falha: 1, Aprovado: 1, Total: 2, exit 1` — Ac02 falha na **linha 123** (caso a: sem assinatura deixa de ser 403) + linha 147 (objeto adulterado) — log `20261122-042625-test-filter.log` |
| M2 | AC-02 | `FakeS3.cs:156` `HashHex(body)` → o próprio header (checagem de payload sempre igual) | `Com falha: 1, Aprovado: 1, exit 1` — Ac02 falha na **linha 141** (caso d: BadDigest deixa de acontecer) + linha 147 — log `20261122-042756-test-filter.log` |
| M3 | AC-01/02 | `S3SigV4.cs:82` canonical → `canonical + "!"` (client assina errado) | `Com falha: 2, Aprovado: 0, exit 1` — **Ac01** na linha 52 (primeiro Put recusado) e Ac02 — log `20261122-043016-test-filter.log` |
| M4 | AC-04/05 | `BackupRunner.cs:114` `catch (Exception failure)` → `when (false)` (degradação removida) | `Com falha: 3, Aprovado: 0, Total: 3, exit 1` — **Ac04 ×2 + Ac05** — log `20261122-043210-test-filter.log` |
| M5 | AC-03 | `RestoreDrillTests.cs:270` `NewRestore(scenario.Offsite)` → `NewRestore(scenario.Target)` (restore do disco apagado) | `Com falha: 1, Aprovado: 7, Total: 8, exit 1` — **só o drill restore-offsite** (linha 271) — log `20261122-043354-test-filter.log` |

Fecho: SHA-256 pré/pós idênticos nos 4 arquivos mutados (`FakeS3.cs` `DCA8C42C…`, `S3SigV4.cs` `A898FBC1…`, `BackupRunner.cs` `41BD02B5…`, `RestoreDrillTests.cs` `58BE6D9B…`), `git status` mostrando só a linha de estado do ODEV no arquivo da task, e os 3 filtros re-executados verdes sobre a árvore restaurada (`36/36`, `8/8`, `2/2`, todos exit 0). Os cenários não são decoração: cada AC vermelhou quando o comportamento quebrou.

## Fanout D-01 — uma linha por destino, sem migração

- `BackupRunner` (`backend/src/Squid.Worker/Backup/BackupRunner.cs:95`): pipeline **local primeiro** com a semântica de sempre (falha local = run falha, `BackupRunner.cs:83`), depois `RunOffsiteAsync` com o seu próprio `BackupPipeline` sobre o **mesmo** registry; captura do envelope via `onPrepared` (`BackupPipeline.cs:109`, invocado antes do send) e, na falha, `RegisterAsync(prepared)` → linha off-site com `verified_at` NULL + warning estruturado (`BackupRunner.cs:114-124`). Sucesso = a própria pipeline registra a linha `Verified`. Ids únicos por pipeline (`NewId`, `BackupPipeline.cs:150`) → **uma linha por destino sem tocar em schema** — confirmado: nenhum arquivo de migração/schema no diff, `target_id` pré-existente em `SqliteBackupEnvelopeStore.cs:31`.
- Descoberta só no primário: `NextSeqFromAsync` lê `_target.ListAsync` (`BackupRunner.cs:59`) — destino off-site inalcançável nunca para o local (provado pelo Ac04: segunda janela contígua com o bucket off-site vazio).
- DI: `SquidServiceHost.cs:228-240` — `BackupS3Options.From` completo registra `S3CompatibleTarget` (a si mesmo, **não** como `IBackupTarget`) e o `BackupRunner` recebe via `GetService<S3CompatibleTarget>()`; local-only fica byte-idêntico a um host sem S3.

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`.

1. `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 633, Total: 633, Duração: 10 m 5 s` (gate de abertura; log `docs/sdd/runs/test-runs/20261122-042134-test-gate.log`, cabeçalho `exit: 0`)
2. `powershell … sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Backup` → exit 0 → `36/36, 1 m 38 s` (log `20261122-042433-test-filter.log`) — família Backup inteira, baseline do DEV confirmada
3. `powershell … sdd-test.ps1 test -TestFilter Category=RestoreDrill` → exit 0 → `8/8, 15 s` (log `20261122-042501-test-filter.log`) — os 8 drills, o `restore-offsite` entre eles
4-8. os 5 ciclos de mutação M1-M5 (tabela acima) → exit 1 nos cinco, logs `20261122-042625`, `042756`, `043016`, `043210`, `043354`
9-11. filtros re-executados após restauração → exit 0 → `36/36` (`20261122-043732`), `8/8` (`20261122-043800`), `2/2` (`20261122-043813`)
12. `git diff --name-only develop...HEAD` + `git diff develop…HEAD -- '*.csproj' 'Directory.Packages.props' 'Directory.Build.props'` → exit 0 → 11 arquivos nos touches, saída de csproj/props **vazia**
13. `podman run --rm -v C:/dev/squid-squad-net/squid-execution:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0057.sh` → exit 0 → `LINUX-BUILD-EXIT=0`, `LINUX-BACKUP-EXIT=0` (`36/36, 1 m 12 s`), `LINUX-DRILLS-EXIT=0` (`8/8, 4 s`) — logs `docs/sdd/qa/QA-0057-linux-build.log`, `QA-0057-linux-test-backup.log`, `QA-0057-linux-test-drills.log`
14. gate de fechamento (após todas as mutações restauradas): `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 633, Total: 633, Duração: 10 m 13 s` (log `docs/sdd/runs/test-runs/20261122-045245-test-gate.log`, cabeçalho `exit: 0`)

## Findings

1. `specs/001-squid-mvp/contracts/backup-recovery.md` §4 passo 4 — baixo — **arrasto da QA-0056 (finding 1), pré-existente**: a recomendação "cortar um teste de recusa por hash do payload na TASK-0057" não foi incorporada aos ACs desta task — nenhum cenário aqui alimenta o restore com bytes divergentes do `contentHash` (a sonda M8 da QA-0056 continua valendo). Não é exigido por nenhum AC de TASK-0057 → não bloqueia; ODEV adjudica (test próprio ou saneamento).
2. `backend/src/Squid.Worker/Backup/BackupS3Options.cs:56` — baixo — **`shortcut:` documentado pelo próprio DEV**: um `Backup:S3:*` com erro de digitação desliga o off-site silenciosamente (`From` devolve `null` → host local-only); upgrade previsto para o painel da TASK-0060. Conforme a regra de `shortcut:` (limite conhecido + caminho de upgrade); não bloqueia.
3. Semântica pós-restore (observação, **nenhum AC violado**): se o primeiro backup depois de um restore mandar `seqFrom 0` de novo, a cadeia off-site recusa em voz alta contra o que já existe até o purgar/rotacionar (TASK-0058) — head-up do DEV, julgado contra SPEC-0012 §B/contrato §5: nenhum AC nem drill desta task executa backup **depois** do restore (o drill para no `/ready` verde), então a sequência não é exercitada por aqui; o comportamento pertence à semântica de cadeia dos drills incrementais (§5) e ao escopo de rotação/expurgo da TASK-0058 (contrato §7: deleção é operação administrativa, nunca automática por falha). Não é defeito contra a AC-03 como está escrita.

Nenhum BLOCKER. Nenhum MAJOR. Nenhum AC violado.

## O que não ficou coberto (unverified)

- **Suíte Linux COMPLETA — `unverified`, nunca assumida verde.** Provado no Linux (contêiner WSL2, SDK 10.0.403 = pin do CI): build exit 0 + família Backup `36/36` + drills `8/8`. Não rodado: a suíte inteira no Linux (falhas de ambiente pré-existentes conhecidas: `libfontconfig`/Skia, `cmd.exe` S-44 — memória bank `build-run-test.md:42`); instalar `libfontconfig1` exige gate indisponível nesta sessão.
- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest) — `unverified`:** a imagem sdk não traz `pwsh` e instalar exige gate; executei o gêmeo `.sh`/`dotnet test` (o runner Linux disponível) com os mesmos filtros.
- **Endpoint S3 real (MinIO/AWS com `Backup:S3:*` de produção) — `unverified` e fora do escopo:** GAP-52 adjudica o fake S3 em processo com validação SigV4 real para o gate; o endpoint real é por configuração (default aceito pelo humano 2026-11-20). Risco: diferenças de um S3 real (ex.: token de continuação opaco, `Host` com porta) só apareceriam na integração de produção.
- **Sequência "restore → backup de novo" (recusa da cadeia off-site, seção de findings item 3) — não exercitada por nenhum AC desta task** — fica com a TASK-0058 (rotação/expurgo). Risco: baixo para os ACs; a recusa é ruidosa, não silenciosa.
- **Rotas fora das duas de backup (`/api/admin/backup/run`, `/api/admin/backup/status`) — não varridas individualmente para credencial:** `redundancy.score`/`backup/status` de leitura são TASK-0060; as rotas existentes que poderiam ecoar configuração são exatamente as duas testadas, e `Backup:S3:*` só é lido por `BackupS3Options.From` (grep). Risco: baixo.

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0057-s3-offsite` (`90959a8`); Linux: podman com a imagem `mcr.microsoft.com/dotnet/sdk:10.0` já presente.
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
3. Esperado: exit 0, `Aprovado: 633, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`); drills pontuais: mesmo comando com `-TestFilter Category=RestoreDrill` → `Aprovado: 8` (~15 s); família S3: `-TestFilter FullyQualifiedName~S3CompatibleTargetTests` → `Aprovado: 2` (<1 s); Linux: `podman run --rm -v <repo>:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0057.sh` → exit 0 com `LINUX-BUILD-EXIT=0`, `LINUX-BACKUP-EXIT=0`, `LINUX-DRILLS-EXIT=0`.
