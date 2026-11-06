# Research: Squid — MVP de rede social federada

**Feature**: `001-squid-mvp` | **Date**: 2026-11-02 | **Reconciliado**: 2026-11-03 (D1=A, D2=A, D3=A, D4=A)

**Entradas**: GAPs do documento mestre §14 (15 itens), riscos §15, decisões de stack §4.1 e os
requisitos FR-001..FR-056 da [spec](./spec.md).

**Regra deste documento**: cada item fecha uma pergunta que, se ficar aberta, faz a implementação
inventar arquitetura no meio do sprint. Nenhum item depende de leitura de terceiros para ser
decidido — as decisões abaixo são as que valem, e as alternativas rejeitadas ficam registradas para
o próximo que discordar.

---

## R-01 — Identidade após recuperação: derivar (continuidade) ou re-atestar?

**Fecha**: GAP 1 — *bloqueante para o Sprint 1* · **Afeta**: FR-004, FR-006

**Decision**: a chave de assinatura Ed25519 é **derivada** da chave raiz (`HKDF("fed/ed25519-sign")`).
Recuperar a `RK` pelo kit restaura exatamente as mesmas chaves, portanto o handle continua
tecnicamente o mesmo ator federado. Não existe fluxo de "re-atestação de identidade" no MVP.

**Rationale**: recuperação que troca a chave pública obrigaria todo seguidor, em toda instância
vizinha, a revalidar e reconfigurar confiança — e a federação não tem mecanismo padronizado de
rotação de chave de ator. Continuidade é o comportamento que o usuário espera de "recuperei minha
conta": o perfil é o mesmo objeto, não um clone. Também elimina uma classe de bug (assinaturas
antigas passando a falhar) e mantém o trilho de auditoria coerente.

**Alternatives considered**:
- *Chave nova + re-atestação assinada pela chave antiga*: correto em teoria, mas exige que a chave
  antiga ainda exista — o que é exatamente o cenário em que ela não existe. Adiado para a fase 3,
  quando o escrow existir.
- *Rotação periódica de chave no MVP*: custo alto de interop para benefício nulo sem E2EE.

**Risco residual**: se o kit for comprometido, a identidade é comprometida de forma permanente até
nova rotação — tratado pela camada 1 (SSS) na fase 2 e documentado no termo de uso.

---

## R-02 — Interop RSA-SHA256 × Ed25519 nas HTTP Signatures

**Fecha**: GAP 2 · **Afeta**: FR-034, FR-039, SC-016 · **Decidido**: 2026-11-03 (D1 = A)

**Decision**: RSA-SHA256 é **requisito de saída e de verificação de entrada**, não "o que o par
exigir". Toda entrega federada sai no formato draft-cavage (`Signature` com
`algorithm="rsa-sha256"`, `keyId="<ator>#main-key"`, `headers="(request-target) host date digest"`);
a entrada aceita `rsa-sha256` e `hs2019` no caminho clássico e também RFC 9421
(`Signature-Input` + `Signature`), que é por onde Ed25519 entra como chave adicional — Ed25519 não é
caminho de saída obrigatório. O critério de aceite do Sprint 5 é uma instância **Mastodon** (checkout
`ideias-references/mastodon-main`, hoje 5.0.0-alpha.1) e uma **Friendica** (2026.08-rc) reais: elas
aceitam nossas entregas e nós aceitamos as delas.

**Rationale**: com interop como critério, "RSA só se um par exigir" deixou de valer. Mastodon só
aceita `rsa-sha256`/`hs2019` na verificação clássica (`app/lib/signed_request.rb:22-23`) e só aceita
`Digest: SHA-256=` (`:65-67`); Friendica assina `rsa-sha256` por padrão
(`src/Util/HTTPSignature.php:1345-1365`) e usa RFC 9421 apenas como fallback (`:299-311`). Publicar
Ed25519 como chave adicional mantém o formato moderno sem pagar um segundo caminho de assinatura na
saída.

**Formato do documento de ator (interop)**: `publicKey` é um **objeto único** RSA com
`id="<ator>#main-key"`, `owner` e `publicKeyPem`; a Ed25519 vai em `squid:ed25519Key`
(`publicKeyMultibase`). Um array de chaves é aceito pelo Mastodon de referência
(`ProcessAccountService`, até 10), mas não é o que Friendica e versões anteriores esperam — o objeto
único é o denominador comum, e `#main-key` é o fragmento que Mastodon e Friendica usam em `keyId`.

