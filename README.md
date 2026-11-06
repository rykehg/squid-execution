# Squid

Rede social open-source, federada e modular: **mensagens**, **grupos** (círculos de amigos) e
**comunidades** (interesses em comum) em uma instância auto-hospedada, com política de acesso
versionada, identidade recuperável e federação por ActivityPub.

- Sem criptografia ponta a ponta no MVP — declarado na UI e no termo.
- Sem e-mail e sem telefone na conta: a recuperação de identidade é o **kit**, não um reset.
- Feed cronológico, sem ranqueamento.
- Interoperabilidade com Mastodon e Friendica é **critério de aceite** da fase de federação.

## Estado atual

O repositório acabou de nascer: fase **P-01 (fundação)**. O estado canônico de execução — fases,
tarefas, QA e evidências — vive em [`docs/sdd/`](docs/sdd/README.md); o plano corrente é
[`docs/sdd/plan/plan.md`](docs/sdd/plan/plan.md).

Documentação de referência neste repositório:

| Onde | O que é |
| --- | --- |
| `docs/features/05-arquitetura-consolidada.md` | documento mestre: domínio, protocolos, execução, ADRs 1..36, sprints |
| `docs/features/03-*.md`, `04-*.md` | detalhe: padrões herdados da análise e hierarquia de chaves |
| `docs/constitution.md` | princípios e definição de pronto |
| `specs/001-squid-mvp/` | spec, plano, research, modelo de dados, contratos e matriz de quickstart |
| `docs/sdd/` | plano, requisitos, spec, tarefas, QA e tools do fluxo de execução |

## Estrutura

```text
backend/     solution .NET (silo, API, worker e cliente de teste) — 10 projetos
web/         front Nuxt 3 + Vue 3, SSR (fase posterior)
infra/       Containerfiles, compose, quadlets
docs/        features (documento mestre), sdd (fluxo de execução), memory-bank
specs/       especificação do MVP (cópia unidirecional do repositório de especificação)
```

## Como rodar

Requisitos: .NET SDK 10, Node 20+ (só a partir da fase do front), Podman (só para verificação em
container) e PowerShell 5.1 — o Windows já traz. `pwsh` é opcional: os comandos abaixo funcionam
nos dois.

```powershell
dotnet build backend/Squid.slnx     # build da solution
dotnet test  backend/Squid.slnx     # suíte de testes
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-state.ps1 check <task>   # confere o estado de uma tarefa
```

`backend/` recebe a solution na entrega TASK-0002 da P-01; `web/` só na fase do front.

## Fluxo de execução

Cada entrega é uma `TASK-NNNN` com control block, critérios de aceite e log append-only em
`docs/sdd/tasks/`. A leitura do fluxo está em [`docs/sdd/README.md`](docs/sdd/README.md).