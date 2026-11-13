<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (último artefato: docs/sdd/runs/test-runs/20261113-182619-test-gate.log)

# QA-0042 — Chave de assinatura em runtime + assinar/verificar HTTP (TASK-0042)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador é do ODEV, QA não escreve nele)
- QA: subagente qa-task + política ponytail-qa (full) — testes EXECUTADOS nesta sessão, não escritos e deixados
- leitura normativa: SPEC-0011 **§B e §C inteiros** (GAP-09 + assinatura HTTP) + AC-01..AC-06 do task file com a **adjudicação da D-01 pelo ODEV 2026-11-13 18:02:25** (SPEC-0011 §B: `Squid:RecoveryKey` = **base64 dos 32 bytes**, mesma forma do kit/estados) + contrato `activitypub.md` **§3 (ordem obrigatória) e §5 (saída)** + memória banc rota `qa-integracao` (`index.md` → `build-run-test.md` linhas S-22/S-23)
- commit sob verificação: `0814968` em `task/TASK-0042-chave-de-assinatura-em-runtime` (**13 arquivos = 9 produto nos touches + 3 teste + task file só com Log/Evidence**; NENHUM fora); baseline DEV `484/484 → 503/503`
- natureza desta QA: **PRODUTO + TESTES** — as behaviour das seis ACs são provadas por **18 mutações can-fail contra o PRODUTO**, restauradas byte-exact, + gate completo
- verdict: **PASSED**

## AC → cenário (6 ACs; tudo executado nesta sessão)

