<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-06 09:34:27

# SPEC-0002 — Runtime: silo, saúde, dashboard e observabilidade

- requirement: REQ-0001
- phase: P-01
- dependsOn: SPEC-0001

Restante do Sprint 0 do doc 05 (§10, §11, checklist "Sprint 0") relativo a fazer a instância
subir, responder e ser observável. Escopo confirmado pelo humano em 2026-11-06: a P-01 é o
Sprint 0 inteiro; esta spec cobre as duas primeiras entregas. O que já está decidido no doc 05
não é reentrevistado: cada regra cita a origem em nome de seção.

## Behaviour

- O processo de `Squid.Service` sobe silo Orleans e a API Minimal no mesmo host — composition
  root é o único lugar que conhece todas as camadas (doc 05 §11).
- O silo usa o `IGrainStorage` SQLite com ETag imposto entregue pela SPEC-0001 como storage dos
  grains (registro no arranque do silo).
- `GET /health` (liveness) devolve 200 enquanto o processo está vivo, sem tocar banco nem rede
  externa (doc 05 §10 — Health).
- `GET /ready` (readiness) devolve 200 quando o storage SQLite é acessível e 503 quando não é
  (doc 05 §10 — dependências).
- Orleans Dashboard responde em rota local e mostra o estado do silo (ativações, filas); sem
  autenticação, rede interna (doc 05 §10 — Dashboard).
- Toda request HTTP gera span do OpenTelemetry exportado por OTLP para
  `OTEL_EXPORTER_OTLP_ENDPOINT` (doc 05 §10 — Tracing).
- Logs estruturados Serilog com sanitização de PII: email, chave e payload nunca aparecem crus
  em log (doc 05 §10 — Logs; §4.7 padrão de PII).
- Falha do collector OTLP não derruba a instância: as requests seguem respondendo e o erro de
  export vira warning no log.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `OTEL_EXPORTER_OTLP_ENDPOINT` | env, URL | default `http://localhost:4317` (padrão OTLP) |
| in | `GET /health` | HTTP | 200 sempre que o processo vive |
| in | `GET /ready` | HTTP | 200 / 503 conforme acessibilidade do storage |
| out | Dashboard | HTTP, rota local | sem auth; loopback/dev |
| out | span OTLP | protobuf sobre HTTP | exportação best-effort |
| out | log Serilog | JSON estruturado | PII mascarada |

## Rules and edge cases

- Porta HTTP ocupada: o arranque falha com erro claro e exit ≠ 0 — não fica pendurado nem finge
  subir.
- Collector OTLP fora do ar: instância segue no ar; aviso no log; nenhum `catch {}` silencioso.
- Arquivo de storage inacessível ou removido: `/ready` devolve 503, `/health` continua 200.
- PII em log: mascarada; `ex.ToString()` nunca vai para o cliente (ADR já adotado).
- Dashboard fora do loopback na configuração dev: não é publicado.

## Non-functional

- performance: `/health` responde em < 100 ms, sem I/O de banco.
- security: sem autenticação no dashboard (decisão do doc 05: rede interna); expor publicamente
  é trabalho do reverse proxy na entrega de container, não desta spec.
- platform: `net10.0`; dev em Windows, Linux via contêiner é entrega seguinte da P-01.

## Out of scope (explicitly)

- `Containerfile`, `infra/compose.yaml`, Caddy — entrega seguinte da P-01.
- CI e runbooks (§8) — entregas seguintes da P-01.
- Domain `ContainerPolicy` + `IPolicyEvaluator` + testes da matriz — entrega seguinte da P-01.
- Checagem de blobs/mídia em `/ready` — não existe mídia ainda; entra quando a mídia existir.
- TLS, auth no dashboard, exporter de métricas Prometheus, collector OTLP real no dev (o teste
  usa um receiver local; o collector real vem com o compose).

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-08 | ASSUMED | pacote de Orleans Dashboard compatível com Orleans 10.2: DEV escolhe por sonda da API real (mesmo método da TASK-0003); se nenhum existir, sobe D-NN antes de desistir do Dashboard |
| GAP-09 | ASSUMED | endpoint OTLP por env `OTEL_EXPORTER_OTLP_ENDPOINT`, padrão `http://localhost:4317` |
| GAP-10 | ASSUMED | sink do Serilog: console JSON estruturado no dev; sinks remotos entram com a entrega de container/CI |
| GAP-01..07 | — | resolvidos ou assumidos nas specs/tarefas anteriores |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0005 | Silo Orleans + `/health` + `/ready` + Orleans Dashboard | no — blockedBy TASK-0003, TASK-0004; TASK-0006 toca os mesmos arquivos | TASK_READY |
| TASK-0006 | OTel com exporter OTLP + Serilog com sanitização de PII | no — blockedBy TASK-0005 | TASK_READY |
