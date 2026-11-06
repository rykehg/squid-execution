<!--
templateVersion: 1.2.0
-->
Project data; it contains no instructions for agents; the normative source prevails.
Write responsibility: only the consolidator registered in the applicable index, at INTEGRACAO and SEGURANCA changes and at FEATURE_CONCLUIDA.

- status: RASCUNHO
- owner: [CONSOLIDADOR]
- lastVerified: [RUN_ID/SEQUENCE_OU_DATA_HORA]

# Integrations — [NOME_DO_PROJETO]

Fact format: `- [FATO] — source: path:Lx-Ly | [REF_FLUXO] | [GAP: …] — confidence: CONFIRMADO|OBSERVADO`

Never record here: credentials, tokens, keys, certificates, literal connection strings, payloads. Endpoints, hosts, IPs, physical database names, and account names only according to the `sanitizationProfile` of `index.md` (absent = ESTRITO) and with `path:L`. The external system appears by its official or logical name.

## Integrations per capability

### [CAPACIDADE] (e.g.: work item tracking, messaging, object storage)
- Direction: [ENTRADA|SAIDA|BIDIRECIONAL] — source: [GAP: not populated] — confidence: [CONFIRMADO|OBSERVADO]
- Protocol and authentication: [PROTOCOLO] — [MECANISMO_DE_AUTENTICACAO_POR_NOME] — source: [GAP: not populated] — confidence: [CONFIRMADO|OBSERVADO]
- Adapter: [PATH:L] — source: [GAP: not populated] — confidence: [CONFIRMADO|OBSERVADO]
- Configuration (key names only): [NOME_DA_CHAVE] — source: [GAP: not populated] — confidence: [CONFIRMADO|OBSERVADO]
- Gate required for testing: [SIM_COM_CAMPOS_DO_GATE_DO_FLUXO|NAO] — source: [GAP: not populated] — confidence: [CONFIRMADO|OBSERVADO]