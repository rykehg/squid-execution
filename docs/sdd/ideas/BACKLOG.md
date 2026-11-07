<!--
templateVersion: 1.0.0
-->
Ideas that are not in the MVP. A parked idea is not a promise and not a backlog item to implement now.
Write responsibility: ODEV appends; never deletes. A human promotes an idea by decision, not by editing this file.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# Ideas — parked

## Parked during interview or planning

| # | Idea | Where it came from | Suggested phase | Why not now |
| --- | --- | --- | --- | --- |
| I-01 | Bots e automação de primeira classe: ator `Service`, token com escopo, webhook assinado sobre a entrega por par (ADR-30) | doc 05 §13, feature 002 do repo de especificação | pós-MVP | exige federação e entrega por par antes de existir |
| I-02 | Eventos, metas e enquetes: `Object.Kind` + agregado de resposta por ator (ADR-31) | doc 05 §13, feature 003 | pós-MVP | amplia o modelo de objeto antes de o feed estar fechado |
| I-03 | Criptografia ponta a ponta (MLS) para mensagem 1-1 e grupo, chave por dispositivo (ADR-32) | doc 05 §13, feature 004 | fase 3 | o corpo da mensagem já nasce opaco; a federação não pode ser criptografada antes de funcionar |
| I-04 | Mutual Sharding + Reed-Solomon entre guardiões, SSS com heartbeat e re-share | doc 05 §12.3 | fase 2 | diferencial do produto, mas só demonstra com pares reais (doc 05 §1.4) |
| I-05 | App nativo mobile (Vue 3 + Capacitor + SQLite local) | doc 05 §12.3 | fase 2 | mobile do MVP é web responsivo primeiro |
| I-06 | AT Protocol como segundo protocolo federado | doc 05 §12.3 | fase 2 | a abstração de protocolo nasce com um protocolo só (ADR-11) |
| I-07 | Busca em comunidades (FTS no armazenamento local × banco gerenciado) — GAP 14 do doc 05 | doc 05 §14 | pós-MVP | não bloqueia nenhuma user story do MVP |
| I-08 | Notificações push | doc 05 §12.3 | fase 2 | dependente do app nativo (I-05) |
| I-09 | Cartões OpenGraph por tipo de mídia | doc 05 §12.3 | P-08+ | preview básico entra na P-08; variação por tipo é refinamento |
| I-10 | Gate de testes em contêiner Linux (`podman compose`): o testhost do VSTest não roda com o processo pai em integridade Low no Windows, e um contêiner não herda rótulo de integridade. Resolve o ambiente para DEV e QA de uma vez e alinha ao ADR-23 (contêiner é verificação) | conversa 2026-11-05, decisão do humano | P-02 (junto com o primeiro CI) | custo de imagem + tempo de build; o gate atual já funciona no terminal Medium e na CI, então não bloqueia a P-01 |

## Surfaced by the flow

Improvements noticed while building: a simpler way to do something already shipped, a risk worth
paying down, a pattern that repeats.

