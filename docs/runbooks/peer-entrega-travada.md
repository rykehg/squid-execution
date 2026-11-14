# Runbook — peer com entrega travada

- fase: P-06 · status: EXECUTÁVEL · origem: doc 05 §8, SPEC-0006 (runbook executável desde a
  P-06: motor de entrega da TASK-0046, comandos provados na TASK-0048)
- comandos `podman compose` rodam da raiz do repositório com `-f infra/compose.yaml`
- leitura da fila usa `python` com o `sqlite3` padrão; o banco é o `Storage:Path` do silo
- marcas: **EXECUTÁVEL** = executado na prova com exit 0 · **NÃO-executar** = destrutivo

## Sintoma

A publicação local funciona, mas nada chega ao par remoto: entregas pendentes se acumulam para
aquele domínio e não avançam; o `peer` do par fica `Down`/`Degraded` com `failure_count` subindo;
no terminal do silo aparecem `peer delivery pass failed; it will resume from the rows still
Pending` ou, pior, `peer delivery dead-lettered (…)`. Este é o gatilho previsto (doc 05 §8,
entrega federada da P-06).

## Diagnóstico

1. **Aviso de chave no boot do silo** (EXECUTÁVEL — observado na prova; aparece uma vez, no
   terminal do silo, nível `Warning`):

   ```
   federation output disabled: Squid:RecoveryKey is missing or invalid; local reads and writes keep working
   ```

   Sem chave válida (base64 de exatamente 32 bytes) a saída federada fica desabilitada e cada
   entrega volta para `Pending` com motivo `federation_output_disabled` (o processo **não** cai).

2. **Fila do silo** (EXECUTÁVEL — exit 0; troque o caminho pelo `Storage:Path` do silo em questão):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "fila:" "select activity_type, state, attempts, reason, next_attempt_at from peer_delivery"
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "pares:" "select domain, status, failure_count, backoff_until, rate_limit_per_minute from peer"
   ```

   O que os valores significam (máquina da `0008_federation.sql` + `PeerDeliveryPump.cs`):

   | Situação | Leitura |
   | --- | --- |
   | `Pending` + `next_attempt_at` no futuro + `reason = 'send_failed'` | recuo em andamento: a tentativa contou (`attempts`) e o motor reagenda com backoff exponencial (N = 8, teto 6 h) |
   | `Pending` + `reason = 'federation_output_disabled'`/`signing_key_unavailable'` | sem `Squid:RecoveryKey` no boot ou sem cópia da `0009` para o ator; tentativa não conta, a linha só espera |
   | `peer.status = 'Down'` + `backoff_until` no futuro | circuito aberto: entrega nova vai direto para `Pending` com `NextAttemptAt` futuro — nunca falha na hora |
   | `Failed` | caiu na DLQ — este runbook **não** é o dele, vá para [dlq-cheia.md](dlq-cheia.md) |
   | `Cancelled` | a reavaliação R-05 negou na hora do envio; estado final por design, sem retry |

3. **Reproduzir a falha sem par real** (EXECUTÁVEL — `Squid.PeerClient` da TASK-0046):

   ```powershell
   dotnet run --project backend/src/Squid.PeerClient -- --help                    # exit 0, imprime a usage
   dotnet run --project backend/src/Squid.PeerClient -- --scenario=500            # cenário de falha transitória
   dotnet run --project backend/src/Squid.PeerClient -- --scenario=timeout        # sem resposta; slow atrasa a resposta
   ```

   O servidor imprime a origem do par sintético
   (`Squid.PeerClient: scenario '500' listening on http://127.0.0.1:57305/` — porta dinâmica
   diferente a cada execução). Aponte uma entrega para ele pela rota de follow — com as variáveis
   `$jarA`/`$csrfA` das contas criadas nos passos 3 e 5 da validação de
   [federacao-interop.md](federacao-interop.md) (202 enfileira; o
   envio é do motor, nunca da request):

   ```powershell
   curl.exe -s -o NUL -w "%{http_code}" -b $jarA -c $jarA -X POST http://127.0.0.1:5601/api/federation/follow -H "Content-Type: application/json" -H "X-CSRF-Token: $csrfA" -d '{\"targetActorUri\":\"http://127.0.0.1:57305/users/peer-test\"}'
   ```

   Prova desta task: com `--scenario=500` a linha nasce `Pending / attempts=1 / send_failed` com
   `next_attempt_at` ~7 min à frente e o par vira `Down` com `failure_count=1` logo na **primeira**
   falha; com `--scenario=401` a primeira tentativa já derruba a linha em `Failed /
   signature_rejected` (4xx permanente → DLQ, sem retry cego) — o caso do próximo runbook.

