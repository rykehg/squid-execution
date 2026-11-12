<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261112-100321-test-gate.log)

# QA-0039 — Anexo em mensagem reusando `/api/media` (TASK-0039)

- runId: R-20261111-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0010 §G + GAP-35 (`SPEC-0010-mensageria-p05.md:122-129`, `:188`) + AC-01..AC-05 do task file + data-model §2.6 (`data-model.md:171-185`) + contrato http-api §5 (`http-api.md:96-113`) e §6 (`http-api.md:125-154`) + GAP-25 original (regra em `MediaEndpoints.cs:17-21` e testes `MediaVisibilityTests`/`QaTask0027Tests.Ac04`) + memória banc rota `qa-integracao` (`index.md:50` → `build-run-test.md#Local tests without gate`)
- commit sob verificação: `3d8a1d9` em `task/TASK-0039-anexo-em-mensagem` (11 arquivos = 7 produto incl. migration 0007 + 4 teste, todos nas `touches`); baseline DEV `468/468 → 473/473`
- verdict: **PASSED**

## AC → cenário (5 ACs; todos executados nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | `POST .../messages` aceita `contentHashes` (≤10), o envelope de leitura devolve os anexos campo a campo, hash inexistente → `400`, acima de 10 → `400` e **nenhuma recusa consome `seq`** | `ThreadAttachmentRoutesTests.Send_carries_contentHashes_and_the_read_envelope_returns_the_Attachment_shape` verde no baseline `20261112-093821-test-filter.log` (**exit 0, 41/41**), no verde com o teste da QA `094107` (**42/42**), nos dois GREEN do driver (`095324`, `095624`) e no gate `100321` (**473/473**). O cap com hash REAL — que o teste do DEV não distingue (ele usa hex fabricado, que o 400 de existência recusaria também) — é o Fact da QA: `QaTask0039AttachmentCapTests.Eleven_references_of_one_real_hash_are_400_and_ten_land_as_ten_attachments` (11× o mesmo upload → 400, 10× → 200 + seq 1 + lista de 10 na leitura). Can-fail: **M1** (coluna 0007 sempre NULL → `094448`, 3 vermelhas: envelope, roundtrip e o próprio AC-04, porque a visibilidade da mídia nasce da referência), **M2** (`alt` fora do envelope → `095543`, `Expected: "foto da mensagem"`), **M3** (cap TRUNCADO para 10 → `094538`, **só** o Fact da QA vermelho, teste do DEV verde), **M4** (hash inexistente ignorado → `094617`, `Expected: BadRequest`), **M5** (validação movida para DEPOIS da CAS com `commit` na recusa → `094700`, 2 vermelhas, `Expected: 1` = o seq virou 2), **M13** (contador `attachment_count` VÁLIDO na 0007 → `095239`, só as 3 migration tests, rotas verdes) | PASS |
| AC-02 | `Image`/`Video`/`Gif` sem `Alt` → `400` **na publicação da mensagem** (upload já bloqueia; a store direta prova que o caminho do envio não contorna) | `ThreadAttachmentRoutesTests.An_image_without_alt_is_refused_only_after_the_428_and_the_422` verde nos mesmos quatro momentos (registro semeado direto na `IMediaStore` sem Alt → 400; escada 428 → 400 → 422; nenhuma recusa grava linha nem número). **E as duas ordens da escada estão dentro deste mesmo teste**: 428 antes do 400 (sem aceite + anexo ruim) e 422 antes do 400 (`replyToId` de outra thread + anexo ruim). Can-fail: **M6** (regra §2.6 desligada na publicação → `094739`, `Expected: BadRequest`), **M10** (gate 428 pulado quando há `contentHashes` → `095034`, `Expected: PreconditionRequired`), **M11** (gate 422 pulado quando há `contentHashes` → `095114`, `Expected: UnprocessableEntity`) | PASS |
| AC-03 | **Cota intocada**: enviar mensagem com anexo NÃO cria/cobra linha nova no `quota_ledger` (mesma linha byte-idêntica) e o `507 quota_exceeded` continua vindo do `POST /api/media` | `ThreadAttachmentQuotaTests.A_send_with_hashes_creates_no_ledger_charge_and_the_507_stays_in_the_upload_route` verde nos mesmos quatro momentos (hash do ledger e `COUNT(*)` iguais antes/depois do envio e depois do 507; o 507 é do upload de 9000 bytes; a mensagem com o MESMO hash segue aceita depois da recusa). **O money shot**: can-fail **M7** (o ENVIO passa a somar `used_bytes` no `quota_ledger` dentro da própria transação do append → `094819`, **41/1, só a suite de cota** — a prova byte-idêntica morre exatamente onde a AC diz), **M12** (reserva da cota desligada em `POST /api/media` → `095157`, **41/1**, o 507 some e o upload acima da cota passa) | PASS |
| AC-04 | GAP-25 ampliada: participante de thread cuja MENSAGEM referencia o hash vê `GET /api/media/{hash}`; outsider (participante de OUTRA thread) → `404` idêntico byte-a-byte; uploader continua vendo; e o ramo POST da regra fica INTACTO | `ThreadAttachmentRoutesTests.GetMedia_serves_the_participant_of_a_thread_referencing_the_hash_and_hides_it_from_everyone_else` verde nos mesmos quatro momentos (par vê 404 ANTES da referência e 200 depois; dono vê; estrangeiro leva o MESMO corpo do hash inexistente; parede da conversa não racha). **Post intacto provado por mutação no MESMO run**: **M8** (ramo da mensagem da `VisibleAsync` removido, filtro Threads+Media → `094915`, **aprovado=68 falha=1** — os 27 testes de Media (piso GAP-25 do post, `MediaVisibilityTests` e `QaTask0027Tests.Ac04`) seguem VERDES enquanto só a mensagem de teste cai, `Expected: OK`), **M9** (sonda de cadeira fora da ampliação → `094955`, `Expected: NotFound` = o estrangeiro passou a ver) | PASS |
| AC-05 | `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora das `touches` | Gate `20261112-100321-test-gate.log` → **`exit: 0` DENTRO do cabeçalho e `Com falha: 0, Aprovado: 473, Total: 473`** (S-22; 5m04s; árvore igual à do DEV, teste temporário da QA removido). Escopo (`QA-0039-scope.log`): `git status -- backend` **0 entradas**; blob-identity `git hash-object` = `git rev-parse HEAD:` em **4/4** arquivos mutados; mtime desde a baseline `09:36` = **exatamente esses 4**; resíduo `QA-MUTATION` = 0; backups TEMP `qa0039-*` = 0; `fora-dos-touches` = **0** (só task file + artefatos `docs/sdd/qa/`) | PASS |

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 3d8a1d9) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | validação de `contentHashes` roda **DENTRO** da transação do append, na escada 404 → 428 → 422 → 400, antes da CAS; "recusa nenhuma consome seq nem escreve linha" | **CONFIRMADA em execução.** Os dois últimos degraus da escada são mutantes próprios (**M10** → `Expected: PreconditionRequired`; **M11** → `Expected: UnprocessableEntity`), o 400 do anexo é **M3/M4/M6**, e "não consome seq" é **M5**: com a validação depois da CAS e `commit` na recusa, o envio limpo nasce no `seq 2` e o teste cai em `Expected: 1`. Código: `SqliteThreadStore.cs:464-633` (leitura) | ciclos M3/M4/M5/M6/M10/M11 (logs `094538`…`095114`) |
| 2 | risco do DEV: upload é content-addressed, **primeira gravação vence para `alt`** — dois envios das mesmas bytes dividem o alt original | **CONFIRMADO POR LEITURA, não por execução.** `SqliteMediaStore.SaveAsync` escreve o blob com `CreateNew` e deixa `InconsistentStateException` passar com o comentário `shortcut: the FIRST upload's record wins` (`SqliteMediaStore.cs:76-92`) — o registro (alt/uploader) do primeiro upload é o que sobrevive. Nenhuma AC-01..05 pede esse cenário e ele não é observável pela rota (a resposta do upload não devolve `alt`), então não executei: fica em "Não coberto" com o risco declarado | `SqliteMediaStore.cs:76-92` |
| 3 | upload, magic bytes, EXIF, Alt e cota 507 "permanecem os da P-04; nada duplicado" | **CONFIRMADA.** Nenhum mutante do envio mexe no pipeline do upload (M7/M12 tocam o ledger/507 e são pegos EXATAMENTE pela suite de cota); o envio só referencia hashes — a resposta `200 + seq` do envio nunca passa por `quota`, `IMediaProcessor` ou `MediaOptions` (`ThreadEndpoints.cs:320-376` não injeta nada disso) | ciclos M7/M12 (`094819`, `095157`) + leitura |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `git rev-parse --abbrev-ref HEAD` + `git status --porcelain -uall` + `wsl -e bash -c "command -v dotnet"` (pwsh) | 0 | `docs/sdd/qa/QA-0039-git-baseline.txt` — HEAD `3d8a1d9`, branch `task/TASK-0039-anexo-em-mensagem`, só o task file, **dotnet ausente no WSL** |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Threads` (baseline) | 0 | `20261112-093821-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 41 / Total 41 |
| 3 | `... sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Media` (baseline do ramo POST da GAP-25) | 0 | `20261112-093902-test-filter.log` — `exit: 0`, 27/27 |
| 4 | `Copy-Item` do teste temporário para `backend/tests/Squid.Tests/Threads/` + mesmo filtro Threads (verde COM o Fact da QA) | 0 | `20261112-094107-test-filter.log` — `exit: 0`, 42/42 (41 DEV + 1 QA) |
| 5 | `powershell ... docs/sdd/qa/qa-mutate-task-0039.ps1 -VerifyOnly` (2×: antes do run cheio e depois da correção do M2) | 0 | stdout `VERIFY total=13 bad=0` + `BOM OK (UTF-8 BOM presente)` — os 14 padrões batem `hits=1` nas fontes pristinas; a 2ª execução está gravada em `QA-0039-verify.log` |
| 6 | `powershell ... docs/sdd/qa/qa-mutate-task-0039.ps1` (execução cheia: 13 ciclos + GREEN) | 1 | `QA-0039-mutations.log` — 12 vermelhos classificados NO LOG (S-22), **1 CLASSIFICACAO-ERRADA (M2 primitivo, ver finding 3)**, 13× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=42 falha=0` (`095324`), `SUMMARY cycles=13 failed=1 residue=0 temp-backups=0` |
| 7 | `powershell ... docs/sdd/qa/qa-mutate-task-0039.ps1 -Only M2` (mutante corrigido: `alt = (string?)null`) | 0 | ledger: `M2 RUN ... aprovado=41 falha=1` (`095543`, `Expected: "foto da mensagem"`), `GREEN 42/42` (`095624`), `SUMMARY cycles=1 failed=0` |
| 8 | `Remove-Item backend/tests/Squid.Tests/Threads/QaTask0039AttachmentCapTests.cs` (fonte copiada antes para `.cs.txt`) + `git status --porcelain -uall -- backend` | 0 | teste temporário fora da árvore (fonte preservada em `docs/sdd/qa/QaTask0039AttachmentCapTests.cs.txt`); git backend = **0 entradas** |
| 9 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test` (gate AC-05, árvore igual à do DEV) | 0 | `20261112-100321-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 473, Total: 473`, 5m04s |
| 10 | varredura de escopo (pwsh): `git status`, `git hash-object` vs `git rev-parse HEAD:`, mtime desde a baseline, resíduo, fora-dos-touches | 0 | `docs/sdd/qa/QA-0039-scope.log` — git-backend 0, blob 4/4 `match=True`, `mtime-count=4`, `residue=0`, `fora-dos-touches=0`, `temp-backups=0` |
| 11 | `powershell ... docs/sdd/qa/qa-check-task-0039.ps1` | 0 | `docs/sdd/qa/QA-0039-check.log` — SUMMARY total/fails (SDD-STATE `check` incluso) |
| 12 | `wsl -e bash docs/sdd/qa/qa-check-task-0039.sh` (gêmeo Linux, parte estática) | 0 | `docs/sdd/qa/QA-0039-check-sh.log` — SUMMARY total/fails (o `sdd-state` é ferramenta Windows, fica só no ps1) |

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos `touches` (`Squid.Api/Threads/**`, `Squid.Api/Media/**`, `Squid.Service/**`, `Squid.Persistence/schema/**`) — nenhum edit fora do escopo, e os 4 arquivos finais são byte-iguais ao `HEAD` (blob-identity 4/4). Filtro padrão: `FullyQualifiedName~Squid.Tests.Threads` (42 testes com o Fact da QA); **M8 usa Threads+Media** (69) para provar o ramo POST intacto no MESMO run.

