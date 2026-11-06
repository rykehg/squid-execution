# Quickstart: validação ponta a ponta do MVP

**Feature**: `001-squid-mvp` | **Date**: 2026-11-02

Guia de execução e verificação — não é documentação de implementação. Cada cenário prova uma user
story da [spec](./spec.md) sem precisar de ferramenta além de `curl` e do navegador, alinhado ao
ADR-24 (inner loop sem daemon).

## 0. Pré-requisitos

| Item | Versão | Nota |
|------|--------|------|
| .NET SDK | 10.x | `dotnet --info` |
| Podman | 4.x | `podman compose up` para verificação de stack |
| Node + pnpm | Node 22+, pnpm 9+ | Só para `web/` |
| Python 3 | 3.10+ | Scripts do Spec Kit (já usados) |
| Domínio + TLS | — | Necessário para federação real; local usa `localhost` |

## 1. Subir o ambiente de desenvolvimento (sem daemon)

```bash
cd backend
dotnet run --project src/Squid.Service        # silo + API + health + dashboard
curl -s localhost:5000/health                 # {"status":"Healthy"}
curl -s localhost:5000/ready                  # storage + blobs
```

Esperado: `/health` 200 e `/ready` 200; trace de uma request visível no coletor OTLP local.

## 2. Verificação por user story

### US1 — Identidade recuperável (P1)

```bash
curl -s -X POST localhost:5000/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"handle":"alice","password":"<forte>","displayName":"Alice"}'
# → 201 com cookie de sessão

curl -s -X POST localhost:5000/api/recovery/kit -b cookies.txt
# → 201 com mnemônico (24 palavras) + credencial

curl -s localhost:5000/api/actors/alice
# → 200 com publicKey[] (RSA + Ed25519)
```

**Passa quando**: handle resolve sem sessão; kit é gerado uma vez; senha errada na importação não
destrói dados; importar o kit em instância limpa produz a **mesma** chave pública.

### US2 — Grupo com hierarquia e política (P1)

```bash
curl -s -X POST localhost:5000/api/containers -b cookies.txt \
  -H 'Content-Type: application/json' \
  -d '{"kind":"group","name":"Turma","slug":"turma"}'          # → preset membersOnly + invite

curl -s -X POST localhost:5000/api/containers/turma/join -b bob.txt
# → 403 policy_denied OU 404 (sem vazar o grupo) — nunca 200

curl -s -X PUT localhost:5000/api/containers/turma/policy -b cookies.txt \
  -H 'Content-Type: application/json' -H 'If-Match: 1' \
  -d '{"posting":"singlePublisher","publisherIds":["https://localhost/users/alice"],
       "visibility":"membersOnly","join":"invite",
       "interaction":{"canComment":"members","canReact":"members",
                      "canShare":"members","canAttach":"members"},"version":2}'
```

**Passa quando**: sem convite não entra; membro comum é bloqueado por `singlePublisher`; o publicador
nomeado publica; segunda `PUT` com `If-Match` antigo retorna `409`.

### US3 — Publicação e feed incremental (P1)

```bash
curl -s -X POST localhost:5000/api/media -b cookies.txt -F 'file=@foto.jpg' -F 'alt=uma foto'
# → 201 {contentHash, blurHash}

curl -s -X POST localhost:5000/api/containers/turma/posts -b cookies.txt \
  -H 'Content-Type: application/json' \
  -d '{"content":"primeiro post","attachmentHashes":["<hash>"]}'

curl -s 'localhost:5000/api/containers/turma/feed?since=0' -b bob.txt
# → itens na ordem de seq + nextSince

curl -N localhost:5000/api/stream -b bob.txt          # SSE: post chega sem refresh
```

**Passa quando**: post chega em < 2 s para membro conectado; `since` entrega só o delta; `unlisted`
não aparece para não membro; excluir remove em < 5 s.

### US4 — Comunidade em escala (P2)

```bash
curl -s -X POST localhost:5000/api/containers -d '{"kind":"community","name":"Rust","slug":"rust"}'
curl -s 'localhost:5000/api/containers/search?q=rust'
```

**Passa quando**: comunidade listada aparece na busca, grupo não; entrada `open` é imediata; com
10.000 posts semeados, a primeira página do feed responde em < 1 s p95.

### US5 — Mensageria sem E2EE (P2)

```bash
curl -s -X POST localhost:5000/api/threads -d '{"kind":"direct","participantIds":["...bob"]}'
curl -s -X POST localhost:5000/api/threads/<id>/messages -d '{"content":"oi"}'
# → 428 até o aceite do aviso
curl -s -X POST localhost:5000/api/threads/<id>/ack-no-e2ee   # → 204

# resposta a uma mensagem específica
curl -s -X POST localhost:5000/api/threads/<id>/messages \
  -d '{"content":"fechado","replyToId":"<msgId>"}'

# reação em mensagem (idempotente) e remoção
curl -s -X POST localhost:5000/api/threads/<id>/messages/<msgId>/reactions -d '{"emoji":"👍"}'
curl -s -X DELETE 'localhost:5000/api/threads/<id>/messages/<msgId>/reactions?emoji=👍'
```

