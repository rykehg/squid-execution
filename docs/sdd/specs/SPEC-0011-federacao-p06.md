<!--
templateVersion: 1.0.0
-->
Specification: the resolved reading of a requirement, precise enough to cut into tasks and to test against.
Write responsibility: ODEV.

- status: ATIVO
- owner: odev
- lastVerified: 2026-11-13 13:42:26

# SPEC-0011 — Federação: dois silos, assinatura RSA-SHA256 e interop real Mastodon/Friendica

- requirement: REQ-0001
- phase: P-06
- dependsOn: SPEC-0007 (identidade, chaves `RK`/`RC`, webfinger/perfil JSON-LD), SPEC-0009 (post,
  `Activity` log de `IActivityStore`, projeções), SPEC-0010 (mensageria — thread **não** federa,
  R-11)

Item do Sprint 5 do doc 05 (`#### Sprint 5 — Federação`): duas instâncias trocam atividades de
verdade. Não reentrevista o que o doc 05 §3.1/§3.2/§4.2/§12.2, o data-model adotado (§2.8, §2.9,
§3.4), os FR-034..FR-041, o contrato `contracts/activitypub.md` (§1..§7), o ADR-28 e as pesquisas
R-02/R-04/R-05/R-13 já decidem.

Decisões humanas desta abertura (entrevista 2026-11-13, 2 rodadas / 6 questões, Henrique):

1. **Escopo** = Sprint 5 inteiro (8 itens + DoD) — default da rodada 1 aceito (11:48:04: o humano
   respondeu só a questão 3; o protocolo da entrevista trata silêncio como aceite do default) e
   confirmado na rodada 2 (13:31:11: "concordo com as sugestões dadas").
2. **GAP-09 — quem destrava a `RK` em runtime** (11:48:04): `RK` entra como **segredo de ambiente
   no boot** (`Squid:RecoveryKey`); o `ActorGrain` desembrulha a chave RSA privada do ator na
   ativação e a mantém **só em memória**; nenhuma rota de reidratação, o kit continua sendo o
   único caminho de recuperação; **produção = gate humano** para injetar o segredo.
3. **Interop real dentro da fase** (11:48:04): o fecho exige Mastodon e Friendica reais aceitando
   a entrega assinada (SC-016) — e o humano pediu como entregável um **manual** para subir as
   instâncias locais e fazer a comunicação real.
4. **`I-11` entra na P-06** como entrega cedo (13:31:11): `Instance:Domain` em config +
   `ForwardedHeaders` — domínio estável é pré-requisito de URL canônica federada.
5. **Manual = runbook próprio** (13:31:11): `docs/runbooks/federacao-interop.md` + índice,
   escrito **antes** da validação de interop, para o humano seguir o passo a passo junto.
6. **Pares reais na validação = Docker local** (13:31:11): Mastodon e Friendica de referência na
   máquina do humano, apontando para os dois silos Squid locais, sem exposição na internet; o
   runbook ganha seção opcional "instância pública real" (domínio + TLS + túnel).

## Behaviour

### A. Domínio canônico e proxy (I-11, decisão 4)

- `Instance:Domain` é configuração **opcional**: quando presente, todo ID canônico (ator, objeto,
  atividade, mídia, coleção, `webfinger.subject`) usa `https://{Instance:Domain}`; quando ausente,
  vale a regra atual scheme+Host (P-02/P-03) — **comportamento byte-idêntico** ao de hoje.
- Os quatro pontos que hoje montam origem a partir de `Request.Scheme`/`Request.Host` passam por um
  helper único de origem canônica; `X-Forwarded-Proto`/`X-Forwarded-Host` só são honrados quando
  há proxy de confiança configurado (`Instance:ForwardedHeaders`), senão são ignorados.
- Com `Instance:Domain` presente, a origem canônica **não** deriva do `Host` da request — atrás de
  Caddy/reverso o ID publicado é o domínio público, não o host interno.
