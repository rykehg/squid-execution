# QA-0054 — Núcleo do backup cifrado

- task: `docs/sdd/tasks/TASK-0054-nucleo-backup-cifrado.md` (status `AWAITING_QA`, qaAttempt 0/2 → esta é a 1/2)
- runId: `R-20261120-01` · branch `task/TASK-0054-nucleo-backup-cifrado` (commit `28dcab9` sobre develop `1e0cd33`, um commit só)
- critérios testados: a lista `## Acceptance criteria` do arquivo da task, como está (SPEC-0012 §A/§B; contrato `specs/001-squid-mvp/contracts/backup-recovery.md` §1–§3; `specs/001-squid-mvp/data-model.md` §2.10)
- política: ponytail-qa `full` · memory bank: rota `qa-integracao` lida (`index.md`, `build-run-test.md#Local tests without gate`, `contracts.md`; `integrations.md` é rascunho de template) — 3 arquivos
- harness: gate do projeto `docs/sdd/_tools/sdd-test.ps1` (Windows, plataforma primária) + gêmeo `docs/sdd/_tools/sdd-test.sh` com alvo explícito (Linux real: contêiner local `mcr.microsoft.com/dotnet/sdk:10.0` = 10.0.403, o mesmo pin do `ci.yml`, via podman; imagem já presente — nada instalado) + scripts próprios `qa-mutate-task-0054.ps1` e `qa-scope-task-0054.ps1`/`.sh`

## AC → cenário

| AC | Cenário executado | Resultado |
| --- | --- | --- |
| AC-01 | `BackupMigrationTests.Ac01`: `SchemaMigrator.ApplyAsync(SchemaMigrations.Load())` (mecanismo existente) aplica a versão 10 (`schema_version` = `backup`), cria `backup_envelope` e `key_epoch` com as colunas exatas do §2.10 (lista por `pragma_table_info`), `key_epoch NOT NULL` recusado pela PRÓPRIA coluna (`SqliteException` com "NOT NULL"), e a 2ª aplicação não muda nada (linha semeada entre as duas aplicações sobrevive, `schema_version` completo) | PASS — filtro `FullyQualifiedName~Squid.Tests.Backup` exit 0 (1/1 do arquivo) + mutação T1 (remove o `NOT NULL`) → vermelho **exatamente** o Ac01, exit 1 |
| AC-02 | `LocalDiskTargetTests.Ac02` (as 5 operações do contrato §1 sobre o volume: Put grava `.bin`+`.json`, Get devolve os bytes, Exists, List ordenado por `seqTo` e filtrado por `fromSeq` — `20 sim / 21 não`, Delete idempotente) + `BackupWiringTests.Ac02` (`SquidServiceHost.Build` com `--Backup:Local:Path=` resolve `IBackupTarget` → `LocalDiskTarget` e a escrita cai no volume CONFIGURADO) | PASS — filtro exit 0 (2/2) + mutações T2 (ignora `fromSeq`) e T2w (chave de config ignorada) → vermelhos os dois cenários próprios, exit 1 |
| AC-03 | `BackupPipelineTests.Ac03`: após um run real, varredura de TODOS os bytes gravados (volume de teste + arquivo de registry) atrás do sentinel de plaintext `PLAINTEXT-SENTINEL-TASK0054-*` e dos bytes da `RK` — nenhum encontrado; o header do destino contém `wrappedDek` (caixa embrulhada, nunca chave em claro) e não contém o sentinel | PASS — filtro exit 0 + mutação T3 (pipeline envia o JSON serializado em claro) → vermelho **o Ac03** (e, por arrasto, Ac05/Ac04_ac06), exit 1 |
| AC-04 | `BackupPipelineTests` ×3: (a) ordem `put → get` com `contentHash` já existente no `Put` (`FlippingTarget.Calls = [put, get]`, `HashSeenAtPut` começa com `sha256:`); (b) byte trocado entre envio e releitura → `Corrupt` com `verified_at` NULL no registro e a linha conta `COUNT=1`; releitura que não sobrevive → exceção e registro VAZIO; (c) `contentHash` = `sha256:` + SHA-256 dos bytes `.bin` gravados e `verifiedAt` presente no registro | PASS — filtro exit 0 (3/3) + mutações T4a (hash do plaintext antes da cifra) e T4b (comparação da releitura sempre-verdadeira) → vermelhos os cenários próprios, exit 1 |
| AC-05 | `BackupKeyTests` ×2 (`HKDF(RK,"fed/backup-kek")`: rótulo exato, caixa sem byte cru da DK, só a mesma RK abre, RK errada falha como `CryptographicException` sem vazar material) + `BackupPipelineTests.Ac05` (dois runs do MESMO estado → `.bin` diferentes; ambos decifram com a MESMA RK pelo fluxo restore (header → unwrap → ChaCha20 → gunzip) e o payload volta idêntico) | PASS — filtro exit 0 (3/3) + mutação T5 (embrulho da DK errado na header) → vermelho **o Ac05** (CryptographicException no decrypt), exit 1 |
| AC-06 | `BackupPipelineTests.Ac04_ac06`: SELECT com aliases das colunas §2.10 da linha persistida comparadas campo a campo com o envelope devolvido (`Id, KeyEpoch, SeqFrom, SeqTo, ContentHash, SizeBytes, TargetId, CreatedAt, VerifiedAt`) + `contentHash` confere com os bytes do disco | PASS — filtro exit 0 + mutação T6 (`@KeyEpoch` zerado no INSERT) → vermelho **o Ac04_ac06**, exit 1 |
| AC-07 | gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0 (607/607, duas execuções: abertura e fechamento desta QA); escopo lido do git: 15 arquivos do commit TODOS dentro dos `touches`, 0 linhas `<PackageReference>`/`<PackageVersion>` adicionadas no diff `develop...HEAD`, `backend/Directory.Packages.props` intocado, árvore de trabalho só com WIP do ODEV em `docs/sdd/` (registrado no baseline antes), `git status -- backend` vazio, resíduo de mutação 0, blobs 5/5 idênticos ao HEAD, exatamente 5 arquivos `.cs`/`.sql` com mtime novo | PASS — gate exit 0 (log `20261121-120707-test-gate.log`, cabeçalho `exit: 0`) + `qa-scope-task-0054.ps1` exit 0 e gêmeo `.sh` no Linux exit 0 |

