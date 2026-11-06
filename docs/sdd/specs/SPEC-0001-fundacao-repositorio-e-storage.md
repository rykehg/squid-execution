<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-04 12:06:31

# SPEC-0001 — Fundação: repositório, solution e storage com ETag

- requirement: REQ-0001
- phase: P-01
- dependsOn: NENHUM

Tradução do material adotado para o formato do fluxo. O que já está decidido no doc 05 não é
reentrevistado: cada regra abaixo cita a origem em nome de seção e não copia texto de referência
externa (Mastodon, Friendica, orders-orleans, federation).

## Behaviour

- O repositório tem o produto na raiz: `backend/`, `web/`, `infra/`, `docs/` — `web/` e `infra/`
  existem como pasta reservada na P-01, sem código (ADR-21).
- O que nunca é versionado: `bin/`, `obj/`, `node_modules/`, `*.db`, `*.db-wal`, `*.db-shm`,
  `.env`, artefatos de cobertura e qualquer checkout de referência (ADR-26).
- `backend/Squid.slnx` lista exatamente 10 projetos: `Squid.Domain`, `Squid.Grains`,
  `Squid.Persistence`, `Squid.Protocols`, `Squid.ReadModels`, `Squid.Api`, `Squid.Service`,
  `Squid.Worker`, `Squid.PeerClient` e `Squid.Tests` (doc 05 §11).
- Toda versão de pacote vive em `Directory.Packages.props` (central package management); nenhum
  `.csproj` carrega `Version=` (ADR-25).
- A regra de dependência é **violação de build**, com mensagem que nomeia os dois projetos: `Domain`
  não referencia Orleans, JSON-LD nem I/O; `Grains` referencia `Domain` e não referencia banco;
  `Api` referencia `Grains` e `ReadModels`; `Persistence` implementa `IGrainStorage`; `Service` e
  `Worker` são as únicas composition roots.
- `IGrainStorage` sobre SQLite impõe ETag: escrita com versão esperada divergente não grava e
  lança `InconsistentStateException` (doc 05 §4.7 #1).
- O nome do store nunca é hardcoded no SQL: ele é parâmetro, e gravações de stores diferentes não
  colapsam na mesma partição lógica (doc 05 §4.7 #2).
- Leitura de estado inexistente devolve ausência, nunca uma `InconsistentStateException` inventada.
- ETag, estado do grain e versão são tratados como **binário/estrutura opaca**, nunca interpretados
  pelo storage; nenhum blob de mídia entra no JSON de estado (doc 05 §4.7 #10).

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `ReadStateAsync(stateName, grainId)` | `string`, `GrainId` | store name sempre um argumento explícito; nunca interpolado de campo do grain |
| out | `IGrainState` | estado desserializado + `ETag` | ausente → `State = null`, `ETag = null` |
| in | `WriteStateAsync(stateName, grainId, grainState)` | estado + `ETag` esperado | `ETag` divergente → `InconsistentStateException`, nada gravado |
| in | `ClearStateAsync(stateName, grainId, grainState)` | estado + `ETag` esperado | mesma regra de ETag da escrita |
| out | build | código de saída do `dotnet build` | projeto violando a regra de dependência → exit ≠ 0 |

## Rules and edge cases

- Dois escritores com a mesma versão esperada: o primeiro grava, o segundo recebe
  `InconsistentStateException`; o estado gravado é o do primeiro, nunca um merge silencioso.
- Escrita com `ETag` nulo sobre linha existente: recusada como inconsistência, não sobrescreve.
- Dois stores com o mesmo `grainId` (ex.: `actors` e `containers`): linhas distintas, cada uma com
  a própria versão; ler um nunca devolve o outro.
- Store inexistente (nenhuma linha): leitura vazia, não erro.
- Versão de schema futura (arquivo de banco de uma versão mais nova): recusa explícita com erro
  claro, em vez de corromper o estado.
- empty / missing / duplicated / unauthorized / slow: leitura vazia → ausência; gravação duplicada
  no mesmo ETag → `InconsistentStateException`; acesso concorrente → serializado por transação
  SQLite; banco lento ou travado → erro logado sem `catch {}` silencioso (doc 05 §4.7 #7).

## Non-functional

- performance: sem alvo numérico na P-01; o alvo do feed (p95 < 1 s com 10k publicações) é da P-04.
- security: nenhum detalhe interno de erro vaza para o cliente (doc 05 §4.7 #6); PII sanitizada em
  log e em span tags (doc 05 §4.7 #12) quando a telemetria entrar nesta fase.
- accessibility / platform: build e suíte rodam em `dotnet build` / `dotnet test` no Windows e no
  Linux; container é verificação, não ambiente de desenvolvimento (ADR-24).

## Out of scope (explicitly)

- Orleans silo, `/health`, Dashboard e OTel — entregas seguintes da P-01, não desta spec.
- Endpoint HTTP, autenticação, Argon2id — P-02.
- Projeção de leitura com Dapper e qualquer query de domínio — P-04.
- Oracle ATP: a interface e a suíte de contratos nascem preparadas para ele, mas o job agendado só
  existe quando houver credencial (GAP-05).
- Front Nuxt: `web/` fica vazio na P-01 (GAP-06).
- Truncamento de log de eventos (doc 05 §4.7 #3): depende do event sourcing da P-02, que é quem
  cria o log.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-02 | ASSUMED | fonte pública `nuget.org`, `nuget.config` versionado no backend |
| GAP-04 | ASSUMED | `etag` = `GrainStateVersion` em base64; comparação exata em `WHERE Version = @Version` |
| GAP-05 | ASSUMED | SQLite é o gate do CI; Oracle em job agendado quando houver credencial |
| GAP-06 | ASSUMED | `web/` e `infra/` como pasta reservada (`web/.gitkeep`, `infra/.gitkeep`) |
| GAP-01, GAP-03 | OPEN | pertencem à P-02, não a esta spec |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0001 | Fundação do repositório: git, `.gitignore`, estrutura de pastas | no — base de todos | TASK_READY |
| TASK-0002 | Solution e regra de dependência verificada pelo build | no — blockedBy TASK-0001 | TASK_READY |
| TASK-0003 | Storage SQLite com ETag imposto | no — blockedBy TASK-0002 | TASK_READY |