- `webfinger` com `Instance:Domain` aceita **o domínio canônico ou o Host alcançado** e publica
  sempre o canônico (`subject = acct:{nome}@{Instance:Domain}`, `links.self` na origem canônica) —
  cláusula esclarecedora da D-01 da TASK-0041 (adjudicada pelo ODEV 2026-11-13, nível especificação;
  domínio estrangeiro continua `404` pela regra B-09a da SPEC-0007).
- O id do log interno de `Activity` continua `urn:squid:activity:{token}` (design da P-04/GAP-23);
  a URL canônica de atividade federada nasce na TASK-0044/0045, não no I-11.

### B. Chave de assinatura em runtime (GAP-09, decisão 2)

- `Squid:RecoveryKey` é lida no boot (env/config). Sem ela: federação de **saída** fica desabilitada
  com aviso claro em log/métrica — o processo **não** cai e a instância segue servindo leitura e
  escrita local.
- Com ela: `ActorGrain` desembrulha a RSA privada do ator (`HKDF(RK, "fed/rsa-wrap")`, regra da
  TASK-0012) na ativação e a mantém **somente em memória**; nunca persiste em claro, nunca loga,
  nunca devolve em rota. `RK` errada → falha auditável na ativação, sem vazar material.
- A Ed25519 continua derivada da `RK` (GAP-01) e é publicada como chave adicional.

### C. Assinatura HTTP (FR-039, ADR-28, contrato §3/§5)

- **Saída**: draft-cavage `rsa-sha256` — `Signature: keyId="...#main-key",algorithm="rsa-sha256",
  headers="(request-target) host date digest",signature=<base64>`; `Digest: SHA-256=<base64>`
  (nunca sha-512); `Date` em HTTP-date GMT; GET assina `(request-target) host date`; `keyId`
  aponta para `#main-key`; assina o ator que age, com a chave RSA dele.
- **Entrada** (ordem obrigatória do contrato §3): 1) assinatura — draft-cavage `rsa-sha256`/
  `hs2019` **ou** RFC 9421 (`Signature-Input`+`Signature`, RSA ou Ed25519); 2) `Digest` do corpo
  confere; 3) `Date` dentro de ±5 min; 4) corpo ≤ 1 MB e profundidade ≤ 32; 5) rate limit por
  domínio (60 req/min padrão, configurável por par); 6) `activity.id` já visto → `202` sem
  reprocessar; 7) domínio não bloqueado e ator resolvível (cache com TTL).
- Falha 1–4 → `401`/`400` sem processar; falha 5 → `429` + `Retry-After`; em nenhum caso o
  processo cai (FR-040). Assinatura rejeitada pelo par na **saída** → entrega `Failed` com motivo
  `signature_rejected` e alerta, nunca retry cego com a mesma chave.

### D. Superfície exposta (contrato §1, fecha S-26)

- Ator `GET /users/{name}` completo: `publicKey` objeto único `#main-key`, `squid:ed25519Key`,
  **`inbox`, `outbox`, `followers`, `following`** (S-26 — o lacuna registrada na P-02 fecha aqui).
- `GET /.well-known/nodeinfo`; `POST /users/{name}/inbox`; `GET /users/{name}/outbox` em
  `OrderedCollection` paginada; `GET /users/{name}/followers|following` idem; `GET /objects/{id}`;
  `GET /c/{slug}` (`Group` + `squid:*`), `POST /c/{slug}/inbox`, `GET /c/{slug}/outbox` (primeira
  página, nunca o feed inteiro), `GET /c/{slug}/members` com visibilidade.
- Negação por Accept: `application/activity+json` e
  `application/ld+json; profile="https://www.w3.org/ns/activitystreams"`; `webfinger` ganha `self`
  apontando ao ator canônico. `rel=profile` **não** entra aqui (P-08, S-26).

### E. Inbox: verificação, dedupe e aplicação (FR-035/040, InboxGrain, contrato §2/§3)

- `POST .../inbox` roda a ordem C; `InboxGrain` guarda a janela de `activity.id` e responde `202`
  sem reprocessar entrega repetida (dedupe).
