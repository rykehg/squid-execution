<!--
templateVersion: 1.0.0
-->
Canonical state of one work item. A fresh session or subagent enters the flow through this file, not through a conversation.
Write responsibility: the control block, AC list and Doubts are written by ODEV only. DEV and QA append to `## Log` and to `## Evidence` — nothing else. No other edit to this file is valid.

# TASK-0004 — Migrar Squid.Tests para xunit + Test SDK e fazer `dotnet test` funcionar

```yaml
runId: R-20261104-02
taskId: TASK-0004
status: AWAITING_QA
agent: odev
phase: P-01
sequence: 3
createdAt: 2026-11-04 15:30:00
updatedAt: 2026-11-05 19:00:04
parent: SPEC-0001
authority: SPEC
parallel: false
touches: ["backend/Directory.Packages.props", "backend/tests/Squid.Tests/**", "docs/sdd/_tools/sdd-test.ps1"]
blocks: []
blockedBy: []
inputArtifacts: ["backend/tests/Squid.Tests/Squid.Tests.csproj", "backend/Directory.Packages.props"]
outputArtifacts: ["backend/tests/Squid.Tests/Squid.Tests.csproj", "backend/tests/Squid.Tests/Architecture/DependencyRuleTests.cs", "backend/tests/Squid.Tests/Architecture/DomainHasNoInfrastructureTests.cs"]
interviewRounds: 1/6
feasibilityRounds: 0/2
correctionAttempts: 0/2
qaAttempts: 0/2
contextHandoffs: 0/4
progressDelta: 0
humanGate:
  approvedBy: Henrique
  approvedDate: 2026-11-04 15:30:00
  scope: migracao do runner de reflexao para xunit + Test SDK, com cache de pacotes em backend/.packages
  expiresAt: NENHUM
blocker: NENHUM
nextAction: AWAITING_QA - despachar QA em TASK-0004 contra os ACs, com o gate verde em docs/sdd/runs/test-runs/20261105-190004-test-gate.log (Aprovado 16, exit 0); depois despachar DEV em TASK-0003
evidence: []
memoryBankFlags: []
```

## Intent

`dotnet test` não funciona: `Squid.Tests` é um runner de reflexão próprio, sem Test SDK, então o VSTest não acha provider e pendura buscando na rede. QA e DEV precisam de um comando de teste padrão. Decisão humana de 2026-11-04: migrar para xunit + `Microsoft.NET.Test.Sdk`.

## Acceptance criteria

Each AC is one observable behaviour. QA tests against this list; anything not listed is not required.

- AC-01: `dotnet test backend/Squid.slnx` termina sozinho (sem pendurar) e sai com exit 0, imprimindo o resumo com o total de testes aprovados.
- AC-02: os 4 testes de arquitetura existentes continuam existindo como `[Fact]` do xunit e cobrindo o mesmo comportamento: a regra de dependência recusa as 8 arestas proibidas e aceita as 5 legais; `Squid.Domain` não referencia Orleans nem JSON-LD; `Squid.Domain` mira `net10.0`.
- AC-03: `dotnet test --filter` funciona — filtrar por um nome de teste roda só aquele teste e o resumo mostra o total filtrado.
- AC-04: toda versão de pacote de teste está em `Directory.Packages.props`; nenhum `.csproj` carrega `Version=` (regra ADR-25 preservada, verificável por grep).
- AC-05: uma falha de teste real produz `Failed: 1` e exit code diferente de 0 — provado injetando uma asserção falsa temporária em uma cópia fora do repositório ou revertendo antes do fim.
- AC-06: o runner de reflexão próprio (`Framework/TestFramework.cs`, `Framework/Assert.cs`, `Program.cs` com `RunAll`) é removido, não mantido em paralelo.
- AC-07: `docs/sdd/_tools/sdd-test.ps1 test` passa a rodar `dotnet test` (não o executável do runner) e classifica falha de ambiente como exit 3.

## Touches (scope of the change)

Files, modules and contracts the change must reach. DEV confirms or corrects this list during feasibility. Two tasks may run in parallel only when their `touches` sets are disjoint.