| Ciclo | Arquivo(s) | O que prova | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| M1 | `Squid.Service/SqliteThreadStore.cs` | AC-01: a lista só é real se a coluna 0007 persistir — o envio responde da memória, a leitura é a prova | `Send_carries...` + `QaTask0039AttachmentCap...` + `GetMedia_serves...` (`094448`, 3 — a visibilidade da mídia nasce da referência gravada) |
| M2 | `Squid.Api/Threads/MessageView.cs` | AC-01: os campos de §2.6 no envelope — `alt` fora e o leitor do contrato cai | `Send_carries...` — `RED-DETAIL Expected: "foto da mensagem"` (`095543`, 1) |
| M3 | `SqliteThreadStore.cs` | AC-01: o teto de 10 é RECUSA, não truncamento — **11 cópias de UM hash real** (Fact da QA; o teste do DEV usa hex fabricado e não distingue) | **só** `QaTask0039AttachmentCapTests...` (`094538`, 1, `Expected: BadRequest` → 200) |
| M4 | `SqliteThreadStore.cs` | AC-01: hash inexistente → 400 (o guarda do `FindAsync` é o próprio degrau) | `Send_carries...` (`094617`, 1, `Expected: BadRequest`) |
| M5 | `SqliteThreadStore.cs` (2 edits: bloco removido e re-inserido DEPOIS da CAS com `commit` na recusa) | AC-01: "nenhuma recusa consome seq" — a recusa grava o número e o envio limpo seguinte nasce no 2 | `Send_carries...` + `QaTask0039AttachmentCap...` (`094700`, 2, `RED-DETAIL Expected: 1`) |
| M6 | `SqliteThreadStore.cs` | AC-02: a regra Alt é da PUBLICAÇÃO — o envio não contorna pela store direta | `An_image_without_alt_is_refused_only_after_the_428_and_the_422` (`094739`, 1, `Expected: BadRequest`) |
| M7 | `SqliteThreadStore.cs` | AC-03 (**money shot**): o ENVIO passa a cobrar `quota_ledger` dentro da transação | `A_send_with_hashes_creates_no_ledger_charge...` (`094819`, **41/1, só a cota**) |
| M8 | `Squid.Api/Media/MediaEndpoints.cs` (filtro Threads+Media) | AC-04: o ramo MENSAGEM da escada GAP-25 é novo e é dele que nasce a visibilidade; o ramo POST fica intacto | `GetMedia_serves...` (`094915`, **aprovado=68 falha=1** — 27 Media verdes no MESMO run) |
| M9 | `SqliteThreadStore.cs` | AC-04: a cadeira (FR-031) também amarra a ampliação — sem ela o estrangeiro vê | `GetMedia_serves...` (`094955`, 1, `Expected: NotFound`) |
| M10 | `SqliteThreadStore.cs` | escada: **428 antes do 400** (sem aceite + anexo ruim → 428) | `An_image_without_alt...` (`095034`, 1, `Expected: PreconditionRequired`) |
| M11 | `SqliteThreadStore.cs` | escada: **422 antes do 400** (`replyToId` de outra thread + anexo ruim → 422) | `An_image_without_alt...` (`095114`, 1, `Expected: UnprocessableEntity`) |
| M12 | `Squid.Api/Media/MediaEndpoints.cs` | AC-03/meio: o **507 vem do upload** — reservar sempre é desligar o degrau | `A_send_with_hashes_creates_no_ledger_charge...` (`095157`, **41/1**) |
| M13 | `schema/0007_message_attachments.sql` | AC-01/escopo: a coluna nova é TEXT, **nunca uma contagem** — migration com lista `pragma_table_info` exata | `ThreadMigrationTests.The_0005/0006/0007...` (`095239`, **39/3**, rotas verdes) |