**Consequência de chave (nova, exigida pelo D1)**: RSA não deriva da `RK` por HKDF. O par RSA-2048 é
gerado por ator no registro, a privada é embrulhada por `HKDF(RK, "fed/rsa-wrap")` e guardada no
estado do ator **e** no kit de recuperação; sem isso o restore recupera a identidade mas não o poder
de assinar, e a conta restaurada não federa. A Ed25519 continua derivada
(`HKDF(RK, "fed/ed25519-sign")`, R-01). O doc 04 §1.2 ganha a linha do RSA antes do Sprint 5; até lá
o contrato está em `data-model.md` §2.1 e §2.12.

**Alternatives considered**:
- *Só dois silos Squid, com Ed25519 (opção B do D1)*: nada prova interop; o GAP 2 fica aberto até a
  primeira instância real aparecer, provavelmente em produção.
- *Mastodon de teste só no CI (opção C do D1)*: Mastodon exige Postgres + Redis + Sidekiq e HTTPS
  válido; um pipeline frágil não é lugar de descobrir assinatura errada. A suíte de interop roda
  serial no fechamento do Sprint 5 (`quickstart.md` §2, US6), fora do CI rápido.
- *Assinar com Ed25519 e RSA ao mesmo tempo*: o caminho clássico não comporta os dois no mesmo
  `Signature`; enviamos o que o ecossistema verifica (RSA) e aceitamos os dois na entrada.

**Risco residual**: par que publica só chave Ed25519 (sem `publicKeyPem`) não é verificável pelo
Mastodon nem por nós no caminho clássico; a RFC 9421 cobre esse caso quando o par a usa.

---

## R-03 — Autenticação: senha + sessão × passkey/OIDC

**Fecha**: GAP 3 · **Afeta**: FR-002, FR-005, FR-009

**Decision**: senha com **Argon2id** (memória 64 MB, iterações 3, paralelismo 4, salt de 16 bytes) +
sessão de servidor com cookie `HttpOnly`, `Secure`, `SameSite=Lax`, expiração deslizante de 30 dias
e revogação por dispositivo. Segundo fator e passkey ficam fora do MVP. Recuperação de acesso à conta
**não** usa e-mail: usa o kit da camada 0.

**Rationale**: passkey em instância auto-hospedada heterogênea (dev em WSL, prod em ARM, clientes
web e mobile) multiplica superfície de teste sem resolver o problema central, que é *recuperar a
identidade*, e isso o kit resolve. Argon2id é o padrão atual para senha e custa uma dependência só.

**Alternatives considered**:
- *E-mail + senha com reset por e-mail*: reintroduz dependência de provedor de e-mail, que é
  exatamente o que a camada 0 evita ("e-mail é alarme, não fechadura").
- *OIDC delegado*: contradiz auto-hospedagem soberana no MVP.
- *Passkey desde o início*: bom destino, não bom primeiro passo.

**Decidido** (2026-11-03, ADR-36): degraus — MVP só senha + sessão, **sem e-mail/telefone e sem reset
por e-mail**; fase 2 passkey/WebAuthn (biometria, chave local, sem IdP); fase 3 OIDC apenas como SSO de
instância (um IdP configurado pelo operador), nunca login social obrigatório.

---

## R-04 — Vocabulário JSON-LD de contêiner

**Fecha**: GAP 7 · **Afeta**: FR-010, FR-034, FR-041

**Decision**: representar contêiner como `Group` do ActivityStreams, com extensão própria em
namespace `squid:` — `squid:kind` (`Group|Community`), `squid:policy` (objeto com as quatro
políticas e `version`), `squid:slug`, `squid:capabilities`. Membros são expostos como
`OrderedCollection` com paginação; o feed do contêiner **não** é exposto como `orderedItems` de
tamanho ilimitado, apenas a primeira página e um `first`/`next`.

**Rationale**: usar `Group` mantém compatibilidade de leitura com implementações que conhecem o tipo;
o namespace próprio permite evoluir a política sem inventar tipo incompatível. Não expor o feed
inteiro evita o hot spot que o documento proíbe no grain (§4.2) e evita payload de megabytes.