- Atividades **aplicadas** na entrada: `Create` (vira post em B — anexos baixados do par, hash
  SHA-256 conferido), `Follow` (relação seguidor + `Accept` de volta), `Undo` (`Follow`),
  `Delete` (tombstone propaga), `Like` (idempotente por ator+objeto), `Flag` (entra na fila de
  moderação existente, `IReportStore`).
- `membersOnly` só é aplicado se o destinatário local é membro ativo do contêiner; `private` não
  gera entrega nem é aceito em claro (R-11).
- Atividade desconhecida **não** é erro: `202` + métrica `federation.unknown_activity`.
- Falha 1–4 do contrato → `401`/`400` sem processar, estado intacto (contrato §7.5).

### F. Saída: fan-out e reavaliação (FR-037/038, §3.2, R-05)

- Ao publicar, o log `Activity` (P-04) vira origem do fan-out; `to`/`cc` refletem a visibilidade
  (contrato §2.1): `public` → `Public` em `to` + `followers` em `cc`; `unlisted` → `followers` em
  `to`; `membersOnly` → cada ator membro ativo em `to`, `squid:policy` no payload, nada em `cc`;
  `private` → **nenhuma entrega**.
- Destinatários: seguidores do autor + coleção `followers` do contêiner + menções; entrega em
  paralelo por par, **assíncrona** — nenhuma entrega bloqueia a resposta local (FR-036).
- **Reavaliação no momento do envio** (R-05): antes de cada envio, resolve destinatário e recomputa
  a decisão com a política vigente; negada → `Cancelled` com motivo auditável, sem erro e sem
  retry. A fila guarda `(activityId, recipientActorUri, policyVersionAtEnqueue)`.
- Rota local `POST /api/federation/follow` (GAP-41) inicia follow de ator remoto: gera `Follow`
  assinado e enfileira; `Accept` remoto cria a relação. Sem UI nesta fase (P-08 consome a rota).

### G. Peer: fila, resiliência e simulação de falha (FR-036, PeerGrain, data-model §2.9/§3.4)

- `PeerGrain` por domínio remoto: `Peer (Status Healthy|Degraded|Down|Blocked, BackoffUntil,
  FailureCount, LastSuccessAt, RateLimitPerMinute)` e `PeerDelivery (State
  Pending|Sent|Failed|Cancelled, Attempts, NextAttemptAt, Reason)` em migration versionada
  (`0008_federation.sql`, família liberada para teste/dev/CI — gate 2026-11-09).
- `Pending` → falha → backoff exponencial com jitter (`attempts++`); `N = 8` tentativas ou 4xx
  permanente → `Failed` (DLQ com motivo e alerta); política reavaliada nega → `Cancelled`;
  circuito aberto por peer manda entrega nova direto para `Pending` com `NextAttemptAt` futuro —
  **nunca falha na hora**; teto de backoff 6 h; peer `Blocked` não recebe nada.
- `Squid.PeerClient` (stub desde a TASK-0002) ganha cenários de falha — 500, timeout, lentidão —
  para exercitar a fila sem par real.
- `Squid:RecoveryKey` ausente desabilita só a saída (item B); fila local continua registrando
  pendências.

### H. Dois silos, manual e interop (DoD, contrato §7)

- Harness de integração com **dois silos** A→B cobre os itens 1–9, 14 e 15 do contrato §7
  (post público aparece com autor/mídia/data; `membersOnly` não chega a não-membro; `private` não
  entrega; dedupe; assinatura inválida → 401 intacto; peer derrubado → `Pending`+backoff; política
  muda → `Cancelled`; 5 MB → 413, profundidade 100 → 400, serviço vivo; webfinger 404 sem vazar;
  `peer.domain` sem PII; `Like` duplicado não duplica contagem; `inReplyTo` preservado).
- Runbook `docs/runbooks/federacao-interop.md` (decisão 5): subir dois silos Squid locais, subir
  Mastodon + Friendica em Docker, apontar, validar comunicação; seção opcional de instância
  pública. Os runbooks `peer-entrega-travada.md` e `dlq-cheia.md` (PENDENTE-fase desde a P-01)
  viram EXECUTÁVEL nesta entrega.
