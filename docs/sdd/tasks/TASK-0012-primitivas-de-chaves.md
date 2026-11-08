<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0012 — Primitivas de chaves: RK/RC, HKDF, Argon2id, embrulho RSA

```yaml
runId: R-20261107-02
taskId: TASK-0012
status: DONE
agent: odev
phase: P-02
sequence: 2
createdAt: 2026-11-07 16:19:35
updatedAt: 2026-11-07 16:19:35
parent: SPEC-0007
authority: SPEC
parallel: true
touches: [backend/src/Squid.Api/Crypto/**, backend/src/Squid.Api/Squid.Api.csproj, backend/Directory.Packages.props, backend/tests/Squid.Tests/Crypto/**]
blocks: ["TASK-0014", "TASK-0016"]
blockedBy: []
inputArtifacts: ["docs/sdd/specs/SPEC-0007-identidade-ator-local-chaves-sessao-kit.md", "specs/001-squid-mvp/data-model.md"]
outputArtifacts: [backend/src/Squid.Api/Crypto/**, backend/src/Squid.Api/Squid.Api.csproj, backend/Directory.Packages.props]
interviewRounds: 0/6
feasibilityRounds: 1/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-07 16:06:40
  scope: instalar BouncyCastle.Cryptography como pacote unico de cripto (Argon2id, Ed25519, HKDF) com versao so em Directory.Packages.props (ADR-25) - resposta a GAP-08
  expiresAt: NENHUM
blocker: NENHUM
nextAction: DONE - QA-0012 PASSED (5/5 ACs + facet D-01, qaAttempt 1/2); TASK-0012 encerrada
evidence: []
memoryBankFlags: []
```

## Intent

Entrega as primitivas cripto que a fase de identidade inteira usa (SPEC-0007 B-02/B-03):
gerar `RK` (32 bytes CSPRNG) e `RC` independente, derivar Ed25519 por
`HKDF(RK, "fed/ed25519-sign")`, cifrar/decifrar `RK` com Argon2id e embrulhar/desembrulhar o par
RSA-2048 por `HKDF(RK, "fed/rsa-wrap")` — sem `RK` em claro persistida nem logada.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `RK` gerada com 32 bytes aleatórios criptográficos e nunca logada/em resposta; derivar
  duas vezes do mesmo segredo produz a mesma Ed25519, e `RC` gerada em paralelo **não** deriva da
  `RK` (mudar a `RK` não altera a `RC`) — provado por teste.
- AC-02: embrulho RSA round-trip: par RSA-2048 gerado, embrulhado por `HKDF(RK, "fed/rsa-wrap")`,
  decifrado de volta produz a mesma chave privada; sem `RK` correta o desembrulho falha (exceção
  tipada, nunca `catch {}` nem `ex.ToString()` ao cliente) — teste.
- AC-03: `RK` cifrada com Argon2id round-trip: cifrar → decifrar devolve os 32 bytes originais;
  senha errada falha sem destruir dado algum — teste.
- AC-04: nenhuma representação serializada do estado ou dos resultados expõe `RK` ou chave privada
  em claro: teste afirma que o texto serializado do objeto de estado não contém o material em claro.