**Alternatives considered**:
- *Tipo novo `squid:Container`*: semanticamente limpo, ilegível para o ecossistema.
- *Reusar `Collection` pura*: perde a semântica de papéis e política.

---

## R-05 — Reavaliação de política no fan-out

**Fecha**: GAP 9 · **Afeta**: FR-037, FR-038

**Decision**: a entrega federada é **reavaliada** no momento do envio, não no momento do
enfileiramento. A fila guarda `(activityId, recipientActorUri, policyVersionAtEnqueue)`; o
`PeerGrain`, imediatamente antes de enviar, resolve o destinatário, recomputa a decisão com a
política vigente e, se negada, marca a entrega como `cancelled` com motivo auditável — sem erro,
sem retry.

**Rationale**: entre mudança de política e envio há janela de segundos a minutos (backoff de peer
caído pode ser horas). Enviar com política velha é vazamento silencioso, que é o pior modo de falha
do produto. Cancelar é barato; vazar não tem desfazer.

**Alternatives considered**:
- *Confiar na decisão do enfileiramento*: aceitável só se a fila fosse síncrona, e não é.
- *Revalidar tudo e reenfileirar*: gera tempestade de retrabalho; cancelar é definitivo e correto.

---

## R-06 — Quota de mídia e cota por instância

**Fecha**: GAP 10 · **Afeta**: FR-029, FR-054

**Decision**: cota por instância em **bytes**, guardada como `long`, com padrão de 10 GiB
configurável e alerta em 80% e 95%. Limites por tipo no MVP: imagem 10 MB, áudio 25 MB, vídeo 100 MB.
Cota é débito no ledger, não soma de diretório a cada upload (contador mantido no agregado de mídia,
reconciliado por varredura diária no Worker).

**Rationale**: contar bytes no diretório a cada upload é O(n) e vira latência no caminho crítico;
contador no agregado é O(1) e a varredura diária corrige deriva. Quota em inteiro é exigência do
§4.7 #9 e o mesmo recurso serve ao sharding depois.

**Alternatives considered**:
- *Sem cota no MVP*: disco cheio em produção é falha operacional garantida.
- *Quota por usuário*: não é o modelo do produto (cota é da instância, solidariedade de infra).

---

## R-07 — Moderação de conteúdo e obrigação legal

**Fecha**: GAP 11 — *fecha antes do Sprint 3* · **Afeta**: FR-028, FR-027 · **Decidido**: 2026-11-03 (D3 = A)

**Decision**: fluxo mínimo de denúncia (motivo taxonômico, denunciante, alvo) + fila de análise para
o operador + ação de remoção com `takedownReason` e retenção do **registro** (não do conteúdo) por
6 meses. Remoção propaga `Delete` ao federar. Conteúdo ilegal não é decidido por regra automática:
há fila humana, com SLA interno de 24h.

**Rationale**: moderação em rede aberta é obrigação do operador em praticamente toda jurisdição onde
o projeto pode ser auto-hospedado, e ignorar isso é risco alto com custo baixo de mitigar. O
registro sem o conteúdo é o que satisfaz auditoria sem obrigar retenção do material.

**Alternatives considered**:
- *Moderação após pós-MVP*: o documento mestre já classifica como **Alta** justamente por ser legal.
- *Classificador automático*: sem dependência nova permitida no MVP e sem E2EE não há desculpa para
  não ter fila humana.

**Decidido**: 2026-11-03 (D3 = A) — a proposta foi aceita como está: denúncia com motivo, fila
humana, remoção com motivo, retenção de 6 meses **do registro** (não do conteúdo) e SLA interno de
24h. Isso fecha o GAP 11 do doc 05 e o GAP-06 do repositório de execução; a Fase P-03 pode começar
sem mudança na spec (FR-028 já cobre o fluxo). Jurisdição diferente ajusta o número por configuração,
não por código.

---

## R-08 — Retenção e exclusão em conteúdo federado

**Fecha**: GAP 12 · **Afeta**: FR-009, FR-027

**Decision**: exclusão local é imediata e definitiva (conteúdo + blobs órfãos limpos por varredura).
Na federação, publica-se `Delete` para todos os destinatários conhecidos, com retry normal; cópias em
instâncias de terceiros **não** são garantidas. O produto declara isso textualmente no fluxo de
exclusão (FR-009) em vez de prometer o impossível.