Nenhum AC ficou `NOT-COVERED`.

## Execuções (comando → exit → cauda)

Todas executadas nesta sessão, cwd `C:\dev\squid-squad-net\squid-execution`.

1. `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` → exit 0 → `Aprovado! – Com falha: 0, Aprovado: 607, Ignorado: 0, Total: 607, Duração: 8 m 52 s` (gate de abertura; log `docs/sdd/runs/test-runs/20261121-120707-test-gate.log`, cabeçalho `exit: 0` — regra S-22: classifiquei sempre pelo log gravado, nunca pelo console)
2. `powershell ... -File docs/sdd/qa/qa-mutate-task-0054.ps1 -VerifyOnly` → exit 0 → `VERIFY total=8 bad=0` (os 8 find strings batem exatamente 1× cada; ASCII limpo — `docs/sdd/qa/QA-0054-verify.log`)
3. 1ª tentativa de `qa-mutate-task-0054.ps1` → exit 1 → abortou no primeiro `Blob()` porque `git` não estava no PATH desta sessão; **nenhuma mutação aplicada** (confirmei `git status -- backend` vazio em seguida); reexecutada com PATH corrigido
4. `powershell ... -File docs/sdd/qa/qa-mutate-task-0054.ps1` → exit 0 → 8 ciclos `EXPECT-OK`, `RESTORE-OK hash=PRE` em todos, `CLEANUP default-backups-folder removed`, fecho `GREEN wrapper=0 logExit=0 aprovado=10 falha=0`, `SUMMARY cycles=8 failed=0 residue-backend=0 elapsed-s=178` (`docs/sdd/qa/QA-0054-mutations.log`; logs de cada ciclo em `docs/sdd/runs/test-runs/20261121-1222*-test-filter.log` … `20261121-122458-test-filter.log`)
5. `powershell ... -File docs/sdd/qa/qa-scope-task-0054.ps1` (1ª execução) → exit 1 → falso positivo: o `git status` cita com aspas o path acentuado `SPEC-0012-backup-rotação-p07.md` (WIP do ODEV) e o prefixo `docs/sdd/` não casou; corrigido no próprio script (`core.quotepath=false`) — nova execução no item 6
6. `powershell ... -File docs/sdd/qa/qa-scope-task-0054.ps1` → exit 0 → `SUMMARY head=28dcab9 commit-files=15 pkg-props=0 pkg-added=0 qa-artifacts=… wip-odev=10 backend-entries=0 residue=0 blob=5/5 mtime=5 fails=0` (`docs/sdd/qa/QA-0054-scope.log`)
7. `bash docs/sdd/qa/qa-scope-task-0054.sh` (Linux real, WSL podman-machine, git do próprio Linux) → exit 0 → mesmo resumo, `fails=0` (`docs/sdd/qa/QA-0054-scope-sh.log`)
8. `bash docs/sdd/_tools/sdd-test.sh test` (Linux, sem target) → exit 1 → `sdd-test: no *.sln found within depth 3` seguido de `dotnet: command not found` e `test: exit 127 (real test failure)` — o `exit 1` do `find_sln` não sai do `$( )` e o script continua (finding 1, pré-existente S-11) — `docs/sdd/qa/QA-0054-sh-default-probe.log`
9. `podman run --rm -v …:/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 bash docs/sdd/qa/qa-linux-task-0054.sh` (gêmeo `.sh` com alvo explícito, como o `ci.yml` faz no Ubuntu) → build exit 0, `test` exit 1 → `Failed: 51, Passed: 556, Total: 607` (`docs/sdd/qa/QA-0054-linux-build.log`, `docs/sdd/qa/QA-0054-linux-test.log`)
10. `podman run … bash docs/sdd/qa/qa-linux2-task-0054.sh` → saída COMPLETA da suíte classificada (o `.sh` só descarta `tail -6`, S-12) em `docs/sdd/runs/test-runs/20261121-154231-test-linux-full.log` → exit 1 com os mesmos 51; e **filtro da família Backup no Linux → exit 0, `Passed: 10, Total: 10`** (`docs/sdd/qa/QA-0054-linux-backup-filter.log`)
11. gate de fechamento (após todas as execuções, árvore restaurada e contêiner já encerrado): `powershell … sdd-test.ps1 test` → exit 0 → `Aprovado: 607, Com falha: 0, Duração: 8 m 48 s` (log `docs/sdd/runs/test-runs/20261121-130327-test-gate.log`, cabeçalho `exit: 0`)

