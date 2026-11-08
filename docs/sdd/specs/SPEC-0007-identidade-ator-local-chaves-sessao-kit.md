<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-07 16:16:12

# SPEC-0007 — Identidade: ator local, chaves, sessão, descoberta e kit

- requirement: REQ-0001
- phase: P-02
- dependsOn: SPEC-0001 (solution, storage SQLite com ETag), SPEC-0002 (silo + `Squid.Service` + API host)

Meta da fase (doc 05 §Sprint 1): existe um ator local com perfil, chaves e descoberta.
DoD do Sprint 1: `webfinger` resolve; perfil é servido em JSON-LD; kit baixado restaura a `RK` em teste.

## Behaviour

- B-01: `POST /api/auth/register` cria um ator local com `handle` único no domínio
  (`[a-z0-9_]{3,30}`, imutável); duplicidade responde erro claro (FR-001).
- B-02: no registro o sistema gera `RK` (32 bytes CSPRNG), deriva a Ed25519 por
  `HKDF(RK, "fed/ed25519-sign")`, gera o par RSA-2048 e o guarda somente embrulhado por
  `HKDF(RK, "fed/rsa-wrap")`, e gera a `RC` independente da `RK` (FR-004, GAP-01, GAP-03).
- B-03: a `RK` em claro nunca aparece em resposta, log ou span — o único portador é o kit (FR-004).
- B-04: senha com no mínimo 10 caracteres, guardada como hash Argon2id; a senha nunca é
  gravada nem logada (FR-002).
- B-05: `POST /api/auth/login` valida e devolve a sessão em cookie `squid_session`
  (`HttpOnly`, `Secure`, `SameSite=Lax`, 30 dias deslizantes) + cookie `squid_csrf`; login com
  usuário ou senha errados devolve a **mesma** resposta 401 (não enumera conta) (FR-002, R-09).
- B-06: toda mutação exige `X-CSRF-Token` igual ao cookie `squid_csrf` (dupla submissão); ausente
  ou diferente → 403.
- B-07: `POST /api/auth/logout` revoga a sessão atual; `POST /api/auth/logout-all` revoga todas as
  sessões do ator — cookie antigo passa a responder 401 (FR-002).
- B-08: `ActorGrain` é event-sourced: eventos por ator, snapshot periódico e **truncamento** do log
  após o snapshot; o estado reconstruído é idêntico ao estado direto (doc 05 §Sprint 1; padrão §4.7).
- B-09: `GET /.well-known/webfinger?resource=acct:<nome>@<domínio>` resolve ator local conhecido
  devolvendo `subject` + `link rel=self` (`application/activity+json`) para `/users/<nome>`;
  desconhecido → 404.
