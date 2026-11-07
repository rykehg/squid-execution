# Runbook — restaurar backup

- fase: P-01 · status: PARCIAL · origem: doc 05 §8, SPEC-0006
- executável hoje: o ciclo arquivo → volume · pendente: gerar o backup (P-07)
- comandos `podman compose` rodam da raiz do repositório com `-f infra/compose.yaml`

## Sintoma

O SQLite do serviço está perdido, corrompido ou trocado e existe um arquivo de guarda
(`squid-grainstate.db`) para devolver ao volume. Sinal: `/ready` responde 503 enquanto
`/health` continua 200 — processo vivo, storage inacessível.

## Diagnóstico

1. `curl -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0: o processo está vivo.
2. `curl -fsS http://localhost:8080/ready` — **EXECUTÁVEL** — exit 0: storage acessível.
   No caso que abre este runbook a resposta é 503 e, com `-f`, o curl termina com exit 22
   (é o comportamento documentado do `curl -f` em resposta 4xx/5xx — daí o procedimento abaixo).
   No Windows use `curl.exe -fsS http://localhost:8080/ready`; em Linux use
   `curl -fsS http://localhost:8080/ready`. Os dois formulários foram executados na prova
   (o de Linux, pela WSL).

## Procedimento

Convenção: tudo da raiz do repositório, com o arquivo de backup em `./squid-grainstate.db`
(este runbook usa esse nome; se o seu arquivo tem outro, troque no comando do passo 3).

### Trecho executável — ciclo arquivo → volume (existe hoje)

1. Pare o serviço — **EXECUTÁVEL**: `podman compose -f infra/compose.yaml down`
   (sem `-v`: o volume `infra_squid-data` é mantido).
2. Monte o contêiner de apoio sobre o volume — **EXECUTÁVEL**:
   `podman create --name squid-restore -v infra_squid-data:/data squid-service:dev`
   (resto de execução interrompida: `podman rm squid-restore` antes — é o comando do passo 4).
3. Devolva o arquivo para o volume — **EXECUTÁVEL**:
   `podman cp ./squid-grainstate.db squid-restore:/data/squid-grainstate.db`
   Atenção: isso sobrescreve o arquivo que está no volume.
4. Remova o contêiner de apoio — **EXECUTÁVEL**: `podman rm squid-restore`
5. Suba o serviço — **EXECUTÁVEL**: `podman compose -f infra/compose.yaml up -d`

### Pendente — gerar o backup (P-07)

**PENDENTE — P-07 — sem passos.** Em P-01 não existe geração de backup: o envelope cifrado
com época de chave e o restore drill no CI são da fase P-07. Este trecho é escopo, não
procedimento (GAP-18) — este runbook não manda a criar arquivo nenhum hoje.

Ponteiros: `docs/sdd/plan/plan.md` (P-07), `specs/001-squid-mvp/contracts/backup-recovery.md`,
spec de backup (SPEC-0007, a cortar).

## Verificação

1. `curl -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0.
2. `curl -fsS http://localhost:8080/ready` — **EXECUTÁVEL** — exit 0: storage acessível de novo.
3. `podman volume ls` — **EXECUTÁVEL** — `infra_squid-data` presente.
