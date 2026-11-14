# Runbook — DLQ cheia

- fase: P-06 · status: EXECUTÁVEL · origem: doc 05 §8, SPEC-0006 (runbook executável desde a
  P-06: fila/DLQ da TASK-0046, comandos provados na TASK-0048)
- leitura da DLQ usa `python` com o `sqlite3` padrão; o banco é o `Storage:Path` do silo
- marcas: **EXECUTÁVEL** = executado na prova com exit 0 · **NÃO-executar** = destrutivo

## Sintoma

A fila de falhas de entrega (`peer_delivery` com `state = 'Failed'`) cresce sem parar — ou chega
perto do ponto em que não vale mais tentar o par — e o terminal do silo registra o alerta desta
fase (EXECUTÁVEL — linha real observada na prova da TASK-0048):

```
peer delivery dead-lettered (Follow -> "127.0.0.1"): "signature_rejected"
```

Cada linha `Failed` carrega o motivo em `reason` e é **terminal**: o motor nunca mais a toca
(N = 8 tentativas ou 4xx permanente). A contagem `federation.delivery_dlq` existe no código
(`FederationMetrics.cs`) mas esta fase não tem pipeline de métricas exportado — o composition root
publica só traces OTLP (`SquidServiceHost.cs:77`), então **o alerta operacional é o aviso de log
acima**.

## Diagnóstico

1. **Contar a DLQ por motivo** (EXECUTÁVEL — exit 0):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "DLQ por motivo:" "select reason, count(*) from peer_delivery where state='Failed' group by reason"
   ```

2. **Ver as linhas** (EXECUTÁVEL — exit 0; `activity_type` + `recipient_actor_id` dizem o que
   caiu e para onde):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "DLQ:" "select activity_type, attempts, reason, recipient_actor_id from peer_delivery where state='Failed'"
   ```

3. **Cada motivo, o que ele é** (fonte: `OutboundPipeline.cs` e `PeerDeliveryPump.cs`):

   | `reason` | Origem | Leitura |
   | --- | --- | --- |
   | `signature_rejected` | o par respondeu `401`/`403` | a assinatura não passou no par: `Squid:RecoveryKey`/cópia da `0009` erradas para o ator, ou `keyId`/chave publicada divergente — nunca retry cego com a mesma chave (contrato §5) |
   | `peer_rejected` | o par respondeu outro `4xx` | recusa permanente do destino (rota inexistente, payload rejeitado) |
   | `max_attempts` | oitava tentativa de `send_failed` | par ficou indisponível tempo demais (backoff até 6 h) |
   | `invalid_recipient` | destinatário não é URI `http(s)` | dado de entrada ruim; a fila não tem para onde entregar |

4. **Reproduzir sem par real** (EXECUTÁVEL — `Squid.PeerClient`): um `401` do par sintético derruba
   a linha em `Failed / signature_rejected` **na primeira tentativa** (prova da TASK-0048):

   ```powershell
   dotnet run --project backend/src/Squid.PeerClient -- --scenario=401     # também 404, ok, timeout, slow; --help sai exit 0
   ```

   Depois aponte uma entrega para a origem impressa (mesmo `POST /api/federation/follow` do
   [peer-entrega-travada.md](peer-entrega-travada.md) §3) e observe a linha e o aviso de log.

## Procedimento

Nada aqui apaga sozinho — o ciclo é **diagnosticar, decidir, agir, verificar**:

1. Classifique as linhas pelo `reason` (diagnóstico 1).
2. Corrija a **causa** antes de qualquer reentrega:
   - `signature_rejected` → confira `Squid:RecoveryKey` no boot e o momento do registro dos atores
     (duplo embrulho da `0009`; atores antigos à config não federam — GAP-50), e o que o par
     publica/aceita (contrato §5: `rsa-sha256`, `keyId` `#main-key`, `Digest` só SHA-256).
   - `peer_rejected`/`max_attempts` → levante o par (use o `Squid.PeerClient` para provar a fila
     de pé) e confirme a allowlist `Federation:TrustedPeerHosts` para pares locais.
   - `invalid_recipient` → corrija a origem do dado que gerou a entrega.
3. **Decisão humana registrada** (obrigatória, doc 05 §8): cada linha `Failed` vira **reentrega**
   ou **descarte**. Reentrega nesta fase = republicar a atividade pela origem (a rota de follow ou
   uma nova publicação) — **não existe rota de requeue** no produto; as linhas atuais ficam como
   registro auditável. Descarte é passo 4.
4. **NÃO-executar** — descarte manual só com decisão registrada, nunca como rotina:

   | Comando | Marca | Efeito |
   | --- | --- | --- |
   | `DELETE FROM peer_delivery WHERE state='Failed'` (via `sqlite3`/`python`) | **NÃO-executar — DESTRUTIVO** | apaga o único registro auditável do motivo da falha (`0008_federation.sql` guarda `reason` justamente para isto) |

5. Nenhuma destas operações derruba o serviço: entregas em `Failed` são ignoradas pelo motor e a
   publicação local continua normalmente (FR-040/§7.6).

## Verificação

1. A contagem da DLQ volta ao patamar decidido (EXECUTÁVEL — exit 0):

   ```powershell
   python -c "import sqlite3,sys;c=sqlite3.connect(sys.argv[1]);print(sys.argv[2]);[print(r) for r in c.execute(sys.argv[3])]" $env:TEMP\squid-a.db "estados:" "select state, count(*) from peer_delivery group by state"
   ```

   Observado na prova da TASK-0048: `('Failed', 1)` ao lado de `('Sent', 2)` e `('Pending', 1)` —
   cada estado é contado por `state`, então DLQ cheia é `Failed` alto e crescendo.

2. Nenhum `peer delivery dead-lettered` novo no terminal do silo depois do conserto.

3. O par voltou a aceitar: linhas novas saem `Sent` e o `peer` correspondente responde
   `('…', 'Healthy', 0, None)` (passo de verificação do [peer-entrega-travada.md](peer-entrega-travada.md)).

4. Gate local do repositório (EXECUTÁVEL):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test
   ```