## Prova de que os testes podem falhar

O driver `docs/sdd/qa/qa-mutate-task-0054.ps1` aplicou 8 mutações — UM literal de código de produto por ciclo, sempre no arquivo de um AC —, rodou o filtro Backup, classificou o vermelho PELO LOG (`exit:` do cabeçalho) e restaurou com `git checkout` comparando o blob PRE vs POST (byte-exact) + bump de mtime (S-23):

| Ciclo | AC | Literal quebrado | Vermelho esperado (saiu) |
| --- | --- | --- | --- |
| T1 | AC-01 | `0010_backup.sql`: `key_epoch INTEGER NOT NULL` → `INTEGER` | Ac01 ✓ (1 falha) |
| T2 | AC-02 | `LocalDiskTarget.cs`: `SeqTo >= fromSeq` → `>= 0` | Ac02 (List) ✓ (1 falha) |
| T2w | AC-02 | `SquidServiceHost.cs`: chave `Backup:Local:Path` ignorada | Ac02 (wiring) ✓ (1 falha) |
| T3 | AC-03 | `BackupPipeline.cs`: envia `payload` (claro) em vez de `blob` | Ac03 ✓ (+ Ac05, Ac04_ac06 por arrasto — 3 falhas) |
| T4a | AC-04 | `contentHash` = SHA-256 do `compressed` (pré-cifra) | Ac04_ac06 ✓ (1 falha) |
| T4b | AC-04 | comparação da releitura vira sempre-verdadeira | Ac04 divergência ✓ (1 falha) |
| T5 | AC-05 | embrulho da DK errado na header (`new byte[32]`) | Ac05 ✓ (1 falha; wrapper imprimiu exit 3 pelo achado 2, LOG diz `exit: 1` — regra S-22) |
| T6 | AC-06 | `SqliteBackupEnvelopeStore.cs`: `@KeyEpoch` → `0` | Ac04_ac06 ✓ (1 falha) |