| AC | Cenário | Execução (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | sem `Squid:RecoveryKey` a instância sobe, serve registro+perfil+`/health` e registra o Warning de saída federada desabilitada | verde no baseline `20261113-180909-test-filter.log` (**exit 0, 19/19**) e no GREEN. Can-fail: **T1** (texto do aviso trocado → `181446`, **falha=1**, RED-DETAIL `Assert.Contains() Failure: Sub-string not found` — a captura espera 30s e não acha `federation output disabled`), **T2** (ring estoura exceção sem a chave → `181457`, **falha=1** em 300ms = o host não sobe) | PASS |
| AC-02 | com RK válida o grain desembrulha (`HKDF(RK,"fed/rsa-wrap")` — `KeyDerivation.cs:19`) e mantém só em memória: assinatura confere com a chave publicada; row + rota `/users/{handle}` + log sem material privado | Can-fail nas DUAS metades: **T3** (ring devolve RSA nova em vez da desembrulhada → `181511`, **falha=1**, `Assert.True() Failure` = `rsa.VerifyData` false), **T4** (Warning de boot passa a colar o `Squid:RecoveryKey` no log → `181529`, **falha=1**, `Assert.DoesNotContain() Failure: Sub-string found (pos 3725)` = o canal de log vazou). `Assert.Contains("masked", signer.ToString())` e os 5 formatos de segredo (PKCS8/PKCS1 b64/hex + RK b64) verdes nos dois momentos | PASS |
| AC-03 | RK errada → falha auditável na ativação, sem vazar material em mensagem, log ou resposta | Can-fail nas DUAS metades: **T5** (re-throw crua → `181542`, **falha=1**, `Assert.Throws() Failure: Exception type was not an exact match Expected: typeof(SigningKeyActivationException)`), **T6** (segredo colado na mensagem interna da falha → `181600`, **falha=1**, `Assert.DoesNotContain() Failure (pos 198)`). Verde: row byte-idêntica (`Assert.Equal(before, after)`), `/health` 200, mensagem cita handle + `Squid:RecoveryKey` e nada de material | PASS |
| AC-04 | saída no formato do contrato §5 (`keyId` `#main-key`, `algorithm="rsa-sha256"`, `headers="(request-target) host date digest"`, `Digest: SHA-256=<base64>`, `Date` GMT, GET sem digest) e verificador independente aceita o par | verde: assinatura reconstruída **do texto do contrato** com RSA cru (não pela biblioteca) + round-trip no nosso verificador. Can-fail: **T7** (linhas fora da ordem declarada → `181611`, RED-DETAIL `the independent verifier did not accept the signature our signer produced`), **T8** (`algorithm=hs2019` → `181624`), **T9** (fragmento `#main-key` não verificado → `181638`), **T10** (`Digest: SHA-512=` → `181649`, `Strings differ pos 4`), **T11** (Date `u` em vez de `r` → `181659`, falha no `EndsWith(" GMT")`), **T12** (GET declara `digest` → `181709`, `Expected: "(request-target) host date"`) | PASS |
| AC-05 | entrada aceita draft-cavage `rsa-sha256`/`hs2019` e RFC 9421 (RSA e Ed25519); rejeita Date ±5 min, Digest divergente e chave que não é a publicada | verde nos 7 cenários (as assinaturas de entrada são montadas **à mão do contrato/ RFC 9421**, nunca pelo nosso signer). Can-fail: **T13** (janela de data ignorada → `181719`, `Expected: StaleDate`), **T14** (Digest não conferido → `181729`, `Expected: DigestMismatch`), **T15** (`RsaVerify` sempre true → `181739`, `Expected: SignatureInvalid`), **T16** (rótulos draft recusados → `181750`, **falha=2** = `rsa-sha256` E `hs2019`), **T17** (case `ed25519` morto → `181800`), **T18** (RFC 9421 mandado ao parser draft → `181810`) | PASS |
| AC-06 | gate `sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | **Lido DENTRO do log** `20261113-182619-test-gate.log` (S-22): cabeçalho `exit: 0` + `Com falha: 0, Aprovado: 503, Ignorado: 0, Total: 503` (6m24s) — reproduzido de forma independente do gate do DEV (503/503 também). Escopo (`QA-0042-scope.log`): blob-identity `git hash-object` = `git rev-parse HEAD:` em **5/5** arquivos mutados; mtime desde a baseline = **exatamente esses 5**; resíduo `QA-MUTATION` = 0; `git status -- backend` = **0**; `fora-dos-touches=0` (3 entradas de docs = WIP do ODEV já na baseline da QA; 7 artefatos novos = desta QA em `docs/sdd/qa/`). Commit `0814968`: 13/13 arquivos dentro dos `touches` (checker `COMMIT-TOUCHES`) | PASS |

## Comandos executados (todos nesta sessão, com exit real; datas vêm dos próprios logs)

| # | comando | exit | artefato / saída |
| --- | --- | --- | --- |
| 1 | `git rev-parse HEAD` + `--show-current` + `status --porcelain -uall` + `show --name-only 0814968` + `wsl -e bash -c 'command -v dotnet'` (pwsh) | 0 | `docs/sdd/qa/QA-0042-git-baseline.txt` — HEAD `0814968`, branch `task/TASK-0042-chave-de-assinatura-em-runtime`, 3 entradas de WIP do ODEV, **dotnet ausente no WSL**, 13 arquivos do commit todos nos touches |
| 2 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~FederationRuntimeKeyTests\|FullyQualifiedName~HttpSignatureTests\|FullyQualifiedName~RuntimeSigningKeyRingTests"` (baseline da QA) | 0 | `20261113-180909-test-filter.log` — `exit: 0`, Com falha 0 / Aprovado 19 / Total 19 (17s) |
| 3 | `powershell ... docs/sdd/qa/qa-mutate-task-0042.ps1 -VerifyOnly` | 0 | `docs/sdd/qa/QA-0042-verify.log` — 18 padrões `hits=1`, `ASCII non-ascii-bytes=0 OK`, `VERIFY total=18 bad=0` |
| 4 | `powershell ... docs/sdd/qa/qa-mutate-task-0042.ps1` (run cheio: 18 ciclos + GREEN) | 0 | `docs/sdd/qa/QA-0042-mutations.log` — 18 vermelhos com RED-AC da AC alvo em cada um (T16 com falha=2), 18× `RESTORE-OK hash=PRE residue=0`, `GREEN ... aprovado=19 falha=0` (`181838`), `SUMMARY cycles=18 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| 5 | `powershell ... docs/sdd/_tools/sdd-test.ps1 test` (gate AC-06, suite inteira com BuildProof) | 0 | `20261113-182619-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 503, Total: 503`, 6m24s |
| 6 | varredura de escopo (pwsh): blob-identity vs `rev-parse HEAD:`, mtime desde a baseline, resíduo, diff contra a baseline do git | 0 | `docs/sdd/qa/QA-0042-scope.log` — blob 5/5 `match=True`, `mtime-count=5`, `residue=0`, `pre-existing-odev-wip=3`, `fora-dos-touches=0`, `git-backend-entries=0` |
| 7 | `powershell ... docs/sdd/qa/qa-check-task-0042.ps1` | 0 | `docs/sdd/qa/QA-0042-check.log` — SUMMARY total/fails (SDD-STATE `check` incluso) |
| 8 | `wsl -e bash docs/sdd/qa/qa-check-task-0042.sh` (gêmeo Linux, mesma bateria estática) | 0 | `docs/sdd/qa/QA-0042-check-sh.log` — SUMMARY total=50 fails=0 (o `sdd-state` é ferramenta Windows, fica só no ps1). **1ª execução (`QA-0042-check-sh-run1.log`, exit 1, fails=19)** falhou no parser do próprio gêmeo: o ledger do driver é CRLF e os âncoras/`falha=2` carregavam o `\r` — bug da ferramenta da QA, corrigido com `tr -d '\r'` e re-executado; nenhum cenário de produto esteve em questão (o ps1 twin dava 54/54 no mesmo ledger) |

As linhas `## Log` e `## Evidence` destas execuções **não** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relógio para o ODEV carimbar (o relógio é dele). Esta tabela acima é a prova; a task file permanece intacta pela QA.

## Ciclos de mutação (can-fail; S-16 SHA256 PRE, S-22 classificação no log, S-23 restore byte-exact + mtime)

Todos nos arquivos de PRODUTO que a behaviour lê (`Squid.Service/SquidServiceHost.cs`, `Squid.Api/Crypto/RuntimeSigningKeyRing.cs`, `Squid.Grains/Actors/ActorGrain.cs`, `Squid.Protocols/ActivityPub/HttpSignatureSigner.cs`, `Squid.Protocols/ActivityPub/HttpSignatureVerifier.cs`) — nenhum edit fora do escopo, e os 5 arquivos finais são byte-iguais ao `HEAD` (blob-identity 5/5).

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | SquidServiceHost.cs | AC-01: o aviso é o registro de "só a saída desabilitada" — trocar a frase quebra a captura | `Ac01_without...` (`181446`, falha=1, `Assert.Contains` Sub-string not found) |
| T2 | RuntimeSigningKeyRing.cs | AC-01: sem a chave a instância tem que SUBIR — exceção no boot derruba o `StartAsync` | `Ac01_without...` (`181457`, falha=1, 300ms) |
| T3 | RuntimeSigningKeyRing.cs | AC-02: o desembrulho tem que devolver a chave DO ATOR — chave nova não confere com a publicada | `Ac02_with...` (`181511`, falha=1, `Assert.True`) |
| T4 | SquidServiceHost.cs | AC-02: log nunca leva material — Warning com o segredo colado | `Ac02_with...` (`181529`, falha=1, `Assert.DoesNotContain` pos 3725) |
| T5 | ActorGrain.cs | AC-03: a recusa é auditável = `SigningKeyActivationException` tipada — re-throw crua | `Ac03_with...` (`181542`, falha=1, `Assert.Throws` type mismatch) |
| T6 | RuntimeSigningKeyRing.cs | AC-03: nada vaza na mensagem — segredo colado no inner exception | `Ac03_with...` (`181600`, falha=1, `Assert.DoesNotContain` pos 198) |
| T7 | HttpSignatureSigner.cs | AC-04: linhas na ordem DECLARADA em headers — o verificador independente (RSA cru) discorda | `Ac04_post...` (`181611`, falha=1, `the independent verifier did not accept`) |
| T8 | HttpSignatureSigner.cs | AC-04: `algorithm="rsa-sha256"` do contrato §5 | `Ac04_post...` (`181624`, falha=1) |
| T9 | HttpSignatureSigner.cs | AC-04: `keyId` sem `#main-key` é recusado (o fragmento é contrato, não escolha do caller) | `Ac04_a_key_id_without...` (`181638`, falha=1) |
| T10 | HttpSignatureSigner.cs | AC-04: `Digest: SHA-256=` — nunca sha-512 (Mastodon rejeita) | `Ac04_post...` (`181649`, falha=1, `Strings differ pos 4`) |
| T11 | HttpSignatureSigner.cs | AC-04: `Date` em HTTP-date GMT | `Ac04_post...` (`181659`, falha=1, EndsWith " GMT") |
| T12 | HttpSignatureSigner.cs | AC-04: GET assina `(request-target) host date`, sem digest | `Ac04_get_signs...` (`181709`, falha=1, `Expected: "(request-target) host date"`) |
| T13 | HttpSignatureVerifier.cs | AC-05: passo 3 da ordem §3 — Date fora de ±5 min rejeita | `Ac05_a_date_outside...` (`181719`, falha=1, `Expected: StaleDate`) |
| T14 | HttpSignatureVerifier.cs | AC-05: passo 2 — Digest do corpo confere | `Ac05_a_digest...` (`181729`, falha=1, `Expected: DigestMismatch`) |
| T15 | HttpSignatureVerifier.cs | AC-05: passo 1 — a chave é a publicada no `keyId` | `Ac05_a_signature_of_a_key...` (`181739`, falha=1, `Expected: SignatureInvalid`) |
| T16 | HttpSignatureVerifier.cs | AC-05: draft-cavage aceito com `rsa-sha256` E `hs2019` (theory) | `Ac05_a_peers_draft_cavage...` (`181750`, **falha=2**, os dois rótulos) |
| T17 | HttpSignatureVerifier.cs | AC-05: RFC 9421 com Ed25519 aceito | `Ac05_an_rfc9421_signature_with_ed25519...` (`181800`, falha=1) |
| T18 | HttpSignatureVerifier.cs | AC-05: RFC 9421 com RSA aceito (o `Signature-Input` tem que chegar ao parser 9421) | `Ac05_an_rfc9421_signature_with_rsa...` (`181810`, falha=1) |

18× `PRE` registrado, 18× `RESTORE-OK hash=PRE residue=0`, 0× `RESTORE-FAIL`, 1× GREEN `19/19 exit 0` (`181838`), resíduo final `QA-MUTATION` = 0, backups TEMP `qa0042-*` = 0, `-VerifyOnly` `bad=0` + `ASCII OK` (`QA-0042-verify.log`), 18× `EXPECT-OK`, 0× `EXPECT-MISS`, 0× `CLASSIFICACAO-ERRADA`.

## Afirmações do DEV a verificar (declarações de nível spec)

| # | Afirmação (Log do task file / commit 0814968) | Julgamento desta QA | Evidência |
| --- | --- | --- | --- |
| 1 | "RuntimeSigningKeyRing le Squid:RecoveryKey no boot e o host avisa saida federada desabilitada" | **CONFIRMADA em execução**: T2 mostra o boot recusando sem a chave (a leitura existe) e T1 mostra o aviso ser o único registro do estado (trocar a frase derruba a AC-01); warning no host pós-`Build` (`SquidServiceHost.cs:227-234`) | ciclos T1/T2 + checkers `WIRE-RING` |
| 2 | "port IActorSigningKeyRing + ActorSigner + ActivateSigningKeyAsync no ActorGrain (chave privada só em memória, SigningKeyActivationException auditável)" | **CONFIRMADA em execução**: T3 (roundtrip com a chave publicada), T4 (canal de log), T5 (tipo auditável), T6 (canal de mensagem) — cada metade da AC-02/AC-03 tem seu próprio vermelho | ciclos T3..T6 |
| 3 | "HKDF(RK, fed/rsa-wrap)" (SPEC-0011 §B, regra TASK-0012) | **CONFIRMADA estaticamente**: `KeyDerivation.RsaWrapInfo = "fed/rsa-wrap"` (`KeyDerivation.cs:19`) é o mesmo caminho de unwrap já testado pelo kit (QA-0015); o anel usa `KeyDerivation.UnwrapRsaPrivateKey` | checker `WIRE-HKDF` + `RuntimeSigningKeyRingTests` |
| 4 | "HttpSignatureSigner (draft-cavage rsa-sha256 do contrato 5)" | **CONFIRMADA em execução**: T7..T12 derrubam, um a um, cada cláusula do formato — inclusive a metade do verificador independente | ciclos T7..T12 |
| 5 | "HttpSignatureVerifier (rsa-sha256/hs2019 + RFC 9421 RSA/Ed25519 na ordem do contrato 3)" | **CONFIRMADA em execução e leitura**: os 4 casos de aceitação e as 3 recusas têm vermelho próprio (T13..T18); a ordem 1→2→3 está no código (`HttpSignatureVerifier.cs:85-103`) e cada passo caiu sozinho na sua mutação | ciclos T13..T18 + leitura |
| 6 | "gate AC-06: sdd-test.ps1 test exit 0 com 503/503" + "nenhum arquivo de produto alterado fora dos touches" | **CONFIRMADA de forma independente**: gate re-executado pela QA = 503/503 exit 0 lido no log; commit 13/13 nos touches; árvore final byte-igual (blob 5/5, `git status -- backend` = 0) | comando 5 + `QA-0042-scope.log` + checkers |

## Findings

Nenhum achado de **produto** (0 BLOCKER, 0 MAJOR, 0 MINOR — 503/503 verdes e os 18 mutantes caem exatamente onde cada AC diz). Notas de QA/fluxo:

| # | Severidade | Onde | O que | Evidência |
| --- | --- | --- | --- | --- |
| 1 | NOTA (pré-existente) | `docs/sdd/_tools/sdd-test.sh` / WSL | gêmeo Linux não roda a suite: `wsl -e bash -c "command -v dotnet"` → vazio, exit 1 (**sondado ao vivo NESTA sessão**, na baseline); instalação exige gate humano — mesmo achado das QA-0033..0041 | comando 1 + linha do Log |
| 2 | NOTA (S-22, wrapper) | `sdd-test.ps1` no ciclo T5 | o console do wrapper imprimiu **exit 3** (falso positivo do `Test-SandboxSignature`: o output do teste contém `handle`) enquanto o **log gravado** diz `exit: 1` com falha real de produto — decidido pelo log como manda S-22; bug conhecido do wrapper (backlog S-22), sem impacto no produto | ledger `T5 RUN wrapper=3 logExit=1 ... falha=1` |
| 3 | NOTA (boundary de escopo) | AC-05 × contrato §3 passos 4–7 | a AC-05 cobre os passos 1–3 (assinatura, digest, data); corpo ≤ 1 MB / profundidade ≤ 32, rate limit, dedupe e remetente **não** estão nas ACs desta task (inbox é TASK-0044/0046) — a QA não inventou cobertura | leitura AC-05 + contrato §3 |
| 4 | NOTA (AC-02 × rotas) | AC-02 "nenhuma rota" | não existe rota de inbox nesta entrega (task diz explicitamente "Nenhuma rota de inbox ainda") — os canais testados são os que existem hoje: linha persistida, `GET /users/{handle}` e log do host, todos varridos contra os 5 formatos do segredo | `FederationRuntimeKeyTests.cs:98-107` + ciclos T3/T4 |
| 5 | NOTA (ferramenta da QA) | `qa-check-task-0042.sh` | 1ª execução do gêmeo falhou em 19 checagens por `\r` do ledger CRLF no parser bash (âncoras e `falha=2`) — corrigido no gêmeo e re-executado 50/50; artefato da falha preservado em `QA-0042-check-sh-run1.log` | comando 8 + run1 |

## Não coberto / unverified

- **Suite de testes em Linux (WSL)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessão: `command -v dotnet` → exit 1; instalação exige gate humano) e o gêmeo `sdd-test.sh` quebra sem ele → **unverified**, nunca assumido igual ao Windows. O gêmeo `.sh` do **checker** (parte estática: logs, resumos, hashes, git) foi executado — comando 8.
- **RFC 9421 com `created`/`expires` fora de janela**: o verificador assina e valida `created` como componente, mas a janela de ±5 min da AC-05 é sobre o header `Date` (contrato §3 passo 3), que é o que foi testado. Risco: baixo (a AC não pede validade de `created`; a janela de replay real fica na inbox da TASK-0044).
- **Interop com Mastodon/Friendica de verdade**: o contrato §5 diz o que eles verificam e o verificador independente prova o formato, mas o par real só existe na TASK-0049 (gate humano, SC-016). Risco: médio — um desvio sutil de implementação deles (ex.: whitespace OWS) só aparece no par real.
- **Rate limit, tamanho e dedupe (§3 passos 4–7)**: fora das AC desta task (TASK-0044/0046) — risco registrado na finding 3.