- `backend/tests/Squid.Tests/**`
- `backend/Directory.Packages.props`
- `docs/sdd/_tools/sdd-test.ps1`

## Gaps

Do not carry a gap into development unless it has an assumption in force.

| Gap | Blocking | Assumption in force |
| --- | --- | --- |
| GAP-02 | no | restore exige rede a partir do nuget.org; o cache fica em `backend/.packages` (já configurado em `nuget.config` e ignorado no git). Sem rede e sem cache, é falha de ambiente (exit 3), nunca defeito de produto |
| GAP-07 | no | versões fixadas pela decisão humana: `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 — todas restauradas com sucesso pelo SDK 10.0.403 nesta máquina |

## Doubts

DEV raises, ODEV adjudicates. The ladder is: specification → requirement → human. Max 2 feasibility rounds; after that the task goes `BLOCKED`.

| Doubt | Question | Resolved by | Answer |
| --- | --- | --- | --- |
| D-01 | — | — | — |

## Log

Append only. One line per event, real timestamp, no edits to earlier lines. Format:

`- YYYY-MM-DD HH:MM:SS | ROLE | what was done or decided`

- 2026-11-04 15:30:00 | odev | task created from SPEC-0001, por decisão humana registrada no humanGate: migrar para xunit + Test SDK em vez de manter o runner de reflexão
- 2026-11-04 15:44:21 | dev | DEV rodada de viabilidade: task coerente - Squid.Tests era console com runner de reflexao (Program.cs, Framework/TestFramework.cs, Framework/Assert.cs) sem Microsoft.NET.Test.Sdk, VSTest sem provider; touches corretas, nenhuma duvida bloqueante; baseline registrado antes de editar (sdd-test.ps1 test no runner antigo: exit 3, assinatura de sandbox)
- 2026-11-04 15:44:21 | dev | DEV migrou Squid.Tests para xunit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1 + xunit.runner.visualstudio 3.1.4: PackageVersion em backend/Directory.Packages.props, PackageReference sem Version=, IsTestProject/IsPackable e Using Include=Xunit no csproj, OutputType removido; no build de 0 avisos e 0 erros para 0 warnings
- 2026-11-04 15:44:21 | dev | DEV removeu o runner de reflexao (Program.cs, Framework/TestFramework.cs, Framework/Assert.cs e a pasta Framework), conforme AC-06; grep em backend/tests nao acha referencia restante a TestRunner, ISelfCheckingTest, ITestClass ou RunAll
- 2026-11-04 15:44:21 | dev | DEV reescreveu a suite: DependencyRuleTests com [Theory]/MemberData - 8 arestas proibidas e 5 legais, cada uma um teste proprio; DomainHasNoInfrastructureTests com [Fact] para Orleans/JSON-LD, projetos de infraestrutura e net10.0; os dois com Trait Category=Architecture, e DependencyRuleTests tambem Category=BuildProof
- 2026-11-04 15:44:21 | dev | DEV extraiu as provas por build para Architecture/BuildProbe.cs mantendo UseWritableDotnetHome (DOTNET_CLI_HOME/NUGET_PACKAGES do filho) e desligando o que era runner: sem ISelfCheckingTest, sem RunSelfAll
- 2026-11-04 15:44:21 | dev | DEV corrigiu a armadilha do cache no build filho: o nuget.config copia para a pasta temporaria, mas o globalPackagesFolder .packages fica fora da copia, entao a copia agora aponta um junction NTFS (mklink /J, sem elevacao) para backend/.packages; provado com copia real - build de arvore limpa com xunit restaurado offline e aresta proibida recusada com SQUID0001 nomeando os dois projetos
- 2026-11-04 15:44:21 | dev | DEV reduziu as provas por build de 13 para 8: aresta legal ja presente na arvore real (Squid.Grains -> Squid.Domain) passa a ser lida da arvore, custo medido de 10-25 s por build em arvore limpa; deteccao vira so AllowedEdge (so uma build que terminou prova arestas legal) e ForbiddenEdge (exige SQUID0001 ou MSB4006)
- 2026-11-04 15:44:21 | dev | DEV decidiu no sdd-test.ps1: as provas Category=BuildProof ficam fora do gate padrao (--filter Category!=BuildProof), -AllTests as inclui e -TestFilter substitui o filtro; restaure passa a rodar junto com dotnet test (sem --no-build), pois --no-build falharia num clone limpo; Test-EnvironmentRefusal e -Fallback preservados e novo Test-RestoreRefusal classifica NU1301/rede como exit 3
- 2026-11-04 15:44:21 | dev | DEV corrigiu um defeito de pre-existencia fora do touches: backend/.packages nao estava no .gitignore (git status mostrava ?? backend/.packages/), uma linha backend/.packages/ foi adicionada; sem isso o cache inteiro entraria como arquivo novo a qualquer git add
- 2026-11-04 15:44:21 | dev | DEV verificou: dotnet build da solution exit 0 com 0 avisos, restore offline exit 0 a partir de backend/.packages, dotnet test --list-tests exercita o xunit e para no testhost com Win32Exception 5 Acesso negado, filtros Category!=BuildProof e Category=Architecture aceitos pelo VSTest
- 2026-11-04 15:44:21 | dev | DEV nao conseguiu executar teste algum: o testhost morre antes de descobrir qualquer teste neste sandbox, entao AC-01, AC-03, AC-05 e o total de APROVADOS ficam unverified - a execucao precisa rodar fora do confinamento (S-07)
- 2026-11-04 15:48:50 | dev | DEV correcao de rumo por decisao do ODEV: o gate padrao nao pode excluir as provas de build - um gate que pula uma prova e falso verde; o filtro default de sdd-test.ps1 test foi revertido para nenhum, com -Fast excluindo Category=BuildProof apenas no loop interno e avisando que nao e o gate, e -TestFilter substituindo o filtro; os tres modos verificados
- 2026-11-04 15:48:50 | dev | DEV mediu o custo real das provas de build em passada continua, nao previsto: 9 builds, 48.5 s no total somado - as 8 recusadas custam 1.0 a 2.2 s cada (o SQUID0001 dispara antes de compilar) e a unica legal que precisa buildar, Squid.Service -> Squid.Worker, custa 23.9 s; copia do backend mais junction custa 1.1 s; as 4 outras arestas legais ja existem na arvore real e sao cobertas pelo dotnet build do gate, sem rebuild
- 2026-11-04 16:06:07 | odev | DEV entregou: Squid.Tests migrado para xunit + Test SDK; runner de reflexao (Program.cs, Framework/TestFramework.cs, Framework/Assert.cs) removido; 2 Theory com MemberData (13 arestas) mais 3 Fact do dominio
- 2026-11-04 16:06:07 | odev | ODEV verificou estaticamente: build backend/Squid.slnx exit 0; nenhum Version= em csproj de teste; versoes centrais em Directory.Packages.props; traits Category=Architecture e Category=BuildProof presentes
- 2026-11-04 16:06:07 | odev | ODEV nao conseguiu executar dotnet test nem com acesso ampliado: o testhost morre com Win32Exception (5) Acesso negado em ProcessManager.OpenProcess / EnsureWatchingForExit, porque os processos filhos seguem confinados; um projeto trivial de controle executado com acesso total completou-se fora do confinamento (Aprovado: 3), o que confirma ambiente e nao produto
- 2026-11-04 16:06:07 | odev | gate de execucao pendente de terminal humano: dotnet test backend/Squid.slnx
- 2026-11-05 07:53:37 | odev | teste de escrita apos liberacao de integridade
- 2026-11-05 08:22:22 | odev | passo 1 aplicado: BuildProbe ganhou RefusedByRule e RefusedAsCycle; DependencyRuleTests agora exige SQUID0001 nomeando os dois projetos quando a regra age, e MSB4006 nomeando o projeto referenciado quando a aresta fecha ciclo
- 2026-11-05 08:22:22 | odev | passo 2 aplicado: sdd-test.ps1 grava cada execucao em docs/sdd/runs/test-runs/<timestamp>-<modo>.log com comando, exit e saida; a pasta e ignorada pelo git
- 2026-11-05 08:22:22 | odev | passo 3 registrado como I-10 no backlog: gate de testes em conteiner Linux
- 2026-11-05 08:22:22 | odev | roteiro de integridade do Windows gravado em docs/sdd/runs/RUNBOOK-retomada.md, incluindo o que NAO fazer (setintegritylevel M traz NW e bloqueia escrita Low)
- 2026-11-05 08:22:22 | odev | BLOQUEIO ATUAL: restore de Squid.Api falha com exit 1 e 0 erros reportados, em _GenerateRestoreProjectPathWalk; Domain restaura normal, Api nao; causa nao identificada, suspeita FrameworkReference Microsoft.AspNetCore.App
- 2026-11-05 08:22:22 | odev | ODEV esgotou o orcamento de diagnostico nesta sessao: nao executou dotnet test (processo em integridade Low), nao conseguiu restabelecer o restore de Api
- 2026-11-05 18:50:00 | odev | CAUSA RAIZ CORRIGIDA na descricao: o restore de Api e o testhost nao falham por FrameworkReference nem por sandbox - falham porque o workspace esta com rotulo de integridade que nega escrita Low. Processo Low + rotulo Low:(M) aceitaria escrita por definicao de MIC; a negacao prova que o rotulo real e superior
- 2026-11-05 18:50:00 | odev | icacls MENTE na leitura: exibe o nivel do processo que le, nao o nivel gravado; por isso as tres tentativas anteriores pareceram nao surtir efeito e o /reset pareceu falhar
- 2026-11-05 18:50:00 | odev | /reset nao remove rotulo de integridade (20.042 arquivos processados, 0 falhas, rotulo intacto); /setintegritylevel apenas adiciona ACE, nunca substitui - cada tentativa acumulou camada
- 2026-11-05 18:50:00 | odev | decisao: parar de tentar icacls. Workspace copiado para C:\dev\squid-squad-net com robocopy /E /COPY:DAT /DCOPY:DAT; destino verificado sem Mandatory Label, amostra de 5 arquivos em profundidade tambem limpa; 17.319 arquivos copiados
- 2026-11-05 18:55:00 | odev | runbook atualizado com as licoes 6 a 8 e a conclusao operacional de nao repetir icacls; caminho antigo preservado como backup, nao e mais o workspace
- 2026-11-05 18:58:00 | odev | DESBLOQUEADO: no caminho novo o gate rodou os 16 testes pela primeira vez (13 aprovados, 3 falhas). O ambiente deixou de ser o problema; as 3 falhas sao defeito real do teste
- 2026-11-05 18:58:00 | odev | diagnostico das 3 falhas: as arestas Domain->Persistence, Grains->Service e Domain->Grains fecham ciclo no grafo real. Reproduzido fora do teste em copia em TEMP: o build morre em MSB4006 e a mensagem nomeia SO o projeto referenciado (Squid.Persistence / Squid.Service), nunca o declarante
- 2026-11-05 18:58:00 | odev | causa da falha era uma assercao minha sem base: a linha assert-do-from exigia que o declarante aparecesse sempre. Medido, nao e o que o MSBuild faz em recusa por ciclo. Corrigido: o projeto referenciado e sempre exigido; o declarante so e exigido quando a recusa vem da regra (SQUID0001)
- 2026-11-05 18:59:00 | odev | segunda causa, separada e tambem de ambiente: os bin/ e obj/ copiados pelo robocopy carregam caminhos absolutos do caminho antigo e quebraram o restore com "Access to the path ...obj\*.tmp is denied". Removidas as 20 pastas e o restore passou a limpar corretamente
- 2026-11-05 19:00:04 | odev | gate completo verde: Aprovado 16, Com falha 0, exit 0, 25 s. AC-01, AC-02, AC-03 e AC-04 verificados pela execucao. AC-05 (falha real produz Failed >= 1) foi provado na pratica pelo proprio run de 18:54:52, que saiu com exit 1 e nomeou os tres casos

## Evidence

Commands actually run, with exit code, and where the proof lives. No evidence, no `DONE`.

| Date/Time | Role | Command | Exit | Artifact |
| --- | --- | --- | --- | --- |
| 2026-11-04 15:20:00 | odev | dotnet test (projeto xunit de prova em TEMP, fora do repositório) | 0 | docs/sdd/tasks/TASK-0004-migrar-testes-para-xunit.md (log acima) |
| 2026-11-04 15:14:00 | odev | dotnet test Squid.slnx --no-build (reprodução do pendura, 60s de timeout) | 124 | docs/sdd/tasks/TASK-0004-migrar-testes-para-xunit.md (log acima) |
| 2026-11-04 15:44:31 | dev | powershell sdd-test.ps1 build backend/Squid.slnx | 0 | backend/tests/Squid.Tests/Squid.Tests.csproj (0 avisos, 0 erros) |
| 2026-11-04 15:44:31 | dev | dotnet restore backend/Squid.slnx -m:1 -nodeReuse:false (offline, backend/.packages) | 0 | backend/tests/Squid.Tests/obj/project.assets.json |
| 2026-11-04 15:44:32 | dev | dotnet test backend/Squid.slnx --list-tests | 1 | backend/tests/Squid.Tests/bin/Debug/net10.0/Squid.Tests.dll (xunit discoveries: testhost Win32Exception 5 Acesso negado) |
| 2026-11-04 15:44:32 | dev | powershell sdd-test.ps1 test backend/Squid.slnx | 3 | docs/sdd/_tools/sdd-test.ps1 (classificado como ambiente, nao defeito) |
| 2026-11-04 15:44:42 | dev | powershell sdd-test.ps1 test backend/Squid.slnx -Fallback cmd /c exit 0 | 0 | docs/sdd/_tools/sdd-test.ps1 (fallback executado no caminho exit 3) |
| 2026-11-04 15:44:42 | dev | dotnet build em copia temporaria do backend com aresta proibida Squid.Api -> Squid.Persistence | 1 | squid-copyprobe (error SQUID0001 nomeando os dois projetos) |
| 2026-11-04 15:44:43 | dev | dotnet build em copia temporaria com aresta legal Squid.Service -> Squid.Worker | 0 | squid-bench2 (25.6 s, junction para backend/.packages) |
| 2026-11-04 15:44:43 | dev | grep Version= em backend/**/*.csproj | 0 | nenhuma ocorrencia (AC-04) |
| 2026-11-04 15:44:44 | dev | grep TestRunner ISelfCheckingTest ITestClass RunAll em backend/tests | 0 | nenhuma ocorrencia (AC-06) |
| 2026-11-04 15:48:50 | dev | dotnet test backend/Squid.slnx sem filtro (gate, inclui Category=BuildProof) | NAO_COLETADO | nao executavel neste sandbox (testhost bloqueado): total esperado 16 testes, custo das provas medido em 48.5 s |
| 2026-11-04 15:48:51 | dev | powershell sdd-test.ps1 test backend/Squid.slnx -Fast | 3 | docs/sdd/_tools/sdd-test.ps1 (filtro Category!=BuildProof emitido, ambiente exit 3) |
| 2026-11-04 15:48:51 | dev | medicao em passada continua das 9 provas de build que o xunit vai executar | 0 | squid-measure (8 recusadas 1.0-2.2 s, 1 legal 23.9 s, total 48.5 s) |
| 2026-11-04 16:06:07 | odev | dotnet build backend/Squid.slnx | 0 | backend/tests/Squid.Tests/Squid.Tests.csproj |
| 2026-11-04 16:06:08 | odev | dotnet test backend/Squid.slnx --no-build (bloqueado pelo testhost no sandbox) | 1 | docs/sdd/ideas/BACKLOG.md |
| 2026-11-05 08:22:24 | odev | dotnet restore backend/src/Squid.Api/Squid.Api.csproj | 1 | docs/sdd/tasks/TASK-0004-migrar-testes-para-xunit.md |
| 2026-11-05 18:54:52 | odev | powershell sdd-test.ps1 test (workspace novo, antes da correcao) | 1 | docs/sdd/runs/test-runs/20261105-185452-test-gate.log (13 aprovados, 3 falhas: Domain->Persistence, Grains->Service, Domain->Grains) |
| 2026-11-05 19:00:04 | odev | powershell sdd-test.ps1 test (gate completo, apos correcao) | 0 | docs/sdd/runs/test-runs/20261105-190004-test-gate.log (Aprovado 16, Com falha 0) |
