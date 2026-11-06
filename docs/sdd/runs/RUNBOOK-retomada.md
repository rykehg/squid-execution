# Runbook de retomada — Squid (sdd-odev)

Para quem (humano ou agente) entra no projeto sem contexto. Máximo 1 minuto de leitura.

## Onde está a verdade

| Fonte | Papel |
| --- | --- |
| `docs/sdd/tasks/TASK-NNNN-*.md` | **estado canônico** da execução: control block, critérios de aceite, log append-only, evidências |
| `docs/sdd/plan/plan.md` | visão corrente: fases, estado atual, próximo passo |
| `docs/sdd/specs/`, `docs/sdd/requirements/` | o que foi resolvido e quais gaps estão em vigor |
| `docs/features/05-arquitetura-consolidada.md` | documento mestre (ADRs, padrões obrigatórios, sprints) |
| `docs/memory-bank/` | documentação viva para LLM; **nunca** estado de tarefa |

## Como retomar

1. Leia `docs/sdd/plan/plan.md` → seção `## Current state`.
2. Abra a tarefa mais recente em `docs/sdd/tasks/` e confira `status`, `nextAction`, `blocker`.
3. Rode `docs/sdd/_tools/sdd-state.ps1 check <tarefa>` — exit 0 significa estrutura íntegra.
4. Só depois leia a spec que a tarefa aponta. Não leia a árvore inteira.

## Ferramentas do fluxo

```powershell
# estado canônico (nunca editar o markdown à mão)
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check <tarefa>
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 set <tarefa> status AWAITING_QA
Get-Content -LiteralPath <tmp-utf8> -Encoding UTF8 | powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 append-log <tarefa> <papel>
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 append-evidence <tarefa> <papel> <comando> <exit> <artefato>
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 build|test
```

`append-log` com `-Text` acentuado **corrompe o arquivo** sob PowerShell 5.1 (ele lê ANSI).
Passe texto por stdin em UTF-8, como no exemplo. Registro: `docs/sdd/ideas/BACKLOG.md` S-03.

## Armadilhas conhecidas

- `pwsh` não está instalado: use `powershell` (5.1) ou o `.sh` no Linux/WSL.
- Falha de build/teste por pipe ou handle, sem diagnóstico real, é ambiente: use `sdd-test.ps1`,
  registre `unverified` e não abra defeito de produto.
- Nada de `git commit`/`push` sem decisão humana: é gate.
- Referências externas (Mastodon, Friendica, orders-orleans, federation) servem como ideia e nome;
  código delas nunca é copiado para cá.

## Integridade do Windows e o `testhost` (leia antes de "consertar" permissão)

Sintoma, ao rodar `dotnet test`:

```
System.ComponentModel.Win32Exception (5): Acesso negado
  at System.Diagnostics.ProcessManager.OpenProcess(...)
  at ...TestHost.DefaultEngineInvoker.SetParentProcessExitCallback(...)
```

Causa medida: o `testhost` abre o handle do **processo pai** para monitorá-lo. No Windows, um
processo de integridade **Low** não abre handle de processo **Medium**. A negação é do rótulo do
Windows (MIC), não de ACL de arquivo — por isso mexer só em ACL de pasta não resolve.

Diagnóstico em 30 segundos:

```powershell
cmd /c "whoami /groups | findstr S-1-16"    # Low = S-1-16-4096 · Medium = S-1-16-8192
icacls . | findstr Mandatory
```

Regras aprendidas na prática (2026-11-04/05), cada uma custou tempo:

1. `icacls <pasta> /setintegritylevel M /T` sobe o rótulo dos **arquivos**, não do processo já em
   execução. Um PowerShell aberto antes continua Low; teste sempre num PowerShell **novo**.
2. `M` vem junto com `(NW)` = No Write Up, que **bloqueia escrita de processos Low** — inclusive o
   agente do harness. Resultado observado: o agente perdeu escrita no repositório.
3. **Nunca** conceda `Everyone:...(DC)` nem deixe `Everyone:(DENY)(DC)`: a negação de Delete Child
   impede criar e mover arquivo, e a ferramenta de estado (grava temp + move) falha com
   `UnauthorizedAccessException`.
4. Rótulos duplicados **coexistem** e o mais restritivo vence: `Low:(M)` + `Medium:(NW)` juntos
   continuam negando escrita.
5. Estado escolhido para este repositório: **tudo em Low**, escrita livre para todos. Consequência:
   `dotnet test` **não roda** na sessão do agente. Para rodar a suíte, use um terminal Medium, ou o
   contêiner planejado em `BACKLOG.md` I-10.
6. **`icacls` MENTE sobre o rótulo.** Quando o rótulo real é *superior* ao do processo que lê, o
   `icacls` exibe o **próprio nível do processo** no lugar do valor gravado. Um processo Low lendo uma
   pasta `Medium:(NW)` vê `Low:(NW)`. Não confie na linha `Mandatory Label` — o valor mostrado pode
   não ser o valor real. (Medido em 2026-11-05.)
7. **`/reset` não remove rótulo de integridade.** Rodamos `icacls <repo> /reset /T` (20.042 arquivos,
   0 falhas) e o `Mandatory Label` continuou idêntico. `/reset` restaura a DACL, não a SACL.
   `/setintegritylevel` **adiciona** uma ACE, nunca substitui — cada tentativa deixou uma camada nova.
8. **Diagnóstico decisivo:** processo Low + rótulo `Low:(M)` **aceita** escrita, por definição de MIC.
   Se a escrita é negada, o rótulo real não é `Low`. Não perca tempo rebaixando o nível — rebaixar
   não apaga o `(NW)` já herdado, e é isso que nega.

### Conclusão operacional

Não existe comando `icacls` que conserte isso a partir de um processo de integridade Low: qualquer
leitura do rótulo vem distorcida e qualquer escrita acumula camadas em vez de limpar. Já custou 5
tentativas e 2 conselhos errados meus. **Não tente de novo.**

A saída que funciona é **mover o workspace para um caminho que nunca recebeu rótulo**, copiando sem
o rótulo de integridade. Foi feito em 2026-11-05:

```powershell
robocopy "<origem>" "C:\dev\squid-squad-net" /E /COPY:DAT /DCOPY:DAT
```

`/COPY:DAT` preserva Dados, Atributos e Datas — e **não** copia o rótulo de integridade, que não faz
parte da DACL. O destino nasce com o padrão do Windows. Verificação que confirma:

```powershell
icacls "C:\dev\squid-squad-net" | findstr Mandatory   # deve sair vazio
```

O caminho antigo (`C:\Users\Ryke\Projetos\squid-squad-net`) fica intacto como backup, ainda com o
rótulo. Não é mais o workspace.

Antes de rodar qualquer coisa no caminho novo, confirme que o **DSH está aberto nele** — a sandbox
indexa por workspace; sessão aberta no caminho antigo não escreve no novo.

## Logs de execução de teste

`sdd-test.ps1 test` grava cada execução em `docs/sdd/runs/test-runs/<timestamp>-<modo>.log`, com
comando, exit code e saída completa. A pasta é ignorada pelo git: o que vale fica no `## Evidence`
da tarefa, não o log bruto.