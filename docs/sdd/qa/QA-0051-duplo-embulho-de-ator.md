<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (ultimos artefatos: docs/sdd/qa/QA-0051-mutations.log [cycles=7 failed=0, GREEN 7/7], QA-0051-verify.log [VERIFY total=7 bad=0], gate 20261114-105858-test-gate.log [exit: 0 no cabecalho, Com falha 0, Aprovado 573, Total 573, 7m46s], QA-0051-scope.log e QA-0051-scope-sh.log [fora-dos-touches=0, fails=0], baseline QA-0051-git-baseline.txt [fails=0], filtro baseline 20261114-102845-test-filter.log [7/7 exit 0])

# QA-0051 — Assinatura de ator registrado: duplo embrulho com `Squid:RecoveryKey` (TASK-0051)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador e do ODEV, QA nao escreve nele)
- QA: subagente qa-task + politica ponytail-qa (full) — testes EXECUTADOS nesta sessao, nao escritos e deixados
- leitura normativa: SPEC-0011 **§B "Duplo embrulho no registro"** (decisao humana GAP-50 opcao 1, 2026-11-14 09:10:08) + AC-01..AC-06 do task file + memoria-bank rota `qa-integracao` (`build-run-test.md#Local tests without gate`, `integrations.md` [template], `contracts.md`)
- commit sob verificacao: `0a7cdf9` em `task/TASK-0051-duplo-embulho-de-ator` (**18 arquivos = produto + testes; NENHUM fora dos touches**; nao pushado — `branch-r-contains=EMPTY`; pai `2c66912` = ponta de develop; `main` = `b9fd628` intocado). Os ` M docs/sdd/{ideas,plan,specs}` e `?? docs/sdd/tasks/TASK-0051..TASK-0052` do working tree sao o WIP do ODEV (gravado no baseline antes de qualquer escrita da QA) — a QA nao os toca nem os committa
- entrega da QA dentro dos touches: **NENHUM arquivo novo em `backend/`** — os cenarios que a lista de AC exige ja existem nos testes do DEV (7 `[Fact]` em `DoubleWrapTests.cs` marcados `// AC-01`..`// AC-05`, + `InstanceSigningKeyMigrationTests`, `ActorSigningKeyActivationTests`, `RuntimeSigningKeyRingTests`, `OutboundFanOutTests` sem contorno); a QA acrescenta apenas artefatos em `docs/sdd/qa/`
- natureza desta QA: **PRODUTO** — 6 ACs provadas por **7 mutacoes can-fail contra o PRODUTO**, restauradas byte-exact (hash PRE=POST), + gate completo lido dentro do log (S-22) + varredura de escopo nas duas shells
- verdict: **PASSED**

## AC → cenario (6 ACs; tudo executado nesta sessao)

