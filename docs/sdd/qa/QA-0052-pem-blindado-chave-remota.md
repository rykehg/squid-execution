<!--
templateVersion: 1.0.0
 -->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: RASCUNHO
- owner: qa
- lastVerified: (ultimos artefatos: filtro baseline 20261114-114012-test-filter.log [5/5 exit 0], mutacao 20261114-114048-test-filter.log [4 falhas exit 1], gate 20261114-114911-test-gate.log [exit: 0, Aprovado 578, Com falha 0, 7m51s])

# QA-0052 — PEM blindado: chave remota resolve de verdade (TASK-0052)

- runId: R-20261113-01 · attempt: **1/2** (bloco de controle `qaAttempts: 0/2` — contador e do ODEV, QA nao escreve nele)
- QA: subagente qa-task + politica ponytail-qa (full) — testes EXECUTADOS nesta sessao, nao escritos e deixados
- leitura normativa: task file **`## Acceptance criteria` AC-01..AC-04** + SPEC-0011 §C/§E + contrato `activitypub.md` §1.1 (fixture) e §3 (ordem de verificacao) + memoria-bank rota `qa-integracao` (`build-run-test.md`, `contracts.md`, `integrations.md` [template])
- commit sob verificacao: `47c905e` em `task/TASK-0052-pem-blindado-chave-remota` (**3 arquivos = 1 produto + 2 testes, todos nos `touches`**; nao pushado; `main` intocado). O ` M docs/sdd/tasks/TASK-0052-...` do working tree e o carimbo do ODEV — a QA nao o toca nem o committa
- entrega da QA dentro dos `touches`: **NENHUM arquivo novo em `backend/`** — as cenarios das ACs ja existem nos 5 `[Fact]` do DEV (`ArmoredPemDecodeTests.cs` x2, `ArmoredRemoteKeyTests.cs` x3, marcados `// AC-01`..`// AC-03`); a QA acrescenta apenas este artefato em `docs/sdd/qa/`
- natureza desta QA: **PRODUTO** — 4 ACs provadas por execucao real + **1 mutacao can-fail contra o PRODUTO** (a propria regressao S-41 reintroduzida, restaurada byte-exact) + gate completo lido dentro do log (S-22) + varredura de escopo em Windows E em shell Linux (WSL)
- verdict: **PASSED**

## AC → cenario (4 ACs; tudo executado nesta sessao)