## Reproduzir

```powershell
# pré-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test       # esperado: exit 0, Aprovado 503, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0042.ps1 -VerifyOnly   # esperado: exit 0, VERIFY total=18 bad=0 + ASCII OK
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0042.ps1     # run fresco: exit 0, SUMMARY cycles=18 failed=0 (18 vermelhos de proposito)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0042.ps1      # esperado: exit 0, SUMMARY fails=0
wsl -e bash docs/sdd/qa/qa-check-task-0042.sh                                               # esperado: exit 0, SUMMARY fails=0
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/qa/qa-mutate-task-0042.ps1` | driver de mutação can-fail (18 ciclos: AC-01 ×2, AC-02 ×2, AC-03 ×2, AC-04 ×6, AC-05 ×6; `-VerifyOnly` com checagem ASCII, S-16/S-22/S-23 embutidos, captura de `RED-AC` = qual cenário da AC caiu) |
| `docs/sdd/qa/QA-0042-mutations.log` | ledger: PRE, vermelhos no log (com RED-AC), restore, GREEN, SUMMARY |
| `docs/sdd/qa/QA-0042-verify.log` | `-VerifyOnly` gravado (18 padrões `hits=1` + `ASCII OK` + `VERIFY total=18 bad=0`) |
| `docs/sdd/qa/qa-check-task-0042.ps1` / `.sh` | checkers de evidência (mesmas checagens estáticas; gêmeo Linux sem o sdd-state, que é ferramenta Windows) |
| `docs/sdd/qa/QA-0042-check.log` / `QA-0042-check-sh.log` | execuções dos checkers (ps1 54/54, sh 50/50) |
| `docs/sdd/qa/QA-0042-check-sh-run1.log` | 1ª execução do gêmeo com exit 1 (bug de CRLF no parser do próprio script; preservada) |
| `docs/sdd/qa/QA-0042-git-baseline.txt` | git + HEAD + sonda WSL capturados antes da QA escrever |
| `docs/sdd/qa/QA-0042-scope.log` | varredura final de escopo (blob-identity, mtime, resíduo, diff contra a baseline do git) |

