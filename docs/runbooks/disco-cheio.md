# Runbook — disco cheio

- fase: P-01 · status: EXECUTÁVEL · origem: doc 05 §8, SPEC-0006
- comandos `podman compose` rodam da raiz do repositório com `-f infra/compose.yaml`

## Sintoma

Erro `no space left on device` em build, pull ou push de imagem; contêiner que não sobe ou
morre ao gravar; volume que para de aceitar escrita; `podman system df` com RECLAIMABLE alto
e o disco do host no fim.

## Diagnóstico

Rode na ordem, antes de apagar qualquer coisa:

1. `podman system df` — **EXECUTÁVEL**. Quanto imagens, contêineres e volumes ocupam e o que
   uma limpeza devolveria (coluna RECLAIMABLE).
2. `df -h` — **EXECUTÁVEL**. Espaço do filesystem onde o podman guarda dados. No Windows/WSL
   use `wsl df -h` (o PowerShell não tem `df`); em Linux use `df -h` — mesma saída. Os dois
   formulários foram executados na prova (o de Linux, pela WSL).
3. `podman volume ls` — **EXECUTÁVEL**. `infra_squid-data` tem de estar listado.

## Procedimento

1. Pare o stack — **EXECUTÁVEL**: `podman compose -f infra/compose.yaml down`
   (mantém o volume; sem `-v`).
2. Libere espaço. Aqui tudo é destrutivo — por isso nenhum destes comandos é executado na
   prova (marca **NÃO-executar**): cada um é decisão consciente do operador, nunca rotina.

   | Comando | Marca | O que apaga | Cuidado |
   | --- | --- | --- | --- |
   | `podman system prune` | **NÃO-executar** | contêineres parados, imagens órfãs, redes sem uso e cache de build | volumes não são afetados sem `--volumes`; pede confirmação |
   | `podman system prune --volumes` | **NÃO-executar** | o de cima + volumes não referenciados | com o stack parado `infra_squid-data` está "não referenciado": apagar é perda de dados |
   | `podman volume prune` | **NÃO-executar** | volumes não referenciados | mesmo risco da linha acima |
   | `podman image prune -a` | **NÃO-executar** | imagens sem tag e não usadas | obriga rebuild depois |
   | `podman compose -f infra/compose.yaml down -v` | **NÃO-executar — DESTRUTIVO** | para o stack E apaga o volume `infra_squid-data`, com o dado dentro | destruição de dados: só com decisão explícita, nunca como rotina |

3. Suba o stack de novo e confira (seção seguinte).

## Verificação

1. `podman compose -f infra/compose.yaml up -d` — **EXECUTÁVEL**.
2. `curl -fsS http://localhost:8080/health` — **EXECUTÁVEL** — exit 0 (HTTP 200) de novo.
   No Windows use `curl.exe -fsS http://localhost:8080/health` (o `curl` do PowerShell é um
   alias de Invoke-WebRequest); em Linux use `curl -fsS http://localhost:8080/health`.
   Os dois formulários foram executados na prova (o de Linux, pela WSL).
3. `podman volume ls` — **EXECUTÁVEL** — `infra_squid-data` presente e com o dado lá dentro:
   nenhum `down` deste runbook usou `-v`.