- AC-05: gate `powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test`
  exit 0; `BouncyCastle.Cryptography` com versão **só** em `Directory.Packages.props` (nenhum
  `.csproj` com `Version=`); nenhum arquivo alterado fora dos `touches`.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/src/Squid.Api/Crypto/**` (novo diretório — correção da D-01: saiu de `Squid.Domain/Crypto/**`)
- `backend/src/Squid.Api/Squid.Api.csproj` (o `PackageReference` do BouncyCastle vive aqui, sem `Version=`)
- `backend/Directory.Packages.props` (versão, ADR-25)
- `backend/tests/Squid.Tests/Crypto/**` (novo diretório)

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-08 | no | pacote escolhido e aprovado pelo humano (BouncyCastle.Cryptography); versão fixada só no props central |
| GAP-09 | no | nenhuma operação desta task destrava `RK` em runtime — só gera, deriva e embrulha |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | Onde vive o `<PackageReference Include="BouncyCastle.Cryptography" />`? | SPEC-0007 (nível spec, 2026-11-07) | Em `Squid.Api.csproj`, módulo em `Squid.Api/Crypto/**`: o `Squid.Domain` mantém zero pacotes (`Assert.Empty` da TASK-0002 é contrato QA-passed, authority CODE, não relaxa). ItemGroup condicionado no props REJEITADO — satisfaz a letra escondendo a dependência do leitor do teste. `touches` corrigidos; consumidor fora de `Squid.Api` (P-06) vira projeto leaf próprio, registrado em SPEC-0007 §Non-functional |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-07 16:19:35 | odev | task created from SPEC-0007 (P-02, lote paralelo inicial com TASK-0013); gate humano registrado (2026-11-07 16:06:40, BouncyCastle)
- 2026-11-07 16:27:04 | odev | dispatch DEV attempt 1/2: TASK_READY -> IN_DEVELOPMENT, sequence 1 -> 2; lote paralelo com TASK-0013 (touches disjuntos verificados); gate humano BouncyCastle 2026-11-07 16:06:40
- 2026-11-07 16:54:07 | dev | dev | viabilidade 1/2: task lida (status IN_DEVELOPMENT, runId R-20261107-02, touches = Crypto/** + Directory.Packages.props + testes Crypto/**); SPEC-0007 B-02/B-03, data-model 2.12 e REQ-0001 lidos; memoria bank ausente [GAP: memory bank absent]; backend/src/Squid.Domain/Crypto ainda nao existe (diretorio novo)
- 2026-11-07 16:54:07 | dev | dev | baseline pre-existente verde antes de qualquer edicao: sdd-test.ps1 test exit 0, Aprovado 98 / Com falha 0 (log docs/sdd/runs/test-runs/20261107-163618-test-gate.log) - vermelho antes do inicio nao seria regressao minha
- 2026-11-07 16:54:07 | dev | dev | touches conferidos: disjuntos de TASK-0013 (Squid.Grains/Actors e Squid.Tests/Actors); nenhum caminho de escrita fora dos tres toques foi usado
- 2026-11-07 16:54:07 | dev | dev | D-01 bloqueante aberta: o PackageReference do BouncyCastle nao tem lar dentro dos touches - backend/src/Squid.Domain/Squid.Domain.csproj fica fora de Crypto/**, e DomainHasNoInfrastructureTests.cs:46 exige Assert.Empty(packages) no csproj do Domain, entao o lugar natural derruba o gate (AC-05); o unico lar permitido e backend/Directory.Packages.props, que satisfaz a letra do AC-05 mas declara a dependencia fora do arquivo que o teste arquitetonico le; spec e requisito nao dizem onde a referencia vive -> DOUBT, zero codigo
- 2026-11-07 16:54:37 | odev | odev | D-01 adjudicada no nivel da especificacao (escada spec -> req -> humano, nivel 1): SPEC-0007 corrigida - modulo de chaves em Squid.Api/Crypto/** com PackageReference em Squid.Api.csproj; o Assert.Empty(packages) de DomainHasNoInfrastructureTests.cs:46 e contrato QA-passed da TASK-0002 com authority CODE e nao e relaxado; proposta de ItemGroup condicionado em Directory.Packages.props REJEITADA por satisfazer a letra do AC-05 escondendo a dependencia do teste arquitetonico
- 2026-11-07 16:54:37 | odev | odev | touches do TASK-0012 corrigidos (Squid.Domain/Crypto -> Squid.Api/Crypto + Squid.Api.csproj); paralelismo re-verificado: segue disjunto de TASK-0013 (Squid.Grains/Actors + Squid.Tests/Actors); feasibilityRounds 0/2 -> 1/2
- 2026-11-07 16:54:37 | odev | odev | status DOUBT_ESCALATED -> IN_DEVELOPMENT; redispatch DEV attempt 2 com a resposta de D-01
- 2026-11-07 17:25:02 | dev | viabilidade 2/2: D-01 seguida conforme SPEC-0007 - modulo em Squid.Api/Crypto, PackageReference em Squid.Api.csproj, touches corrigidos lidos no control block e confirmados disjuntos de TASK-0013; pacote BouncyCastle.Cryptography 2.7.0 escolhido como ultima estavel do nuget.org e API confirmada por reflexao no nupkg (Builder com With*, Argon2id=2, defaults 4096KiB/3/1, Ed25519PrivateKeyParameters de seed de 32 bytes)
- 2026-11-07 17:25:02 | dev | implementacao dentro dos 4 touches: versao central em Directory.Packages.props, PackageReference sem Version= em Squid.Api.csproj, modulo Crypto (RootKey, KeyDerivation, AesGcmBox, RootKeyCipher, KeyState) e 6 testes em Squid.Tests/Crypto; nenhum arquivo do Domain escrito, Assert.Empty(packages) intacto
- 2026-11-07 17:25:02 | dev | build: sdd-test.ps1 build exit 1 na primeira passada com 3 erros reais (HKDF exige byte[] e ImportRSAPrivateKey exige out int) - correcao na causa em KeyDerivation.Derive/Unwrap, re-build exit 0 com 0 avisos e 0 erros
- 2026-11-07 17:25:02 | dev | testes novos: sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto exit 0 com 6/6 aprovados em 1 s (AC-01, AC-02, AC-03, AC-04)
- 2026-11-07 17:25:02 | dev | gate AC-05: sdd-test.ps1 test exit 0 com 119/119 (98 de baseline + 6 meus + 15 do DEV paralelo TASK-0013), log docs/sdd/runs/test-runs/20261107-171957-test-gate.log; nenhuma colisao de arquivo com o DEV paralelo
- 2026-11-07 17:25:02 | dev | verificacao estatica AC-05: 0 PackageReference com Version= e 0 PackageVersion nos 10 csproj, exatamente 1 linha de versao BouncyCastle.Cryptography 2.7.0 so em Directory.Packages.props; git status mostra meus arquivos = os 4 touches; git grep catch nos diretorios novos exit 1 = nenhum catch no codigo novo
- 2026-11-07 17:25:02 | dev | observacao para ODEV: a secao ## Touches do task file ainda lista os caminhos antigos (Squid.Domain/Crypto) enquanto o control block ja traz os corrigidos - so a ODEV edita essa secao
- 2026-11-07 17:25:39 | odev | odev | ODEV verificou antes de transicionar: check + leitura propria do gate docs/sdd/runs/test-runs/20261107-171957-test-gate.log com Aprovado 119 / Com falha 0 / exit 0; varredura propria ADR-25 = 0 PackageReference com Version= nos 10 csproj e 1 PackageVersion BouncyCastle 2.7.0 so no props; git status -- backend/src/Squid.Domain/ vazio = Domain intocado, Assert.Empty preservado
- 2026-11-07 17:25:39 | odev | odev | secao ## Touches do task file corrigida para os 4 caminhos de Squid.Api (o DEV sinalizou a divergencia; so o ODEV edita essa secao)
- 2026-11-07 17:25:39 | odev | odev | observacao do DEV registrada como contexto para a TASK-0014: Crypto/** sela a RK (RootKeyCipher) mas nao hasheia senha de login (B-04) - o helper Argon2id de senha e do proprio Auth, dentro dos toques da 0014
- 2026-11-07 17:25:39 | odev | odev | status IN_DEVELOPMENT -> AWAITING_QA; step result DONE aceito (attempt feasibility 2/2, logLines e evidenceRows completos sem timestamps)
- 2026-11-07 18:07:09 | qa | qa attempt 1/2: verificacao independente TASK-0012 - 5 ACs + facet D-01 executados: filtro Crypto 6/6 exit 0 (log 173517), DomainHasNoInfrastructureTests 3/3 exit 0 (log 173542), gate 119/119 exit 0 log-verificado (log 175625); checkers qa-check-task-0012.ps1 16/16 exit 0 (Windows) e .sh 16/16 exit 0 (WSL2 Linux); ADR-25: 0 atributos Version= em 10 csproj, BouncyCastle 2.7.0 so em Directory.Packages.props:38
- 2026-11-07 18:07:09 | qa | provas de mutacao S-16: M1 AesGcmBox sem chave (wrong-key path) -> Ac02:64 + Ac03:78 vermelhas (log 174912 exit 1) e checker FAIL AC02-aead-box; M2 RecoveryCode = HKDF(RK) -> Ac01:50 vermelha (log 175108 exit 1) e checker FAIL AC01-rc-csprng-src; restauracao byte-exact SHA256 PRE=POST (AesGcmBox 3ED0A774..., KeyState C2690BA3...), residue scan 0, gate final verde
- 2026-11-07 18:07:09 | qa | achados: MINOR 1 - evidencia git grep do DEV e vacua em diretorio nao-rastreado (probe RootKey exit 1 vs 28 hits), conclusao re-verificada TRUE; MINOR 2 - restore com mtime antigo deixa build incremental obsoleto (gate 175335 vermelho com fonte = PRE, bump de mtime -> gate 175625 verde), companheiro de S-16; MINOR 3 - Version= literal so em comentarios dos csproj; Linux dotnet gate unverified (WSL so tem podman-machine sem dotnet); S-22 nao disparou nos 6 runs (pares console/log 0/0 0/0 1/1 1/1 1/1 0/0); QA-0012 nao tocou paths da TASK-0013
- 2026-11-07 18:11:07 | odev | odev | ODEV verificou antes de transicionar: gate final 20261107-175625-test-gate.log com exit 0 e Aprovado 119 / Com falha 0 lido no proprio arquivo; SHA256 atual de KeyState.cs = C2690BA35B9BEE5CA87D4D639E9428F4DE00377C08D4893D0AC89DF1EDA17700 = PRE/POST da mutacao M2 da QA (byte-exact confirmado); QA docs QA-0012-primitivas-de-chaves.md e qa-check-task-0012.ps1 existem; check exit 0 (23 logs, 34 evidencias)
- 2026-11-07 18:11:07 | odev | odev | finding 1 (evidencia git grep vacua em diretorio nao-rastreado) adjudicado: a TECNICA e invalida (git grep nao ve untracked - probe da QA exit 1 com RootKey 28x presente), mas a CONCLUSAO 0 catch foi re-verificada por scan direto nas duas plataformas e e VERDADEIRA; a linha de evidencia fica (append-only) e a licao vira S-24 no backlog
- 2026-11-07 18:11:07 | odev | odev | finding 2 (gate 175335 vermelho) adjudicado: binario obsoleto, nao produto - o unico teste falho foi Ac01_RecoveryCodeIsNotAFunctionOfTheRk (o alvo da mutacao M2) com o fonte ja em PRE; mtime rebobinado pela restauracao byte-exact fez o build incremental rodar o binario mutado - mesmo padrao ja flagrado na TASK-0011; bump de mtime -> gate 175625 verde; vira S-23 no backlog (companheiro de S-16)
- 2026-11-07 18:11:07 | odev | odev | finding 3 (substring Version= so em comentarios de aviso): leitura registrada; varredura de atributo Version\s*=\s*= 0 nos 10 csproj e BouncyCastle 2.7.0 unico no props - AC-05/ADR-25 valem
- 2026-11-07 18:11:07 | odev | odev | S-22 seguida pela QA: 6 pares console/log conferidos (0/0 0/0 1/1 1/1 1/1 0/0), classificacao toda vinda do log gravado; a classificacao errada da QA-0011 nao disparou aqui
- 2026-11-07 18:11:07 | odev | odev | verdicto QA-0012 adjudicado: PASSED com 5/5 ACs + facet D-01/ADR-25 provadas por execucao propria (checker 16/16 nas duas plataformas, 2 mutacoes can-fail vermelhas, gate 119/119); unverified aceitos (Linux dotnet SDK exige gate; RK em resposta HTTP e prova estrutural, teste dinamico na TASK-0014); 0 BLOCKER; transicao AWAITING_QA -> DONE

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| — | — | — | — | — |
| 2026-11-07 16:54:07 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-163618-test-gate.log - baseline 98/98 (Aprovado 98, Com falha 0) |
| 2026-11-07 16:54:08 | dev | git grep -n Assert.Empty(packages) -- backend/tests | 0 | backend/tests/Squid.Tests/Architecture/DomainHasNoInfrastructureTests.cs:46 - o csproj do Domain precisa de zero PackageReference (AC-02 da TASK-0002) |
| 2026-11-07 16:54:08 | dev | git grep -n PackageReference -- backend/src/Squid.Domain | 0 | backend/src/Squid.Domain/Squid.Domain.csproj:4 - Domain has no ProjectReference and no PackageReference |
| 2026-11-07 16:54:09 | dev | Test-Path backend/src/Squid.Domain/Crypto ; Test-Path backend/src/Squid.Domain/Squid.Domain.csproj | 0 | False / True - o csproj fica fora do toque Crypto/** |
| 2026-11-07 16:54:09 | dev | git status --porcelain | 0 | nenhuma mudanca de produto na arvore antes do inicio da task (so artefatos da fase P-02 nao commitados) |
| 2026-11-07 17:25:02 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (1a passada) | 1 | 3 erros reais CS1503/CS7036 em backend/src/Squid.Api/Crypto/KeyDerivation.cs (defeito do DEV, nao ambiente) |
| 2026-11-07 17:25:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build (apos correcao na causa) | 0 | backend/Squid.slnx - 0 avisos, 0 erros |
| 2026-11-07 17:25:03 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto | 0 | docs/sdd/runs/test-runs/20261107-171847-test-filter.log - Aprovado 6, Com falha 0 |
| 2026-11-07 17:25:04 | dev | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test | 0 | docs/sdd/runs/test-runs/20261107-171957-test-gate.log - Aprovado 119, Com falha 0 (baseline 98 + 6 novos + 15 do DEV paralelo) |
| 2026-11-07 17:25:04 | dev | Select-String nos 10 csproj -Pattern PackageReference com Version= e PackageVersion | 0 | hits=0 nos dois - versao so no props (ADR-25, AC-05) |
| 2026-11-07 17:25:05 | dev | Select-String backend/Directory.Packages.props -Pattern BouncyCastle.Cryptography | 0 | 1 hit - PackageVersion Version=2.7.0 na linha 38, versao central unica |
| 2026-11-07 17:25:06 | dev | git status --porcelain | 0 | meus arquivos = Directory.Packages.props, Squid.Api.csproj, src/Squid.Api/Crypto/, tests/Squid.Tests/Crypto/ (os 4 touches); Grains/Actors e Tests/Actors = DEV paralelo |
| 2026-11-07 17:25:06 | dev | git grep -n -e catch -- backend/src/Squid.Api/Crypto backend/tests/Squid.Tests/Crypto | 1 | nenhuma ocorrencia - AC-02 sem catch{} e sem ex.ToString() |
| 2026-11-07 17:25:07 | dev | Invoke-WebRequest nuget.org flatcontainer + reflexao no nupkg BouncyCastle.Cryptography 2.7.0 | 0 | versao 2.7.0 (ultima estavel) e API real confirmada antes de escrever codigo |
| 2026-11-07 18:07:09 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto | 0 | docs/sdd/runs/test-runs/20261107-173517-test-filter.log - baseline QA: Aprovado 6, Com falha 0, log exit 0 |
| 2026-11-07 18:07:10 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~DomainHasNoInfrastructureTests | 0 | docs/sdd/runs/test-runs/20261107-173542-test-filter.log - Aprovado 3, Com falha 0, log exit 0 - facet D-01/AC-05 |
| 2026-11-07 18:07:10 | qa | Select-String nos 10 csproj -Pattern Version= e PackageVersion; Select-String Directory.Packages.props -Pattern BouncyCastle | 0 | 0 atributos Version= com aspas nos 10 csproj; 0 PackageVersion fora do props; BouncyCastle 2.7.0 unico em Directory.Packages.props:38; PackageReference unico em Squid.Api.csproj:12 sem Version= |
| 2026-11-07 18:07:11 | qa | git grep -n RootKey -- backend/src/Squid.Api/Crypto | 1 | probe de vacuidade da evidencia DEV: exit 1 com a string presente enquanto scan direto acha 28 hits - git grep nao ve diretorio nao-rastreado (finding 1; conclusao 0 catch re-verificada por scan direto) |
| 2026-11-07 18:07:11 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1 (1a execucao) | 1 | FAIL AC02-aead-box hits=0 - bug da propria QA (Hits recebia arquivo onde esperava diretorio); corrigido na causa e re-executado |
| 2026-11-07 18:07:12 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1 (apos correcao) | 0 | SUMMARY total=16 fails=0 - RESULT PASS (Windows PowerShell 5.1) |
| 2026-11-07 18:07:12 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0012.sh | 0 | docs/sdd/qa/qa-check-task-0012.sh - SUMMARY total=16 fails=0 em Linux 6.18.54.1-microsoft-standard-WSL2 |
| 2026-11-07 18:07:13 | qa | Get-FileHash SHA256 backend/src/Squid.Api/Crypto/AesGcmBox.cs + copia de backup (pre-mutacao M1) | 0 | PRE=BACKUP=3ED0A774CEDED54773B145D29AC825EB70D6D7F2CD839C528127772BA1596948 match=True (S-16) |
| 2026-11-07 18:07:13 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1 (sob mutacao M1) | 1 | FAIL AC02-aead-box hits=0 - prova pode-falhar do checker (checker acusa a mutacao) |
| 2026-11-07 18:07:14 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto (mutacao M1: AesGcmBox sem chave) | 1 | docs/sdd/runs/test-runs/20261107-174912-test-filter.log - log exit 1; Com falha 2, Aprovado 4: Ac02 linha 64 ThrowsAny sem excecao + Ac03 linha 78 (AC-02/AC-03 provados poderem falhar) |
| 2026-11-07 18:07:14 | qa | Get-FileHash SHA256 AesGcmBox.cs apos restauracao (M1) | 0 | POST=PRE=3ED0A774CEDED54773B145D29AC825EB70D6D7F2CD839C528127772BA1596948 BYTE_EXACT=True; residue scan 0 (S-16) |
| 2026-11-07 18:07:15 | qa | Get-FileHash SHA256 backend/src/Squid.Api/Crypto/KeyState.cs + copia de backup (pre-mutacao M2) | 0 | PRE=BACKUP=C2690BA35B9BEE5CA87D4D639E9428F4DE00377C08D4893D0AC89DF1EDA17700 match=True (S-16) |
| 2026-11-07 18:07:15 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1 (sob mutacao M2) | 1 | FAIL AC01-rc-csprng-src - prova pode-falhar do checker (RC derivada da RK acusada) |
| 2026-11-07 18:07:16 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Crypto (mutacao M2: RC = HKDF(RK)) | 1 | docs/sdd/runs/test-runs/20261107-175108-test-filter.log - log exit 1; Com falha 1, Aprovado 5: Ac01_RecoveryCode linha 50 (AC-01 provado poder falhar) |
| 2026-11-07 18:07:16 | qa | Get-FileHash SHA256 KeyState.cs apos restauracao (M2) | 0 | POST=PRE=C2690BA35B9BEE5CA87D4D639E9428F4DE00377C08D4893D0AC89DF1EDA17700 BYTE_EXACT=True; residue scan 0 (S-16) |
| 2026-11-07 18:07:16 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0012.ps1 (apos restauracoes) | 0 | SUMMARY total=16 fails=0 - fonte restaurada verde no checker (Windows) |
| 2026-11-07 18:07:17 | qa | wsl -e bash docs/sdd/qa/qa-check-task-0012.sh (apos restauracoes) | 0 | SUMMARY total=16 fails=0 - fonte restaurada verde no checker (WSL2 Linux) |
| 2026-11-07 18:07:17 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate apos restore, binario obsoleto) | 1 | docs/sdd/runs/test-runs/20261107-175335-test-gate.log - log exit 1; Com falha 1, Aprovado 118: Ac01_RecoveryCode com fonte ja restaurada (SHA256=PRE) - build incremental obsoleto por mtime rewind (finding 2, nao defeito de produto) |
| 2026-11-07 18:07:18 | qa | ((Get-Item backend/src/Squid.Api/Crypto/KeyState.cs).LastWriteTime = Get-Date) e o mesmo em AesGcmBox.cs | 0 | mtime 17:13:46 -> 17:55:23; SHA256 segue PRE (stillPRE=True x2) - conteudo intocado, so o relogio para o MSBuild recompilar |
| 2026-11-07 18:07:19 | qa | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test (gate final apos bump de mtime) | 0 | docs/sdd/runs/test-runs/20261107-175625-test-gate.log - log exit 0; Aprovado 119, Com falha 0, Total 119 (AC-05) - inclui 15 testes da TASK-0013 verdes |