- **Interop real** (fecho): instância Mastodon e instância Friendica de referência em Docker local
  exibem o post público assinado por nós e nossas atividades assinadas por elas são aceitas com
  `202`; log do par sem `signature verification failed` (SC-016, ADR-28). É gate humano: o humano
  executa o manual junto com o agente e valida os logs do par.

## Inputs and outputs

| Direction | Name | Shape | Notes |
| --- | --- | --- | --- |
| in | `POST /users/{name}/inbox` | ActivityPub assinada (JSON-LD) | ordem §3; 202/401/400/429 |
| in | `POST /c/{slug}/inbox` | idem | inbox do contêiner |
| in | `POST /api/federation/follow` | `{targetActorUri}` | sessão + CSRF; gera `Follow` enfileirado |
| in | config `Instance:Domain`, `Instance:ForwardedHeaders`, `Squid:RecoveryKey` | string? | 0041/0042; ausência = comportamento atual |
| out | `GET /users/{name}` | documento de ator (`publicKey` + `inbox`/`outbox`/`followers`/`following`) | contrato §1.1 |
| out | `GET /users/{name}/outbox\|followers\|following` | `OrderedCollection` paginada | contrato §1 |
| out | `GET /objects/{id}`, `GET /c/{slug}`, `GET /c/{slug}/outbox\|members` | JSON-LD | contrato §1/§1.2 |
| out | `GET /.well-known/nodeinfo` | metadados de software/versão | contrato §1 |
| out | entrega saída | `Signature`/`Digest`/`Date` draft-cavage | contrato §5 |
| out | erro | 401 (assinatura/digest), 400 (malformado/profundidade), 413 (>1 MB), 429 (+`Retry-After`), 404 sem vazar | FR-040/§3 |

## Rules and edge cases

- Ordem de verificação de entrada é fixa (§3): pular uma etapa é defeito de QA, não escolha.
- `activity.id` repetido → `202` idêntico, zero reprocessamento (contrato §7.4).
- Payload 5 MB → `413`; aninhamento 100 → `400`; ambos sem derrubar o serviço (§7.8).
- `Digest` divergente ou assinatura inválida → `401`, estado do receptor intacto (§7.5).
- `membersOnly` entregue a não-membro é `Cancelled` na reavaliação (§7.2/§4); `private` nunca
  gera `PeerDelivery` (§7.3).
- Peer `Down`/`Blocked`/circuito aberto: fila local com backoff, publicação local segue (§7.6).
- Assinatura rejeitada pelo par → `Failed signature_rejected` + alerta, sem retry cego (§5).
- `webfinger` de handle inexistente → `404` idêntico, sem sugestão de ator (§7.9).
- Erro federado nunca expõe stack trace, caminho interno ou nome de tabela (FR-053).
- HTML remoto é dado não confiável: sanitizado antes de qualquer exibição (§6).
- `Like` do mesmo ator ao mesmo objeto não duplica contagem (§7.14).
- Resposta federada preserva `inReplyTo`; conversa privada não gera entrega (§7.15, R-11).

## Non-functional

- security: `Squid:RecoveryKey` só em memória, nunca em log/rota/persistência; chave privada do
  ator nunca sai do processo; `keyId` sempre `#main-key`; janela de replay ±5 min; rate limit por
  domínio; sanitização de HTML remoto; sem vazamento em 404/erro (SC-005/FR-053).
- observability: atributo de trace `peer.domain` presente em **100%** das entregas e sem PII
  (SC-015); métricas `federation.unknown_activity`, entrega por estado, DLQ com alerta.
- reliability: entrega nunca bloqueia request local (FR-036); processo nunca cai por payload
  federado (FR-040); backoff teto 6 h; DLQ com motivo.
- performance: nenhuma meta numérica nova; paginação de coleções com índice; fan-out por par em
  paralelo.
- platform: SQLite local; `0008_federation.sql` idempotente liberado para teste/dev/CI
  (gate 2026-11-09), produção segue gate humano.

## Out of scope (explicitly)

- **Federação de contêineres** (`Join`/`Leave`/`Announce` de grupo, FEP-1b12) — backlog do doc 05
  §12.3; `Announce` como ator de grupo já é nota D4-e do contrato.