**Rationale**: federação é replicação por natureza; prometer apagamento remoto é mentira técnica. A
transparência aqui é requisito de produto (§9 "o que o MVP não protege"), não nota de rodapé.

**Alternatives considered**:
- *Tombstone eterno com conteúdo cifrado*: não resolve a obrigação legal e cria dívida de storage.
- *Não propagar delete*: pior para o usuário e para a moderação.

---

## R-09 — Sessão HttpOnly em SSR sem XSS/CSRF

**Fecha**: GAP 13 · **Afeta**: FR-002, FR-042

**Decision**: o token de sessão **nunca** chega ao JavaScript do cliente. O SSR do front lê o cookie
`HttpOnly` no servidor e hidrata apenas estado derivado. Mutações usam CSRF token de dupla submissão
(cookie `SameSite=Lax` + header `X-CSRF-Token`), exigido em todo verbo não idempotente. HTML de
conteúdo do usuário é sanitizado no servidor com allowlist (sem `innerHTML` de terceiros), e o
`Content-Security-Policy` bloqueia script inline.

**Rationale**: essa combinação resolve os dois vetores sem inventar refresh-token em memória, que é
onde a maioria dos SSR quebra. Dupla submissão é suficiente porque o cookie já é `SameSite=Lax`.

**Alternatives considered**:
- *Token em `localStorage`*: XSS vira tomada de conta; rejeitado.
- *Refresh token em memória + access token curto*: complexidade de cliente para ganho marginal aqui.
- *`SameSite=Strict`*: quebra links de entrada vindos de mensageiros — justamente o caso de uso de
  compartilhamento externo (FR-043).

---

## R-10 — Busca em comunidades

**Fecha**: GAP 14 · **Afeta**: FR-016, FR-023

**Decision**: no MVP, busca por **nome/slug/descrição** de comunidades listadas usando índice de
texto do próprio banco de leitura, sem indexar conteúdo de posts. Busca full-text de conteúdo fica
pós-MVP (backlog §12.3).

**Rationale**: o requisito real de descoberta no MVP é achar comunidade, não garimpar posts. Índice
de conteúdo é onde se paga caro em tuning e é onde a moderação e a visibilidade se cruzam de forma
perigosa (vazar trecho de conteúdo restrito pela busca).

**Alternatives considered**:
- *Full-text em posts desde o MVP*: risco de vazamento por snippet + custo de tuning; adiado.
- *Busca federada*: explicitamente fora de escopo.

---

## R-11 — Federação de contêineres privados: o que trafega

**Fecha**: GAP 15 · **Afeta**: FR-037, FR-038

**Decision**: `public` federa normalmente. `membersOnly` federa **apenas** para instâncias com
vínculo ativo de membro, e o payload é marcado com a política e assinado. `unlisted` federa sem
entrar em coleções públicas de descoberta. `private` **não** federa em claro — o sistema registra a
limitação e não cria promessa falsa de conversa privada federada.

**Rationale**: sem E2EE, "privado federado" é uma frase que não significa nada tecnicamente. A única
posição honesta é não federar e dizer por quê (FR-037 + nota na UI).

**Alternatives considered**:
- *Federar privado cifrando por destinatário*: exige chaves por dispositivo = E2EE = fase 3.
- *Bloquear membersOnly na federação*: perde o caso de uso de grupo entre instâncias.

---

## R-12 — Reed-Solomon, SSS e escolha de campo

**Fecha**: GAPs 5 e 6 · **Afeta**: nada no MVP (contratos preparam)

**Decision**: **adiado** para a fase 2. Nenhuma biblioteca é escolhida agora. O que fica pronto no
MVP é o contrato que evita retrabalho: `IBackupTarget` plugável, `keyEpoch` no envelope, hash
SHA-256 de conteúdo e marcador de sequência no delta de backup.

**Rationale**: escolher biblioteca de RS ou de SSS sem um consumidor real é escolher por benchmark de
README. O custo de adiar é próximo de zero porque o formato do envelope é o mesmo; o custo de
escolher errado é reescrever pipeline com dados reais.

**Alternatives considered**:
- *Implementar SSS já com `IBackupTarget`*: 5 guardiões exigem 5 nós, que não existem.

---

## R-13 — Serialização de estado Orleans