Fecho: `GREEN … aprovado=10 falha=0` sobre a árvore restaurada, `residue-backend=0`, blobs 5/5 idênticos ao HEAD e os 5 arquivos mutados como único mtime novo. Os cenários, portanto, não são decoração: cada um vermelhou quando o comportamento quebrou.

## Classificação das 51 falhas Linux (pré-existente × desta task)

- **0 (zero) falhas da família `Squid.Tests.Backup`** — os 10 testes desta task passam no Linux (filtro exit 0, execução 10).
- 13 `DependencyRuleTests` (`Category=BuildProof`) → `Win32Exception … cmd.exe` (`BuildProbe.cs:136`): **S-44** (BACKLOG.md:78), Windows-only, pré-existente; inclui a teoria `Service→Worker` que no Windows está verde no gate.
- 29 diretas `DllNotFoundException … libfontconfig.so.1` (SkiaSharp na imagem do contêiner): mesma classe pré-existente da QA-0053 finding 1 (backlog I-10/S-44); instalar `libfontconfig1` exige gate de aprovação, indisponível nesta sessão.
- 9 `Assert` a jusante (upload/quota/thread que cruzam o `IMediaProcessor` → 500): **todas as 9 já constavam no log Linux pré-task `20261114-142204-test-linux-full.log`** (roda em develop antes do branch).
- Delta de 1 × aquele log: `TwoSiloFacetTests.Ac01…media` — adicionado por **TASK-0047** (`b953771`, confirmado ancestral de develop, intocado por `28dcab9`), falha no fixture Skia `MediaHost.JpegWithExif` pelo mesmo `libfontconfig` ausente.
- Conclusão: as 51 são ambiente/pré-existentes; nenhuma é da product change desta task. A suíte **verde** no Linux continua `unverified` (não assumida verde) — ver seção própria.

## Findings