14× `PRE` registrado (M5 é 2 edits), 14× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 2× GREEN `42/42 exit 0` (`095324`, `095624`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0039-*` = 0, `-VerifyOnly` `bad=0` + `BOM OK` (`QA-0039-verify.log`).

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (BOM) | driver (esta QA) | `qa-mutate-task-0039.ps1` nasceu **sem UTF-8 BOM** (6ª ocorrência do defecto das QA-0034..0038) e a edição de correção do M2 **derrubou o BOM de novo** — BOM re-posto antes de qualquer execução e reconferido após CADA edição, agora em três lugares: `-VerifyOnly` (`BOM OK`), `QA-0039-verify.log` e os dois checkers (`DRIVER-BOM`) | comando 5 + `DRIVER-BOM` |
| 2 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → `dotnet-missing` (**sondado ao vivo NESTA sessão**); instalação exige gate humano — mesmo achado das QA-0033..0038 | comando 1 + linha do Log |
| 3 | NOTA (auto-catch do mutante, S-22) | `qa-mutate-task-0039.ps1` ciclo M2 | o primeiro mutante do envelope nasceu **inválido na COMPILAÇÃO**: `alt = null` num tipo anônimo é `CS0828` ("não é possível atribuir `<nulo>`"). O run do ciclo morreu com `aprovado=0 falha=0` e o **próprio driver** o classificou (`CLASSIFICACAO-ERRADA`, nunca um vermelho de produto) — corrigido para `alt = (string?)null`, `-VerifyOnly` re-verificado e o ciclo re-executado com `-Only M2`: agora a prova é cirúrgica (1 vermelha, `Expected: "foto da mensagem"`). O ciclo inválido NÃO conta como prova e fica registrado no ledger | ciclo M2 (`094458` → `095543`) |
| 4 | NOTA (alcance do mutante) | `schema/0007_message_attachments.sql` | somar uma coluna de contagem VÁLIDA à família 0007 derrubou **3 testes** (`The_0005`, `The_0006` e `The_0007` — todas pinam a lista exata de `pragma_table_info` de `message`), com as rotas verdes (INSERT/SELECT nomeiam colunas): a defesa da "nunca fonte/sem contagem" é estrutural em três famílias, não acidental de um teste | ciclo M13 (`095239`, 39/3) |
| 5 | NOTA (alcance do mutante, código) | `SqliteThreadStore.cs` persistência x `MediaEndpoints.cs` visibilidade | desligar a escrita da coluna 0007 (M1) derrubou TAMBÉM o teste de visibilidade AC-04: `GET /api/media/{hash}` do participante depende da referência GRAVADA — é a prova cruzada de que a ampliação GAP-25 é lida da coluna da mensagem (`json_each`), não de uma inferência de participação | ciclo M1 (`094448`, 3) |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → `dotnet-missing`; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 12.
- **Primeira gravação vence para `alt`** (risco levantado pelo DEV): confirmado **por leitura** (`SqliteMediaStore.cs:76-92`, `shortcut` anotado), não por execução — exigiria dois uploads dos mesmos bytes com `alt` diferentes e observar o `alt` pelo envelope de uma mensagem que referencia o hash. Risco: baixo (o próprio `shortcut` documenta a decisão e nenhuma AC cobra o cenário).
- **`kind: Link` em anexo de mensagem** (§2.6 "Link não tem blob"): o caminho do envio só resolve hashes de upload (blob), então `Link` não nasce de `contentHashes` — nenhum teste envia uma mídia que sinalize `Link`. Risco: baixo (a invariante que importa aqui é o Alt).
- **`GET /api/media/{hash}/thumb` com a ampliação da mensagem**: a rota usa o MESMO `VisibleAsync` que M8/M9 mutam, mas nenhum teste desta QA chama `/thumb` com um participante de thread — a prova é de leitura (função compartilhada) + os ciclos no ladder. Risco: baixo.
- **Corrida de dois envios com anexo ao mesmo tempo** (`BEGIN IMMEDIATE` + CAS): os testes são de cliente único; a ordem das escadas e a atomicidade recusa→rollback estão provadas sequencialmente (M5 + verdes), a corrida em si não foi executada (mesma limitação das QA-0033..0038).

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 473, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=13 bad=0 + BOM OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0039.ps1     # run fresco: exit 0, SUMMARY cycles=13 failed=0 (13 vermelhos de propósito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0039.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0039.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0039.ps1` | driver de mutação can-fail (13 ciclos, `-VerifyOnly` com checagem de BOM, S-16/S-22/S-23 embutidos, ciclo M2 corrigido documentado no próprio código) |
| `docs/sdd/qa/QA-0039-mutations.log` | ledger: PRE, vermelhos no log, restore, GREEN, SUMMARY (full + re-run do M2) |
| `docs/sdd/qa/QaTask0039AttachmentCapTests.cs.txt` | fonte do teste temporário da QA (cap ≤10 com hash REAL) — **executado e REMOVIDO** da árvore após os ciclos; prova viva nos logs `094107` (verde 42/42), `094538` (M3) e `094700` (M5) |
| `docs/sdd/qa/QA-0039-verify.log` | `-VerifyOnly` gravado (14 padrões `hits=1` + `BOM OK` + `VERIFY total=13 bad=0`) |
| `docs/sdd/qa/qa-check-task-0039.ps1` / `.sh` | checkers de evidência (estáticos; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0039-check.log` / `QA-0039-check-sh.log` | execuções dos checkers |
| `docs/sdd/qa/QA-0039-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0039-scope.log` | varredura final de escopo (gate, git, blob-identity, mtime, resíduo, fora-dos-touches) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina, instalacao exige gate humano (pre-existente, sondado ao vivo nesta sessao; mesmo achado das QA-0033..0038)]` · `[PADRAO_NOVO: escada de anexos DENTRO da transacao do append (404 -> 428 -> 422 -> 400 antes da CAS, recusa sem commit = sem seq) - confirmados em execucao pelos mutantes M10/M11 (428 e 422 antes do 400), M3/M4/M6 (400 do anexo) e M5 (seq nao consumido); a visibilidade GAP-25 da mensagem e lida da coluna 0007 com json_each (M1 derruba o AC-04 junto) - source: backend/src/Squid.Service/SqliteThreadStore.cs:529]` · `[SUSPEITO: nao - nenhuma contradicao entre spec e codigo encontrada; as tres afericoes do DEV (escada, cota intacta, primeira gravação vence o alt) sao confirmadas ou registradas acima]` · restante NENHUM (novo) — rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md#Local tests without gate`: S-15/S-22/S-23 confirmados **em execução real** nesta sessão; SPEC-0010 §G, o data-model §2.6 e o contrato §5/§6 foram lidos diretamente das fontes normativas).