| AC | Cenario | Execucao (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | com `Squid:RecoveryKey`, o registro embrulha a MESMA RSA com `HKDF(RK_instancia, "fed/rsa-wrap")` e persiste SO a copia na tabela `0009` (idempotente); sem a config, registro igual ao de hoje e nenhuma copia nasce | Verde na baseline (`20261114-102845-test-filter.log`, exit 0, **7/7**) com `Ac01_with_a_secret_the_registration_persists_only_the_wrapped_instance_copy` (linha `wrapped_signing_key` da tabela `instance_signing_keys` abre com a RK de BOOT e devolve a MESMA RSA publicada `#main-key`, e os bytes privados nao aparecem na linha) e `Ac01_without_the_secret_registration_births_no_copy_and_answers_as_before` (`COUNT(*)=0` na tabela + kit/sessao/201 byte-como-sempre); idempotencia da migration no gate com `The_0009_version_creates_the_instance_copy_table_and_reapplying_changes_nothing` (re-aplica nada, linha semeada intacta). Can-fail: **T1** (SaveAsync da copia morto em `AuthEndpoints.cs:116` → **falha=4**, RED = Ac01w + Ac02 + Ac04c + Ac05) | PASS |
| AC-02 | ator registrado pela API federado SEM contorno: ativacao abre a copia de instancia, assina com a RSA propria do ator (`keyId #main-key`) e verificador independente aceita o par | Verde com `Ac02_a_registered_actor_federates_signed_and_an_independent_verifier_accepts` (registro HTTP real → `OutboundPipeline.SendAsync` → `Sent`, `keyId="{origin}/users/{handle}#main-key"` + `rsa-sha256` no fio; verificador `HttpSignatureVerifier` alimentado SO com o `publicKeyPem` lido do `/users/{handle}` publicado + checagem crua `RSA.VerifyData` sem codigo em comum com quem assinou). O contorno antigo sumiu: `OutboundFanOutTests.SignableAsync` agora nasce de `RegisterAsync` (diff do commit). Can-fail: **T2** (ativacao devolve RSA estranha em `RuntimeSigningKeyRing.cs:99` → **falha=1**, RED = so Ac02, verificador rejeita) | PASS |
| AC-03 | kit e continuidade (GAP-01) intactos: `EncryptedRootKey` + wrapped da RK do ator nao mudam; restaurar o kit devolve a mesma identidade; regressoes P-02 verdes | Verde com `Ac03_the_kit_path_is_untouched_and_a_restore_devolves_the_same_identity` (kit abre com a senha e a Ed25519 dele e a publicada; rota `/api/recovery/kit` devolve o embrulho do ATOR — OUTRO valor que a copia da `0009`; restauracao em instancia NOVA sem segredo devolve o mesmo `publicKeyPem`) + as suites Recovery da P-02 verdes dentro do gate 573/573. Can-fail: **T3** (kit embrulhado com RK estranha em `KeyState.cs:50` → **falha=1**, RED = so Ac03, `UnwrapRsaPrivateKey(actorRk, ...)` explode) | PASS |
| AC-04 | a copia nunca em claro: tabela guarda so material embrulhado, nenhuma rota/log devolve a copia, e sem `Squid:RecoveryKey` no boot a ativacao falha auditavel como hoje (AC-01/03 da TASK-0042 preservadas) | Verde com `Ac04_the_copy_never_leaves_the_table_and_no_route_or_log_carries_it` (sementes de CANAIS: clear PKCS8/RAW em base64+hex + a propria RK de boot varridos de registro, `/users/{handle}`, `/api/auth/me`, `/api/recovery/kit`, linha do ator e Console capturado — nenhum aparece; e o proprio valor embrulhado nao servido por rota nenhuma) e `Ac04_without_the_secret_activation_stays_auditably_disabled` (`ActivateSigningKeyAsync` devolve nulo, sem excecao, com o aviso `"federation output disabled"` + `Squid:RecoveryKey` + `"Level":"Warning"` no log). Can-fail: **T4** (copia = PKCS8 claro em `KeyState.cs:51` → **falha=5**, RED inclui Ac01w/Ac04c no `UnwrapRsaPrivateKey` e Ac01n, porque o mutante nasce copia ate sem segredo — artefato do mutante), **T5** (copia vazada no corpo de registro em `AuthEndpoints.cs:130` → **falha=1**, RED = so Ac04c, `DoesNotContain(wrapped, registerBody)`), **T6** (sem segredo a ativacao devolve chave em `RuntimeSigningKeyRing.cs:83` → **falha=1**, RED = so Ac04n, `Assert.Null`) | PASS |
| AC-05 | ator registrado ANTES da config → sem copia, saida desabilitada com aviso auditavel (limite do gate GAP-50); registrar DEPOIS nasce com copia e federationa | Verde com `Ac05_without_a_copy_the_output_stays_disabled_while_a_later_actor_federates` (host 1 sem segredo grava o ator; host 2 com segredo reabre o MESMO storage: `ActivateSigningKeyAsync(old)` = nulo, fila `Pending` + reason `federation_output_disabled`, `sender.Calls` vazio, aviso com `instance signing copy` + o handle no console; ator novo: linha na `0009`, ativacao != nulo, `Sent` de verdade). Can-fail: **T7** (cauda do aviso cortada em `OutboundPipeline.cs:195` → **falha=1**, RED = so Ac05, `Assert.Contains("instance signing copy", text)` falha) | PASS |
| AC-06 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate DA QA re-executado: `20261114-105858-test-gate.log` com `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 573, Ignorado: 0, Total: 573` (7m46s) — mesmo total do DEV (559 baseline + 14 novos). Escopo nas DUAS shells: `QA-0051-scope.log` (exit 0 — commit **18/18 nos touches**, `wip-odev=5` so do ODEV, `git-backend-entries=0`, `residue=0`, blob-identity **4/4**, `mtime=4`, `fora-dos-touches=0`, `fails=0`) e `QA-0051-scope-sh.log` (exit 0 sob WSL, mesmas contagens) | PASS |

## Comandos executados (todos nesta sessao, com exit real; datas vem dos proprios logs)

| # | comando | exit | artefato / saida |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-baseline-task-0051.ps1` | 0 | `docs/sdd/qa/QA-0051-git-baseline.txt` — HEAD `0a7cdf9`, branch da task, so o WIP do ODEV no status + artefatos da QA, 18 arquivos do commit, `branch-r-contains=EMPTY`, `develop-tip=2c66912` (= pai do HEAD), `main=b9fd628`, sondas: `wsl-dotnet=ABSENT`, `wsl-git=/usr/sbin/git`, `gitbash-probe=/c/Program Files/dotnet ; /ucrt64/bin/git`, `windows-dotnet=10.0.403`, `fails=0` |
| 2 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1 -VerifyOnly` | 0 | `docs/sdd/qa/QA-0051-verify.log` — 7 padroes `hits=1`, `non-ascii-bytes=0`, `VERIFY total=7 bad=0` (re-executado apos o redesenho do T7) |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DoubleWrapTests` | 0 | `docs/sdd/runs/test-runs/20261114-102845-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 7 / Total 7 (48 s) — baseline verde antes de qualquer mutacao |
| 4 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1` (run unico) | 0 | `docs/sdd/qa/QA-0051-mutations.log` — 7 vermelhos de proprio com RED-AC da propria AC em cada um, 7x `RESTORE-OK hash=PRE residue=0`, `GREEN wrapper=0 logExit=0 aprovado=7 falha=0`, `UNTRACKED backend=0`, `SUMMARY cycles=7 failed=0 residue-backend=0 elapsed-s=457` |
| 5 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-06, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261114-105858-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 573, Ignorado: 0, Total: 573` |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0051.ps1` | 0 | `docs/sdd/qa/QA-0051-scope.log` — `SUMMARY commit-files=18 ... residue=0 blob=4/4 mtime=4 fails=0` |
| 7 | `wsl -e bash docs/sdd/qa/qa-scope-task-0051.sh` ( gemeo Linux do escopo ) | 0 | `docs/sdd/qa/QA-0051-scope-sh.log` — mesmas contagens sob bash/WSL, `fails=0` |
| 8 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DoubleWrapTests.Ac05_without_a_copy` | 0 | `docs/sdd/runs/test-runs/20261114-104011-test-filter.log` — investigacao do T7 cico 1: com a mutacao ANTIGA (cabeca do aviso trocada, cauda preservada) o teste ficou VERDE — prova do achado NOTA-1 abaixo |
| 9 | `git checkout -- backend/src/Squid.Service/OutboundPipeline.cs` | 0 | mutante manual da investigacao restaurado byte-exact antes de qualquer execucao seguinte (deposito limpo: `git status -- backend` = 0) |

As linhas `## Log` e `## Evidence` destas execucoes **nao** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relogio para o ODEV carimbar (o relogio e dele). A tabela acima e a prova; a task file permanece intacta pela QA.

## Ciclos de mutacao (can-fail; S-16 hash PRE = blob do commit, S-22 classificacao no log, S-23 restore byte-exact)

Todos nos arquivos de **PRODUTO** que a behaviour le (`AuthEndpoints.cs`, `RuntimeSigningKeyRing.cs`, `KeyState.cs`, `OutboundPipeline.cs`) — nenhum edit fora do escopo, e os 4 arquivos finais sao byte-iguais ao `HEAD` (blob-identity 4/4; `git status -- backend` = 0).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | `AuthEndpoints.cs:116` | AC-01: o registro PERSISTE a copia embrulhada na `0009` — sem a linha nada nasce e todo AC dependente perde o material | `Ac01w` + `Ac02` + `Ac05` + `Ac04c` (`falha=4`, log `20261114-104403-test-filter.log`) |
| T2 | `RuntimeSigningKeyRing.cs:99` | AC-02: a ativacao entrega a CHAVE DO ATOR aberta da copia — RSA estranha faz o verificador independente rejeitar o fio | `Ac02` (`falha=1`, log `20261114-104502-test-filter.log`) |
| T3 | `KeyState.cs:50` | AC-03: o embrulho do KIT continua sob a RK do ATOR — RK estranha quebra a restauracao (GAP-01) | `Ac03` (`falha=1`, log `20261114-104556-test-filter.log`, `UnwrapRsaPrivateKey(actorRk, ...)`) |
| T4 | `KeyState.cs:51` | AC-04: a tabela guarda SO material embrulhado — PKCS8 claro na copia explode todo opener | `Ac01w` + `Ac04c` + `Ac05` + `Ac02` + `Ac01n` (`falha=5`, log `20261114-104651-test-filter.log`; Ac01n vermelha e artefato do mutante: o ternario mutante usa `instanceKeys is null`, e a instancia do ring existe ate sem segredo — entao o mutante nasce copia no registro sem config) |
| T5 | `AuthEndpoints.cs:130` | AC-04: nenhuma rota serve a copia — vaza-la no corpo de registro tripada o `DoesNotContain` do wire | `Ac04c` (`falha=1`, log `20261114-104750-test-filter.log`) |
| T6 | `RuntimeSigningKeyRing.cs:83` | AC-04: sem `Squid:RecoveryKey` no boot a ativacao e NULO auditavel — devolver chave aqui derrota o estado desabilitado (AC-01/03 da TASK-0042) | `Ac04n` (`falha=1`, log `20261114-104848-test-filter.log`, `Assert.Null`) |
| T7 | `OutboundPipeline.cs:195` | AC-05: a saida do ator SEM copia e auditavel — cortar a cauda `instance signing copy` do aviso deixa o console sem registro | `Ac05` (`falha=1`, log `20261114-104943-test-filter.log`) |

7x `PRE` com `head-match=true` (o hash de trabalho e o blob do commit `0a7cdf9`), 7x `RESTORE-OK hash=PRE residue=0`, 0x `RESTORE-FAIL`, 0x `TOOLING-FAIL`, 0x `BUILD-FAIL`, 7x `EXPECT-OK`, 0x `EXPECT-MISS`, 1x GREEN `7/7 exit 0` (`20261114-105041-test-filter.log`), residuo final de tokens mutantes = 0, `UNTRACKED backend=0`, `SUMMARY cycles=7 failed=0 residue-backend=0 elapsed-s=457`.

## Afirmacoes do DEV a verificar (declaracoes do `## Log` e do commit 0a7cdf9)

| # | Afirmacao | Julgamento desta QA | Evidencia |
| --- | --- | --- | --- |
| 1 | "migration 0009 idempotente, store SqliteInstanceSigningKeyStore, ring embrulha a MESMA RSA no registro e abre por handle na ativacao" | **CONFIRMADA em execucao**: Ac01w abre a copia com a RK de boot e compara com a `#main-key` publicada; `The_0009_version_..._reapplying_changes_nothing` verde no gate; Ac02 prova a abertura por handle no caminho real de saida | comandos #3/#4/#5 + ciclos T1/T2 |
| 2 | "ActorGrain ativa por handle com cache por ator (latch 0046: o segundo ator nunca reusa a chave do primeiro)" | **CONFIRMADA em leitura e execucao**: `ActorGrain.cs:130-153` guarda `Dictionary<string,RSA>` por handle; `One_instance_activates_each_handle_with_its_own_key` verde no gate (dois atores na mesma instancia, chave de cada um verifica o proprio e REJEITA a do outro) | `backend/src/Squid.Grains/Actors/ActorGrain.cs:130` + comando #5 |
| 3 | "OutboundPipeline emite warning auditavel quando a saida de um ator fica desabilitada" | **CONFIRMADA**: aviso em `OutboundPipeline.cs:193-196` lido no console capturado de Ac05 (verde) e derrubado pelo ciclo T7 | comandos #3/#4 + ciclo T7 |
| 4 | "14 novos testes ... primeira execucao da classe apontou 4 vermelhos, corrigidos e re-executada 7/7" | **CONFIRMADA**: 14 `[Fact]`/metodos novos no diff (7 DoubleWrap + 4 ativacao + 1 migration + 2 ring); classe 7/7 verde na baseline da QA e no GREEN do ledger | diff do commit + comandos #3/#4 |
| 5 | "gate AC-06: sdd-test.ps1 test exit 0 com 573/573" | **CONFIRMADA de forma independente**: gate DA QA re-executado → mesmo total (559 baseline + 14 novos), `exit: 0` e `Com falha: 0` lidos dentro do log | comando #5 |
| 6 | "18 arquivos ... todos em touches, fora-dos-touches 0, commit unico 0a7cdf9, sem push, main intacta" | **CONFIRMADA**: 18/18 nos seis glob sets do bloco `touches`, `branch-r-contains=EMPTY`, pai = ponta de develop, `main=b9fd628` | comandos #1 e #6/#7 |
| 7 | "achado fora dos touches: ProfileJsonLd.TryDecodePublicKeyPem nao decodifica PEM blindado; HttpFederatedActorClient.KeyOf depende dele" | **CONFIRMADA por leitura**: o caminho existe (`ProfileJsonLd.cs:78` chamado por `HttpFederatedActorClient.cs:172`) e esta FORA dos touches (Squid.Protocols); a AC-02 nao depende dele (o teste le a chave publicada via `ProfileJsonLd.Materialize`). ODEV ja cortou a TASK-0052 e registrou S-41/S-42 | leitura dos dois arquivos + diff do commit (arquivo nao alterado) |

## Findings

Nenhum achado de **produto** que impeca o verdict (0 BLOCKER, 0 MAJOR — 573/573 no gate da QA e os 7 mutantes caem exatamente onde cada AC diz). Achados e notas:

| # | Severidade | Onde | O que | Evidencia |
| --- | --- | --- | --- | --- |
| 1 | NOTA (robustez de teste, nao de produto) | `backend/tests/Squid.Tests/ServiceHost/DoubleWrapTests.cs:366-369` | em Ac05 o `HeldWarning` ("signing key unavailable for") alimenta SO o wait — a frase ASSERTADA e `instance signing copy` (linha 369). A primeira versao do ciclo T7 trocou so a cabeca do aviso e o teste ficou verde (ciclo 1: log `20261114-103613-test-filter.log` exit 0; reproducao manual: `20261114-104011-test-filter.log` exit 0, com o mutante confirmado no binario via busca UTF-16 no `Squid.Service.dll`). Risco: baixo — um aviso REMOVIDO por completo continua vermelho (a cauda asserted sumiria); a QA redesenhou o T7 para cortar a frase asserted e o vermelho foi provado | comandos #8 + ciclo T7 do run final |
| 2 | NOTA (atalho pre-existente, direcao segura) | `backend/src/Squid.Api/Auth/AuthEndpoints.cs:111-117` | a copia embarca no mesmo atalho nao transacional da credencial: falha aqui deixa a conta SEM copia — estado que o proprio spec aceita para atores pre-configuracao (saida desabilitada e auditavel), nunca copia em claro nem meio-embarulhada. Seria `shortcut:` se o bloco seguisse a familia do comentario de 106 | leitura do codigo + Ac01w/Ac05 verdes |
| 3 | NOTA (pre-existente, fora dos touches, ja roteado) | `backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs:78`, `backend/src/Squid.Service/HttpFederatedActorClient.cs:172` | o leitor de `publicKeyPem` do documento NAO decodifica PEM blindado (Compact remove o espaco do armor antes do Replace) → `KeyOf` devolve null para chave remota. Nao e AC desta task e o fix fugiria dos touches; ODEV ja cortou a TASK-0052 (S-41). Risco se nao corrigir: TASK-0047/0049 falham na resolucao de ator remoto | leitura + diff (arquivo intocado no 0a7cdf9) |
| 4 | NOTA (plataforma, pre-existente) | WSL / `docs/sdd/_tools/sdd-test.sh` | `wsl -e bash -c 'command -v dotnet'` → **vazio** (sondado ao vivo no baseline): a suite NAO roda em Linux nesta maquina e o gemeo `.sh` do gate quebra sem ele (memoria banc, backlog S-11/S-12; instalacao exige gate humano) — Git Bash tem `dotnet`, mas e o MESMO kernel Windows: nao acrescenta plataforma | `QA-0051-git-baseline.txt` + `build-run-test.md:42` |

## Nao coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo: `wsl-dotnet=ABSENT`; instalacao exige gate humano) e o gemeo `sdd-test.sh` quebra sem ele (S-11/S-12) → **unverified**, nunca assumido igual ao Windows. O gemeo `.sh` **estatico** do escopo roda — comando #7 sob WSL. Risco: baixo-medio (o CI ubuntu ja rodou a suite em janela anterior, mas nao nesta arvore).
- **Forma `appsettings.json` da chave**: os cenarios constroem o host com argumento de linha de comando (`--Squid:RecoveryKey=`) — o provedor JSON de arquivo nao e exercitado. Risco baixo (a mesma abstracao `IConfiguration`, mesma chave).
- **Ativacao com secret ERRADO/malformado via host real**: a recusa tipada (`SigningKeyActivationException` sem material) e coberta por `RuntimeSigningKeyRingTests` e `ActorSigningKeyActivationTests` no gate; o caminho host-real com secret torto nao e montado aqui (a familia ja foi provada na QA-0042). Risco baixo.
- **Container/compose (podman)**: nenhum AC desta task nomeia o DoD de container → nao executado. Risco baixo.
- **Interop real Mastodon/Friendica (TASK-0049) e harness de dois silos (TASK-0047)**: tarefas futuras desta fase; os cenarios usam `FakePeerSender` e verificador proprio por desenho. Risco: medio se o par real rejeitar — e exatamente o que 0047/0049 vao medir.
- **Leitor de chave remota (`KeyOf`/PEM blindado)**: fora da lista de AC (achado ja roteado a TASK-0052) → nao coberto aqui. Risco: ja descrito na NOTA-3.

## Reproduzir

```powershell
# pre-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DoubleWrapTests   # esperado: exit 0, Aprovado 7, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1 -VerifyOnly                                   # esperado: exit 0, VERIFY total=7 bad=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0051.ps1                                               # run fresco: exit 0, SUMMARY cycles=7 failed=0 (7 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                 # gate: exit 0, Com falha 0, Aprovado 573
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-scope-task-0051.ps1                                                # esperado: exit 0, fora-dos-touches=0
wsl -e bash docs/sdd/qa/qa-scope-task-0051.sh                                                                                         # esperado: exit 0, fora-dos-touches=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/QA-0051-git-baseline.txt` | baseline antes de qualquer escrita: HEAD `0a7cdf9`, branch, status (WIP do ODEV), arquivos do commit, nao-push, sondas de plataforma (WSL/Git Bash/dotnet) |
| `docs/sdd/qa/qa-baseline-task-0051.ps1` | script que grava a baseline (e tambem o ancora de mtime do S-23) |
| `docs/sdd/qa/qa-mutate-task-0051.ps1` | driver de mutacao can-fail (7 ciclos: AC-01 x1, AC-02 x1, AC-03 x1, AC-04 x3, AC-05 x1; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, `RED-AC` = qual cena da AC caiu) |
| `docs/sdd/qa/QA-0051-verify.log` | `-VerifyOnly` gravado (7 padroes `hits=1` + ASCII OK + `VERIFY total=7 bad=0`) |
| `docs/sdd/qa/QA-0051-mutations.log` | ledger final: PRE (hash = blob do commit), vermelhos no log com RED-AC, restore, GREEN, UNTRACKED, SUMMARY |
| `docs/sdd/qa/qa-scope-task-0051.ps1` | varredura de escopo Windows: commit vs touches, classificacao do `git status` (WIP do ODEV explicito), residuo, blob-identity, mtime |
| `docs/sdd/qa/QA-0051-scope.log` | saida da varredura (18/18 touches, blob 4/4, mtime 4, residue 0, fora-dos-touches 0) |
| `docs/sdd/qa/qa-scope-task-0051.sh` | gemeo bash da mesma varredura |
| `docs/sdd/qa/QA-0051-scope-sh.log` | saida do gemeo sob WSL (mesmas contagens, fails=0) |
| `backend/` | **intocado pela QA** — `git status -- backend` = 0 entradas (a QA nao acrescentou teste: as cenas das ACs sao as do DEV) |
| `docs/sdd/qa/QA-0051-duplo-embulho-de-ator.md` | este documento |