| AC | Cenario | Execucao (destaque) | Resultado |
| --- | --- | --- | --- |
| AC-01 | PEM blindado multi-linha do contrato §1.1 (armor BEGIN/END com quebras de linha em 64 colunas, como Mastodon/Friendica publicam) decodifica e a chave RSA resultante assina→verifica | Verde na baseline (`20261114-114012-test-filter.log`, exit 0, **5/5**) com `ArmoredPemDecodeTests.An_armored_multiline_pem_decodes_into_a_key_that_signs_and_verifies` (der decodificado byte-igual ao SPKI publicado + round-trip `SignData`/`VerifyData` SHA-256 com a chave DECODIFICADA). Can-fail: **T1** (o fix revertido — `Compact` antes do `Replace` do armor, `ProfileJsonLd.cs:121`) → **falha=4**, RED inclui `An_armored_multiline...` (`Assert.NotNull` nulo) | PASS |
| AC-02 | `HttpFederatedActorClient.KeyOf` resolve a chave RSA de um documento de ator remoto byte-realista do §1.1 (`publicKey.id`/`owner`/`publicKeyPem` blindado) e a verificacao de entrada aceita a atividade assinada com ela (`202`) | Verde com `ArmoredRemoteKeyTests.KeyOf_resolves_the_armored_rsa_of_a_realistic_actor_document` (fetch PRODUCAO via handler fake, SSRF/DNS/tamanho reais, `ResolveKeyAsync` devolve o SPKI exato, round-trip assina→verifica, `ExistsAsync` do proprio ator) e `ArmoredRemoteKeyTests.The_inbox_accepts_the_activity_signed_with_the_realistic_documents_key` (host real em 127.0.0.1:0, `Follow` assinado com a chave publicada → **202 + Accept** + follower persistido; controle MESMA keyId com OUTRA chave → **401** sem estado — o 202 so pode vir da chave publicada verificando). Fixture: documento §1.1 campo a campo, `publicKeyPem` com armor + 64 colunas escapado como viaja no JSON. T1 → RED em ambos (`KeyOf_resolves` nulo; inbox `Expected: Accepted / Actual: Unauthorized`) | PASS |
| AC-03 | Regressoes preservadas: PEM de linha unica continua aceito e `squid:ed25519Key` (`publicKeyMultibase`) continua resolvendo | Verde com `ArmoredPemDecodeTests.The_single_line_spellings_keep_decoding_and_hostile_material_stays_a_null_verdict` (base64 puro de linha unica E armor em linha unica devolvem o SPKI; material hostil/`null`/branco continuam veredito nulo, nunca excecao — FR-040) e `ArmoredRemoteKeyTests.The_ed25519_multibase_of_a_realistic_document_still_resolves` (vetor `z6Mk...` decodifica para os 32 bytes certos pelo caminho que nao toca no leitor de PEM). T1 deixou o 1 verde justamente o ed25519 (caminho independente) e derrubou a grafia armor-de-linha-unica (parte do mesmo fix) | PASS |
| AC-04 | Gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` exit 0; nenhum arquivo alterado fora dos `touches` | Gate DA QA re-executado: `20261114-114911-test-gate.log` com `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 578, Ignorado: 0, Total: 578` (7 m 51 s) — 573 do baseline do DEV + 5 Armored desta task = 578. Escopo: `git status --porcelain` = **so o carimbo do ODEV na task file** (mais este artefato da QA) e `git diff --name-only 47c905e~1 47c905e` = **3/3 nos `touches`** (`git diff --stat -- backend` vazio, HEAD = `47c905e` na branch da task); os logs de teste sao gitignored (`.gitignore:43`) e a mesma varredura foi repetida de uma shell LINUX (`wsl -e bash`: mesmos 3 arquivos, mesmo status) | PASS |

## Comandos executados (todos nesta sessao, com exit real; datas vem dos proprios logs)

| # | comando | exit | artefato / saida |
| --- | --- | --- | --- |
| 1 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored` | 0 | `docs/sdd/runs/test-runs/20261114-114012-test-filter.log` — `exit: 0`, `Aprovado: 5, Com falha: 0, Total: 5` (7 s) — baseline verde ANTES de qualquer mutacao |
| 2 | edicao temporaria em `backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs` (`PemBody` voltou a compactar ANTES do `Replace` do armor — a regressao S-41 exata) | 0 | mutacao T1 aplicada para provar que os testes desta sessao podem falhar |
| 3 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored` | 1 | `docs/sdd/runs/test-runs/20261114-114048-test-filter.log` — `exit: 1` lido dentro do log + `Com falha: 4, Aprovado: 1, Total: 5` — RED esperado da T1 (4 cenarios AC-01/02/03 caem, ed25519 fica verde) |
| 4 | `git restore -- backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs` (+ bump de mtime, S-23) | 0 | restore byte-exact: `git hash-object` do arquivo = `2f267df1ca86df0a5c00a05612501609c4ed42f3` = blob de `47c905e` para o mesmo caminho; `git status -- backend` vazio; mtime novo para o build incremental nao reusar o binario mutado |
| 5 | `wsl -e bash -c "cd /mnt/c/dev/squid-squad-net/squid-execution && git status --porcelain && git log --oneline -1 && git diff --name-only 47c905e~1 47c905e"` | 0 | escopo repetido de shell Linux: mesmo status (so o carimbo do ODEV), HEAD `47c905e`, mesmos 3 arquivos do commit; sonda `command -v dotnet` → `dotnet-ABSENT` no WSL |
| 6 | `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test` (gate AC-04, suite inteira com BuildProof) | 0 | `docs/sdd/runs/test-runs/20261114-114911-test-gate.log` — `exit: 0` lido **dentro do log** (S-22) + `Com falha: 0, Aprovado: 578, Ignorado: 0, Total: 578` (7 m 51 s) |

As linhas `## Log` e `## Evidence` destas execucoes **nao** foram gravadas pela QA: o dispatch devolve `logLines`/`evidenceRows` sem relogio para o ODEV carimbar (o relogio e dele). A tabela acima e a prova; a task file permanece intacta pela QA.