- B-09a: **fonte do domínio da instância (D-01 da TASK-0015, resolvido na spec 2026-11-07)**:
  o domínio é o `Host` da própria requisição (origem = scheme do request + `Host`), **sem** chave
  de configuração — o requirement torna o domínio público dependência de implantação
  (`specs/001-squid-mvp/spec.md` §Dependencies: "domínio público próprio apontando para a
  instância"), logo quem responde já está no domínio certo; `resource=acct:<nome>@<outro
  domínio>` → 404 sem regra nova. `subject`, `link.href` e o `id` do JSON-LD derivam todos dessa
  mesma origem (um só lugar, consistente). `X-Forwarded-Proto/Host` ficam para a fase de proxy
  atrás do Caddy — parked I-11.
- B-10: `GET /users/<nome>` (sem sessão) serve o perfil em JSON-LD com as chaves públicas
  (RSA-2048 `#main-key` + Ed25519) expostas para verificação por terceiros (FR-003).
- B-11: round-trip canônico ↔ JSON-LD do perfil é isento de perda (id → JSON-LD → id igual).
- B-12: `POST /api/recovery/kit` gera o kit e devolve o mnemônico de 24 palavras **uma única vez**;
  `mnemonic` não é recuperável depois da resposta (FR-005).
- B-13: `POST /api/recovery/kit/verify` marca o kit como verificado; `GET /api/auth/me` avisa de
  forma persistente enquanto o kit não foi verificado ou tem mais de 12 meses (FR-007).
- B-14: `POST /api/recovery/import` em instância limpa (banco novo) com kit + senha restabelece a
  **mesma** identidade: mesmo `handle`, mesma chave pública Ed25519 e mesma RSA (FR-006).
- B-15: import com senha errada falha sem destruir nenhum dado existente (invariante do data-model §2.12).
- B-16: a conta não coleta nem exige e-mail ou telefone, e nenhuma rota oferece reset por e-mail (FR-062).

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `POST /api/auth/register` | `{ handle, password, displayName? }` | `handle` `[a-z0-9_]{3,30}` único; `password` ≥ 10 |
| out | `POST /api/auth/register` | sessão (cookies) + kit **uma vez** | duplicidade → 409 com mensagem clara |
| in | `POST /api/auth/login` | `{ handle, password }` | erro único 401 para usuário inexistente ou senha errada |
| out | `POST /api/auth/login` | cookies `squid_session` + `squid_csrf` | `SameSite=Lax`, 30 dias deslizantes |
| in | `GET /.well-known/webfinger` | `?resource=acct:<nome>@<domínio>` | domínio ≠ local → 404 (sem proxy no MVP) |
| out | `GET /.well-known/webfinger` | `{ subject, links[] }` | desconhecido → 404 |
| out | `GET /users/<nome>` | JSON-LD do perfil + chaves públicas | sem sessão; desconhecido → 404 |
| out | `POST /api/recovery/kit` | `{ recoveryCredential, mnemonic[24], encryptedRootKey, createdAt }` | `mnemonic` só nesta resposta |
| in | `POST /api/recovery/import` | kit + senha | senha errada → erro, nada destruído |
| out | `GET /api/auth/me` | ator + avisos pendentes | kit não verificado/velho → aviso |

## Rules and edge cases

- `handle` duplicado no registro → 409, mensagem clara, nada é criado (FR-001).
- `handle` malformado → 422 com a regra `[a-z0-9_]{3,30}` na mensagem.
- senha < 10 caracteres → 422.
- login com credenciais erradas → 401 idêntico nos dois casos (não enumera).
- sessão revogada (`logout`/`logout-all`) → 401; CSRF ausente/divergente → 403.
- webfinger/usuário desconhecido, ou domínio que não é o desta instância → 404.
- import com senha errada → erro sem tocar no estado existente; import duas vezes → idempotente no
  resultado (mesma identidade).
- evento de ator com `handle` já tomado por outro ator → rejeição no grain (índice único), não
  chega a persistir.
- snapshot: ao cruzar o limiar, grava snapshot e **trunca** o log — reconstruir após truncamento
  produz estado idêntico (o teste falha se o truncamento perder evento).
- PII: senha, `RK`, mnemônico e `RC` nunca aparecem em log, span tag ou resposta de erro.

## Non-functional

- performance: registro, login e webfinger respondem em < 500 ms sob o gate local (sem carga formal
  nesta fase).
- security: Argon2id (parâmetros padrão do BouncyCastle, registrados no código); cookies `Secure`;
  `Rk` jamais em claro (FR-004); gate `sdd-test.ps1 test` exit 0.
- platform: sem dependência de e-mail/telefone; funciona em instância limpa (DoD do kit).
- arquitetura (D-01, corrigido 2026-11-07): o módulo de chaves vive em `Squid.Api/Crypto/**` com o
  `PackageReference` em `Squid.Api.csproj` e a versão **só** em `Directory.Packages.props` (ADR-25).
  O `Squid.Domain` mantém **zero** pacotes — `DomainHasNoInfrastructureTests` (`Assert.Empty`) é
  contrato QA-passed da TASK-0002 com authority CODE e não é relaxado; `Squid.Domain/Crypto/**`
  está errado nesta spec e foi corrigido. Se um consumidor fora de `Squid.Api` aparecer (ex.:
  `Squid.Protocols` na P-06, que não pode referenciar `Squid.Api`), o módulo vira projeto leaf
  próprio nessa fase — registrado aqui para a spec da P-06 ler.

## Out of scope (explicitly)

- Federação: assinatura de saída, `HTTP Signature` e verificação de entrada (P-06).
- `rel=profile` no webfinger e `inbox`/`outbox`/`followers`/`following` no documento do ator —
  o contrato `activitypub.md` §1/§1.1 lista-os como **estado final**, mas nenhuma rota deles existe
  ainda e B-09/B-10 não os exigem: entram em P-06 (rotas de federação) e P-08 (`rel=profile` com o
  front) — backlog S-26, levantado pela QA-0014 como `[DIVERGENCIA]` e adjudicado aqui 2026-11-07.
- `POST /api/actors/{handle}/follow|unfollow`, `PATCH /api/actors/me`, rotação de chaves
  (`keys/rotate`), `DELETE /api/actors/me` (FR-009) — contrato já previsto, entra em fase posterior.
- Avatares/headers, `IsBot`, operador/admin (FR-008), suspensão.
- Passkey e OIDC (reservados no contrato, fase 2/3 — nunca substituem o kit).
- Web front (P-08); SSS/guardiões (fase 2); restore drill no CI (P-07).

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-01 | RESOLVED | Ed25519 derivada da `RK` via HKDF — HUMAN 2026-11-07 16:06:40 (FR-006) |
| GAP-03 | RESOLVED | Par RSA-2048 gerado no registro, embrulhado por `HKDF(RK,"fed/rsa-wrap")`, no estado + kit — HUMAN 2026-11-07 16:06:40 |
| GAP-07 | RESOLVED | Senha + sessão cookie com Argon2id; passkey/OIDC reservados — HUMAN 2026-11-07 16:06:40 |
| GAP-08 | RESOLVED | `BouncyCastle.Cryptography` (Argon2id, Ed25519, HKDF), versão só em `Directory.Packages.props` — HUMAN 2026-11-07 16:06:40 |
| GAP-02 | ASSUMED | `nuget.org` público com `nuget.config` versionado se necessário (default em vigor desde a P-01) |
| GAP-09 | OPEN | Quem destrava `RK` para assinatura diária: o registro gera e deriva em memória, persiste só material embrulhado + público, e **nenhuma operação da P-02 precisa reabrir `RK`** (o kit é o único caminho de reidratação). Destravar para assinar em runtime é decisão da P-06 (federação) — não bloqueante aqui, porque nenhum AC desta fase depende dela |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0012 | Primitivas de chaves: RK/RC, HKDF, Argon2id, embrulho RSA | yes — toques disjuntos de TASK-0013 (`Squid.Api/Crypto` + `Squid.Api.csproj` vs `Squid.Grains`) | DONE (QA-0012 PASSED) |
| TASK-0013 | `ActorGrain` event-sourced com snapshot e truncamento | yes — disjunto de TASK-0012 | DONE (QA-0011 PASSED) |
| TASK-0014 | Registro + login/sessão (cookie, CSRF, logout/logout-all) | no — bloqueada por 0012+0013; toca `Squid.Api` | DONE (QA-0013 PASSED) |
| TASK-0015 | Webfinger + perfil JSON-LD com round-trip | no — bloqueada por 0014; toca `Squid.Api` | DONE (QA-0014 PASSED) |
| TASK-0016 | Kit de recuperação: gerar, verificar, importar (FR-005..007) | no — bloqueada por 0014; toca `Squid.Api` | DONE (QA-0015 PASSED) |