**Fecha**: risco §15 · **Afeta**: todos os grains

**Decision**: estado persistido como DTO próprio, com payload federado como `JsonNode` e `[Id(n)]`
apenas em tipos estáveis. Nunca serializar tipos do domínio ricos diretamente. Contract type do
serializer fixado e versionado junto com o schema do estado.

**Rationale**: o risco mapeado é "serialização consumir semanas". A causa quase sempre é tipo de
domínio com hierarquia/interface sendo serializado por reflexão. Isolar em DTO com payload cru corta
o problema pela raiz e permite migração de schema explícita.

**Alternatives considered**:
- *Serializar agregados ricos*: confortável no começo, dor quando o agregado evolui com estado já
  gravado.

---

## R-14 — Content-addressing e dedupe de mídia

**Decision**: todo blob é endereçado por SHA-256 do conteúdo. Upload repetido do mesmo arquivo não
cria blob novo; incrementa referência. Limpeza só quando a contagem chega a zero, com carência
(24h) para não apagar em corrida com upload concorrente.

**Rationale**: dedupe é gratuito quando o endereço é o hash, e a mesma primitiva serve à integridade
de shard na fase 2. Carência evita a corrida clássica de "apagou o blob que o post ia usar".

**Alternatives considered**:
- *UUID por blob*: simples, sem dedupe, sem verificação de integridade.

---

## R-15 — Backup incremental por sequência

**Decision**: delta é `WHERE seq > @lastBackedUpSeq`, com a sequência monotônica por agregado e um
registro global de "último seq consolidado". Snapshot completo semanal, incremental diário.

**Rationale**: reaproveita o `Sequence` que já existe para outbox e delta sync mobile — uma
primitiva, três usos. Evita backup incremental por timestamp, que erra com relógio desalinhado.

**Alternatives considered**:
- *Backup diferencial por `updatedAt`*: quebra com escrita concorrente e clock skew.

---

## R-16 — Streaming ao vivo (SSE)

**Decision**: um `Channel<T>` por assinante, alimentado pela projeção de leitura, exposto como
`IAsyncEnumerable` com `text/event-stream`; cabeçalho `Last-Event-ID` mapeia para o marcador de
sequência, permitindo reconexão que entrega só o delta (FR-025). Limite de assinantes por conexão e
heartbeat de 15 s.

**Rationale**: SSE atravessa proxy e Caddy sem configurar WebSocket, e o `Last-Event-ID` já resolve
o caso "caí e voltei". WebSocket só se depois houver necessidade de cliente → servidor por conexão.

**Alternatives considered**:
- *Polling*: latência e carga piores com o mesmo esforço de cliente.
- *WebSocket*: mais peças móveis para ganho nulo no caso de uso de feed.

---

## R-17 — Teste de dois silos

**Decision**: dois `TestingHost` em processo, portas e diretórios de storage distintos, com
`PeerClient` injetável para simular 500, timeout, resposta lenta e entrega duplicada. Testes de
federação são de integração, nunca unitários com mock de HTTP.

**Rationale**: federação falha em detalhes que mock não reproduz (ordem, assinatura, retry, dedupe).
Dois silos locais é o menor ambiente que exercita o caminho real, e roda em CI sem rede externa.

**Alternatives considered**:
- *Container de Mastodon no CI*: lento, frágil e não testa o nosso lado em isolamento.

**Reconciliação D1 (2026-11-03)**: o teste de dois silos continua sendo o gate do CI; a interop real
(Mastodon + Friendica) entra como suíte serial de fechamento do Sprint 5, com versões fixadas. O que
o R-17 rejeita é *Mastodon no CI rápido*, não a verificação real.

---

## R-18 — Projeção de leitura: consistência e atraso

**Decision**: projeção é atualizada por Worker lendo o log de eventos; consistência é eventual com
alvo de < 1 s e reconexão do consumidor por marcador. A escrita responde quando o evento é aceito no
grain (não quando a projeção atualiza); o cliente lê o próprio post na resposta da escrita e o feed
converge.

**Rationale**: acoplar escrita à projeção volta a colocar o leitor no caminho crítico do escritor.
O `since` já cobre a janela de atraso, e o cliente mostra o post otimisticamente.

**Alternatives considered**:
- *Projeção síncrona na transação*: uma projeção lenta derruba escrita de todo o silo.

