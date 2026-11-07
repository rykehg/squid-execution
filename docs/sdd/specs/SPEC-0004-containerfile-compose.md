<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-06 18:40:29

# SPEC-0004 — Runtime em contêiner: Containerfile, compose dev e trace no coletor

- requirement: REQ-0001
- phase: P-01
- dependsOn: SPEC-0002

Itens do Sprint 0 do doc 05: "`Containerfile` multi-stage com volumes nomeados" e
"`infra/compose.yaml` — `podman compose up` de dev (ADR-23)". Fecham o DoD da fase:
`podman compose up` → `/health` verde → Dashboard acessível → trace de uma request visível no
coletor (doc 05 §12.2). Regras de origem: §4.7 #4 (volume nomeado), §4.7 #5 (imagem `runtime`,
não `sdk`), §7.2 (multi-stage, não-root, rootless, portas internas fechadas), ADR-23
(`podman compose`, não Aspire).

## Behaviour

- `infra/Containerfile` multi-stage: estágio de build (SDK) e estágio final imagem `runtime`
  com usuário não-root; o binário publicado do `Squid.Service` é o conteúdo da imagem final
  (doc 05 §4.7 #5, §7.2).
- `infra/compose.yaml` sobe o serviço `squid` (API + silo num só processo, o composition root da
  SPEC-0002) e um collector OTLP de dev — a escolha da imagem do collector é por sonda do DEV
  (Jaeger all-in-one ou equivalente que aceite OTLP e permita consultar o span), no mesmo
  método das entregas anteriores.
- Todo dado persistente do serviço vive em **volume nomeado e explícito** — no MVP, o arquivo
  SQLite; um arquivo criado no volume sobrevive a `compose down` e `compose up` (§4.7 #4).
- O host exporta OTLP para o collector conforme a SPEC-0002 (`OTEL_EXPORTER_OTLP_ENDPOINT`
  apontando para o serviço do collector na rede do compose).
- No dev, a porta da API é exposta para o host; o collector é acessível para consulta do span.
  Portas internas entre serviços fechadas além do necessário (§7.2).
- DoD da fase executado de ponta a ponta: `podman compose up` → `GET /health` 200 pela porta
  exposta → Dashboard 200 → uma request gera span consultável no collector.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `podman compose up -d` | comando | dev no Windows/WSL com podman 6.x |
| in | `GET /health` (host) | HTTP via porta exposta | 200 = vivo |
| in | query do collector | API do collector | devolver o span da request |
| out | volume nomeado | arquivo SQLite persistente | sobrevive down/up |
| out | span exportado | consultável no collector | DoD da fase |

## Rules and edge cases

- Imagem final é `runtime`, nunca `sdk`, usuário não-root — violação é defeito (§4.7 #5).
- `compose down` **sem** `-v` nunca apaga o dado do volume; `down -v` é destruição explícita e
  fora do fluxo de dev.
- Se a imagem final não tiver cliente HTTP para `HEALTHCHECK`, usar alternativa disponível
  (probe baseado em `dotnet`) ou omitir o healthcheck do compose — nunca reintroduzir `sdk`
  na imagem final só para ter `curl`.
- Se `podman compose` não existir nesta máquina (podman 6.1.4 instalado; subcomando precisa ser
  provado), o DEV levanta D-NN com as opções antes de instalar qualquer ferramenta nova —
  instalar ferramenta é gate humano.
- Build que exige rede (pull das imagens base) ou falha de runtime sem diagnóstico é ambiente,
  não produto (exit 3 pelo `sdd-test.ps1`, `unverified` quando não executável).
- Collector é ferramenta de dev: não vira dependência de produção nem entra no MVP de deploy.

## Non-functional

- performance: `compose up` a partir de imagem construída é operação de dev, não medida aqui;
  build completo deve terminar dentro do tempo normal do gate.
- security: usuário não-root na imagem final; rootless (podman não exige daemon nem sudo);
  sem porta exposta além da API dev; nenhum segredo no compose (token/credencial nunca entra).
- platform: Windows/WSL com podman 6.1.4 (medido nesta máquina); Linux é o alvo de produção
  (§7.2) e a imagem deve ser portável (base multi-arch quando aplicável).

## Out of scope (explicitly)

- Caddy, TLS, quadlets/systemd — deploy de produção (§7.2, §5.2); entra quando houver fase de deploy.
- `web/` (Nuxt) e `Squid.Worker` no compose — P-08 e fase 2 (§11).
- CI (build + testes) — entrega seguinte da P-01.
- Runbooks (§8) — entrega seguinte da P-01.
- Produção/orquestração além do compose dev — ADR-23 adia Aspire; nada de K8s (fora do MVP).

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-13 | ASSUMED | imagens base públicas (SDK/runtime .NET + coletor) baixadas por rede na primeira vez; cache do podman segura as rebuilds; sem rede é falha de ambiente (exit 3), nunca defeito |
| GAP-14 | ASSUMED | ferramenta de compose: provar `podman compose` nativo do podman 6.1.4 primeiro; se inexistente, DEV levanta D-NN com opções (podman-compose, binário docker-compose) — instalar ferramenta nova é gate humano |
| GAP-09, GAP-10 | ASSUMED | herdados da SPEC-0002: endpoint OTLP por env com default; Serilog console JSON |
| GAP-15 | ASSUMED | saúde do serviço no compose: healthcheck com ferramenta da imagem `runtime` ou omitido — nunca `sdk` na imagem final só para `curl` |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0008 | Containerfile multi-stage + `infra/compose.yaml` + DoD da fase executado | yes — toca só `infra/**` (+ `_tools` se o gate precisar de modo compose), disjunto de tudo aberto | TASK_READY |