**Passa quando**: envio exige aceite; terceiro recebe 404 sem ver conversa, resposta ou reação; a
resposta referencia a mensagem original; reação repetida não duplica; emoji fora da whitelist é
recusado; ordem estável sob envios concorrentes; marcação de leitura propaga.

### US6 — Federação entre dois silos (P2)

```bash
# terminal 1 e 2: instâncias A e B com storage e portas diferentes
dotnet run --project src/Squid.Service -- --urls http://localhost:5001   # B
dotnet run --project src/Squid.PeerClient -- --simulate slow,500,timeout
```

**Passa quando**: público de A aparece em B; `membersOnly` não vaza; duplicata da mesma atividade não
duplica; derrubar B não trava publicação em A; política alterada entre enfileiramento e envio
cancela a entrega.

#### Interop real (gate de saída do Sprint 5 — D1=A)

Pré-requisito: Mastodon (`ideias-references/mastodon-main`, 5.0.0-alpha.1) e Friendica
(`friendica-2026.08-rc`), ou imagens oficiais com a mesma versão, acessíveis por **HTTPS com domínio
público** — Mastodon não federa por `localhost` nem por HTTP. Em máquina local, use um túnel ou Caddy
com CA instalada nos contêineres.

```bash
# seguir o ator remoto (rota de follow do Sprint 5; contrato em http-api.md §3)
curl -s -X POST localhost:5000/api/actors/@alice@mastodon.test/follow -b cookies.txt
# publicar público e conferir o perfil do par
curl -s -X POST localhost:5000/api/containers/rust/posts -b cookies.txt \
  -H 'Content-Type: application/json' -d '{"content":"interop"}'
# o par posta mencionando o ator Squid; a entrada deve ser aceita
curl -s localhost:5000/api/admin/peers -b admin.txt
```

**Passa quando**: o post aparece no perfil público do par com autor e mídia; nenhuma entrega fica
`Failed` por assinatura; atividade criada no Mastodon e na Friendica chega ao Squid com `202` e o
autor é resolvido; POST sem `Signature` recebe `401` e nada é processado. **Não** roda no CI rápido:
é suíte serial de fechamento do Sprint 5, com as versões dos pares no relatório.

### US8 — Backup e restauração (P1)

```bash
curl -s -X POST localhost:5000/api/admin/backup/run -b admin.txt
curl -s localhost:5000/api/admin/backup/status -b admin.txt   # verifiedAt + redundancy score

dotnet test tests/Squid.Tests --filter Category=RestoreDrill
```

**Passa quando**: drill cria → backup → destrói → restaura → hashes idênticos; off-site restaura sem o
disco local; backup interrompido não gera envelope corrompido aceito.

### US7 — Compartilhamento externo (P3)

```bash
curl -s localhost:5000/o/<postId> | grep -E 'og:title|og:image|twitter:card'
```

**Passa quando**: visitante sem sessão vê título, descrição e imagem; post restrito não expõe
metadado; no celular, share sheet abre e o link volta direto ao post.

## 3. Stack completa (verificação, não desenvolvimento)

```bash
cd infra && podman compose up -d
podman compose ps            # api, worker, web, caddy
curl -s localhost:8080/health
```

Esperado: `/health` verde atrás do Caddy, volumes nomeados presentes, imagem final em estágio
`runtime` com usuário não-root. `podman compose down` **não** apaga banco, blobs nem backups.

## 4. Testes automatizados

```bash
dotnet test backend/Squid.slnx                 # unit + integração + dois silos
pnpm --prefix web test                          # front
./backend/tests/integration/run-all.sh          # QA sem daemon (curl + scripts)
```

## 5. Matriz de aceitação (o que não pode passar)

| Cenário | Resultado proibido |
|---------|--------------------|
| Não membro pede post de grupo fechado | `403` com nome do grupo, contagem ou preview |
| Preview social de post restrito | Qualquer metadado do conteúdo |
| Entrega duplicada da mesma atividade | Item duplicado no feed |
| Peer fora do ar durante publicação | Falha na operação local |
| Backup com hash divergente | Envelope marcado como íntegro |
| Restore de `keyEpoch` sem chave | Qualquer escrita destrutiva |
| Log ou span com nome/e-mail/handle de usuário | Presença na telemetria |
| Erro interno | Stack trace, caminho de arquivo ou nome de tabela na resposta |
| Upload `.jpg` de conteúdo executável | Aceite com `200` |