---

## R-19 — Cota e dinheiro como inteiro

**Decision**: toda grandeza monetária/quantitativa é `long` (bytes, créditos) ou `decimal`
serializado como string no JSON. Proibido `double`/`float` em ledger. Teste de contrato garante que
nenhum tipo de domínio de ledger usa ponto flutuante.

**Rationale**: §4.7 #9. É a classe de bug que aparece só depois de meses, no relatório do usuário.

---

## R-20 — Rotação de chave e `keyEpoch`

**Decision**: envelope de backup carrega `keyEpoch`. Rotação grava nova época e mantém as antigas
para leitura; restore aceita qualquer época para a qual exista a chave correspondente no kit. Nenhum
backup antigo é reescrito.

**Rationale**: reescrever backups na rotação exige confiança total no destino, que a cifra na origem
existe justamente para não exigir.

---

## R-21 — Referências abertas: postura e material adotado (D4 = A)

**Fecha**: a pergunta "o `federation/` é a execução canônica?" · **Afeta**: todos os artefatos

**Decision**: `ideias-references/federation` é **entrada de validação**, não fonte de decisão: as
decisões dele (`Ed25519` derivada, política em código) **não** são importadas; o spec-kit continua
sendo DECISÃO e o doc 05 a fonte da verdade. Das referências abertas entram no MVP dois padrões:
`max_id`/`since_id` + `Link` na paginação
(`mastodon-main/app/controllers/concerns/api/pagination.rb:10-22`) e teto de contadores declarados por
par remoto (`mastodon-main/app/models/status_stat.rb:24,44-47`, `MAX_UNTRUSTED_COUNT`). Ficam
**fora**: sincronização de seguidores por `collectionId`/`digest`/`url` (D4-c) e FEP-1b12 com o
contêiner como ator (D4-e); `sqlite-vec`/busca (D4-d) não se aplica.

**Rationale**: copiar decisões do repositório de execução faria o spec-kit perseguir um alvo que muda
sob os pés; aproveitar os padrões pontuais que resolvem um problema concreto (paginação estável,
grafo social hostil) é o que a referência serve para. As duas exclusões são de escopo: sincronização
de seguidores exige `Move`/`Block`/`Accept` (fase 2) e FEP-1b12 muda o modelo de entrega do feed
(ADR-11, contrato §1), o que não cabe no MVP.

**Verificado nas referências (notas, sem virar requisito)**:
- **E1** — o `orders-orleans` **não** registra o Orleans Dashboard: não há `AddDashboard()` nem
  `MapOrleansDashboard()` em `.cs`; o pacote aparece só em `bin/.../deps.json`. O doc 03 §3.8
  descreve um estado que não existe; nosso FR-055 já exige o painel de verdade, então nada muda aqui.
- **E2** — os "dez defeitos" do doc 03 §7 já foram corrigidos na própria referência:
  `SqliteGrainStorage.cs:14-30` documenta store name e compare-and-swap, e o código aplica. A lista
  deve virar "verificado e fechado" quando o doc 03 for revisto — fora desta rodada.
- **E3** — amarrar o contêiner a um ator `Group` com `Announce` (FEP-1b12) é o que faz o feed de
  comunidade chegar agregado ao Mastodon; fica como nota no `contracts/activitypub.md` §1, **não**
  como requisito (D4-e fora de escopo).

---

## Itens deliberadamente **não** pesquisados

- **Reed-Solomon / SSS / MLS / AT Protocol**: fase 2+; pesquisar agora produz decisão sem consumidor.
- **Benchmark de banco**: SQLite no MVP com interface única; ATP entra por flag e será medido com
  dados reais, não em teoria.
- **Algoritmo de feed**: inexistente por decisão de produto (cronológico).

## Conclusão da Phase 0

Nenhum `NEEDS CLARIFICATION` permanece. As 15 lacunas do doc 05 §14 estão fechadas (8 com decisão
implementável: R-01..R-07 e R-11; 2 de escopo/ação: R-08, R-09; 2 adiadas com contrato: R-10, R-12;
retenção configurável em R-07/R-08) e as decisões humanas de 2026-11-03 estão registradas: D1 (RSA em
saída + interop real, R-02) e D3 (moderação aceita, R-07). A postura sobre as referências fica em
R-21. Nenhuma bloqueia o plano.