## Ciclo de mutacao (can-fail; S-16 hash PRE = blob do commit, S-22 classificacao no log, S-23 restore byte-exact)

Unico ciclo, no arquivo de **PRODUTO** que a behaviour le — `ProfileJsonLd.cs:121` (`PemBody`), o fix exato desta task.

| Ciclo | Arquivo | O que prova (AC) | Vermelho classificado NO LOG |
| --- | --- | --- | --- |
| T1 | `backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs:121` — `Compact(pem.Replace(...))` revertido para `Compact(pem).Replace(...)` (a causa original do S-41: o armor ja sem espacos nunca casa no `Replace`) | AC-01 + AC-02 + a grafia armor-da-AC-03: sem o fix, o base64 do armor nunca valida, `TryDecodePublicKeyPem` devolve null, `KeyOf` nao resolve e a inbox responde 401 no lugar do 202 | `An_armored_multiline_pem_decodes_into_a_key_that_signs_and_verifies` + `The_single_line_spellings_...` + `KeyOf_resolves_the_armored_rsa_...` + `The_inbox_accepts_...` (`falha=4`, `Expected: Accepted / Actual: Unauthorized`, log `20261114-114048-test-filter.log`), 1 verde = `The_ed25519_multibase_...` (caminho independente do leitor de PEM) |