4. **Procurar os avisos no log do silo**: o log é JSON estruturado no próprio terminal do silo
   (Serilog, nível Information). Padrões que importam aqui:
   `federation output disabled…`, `outbound delivery held: signing key unavailable…`,
   `peer delivery pass failed; it will resume from the rows still Pending`,
   `federated delivery cancelled on re-evaluation…`.

## Procedimento

Na ordem — cada passo conserta a causa, a fila **se esvazia sozinha** (o motor roda a cada 1 s por
padrão: `Federation:DeliveryPumpSeconds`, `SquidServiceHost.cs:227`):

1. **Chave ausente/errada** → reinicie o silo com `--Squid:RecoveryKey=<base64-de-32-bytes>`
   (geração da chave em [federacao-interop.md](federacao-interop.md) §1). Ator registrado **antes**
   da chave não tem cópia embrulhada (GAP-50): a saída dele continua desabilitada com aviso —
   numa instância nova, registre de novo depois da chave (limite aceito pelo humano).
2. **Pares locais não liberados** → reinicie com `--Federation:TrustedPeerHosts=127.0.0.1,localhost`
   (allowlist exata, vazia por padrão, GAP-49); sem o nome do par, a entrega é recusada antes do
   socket e conta `send_failed` até a DLQ.
3. **Par fora do ar / circuito aberto** → levante o par ou espere o recuo (`peer.backoff_until`;
   teto de 6 h na oitava tentativa). Nada a consertar no silo: a linha volta a `Sent` sozinha
   quando o par responde. Confirme o par de teste com o `Squid.PeerClient` (diagnóstico 3).
4. **Motor parado** → `Federation:DeliveryPumpSeconds` `0`/negativo deixa o pump registrado e
   ocioso (comportamento de teste); reinicie com o padrão (1 s) ou valor positivo.
5. **Cota por par** → `peer.rate_limit_per_minute` (padrão 60, coluna da `0008`) segura o envio
   dentro da janela; as linhas ficam `Pending` e saem no minuto seguinte — sem ação.
6. **Linhas `Failed`** → este runbook não as trata: siga para [dlq-cheia.md](dlq-cheia.md).
7. **Linhas `Cancelled`** → nada a reprocessar: a reavaliação R-05 negou com motivo auditável na
   própria linha (`reason`), por design.

**NÃO-executar** — não existe rota de reprocessamento na P-06 (nenhuma API de reenfileirar, apagar
ou editar entrega). `UPDATE`/`DELETE` manual em `peer_delivery` ou `peer` mexe no único registro
auditável da fila: só com decisão registrada pelo humano, nunca como rotina, e nunca para
"destravar" um `Pending` — o motor reprocessa sozinho.

## Verificação

1. Contagem por estado (EXECUTÁVEL — exit 0; o `Pending` do par consertado desce até sumir):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "estados:" "select state, count(*) from peer_delivery group by state"
   ```

   Resultado de sucesso observado na prova: linhas do par saem de `Pending` para `Sent`
   (`attempts` incrementado, `reason` vazio) e o par vira
   `('127.0.0.1:5602', 'Healthy', 0, None)` — `status = 'Healthy'`, `failure_count = 0`,
   `backoff_until` nulo.

2. O destinatário confirma: no outro silo o `Follow`/`Create` aparece aplicado — coleção
   `followers` com `totalItems:1` e a nota na outbox do contêiner (passos 5 e 9 de
   [federacao-interop.md](federacao-interop.md)).

3. Nenhum aviso novo de `peer delivery pass failed` / `peer delivery dead-lettered` no terminal
   do silo depois do conserto.

4. Gate local do repositório (EXECUTÁVEL):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
   ```