- Atividades `Update`, `Announce`, `Join`, `Leave`, `Block` **aplicadas** — só `202` + métrica
  (GAP-42); `Flag` de saída, `Like`/`Announce` de saída (boost, reação em objeto remoto) fora
  (GAP-43).
- `rel=profile` no webfinger (P-08, junto com o front).
- Qualquer UI: seguir, ver perfil remoto, feed federado é P-08 — aqui só backend + runbooks.
- Conversa (thread) federada (não tem representação AP, R-11); E2EE de qualquer formato (I-03).
- AT Protocol/Nostr (I-06); push/notificação de atividade remota; busca federada.
- Exposição pública da instância (TLS, túnel, DNS) — seção opcional do runbook, execução humana.

## Gaps carried from the requirement

| Gap | Status | Resolution or assumption in force |
| --- | --- | --- |
| GAP-09 | RESOLVED (HUMAN) | decisão 2 (11:48:04): `Squid:RecoveryKey` no boot, desembrulho em memória no `ActorGrain`, sem rota de reidratação, kit = recuperação, produção = gate humano |
| GAP-41 | ASSUMED | rota local `POST /api/federation/follow` existe para iniciar follow de ator remoto (sem UI; P-08 consome) — sem ela o DoD dos dois silos não tem por onde nascer a relação |
| GAP-42 | ASSUMED | `Update`/`Announce`/`Join`/`Leave`/`Block` na entrada → `202` + métrica, sem aplicar estado (DoD do Sprint 5 não exige; vira backlog) |
| GAP-43 | ASSUMED | saída limitada a `Create`, `Follow`, `Accept`, `Undo(Follow)` e `Delete`; `Like`/`Announce` de saída (boost, reação em objeto remoto) e `Flag` de saída ficam fora — o contrato §2 já lista `Flag` só na entrada |
| GAP-44 | ASSUMED | mídia remota: baixada do par pelo caminho de fetch com guarda SSRF, integridade pelo SHA-256 do próprio blob; falha de download **não** rejeita o post (anexo fica ausente/marcado); baixa remota não cobra cota do usuário local |
| GAP-45 | ASSUMED | `Instance:Domain` é opt-in; sem config, origem canônica continua scheme+Host byte-idêntica ao atual; `X-Forwarded-*` só com proxy de confiança configurado |
| GAP-46 | ASSUMED | `Accept` de `Follow` é automático quando a política do ator/contêiner permite seguir; sem UI de aprovação nesta fase |
| GAP-47 | ASSUMED | runbook de interop e validação com pares reais em Docker local na máquina do humano; instância pública real é seção opcional do runbook e gate humano à parte |

## Tasks

| Task | Title | Parallel-safe | Status |
| --- | --- | --- | --- |
| TASK-0041 | Domínio canônico e proxy (`Instance:Domain` + `X-Forwarded-*`) | no — base da fase | TASK_READY |
| TASK-0042 | Chave de assinatura em runtime (GAP-09) + assinar/verificar HTTP | no — serial | TASK_READY |
| TASK-0043 | Superfície ActivityPub: ator completo, coleções, objeto, contêiner (fecha S-26) | no — serial | TASK_READY |
| TASK-0044 | Inbox: pipeline de verificação, dedupe e aplicação de atividades | no — serial | TASK_READY |
| TASK-0045 | Saída: fan-out por visibilidade, reavaliação R-05 e rota de follow | no — serial | TASK_READY |
| TASK-0046 | PeerGrain: fila, backoff, circuit breaker, DLQ + `PeerClient` com falha | no — serial | TASK_READY |
| TASK-0047 | Facet de dois silos (A→B, contrato §7 itens 1–9/14/15) | no — serial | TASK_READY |
| TASK-0048 | Runbook de federação (manual + runbooks P-06 executáveis) | no — serial, blockedBy 0047 | TASK_READY |
| TASK-0049 | Interop real Mastodon/Friendica + facet de fecho do DoD | no — última, gate humano | TASK_READY |