`PRE` com hash de trabalho = blob de `47c905e`, `RESTORE-OK hash=PRE` (blob-identity 1/1, `git status -- backend` = 0), residuo final de tokens mutantes = 0, GREEN pos-restore provado pelo gate (comando #6).

## Afirmacoes do DEV a verificar (declaracoes do `## Log` e do commit 47c905e)

| # | Afirmacao | Julgamento desta QA | Evidencia |
| --- | --- | --- | --- |
| 1 | "fix em ProfileJsonLd.cs: armor removido ANTES do Compact pelo novo PemBody privado, compartilhado com FromPem" | **CONFIRMADA em leitura e execucao**: `ProfileJsonLd.cs:121-124` remove armor e depois compacta; `TryDecodePublicKeyPem` (:80) e `FromPem` (:169) compartilham `PemBody` (uma so ordem, sem dupla verdade); T1 revertendo essa ordem derruba 4 cenarios | leitura de `ProfileJsonLd.cs` + comandos #1/#3 |
| 2 | "nenhum outro arquivo de produto alterado" | **CONFIRMADA**: `git diff --name-only 47c905e~1 47c905e` = 3 arquivos (1 produto em `Squid.Protocols` + 2 testes), todos nos `touches`; `git status -- backend` vazio | comandos #4/#5 |
| 3 | "prova vermelha pre-fix: exit 1 com 4 vermelhos (TryDecodePublicKeyPem nulo, ResolveKeyAsync nulo, inbox 401 no lugar do 202) e 1 verde (ed25519)" | **REPRODUZIDA de forma independente**: a T1 da QA e a MESMA regressao e produz exatamente 4 vermelhos + o mesmo 1 verde, com as mesmas assinaturas de falha | log `20261114-114048-test-filter.log` (o pre-fix do DEV: `20261114-112355-test-filter.log`, lido pelo ODEV) |
| 4 | "apos o fix: filtro 5/5 exit 0 e gate 578/578 exit 0" | **CONFIRMADA de forma independente**: baseline da QA 5/5 exit 0 e gate DA QA re-executado com `exit: 0` e 578/578 lidos dentro do log | comandos #1 e #6 |
| 5 | "`HttpFederatedActorClient.KeyOf` resolve a chave RSA do documento de ator realista" | **CONFIRMADA em execucao**: `KeyOf` e `private` e e alcancado pelo caminho de producao (`ResolveKeyAsync` → fetch → `KeyOf`, `HttpFederatedActorClient.cs:76,:164`) com apenas os sockets falsos | `ArmoredRemoteKeyTests.cs:35` verde + T1 |

## Findings

Nenhum achado de **produto** que impeca o verdict (0 BLOCKER, 0 MAJOR — gate 578/578 e o unico mutante da QA cai exatamente onde a AC diz). Notas:

| # | Severidade | Onde | O que | Evidencia |
| --- | --- | --- | --- | --- |
| 1 | NOTA (tolerancia por desenho, nao defeito) | `backend/src/Squid.Protocols/ActivityPub/ProfileJsonLd.cs:80-91` | o leitor aceita PEM sem armor tambem (Remove so o que casa) e valida so o base64 — armor trocado/ausente cai no mesmo veredito utilizavel enquanto o corpo for SPKI valido. Contrato §1.1 publica `-----BEGIN PUBLIC KEY-----`, e `KeyOf` importa SPKI (`HttpFederatedActorClient.cs:175`), entao material de outra forma nao passa da importacao | leitura + `ArmoredPemDecodeTests.cs:39` (hostile material = null) |
| 2 | NOTA (plataforma, pre-existente) | WSL / `docs/sdd/_tools/sdd-test.sh` | `command -v dotnet` → `dotnet-ABSENT` no WSL (sondado ao vivo nesta sessao): a suite NAO roda em Linux nesta maquina e o gemeo `.sh` do gate quebra sem ele (memoria-bank `build-run-test.md:42`, backlog S-11/S-12; instalacao exige gate humano) | comando #5 |

## Nao coberto / unverified

- **Suite de testes em Linux (WSL/container)**: `dotnet` ausente no WSL (sondado ao vivo nesta sessao) e o gemeo `sdd-test.sh` quebra sem ele (S-11/S-12) → **unverified**, nunca assumido igual ao Windows. O que roda em Linux: a varredura de escopo por git (comando #5). Risco: medio-baixo — o CI ubuntu ja rodou a suite `.ps1` em janelas anteriores, mas nao nesta arvore e a QA nao pusha nem dispara CI.
- **Interop real Mastodon/Friendica (TASK-0049) e harness de dois silos (TASK-0047)**: tarefas futuras desta fase; os cenarios usam `FakeUnfurlHandler`/`FakeUnfurlResolver` por desenho (so os sockets sao falsos, fetch/SSRF/TTL/`KeyOf` sao producao). Risco: medio se o par real publicar outra grafia de PEM — e o que 0047/0049 vao medir.
- **Chave RSA remota com armor `\r\n` (Windows) ou sem footer**: nao exercitado como caso proprio — `Compact` remove qualquer whitespace entao `\r\n` corpo resolve, mas o vetor publico do contrato usa `\n`. Risco baixo.
- **Container/compose (podman)**: nenhum AC desta task nomeia o DoD de container → nao executado. Risco baixo.

## Reproduzir

```powershell
# pre-requisito: .NET 10 SDK + PowerShell no Windows, repo C:\dev\squid-squad-net\squid-execution
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Armored   # esperado: exit 0, Aprovado 5, Com falha 0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                          # gate: exit 0, Com falha 0, Aprovado 578
git diff --name-only 47c905e~1 47c905e                                                                                        # esperado: exatamente os 3 arquivos dos touches
```

## Artefatos desta QA

| Arquivo | Papel |
| --- | --- |
| `docs/sdd/runs/test-runs/20261114-114012-test-filter.log` | baseline verde 5/5 (gitignored, roda junto com o gate) |
| `docs/sdd/runs/test-runs/20261114-114048-test-filter.log` | RED da mutacao T1 (4 falhas classificadas) |
| `docs/sdd/runs/test-runs/20261114-114911-test-gate.log` | gate da QA (578/578, `exit: 0` dentro do log, 7 m 51 s) |
| `backend/` | **intocado pela QA** — a mutacao T1 foi restaurada byte-exact (`git status -- backend` = 0); nenhum arquivo novo em `backend/` |
| `docs/sdd/qa/QA-0052-pem-blindado-chave-remota.md` | este documento |