1. `docs/sdd/_tools/sdd-test.sh:21` e `:64` — baixo — **pré-existente** (lacuna **S-11**, `BACKLOG.md:46`; o arquivo não entra em `28dcab9`): sem argumento, o `exit 1` do `find_sln` acontece dentro do `$( )` e NÃO para o script — ele segue com `sln` vazio, imprime `test: `, tenta `dotnet test ""` → `dotnet: command not found` → rotula `test: exit 127 (real test failure)` e sai 1. Um erro de uso vira um suposto vermelho de produto. Reproduzido hoje (`QA-0054-sh-default-probe.log`). Não bloqueia: o CI roda o `.ps1` sob pwsh (`ci.yml:39-42`) e com alvo explícito o gêmeo funciona (execuções 9–10).
2. `docs/sdd/_tools/sdd-test.ps1:80-83` — baixo — **pré-existente** (S-22, `docs/memory-bank/build-run-test.md:54`), gatilho NOVO observado: a regex solta `handle` casa `SafeKeyHandle` em qualquer stack de criptografia e o sumário pt-BR `Com falha!` não está na alternância `Passed!|Failed!|Aprovado!|Falhou!` → um vermelho REAL é classificado como ambiente (exit 3). Aconteceu na mutação T5 (o driver registra `wrapper=3 logExit=1`); a regra do cabeçalho `exit:` do log gravado salvou a classificação correta. Sugestão já prevista no item de correção do próprio S-22.
3. `backend/src/Squid.Worker/Backup/LocalDiskTarget.cs:44` + `backend/src/Squid.Worker/Backup/BackupEnvelope.cs:57` — baixo — **produto desta task**: o `.json` do destino é serializado NO `PutAsync`, antes da releitura calcular `Status`/`VerifiedAt` (`BackupPipeline.cs:104-121`), então a header do destino guarda para sempre `status: "Pending"` e `verifiedAt: null`, mesmo de um backup verificado; a verdade fica só no registro (`backup_envelope.verified_at`, correto). Nenhum AC exige o contrário (o §2.10 é a entidade registro), mas um consumidor que ler a header do destino em vez do registro (candidato natural: TASK-0055) interpreta falso. Não bloqueante.
4. `backend/src/Squid.Worker/Backup/LocalDiskTarget.cs:44-47` e `:72-74` — baixo — **produto desta task**: a escrita da header com `File.WriteAllTextAsync` não é atômica; um crash no meio deixa um `.json` rasgado e o `ListAsync` LANÇA `InvalidOperationException` (`LocalDiskTarget.cs:74`) em vez de pulá-lo — a descoberta do que já foi enviado (`contrato §1`) quebra até alguém remover o arquivo à mão. A própria classe promete (doc `:8-9`) que uma backup interrompida fica invisível ao `ListAsync`; isso vale para o `.bin` (escrito antes), não para um header rasgado. Nenhum AC cobre essa borda; janela pequena, volume local. Não bloqueante.

Nenhum BLOCKER. Nenhum defeito de produto que viole um AC.

## O que não ficou coberto (unverified)

- **Suíte Linux COMPLETA verde — `unverified`, nunca assumida verde.** O que está provado no Linux: build exit 0 (SDK 10.0.403 = pin do CI), a família `Squid.Tests.Backup` 10/10 exit 0 e o escopo (`.sh`) exit 0. O que não: as 51 falhas acima continuam vermelhas por ambiente — instalar `libfontconfig1` no contêiner exige gate indisponível nesta sessão, e as 13 de BuildProof dependem do `cmd.exe` (S-44). Risco: um defeito real só-Linux fora da família Backup ficaria escondido atrás desse ruído (a família Backup em si está exercitada nos dois sistemas).
- **Caminho do CI (`pwsh sdd-test.ps1 test` em ubuntu-latest) — `unverified`:** a imagem sdk não traz `pwsh` e instalar exige gate; executei o gêmeo `.sh` (o runner Linux disponível) com o mesmo alvo e as mesmas flags.
- **Cenário de distinção de ciphertexts do AC-05 por mutação** — a asserção `Assert.False(one.SequenceEqual(two))` roda em todo ciclo verde, mas forçá-la a falhar exige MUTAR DOIS literais (nonce E DK) no mesmo ciclo; fiz a mutação do par decrypt (T5), que prova que o teste em si vermelha sob demanda. Risco residual: baixo — nonce e DK são `RandomNumberGenerator` por run, inspecionados no código (`BackupPipeline.cs:70,74`).
- **Restauração/rotacao/drills/S3** — fora do escopo desta task (GAP-55, TASK-0055/0056/0057/0058).

## Reproduzir

1. Pré-requisito: Windows com .NET 10 SDK, checkout da branch `task/TASK-0054-nucleo-backup-cifrado` (`28dcab9`).
2. Comando: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
3. Esperado: exit 0, `Aprovado: 607, Com falha: 0` (log novo em `docs/sdd/runs/test-runs/`); filtro pontual: mesmo comando com `-TestFilter FullyQualifiedName~Squid.Tests.Backup` → `Aprovado: 10`; prova can-fail: `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0054.ps1` → exit 0 com 8 `EXPECT-OK` e fecho `GREEN … falha=0`; escopo: `… qa-scope-task-0054.ps1` → exit 0 (Linux: `bash docs/sdd/qa/qa-scope-task-0054.sh` → exit 0).