memoryBankFlags: `[GAP: suite Linux/WSL unverified - dotnet ausente na maquina (sondado ao vivo nesta sessao), instalacao exige gate humano (pre-existente, mesmo achado das QA-0033..0041)]` · `[SUSPEITO: nao - nenhuma contradicao spec x codigo encontrada; 503/503 verdes e os 18 mutantes caem exatamente onde cada AC diz (RED-AC por ciclo, 18/18 EXPECT-OK)]` · `[MUDANCA_RELEVANTE: SEGURANCA - Squid:RecoveryKey vira segredo de boot (base64 dos 32 bytes, D-01 adjudicada) com desembrulho da RSA do ator SO em memoria no ActorGrain via porta IActorSigningKeyRing; sem ela a saida federada fica desabilitada com Warning e a instancia nao cai - source: backend/src/Squid.Service/SquidServiceHost.cs:227]` · `[PADRAO_NOVO: porta IActorSigningKeyRing na composition root + ActorSigner que so assina (ToString mascarado) - nenhuma rota/log/row recebe a chave privada; candidato a pattern.md na consolidacao da fase - source: backend/src/Squid.Grains/Actors/IActorSigningKeyRing.cs:13]` · rota `qa-integracao` lida (`docs/memory-bank/index.md` + `build-run-test.md`: S-22/S-23 confirmados **em execução real** nesta sessão; SPEC-0011 §B/§C e contrato §3/§5 lidos diretamente das fontes normativas).