| # | Observation | Evidence | Suggestion | Cost |
| --- | --- | --- | --- | --- |
| S-01 | O pacote de execução chegou sem `docs/sdd/`, então todo o estado de execução precisa nascer do zero neste repositório | `verify-hashes.ps1` → 13/13 artefatos, 0 problemas | estado canônico passa a viver só em `docs/sdd/` (feito no bootstrap) | S |
| S-02 | `MANIFEST.md` cobre só os 13 artefatos sincronizados; README/DECISIONS/verify ficam fora da conferência | `MANIFEST.md:5-L7` | nenhuma ação: eles são handoff, não fonte | S |
| S-03 | `sdd-state.ps1` sob PowerShell 5.1 lê `-Text` na página de código ANSI e grava acentos como `?`: 19 caracteres de um arquivo de tarefa foram corrompidos em uma chamada (recuperado reescrevendo o arquivo; nenhuma linha sobreviveu corrompida) | `docs/sdd/_tools/sdd-state.ps1` (uso `-Text`); execução de 2026-11-04 12:03 | enviar texto por stdin em UTF-8 (ou corrigir o script para forçar entrada UTF-8) e usar o `.ps1` como está no Linux; nunca `-Text` com acento no Windows | S |
| S-04 | O `check` valida estrutura (seções, contagem de linhas) mas não valida encoding; um arquivo com `?` no lugar de acentos passaria | `docs/sdd/_tools/sdd-state.ps1` (comando `check`) | acrescentar ao `check` uma recusa a U+FFFD e a sequência `?` em palavras acentuadas conhecidas | S |
| S-05 | A regra `SQUID0001` em `backend/Directory.Build.targets` dispara só quando existe `ProjectReference`; `Squid.Domain` tem zero por design, então um `PackageReference` do Orleans no Domain não é avaliado pela regra de build. Não reproduzido com pacote real (sem cache NuGet e sem rede: NU1301) | `backend/Directory.Build.targets` (target `SquidCheckDependencyRule`); log de TASK-0002 | fazer o target avaliar `PackageReference` quando a camada for `domain` (e quando for qualquer camada com lista de pacotes banidos), com teste que injeta um pacote e espera `SQUID0001` | M |
| S-06 | `Squid.Tests` é um runner de reflexão próprio (sem xunit) porque não havia NuGet no ambiente; `dotnet test` não encontra provider e pendura | `backend/tests/Squid.Tests/Squid.Tests.csproj` | **em tratamento na TASK-0004**: migração para xunit + `Microsoft.NET.Test.Sdk`, com cache de pacotes em `backend/.packages` para funcionar offline | M |
| S-07 | O `testhost` do VSTest morre com `Win32Exception (5): Acesso negado` em `ProcessManager.OpenProcess` quando o processo pai está em integridade **Low**. Não é defeito de produto: o mesmo projeto roda `Aprovado: 3` num terminal Medium | prova em TEMP com projeto xunit trivial; log de diagnóstico do VSTest de 2026-11-05 | mantido Medium para o terminal e daemon de teste separado para sessões Low; documentado em `docs/sdd/runs/RUNBOOK-retomada.md` | S |
| S-08 | A regra `SQUID0001` não alcança aresta que fecha ciclo: o NuGet aborta a restauração com `MSB4006` antes de qualquer target rodar, e MSBuild não permite avaliar item list na passagem de avaliação (`MSB4099`). Medido: `Restore` no `BeforeTargets` não funciona | `backend/Directory.Build.targets`; execução de 2026-11-05 com cópia em TEMP | comportamento aceito: o teste exige `MSB4006` nomeando o projeto referenciado nesses casos; ADR se algum dia a mensagem precisar ser `SQUID0001` | S |
| S-09 | Rótulo de integridade do Windows (MIC) grudado na árvore do repositório custou uma sessão inteira e não tem conserto a partir de um processo Low: `icacls` **exibe o nível do processo que lê** em vez do valor gravado, `/reset` não toca em rótulo, e `/setintegritylevel` só adiciona ACE. Resolvido copiando a árvore com `robocopy /COPY:DAT` para outro caminho (rótulo não é copiado) | execuções de 2026-11-04/05: 5 tentativas de `icacls`, `/reset /T` em 20.042 arquivos, verificação do destino em `C:\dev` | manter o repositório em caminho fora de `C:\Users`; se a negação reaparecer, **copiar** a árvore em vez de mexer em rótulo. Detalhes em `runs/RUNBOOK-retomada.md` | S |
| S-10 | Depois de copiar o repositório, os `bin/` e `obj/` levados junto carregam caminhos absolutos do caminho antigo e quebram o restore com `Access to the path ...obj\*.tmp is denied`. O `robocopy /E` também preserva o atributo read-only, o que impede `Remove-Item -Force`; foi preciso `Directory.Delete(recursive)` | execução de 2026-11-05 18:59, logo após mover para `C:\dev` | ao mover ou copiar o repositório, apagar `bin/` e `obj/` antes de rodar restore. Vale para qualquer cópia de árvore .NET | S |
| S-11 | O gêmeo Linux/macOS do gate, `docs/sdd/_tools/sdd-test.sh`, não acha a solução: `find_sln` procura só `*.sln` e o repositório usa `backend/Squid.slnx`, então `sdd-test.sh test` sem target falha com MSB1003 rotulado como falha de produto (exit 1) | QA-0002, `docs/sdd/_tools/sdd-test.sh:20` | procurar `*.slnx` também e classificar ausência de solução como erro de uso (exit distinto), nunca como falha de produto | S |
| S-12 | O `sdd-test.sh` diverge do contrato que ele mesmo declara em `sdd-test.sh:3`: roda `dotnet test --no-build` (falharia em clone limpo), não grava log em `runs/test-runs/` nem classifica NU1301/rede como o `.ps1` | QA-0002, `docs/sdd/_tools/sdd-test.sh:64` | alinhar ao `.ps1`: restaurar junto, gravar o log no mesmo padrão e classificar falha de rede como ambiente | S |
| S-13 | `sdd-test.ps1` classificou corretamente a falha de TEMP negado como ambiente (exit 3) mas imprimiu a assinatura do testhost como motivo, porque o stack contém "handle" — motivo impresso impreciso | QA-0002, `docs/sdd/_tools/sdd-test.ps1:188` | desempatar a detecção: só aceitar a assinatura do testhost quando o processo citado for o testhost | S |
| S-14 | O detector do AC-06 (`StoreNameLiteralTests.cs:16`) é regex fixa `\b(?:actors?\|containers?)\b` — exatamente os stores de hoje; um store novo precisa entrar na lista à mão e até lá um literal dele passaria no teste | QA-0003, `backend/tests/Squid.Tests/Storage/StoreNameLiteralTests.cs:16` | registrar os nomes de store como dado (constante/registro único) e fazer o teste derivar a regex dele, ou falhar quando a lista ficar desatualizada | S |
| S-15 | `Start-Process -RedirectStandardOutput/-RedirectStandardError` devolve `Process.ExitCode` **vazio** neste sandbox (sonda isolada: sem redirect sai 7, com redirect sai vazio) — capturar exit de processo redirecionado exige `cmd /c ... && echo %ERRORLEVEL%` ou `$LASTEXITCODE` | QA-0004, `docs/sdd/qa/qa-check-task-0005.ps1` (nota do AC-05) | nos scripts de QA, capturar exit via `cmd /c` + `%ERRORLEVEL%` ou pipeline com `$LASTEXITCODE`; nunca confiar no `ExitCode` de `Start-Process` redirecionado | S |
| S-16 | Antes de aplicar mutação em arquivo de produto novo (untracked, sem baseline no git), capturar o SHA256 pré-mutação — em QA-0005 um arquivo ficou sem hash de origem e a restauração só pôde ser provada por identidade de texto (60/60 linhas) | QA-0005, finding F2, `docs/sdd/qa/QA-0005-otel-serilog-pii.md` | no passo de mutação dos scripts de QA: hash antes de quebrar, hash depois de restaurar, comparação byte-exact obrigatória | S |
| S-17 | A máquina podman deste host é **rootful** (`podman info` → `Rootless=false`, machine inspect → `Rootful=true`): a facet "rootless" estrita do AC-05 da TASK-0008 não é demonstrável aqui (as demais — imagem non-root, `privileged=false`, sem sudo — foram provadas) | QA-0007, finding F1, `docs/sdd/qa/QA-0007-containerfile-compose.md` | ao criar a máquina podman num host novo, usar `podman machine init --rootless`; no CI Linux o runner já é rootless e a facet passa a ser provável | S |
| S-18 | O OTLP exporta `url.query` **redigido** (`?qa=Redacted`) ao Jaeger — comportamento upstream do OTel (0 ocorrências de "Redacted" no backend): um marcador de teste em query string nunca casa no trace | QA-0007, finding F2, `docs/sdd/qa/QA-0007-containerfile-compose.md` | gates de span localizam por janela de tempo + `user_agent` (como QA-0007 fez) ou por operação; nunca por valor de query | S |
| S-19 | O `.dockerignore` da raiz usa o padrão `.vs/`, que só exclui o `.vs` de nível raiz — `backend/.vs/` (criado ao abrir a solution no Visual Studio) entra no contexto de build; com o VS aberto os `.vsidx` têm lock exclusivo e `podman compose build` falha no COPY com `permission denied` (build_exit=125) | TASK-0011 interim 2026-11-07 (DEV) | trocar por `**/.vs` no `.dockerignore` (fora dos toques da TASK-0011, virou backlog); enquanto o VS estiver aberto, compose build é ambiente (exit 3) | S |

## Promoted

An idea becomes work only here, with a human decision recorded.

| # | Promoted to | Decided by | Date/Time |
| --- | --- | --- | --- |
| NENHUM | — | — | — |