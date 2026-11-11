<!--
templateVersion: 1.0.0
-->
Tests that prove the work item, and how a human reproduces them. Written by QA; the scripts are real and were executed.
Write responsibility: QA.

- status: VEREDITO
- owner: qa
- lastVerified: 2026-11-10 17:55

# QA-0029 — Link unfurl com guarda SSRF

- task: TASK-0029
- spec: SPEC-0009
- runId: R-20261109-01
- verdict: PASSED
- qaAttempt: 1/2

Normative reading used: SPEC-0009 section `G` + `Gaps` (GAP-26 — síncrono, timeout 3 s, fail-open,
SSRF por IP resolvido com revalidação a cada salto, só `http/https`, ≤ 1 MB, redirects
revalidados), contrato `http-api.md` §5 (post que é uma URL ganha `Attachment.Kind: Link` com
`og:title`/`og:description`/`og:image`) e `data-model.md` §2.6 (colunas fixas do `Attachment` +
invariante "`Link` não tem blob"). Mapeamento OG nas colunas fixas de §2.6 (`url`=og:image
externo, `alt`=og:title, `blurHash`=og:description, `contentHash` vazio, `mimeType`=Content-Type)
adjudicado em nível de especificação pelo ODEV antes do dispatch — testado como está, não revisto.

**Verdict: PASSED** — the five ACs are proven by executed scenarios, no BLOCKER finding, and every
DEV decision the dispatch asked me to verify was verified **by execution, not by reading**:

1. **one lone URL only** — `https://…` alone unfurls; a text with an embedded URL and plain text
   publish with zero attachments and the handler is never called (DEV `gap26` test + unit rule);
2. **`AllowAutoRedirect=false` + manual hops, max 3** — the registration is read at
   `SquidServiceHost.cs:144`, and the budget is executed: a chain that never ends makes exactly
   **4** fetches and then fails open (QA `A_chain_beyond_the_three_hop_budget…`), while a
   legitimate 2-hop public chain follows both hops and still produces the card;
3. **declared `Content-Length` above the cap → zero bytes read** — DEV's counting stream reads
   `0` of 8192 bytes against the 4096 cap (can-fail **M5**);
4. **the resolver's ALL answers are validated, every hop** — a name that resolves **only** to a
   private answer never fetches (6 answers exercised: `10.1.2.3`, `169.254.169.254`, `100.64.0.1`,
   `172.16.0.9`, `fd00::1`, `::1`), a mixed round is refused by DEV's test, and a **redirect to a
   name that resolves private is refused mid-chain** with exactly 1 request made (can-fail
   **M2**, **M3**);
5. **`SqliteObjectStore` skips the media-index row for an empty `contentHash`** — after a Link
   post and a media post on the same host the `GrainState` table holds **exactly one**
   `post-media-ref` row, and it carries the media hash (QA `A_link_attachment_never_borns…`);
6. **the card only fits under the 10-attachment cap** — 10 media attachments + a lone-URL content
   publishes `201` with 10 images and **no** link card, while the unfurl did run (1 request);
7. **unfurl only on create** — a reply whose content is a URL publishes with zero attachments and
   only the parent was fetched (replies parked, not an AC);
8. **no real network in this task's tests** — static scan of `tests/Squid.Tests/Unfurl/**` finds
   no `HttpClient(`/`Dns.`/`WebRequest`/`TcpClient`/`SocketsHttpHandler`, the fixture resolves
   both seams to the fakes (`Assert.IsType` over DI), and every hostname is either mapped in the
   fake resolver or an IP literal.

## What is proven

| AC | Scenario (executed) | Script | Result |
| --- | --- | --- | --- |
| AC-01 | DEV: `UnfurlPublishTests.A_post_whose_content_is_one_url_publishes_the_opengraph_card` — `201`, one `kind=link`, `alt`=og:title, `blurHash`=og:description, `url`=og:image externo, `contentHash` vazio, `mimeType=text/html`, **one** fetch to the RAW url (o `&amp;` armazenado nunca é pedido) e round-trip por `GET /api/posts/{id}`. QA: `QaTask0029Tests.A_public_redirect_chain_is_followed_and_the_card_comes_out` — o card sai também com 2 saltos seguidos à mão. Can-fail **M6** (`Alt = og.Title` → `string.Empty`, vermelho no teste do DEV **e** no da QA) | `backend/tests/Squid.Tests/Unfurl/{UnfurlPublishTests,QaTask0029Tests}.cs` · filtro `FullyQualifiedName~Squid.Tests.Unfurl` → `20261110-172550-test-filter.log` 63/63 exit 0 | PASS |
| AC-02 | DEV: `A_fetch_that_never_answers_publishes_the_url_alone` (handler em `Task.Delay(Timeout.InfiniteTimeSpan)`, host com `Unfurl:TimeoutMs=500`, cronômetro < 10 s) e `A_failed_fetch_publishes_the_url_alone` (`500` → `201` sem card). QA: `A_chain_beyond_the_three_hop_budget…` — cadeia infinita de `302` → `201` só com a URL depois de exatamente 4 fetches. Can-fail **M4** (`catch (Exception)` → `when (ex is null)`: só o teste do timeout ficou vermelho, `20261110-173617`) | mesmos arquivos · `20261110-173617-test-filter.log` (M4) | PASS |
| AC-03 | **Literais/esquemas** (DEV): teoria com `127.0.0.1`, `10.1.2.3`, `192.168.1.10`, `169.254.169.254`, `100.64.0.1`, `[::1]`, `file:`, `ftp:` → `201` sem card e **0** chamadas do handler; `SsrfGuard` unitário com 19 recusas + 7 admissões nas fronteiras exatas. **IP resolvido** (QA): `A_name_that_resolves_only_to_private_answers_never_fetches` (6 respostas, inclusive só-AAAA `fd00::1`) → 0 fetches; DEV `A_host_with_even_one_private_answer_never_fetches` (rodada mista). **Revalidação por salto**: DEV `A_redirect_outside_the_perimeter_is_never_followed` (`192.168.0.10`, `[::1]`, `file:` → 1 request só) e QA `A_redirect_to_a_name_that_resolves_private_is_refused_mid_chain` (`public.test`→pública, `302` para `interno.test`→`10.0.0.5` → 1 request, o 2º salto nunca pedido). Can-fail **M1** (faixa CGNAT removida → 4 vermelhas), **M2** (respostas do resolver ignoradas → 9 vermelhas, entre elas as duas da QA), **M3** (revalidação só no hop 0 → 3 vermelhas, entre elas a da QA) | `QaTask0029Tests.cs` · `UnfurlUnitTests.cs` · `20261110-173501` (M1) / `173526` (M2) / `173552` (M3) | PASS |
| AC-04 | DEV: `A_declared_body_above_the_cap_is_never_read` — `Content-Length` declarado 8192 > teto 4096 → `BytesRead == 0` (nenhum byte lido) e `201` sem card; `A_streamed_body_above_the_cap_is_cut_at_the_limit` — corpo sem `Content-Length` → leitura cortada no teto (`≤ 4096`, `< 8192`) e `201`. Defaults do spec (`3000` ms / `1048576` bytes) provados por `The_options_default_to_the_spec_and_override_from_configuration`. Can-fail **M5** (`if (declared > MaxBytes)` neutralizado → só `A_declared_body_above_the_cap_is_never_read` vermelha, `20261110-173643`) | `UnfurlPublishTests.cs` · `UnfurlUnitTests.cs` · `20261110-173643-test-filter.log` (M5) | PASS |
| AC-05 | **Gate:** `sdd-test.ps1 test` → `20261110-174218-test-gate.log` com `exit: 0` e `Com falha: 0, Aprovado: 411, Total: 411` lido **dentro** do log (S-22); baseline da QA `20261110-172026` = 398/398 exit 0. **Escopo:** 37 arquivos com mtime desde o dispatch 15:27 — código **todos** dentro dos `touches` (`Squid.Api/Posts/**`, `Squid.Service/**`, `tests/Squid.Tests/Unfurl/**`), o resto é artefato de fluxo (task files, logs do gate) e desta QA (0 fora da allowlist); delta git final vs `QA-0029-git-baseline.txt` → **+10, todas** `docs/sdd/qa/QA-0029*`/`qa-*-task-0029.*`/`tests/Squid.Tests/Unfurl/QaTask0029Tests.cs`, **0 removidas**. **Sem rede real:** varredura estática (0 achados) + `Assert.IsType` das duas costuras no DI + todo hostname ou mapeado no resolver falso ou literal de IP. Resíduo `QA-MUTATION` em `backend/` = **0**; SHA256 dos 2 arquivos mutados = PRE após cada ciclo (`SsrfGuard.cs 69A320484467`, `LinkUnfurler.cs 818B60E9A866`); backups `qa0029-*` no TEMP = 0 | gate + varreduras + `qa-check-task-0029.ps1` / `.sh` | PASS (suite Linux: unverified) |

## Scripts

| Script | Language / runner | What it does |
| --- | --- | --- |
| `backend/tests/Squid.Tests/Unfurl/QaTask0029Tests.cs` | xunit via `sdd-test.ps1 test -TestFilter` | 13 cenários próprios (hard spots): perímetro contra IP **resolvido** (6 respostas), revalidação do salto contra IP resolvido, cadeia pública legítima, orçamento de 3 redirects, costuras DI = fakes, linha do índice de mídia sem `contentHash`, teto de 10 anexos e reply sem card |
| `backend/tests/Squid.Tests/Unfurl/{UnfurlHost,UnfurlPublishTests,UnfurlUnitTests}.cs` | xunit (testes do DEV, re-executados) | os 50 testes do gate 398 — re-executados no filtro `~Unfurl` e no gate final |
| `docs/sdd/qa/qa-mutate-task-0029.ps1` | pwsh (gate `sdd-test.ps1`) | **6** ciclos can-fail (M1..M6): SHA256 PRE (S-16) → edição `QA-MUTATION` com pattern-count=1 → vermelho classificado **no log** (S-22) → restore byte-exact da memória + bump de mtime (S-23) → hash=PRE → resíduo 0 → GREEN 63/63 |
| `docs/sdd/qa/qa-check-task-0029.ps1` / `.sh` | pwsh / bash (WSL) | 10 checks de evidência (gates, mutações, resíduo, escopo/git, control block, sem-rede, GREEN, doc) + o `.sh` grava o platform probe do `sdd-test.sh` |
| `docs/sdd/qa/QA-0029-mutations.log` | artefato | saída dos 6 ciclos + `SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0` |
| `docs/sdd/qa/QA-0029-sh-twin.log` | artefato (bash/WSL) | `sdd-test.sh probe` e `test` no Linux da máquina |
| `docs/sdd/qa/QA-0029-git-baseline.txt` | artefato | `git status --porcelain -uall` capturado **antes** da QA escrever (160 entradas) |

## How to reproduce

Prerequisites: Windows com `dotnet` 10 e o repositório em `C:\dev\squid-squad-net\squid-execution`
(nenhuma instalação, nenhum serviço externo — os testes sobem host local em porta dinâmica e o
unfurl só enxerga os handlers falsos).

```
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test -TestFilter "FullyQualifiedName~Squid.Tests.Unfurl"   # esperado: exit 0, Aprovado 63, Total 63
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-mutate-task-0029.ps1                                                  # esperado: exit 0, SUMMARY cycles=6 failed=0 residue-QA-MUTATION=0 temp-backups=0
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/_tools/sdd-test.ps1 test                                                    # esperado: exit 0, Com falha 0 / Aprovado 411 / Total 411
powershell -NoProfile -ExecutionPolicy Bypass -File docs/sdd/qa/qa-check-task-0029.ps1                                                   # esperado: SUMMARY total=10 fails=0, exit 0
bash docs/sdd/qa/qa-check-task-0029.sh                                                                                                   # esperado: SUMMARY total=10 fails=0, exit 0 (WSL)
```

## Last run

| Date/Time | Command | Exit | Output (max 10 lines) |
| --- | --- | --- | --- |
| 2026-11-10 17:20:26 | `sdd-test.ps1 test` (baseline da QA) | 0 | `20261110-172026-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 398, Total: 398` (3 m 56 s) |
| 2026-11-10 17:25:50 | `sdd-test.ps1 test -TestFilter FullyQualifiedName~Squid.Tests.Unfurl` | 0 | `20261110-172550-test-filter.log` → `Com falha: 0, Aprovado: 63, Total: 63` (50 DEV + 13 QA) |
| 2026-11-10 17:35:01–17:37:10 | `qa-mutate-task-0029.ps1` (6 ciclos) | 0 | `QA-0029-mutations.log` → M1..M6 `RED-IN-LOG` + `RESTORE-OK hash=PRE residue=0` · vermelhos nos logs `20261110-173501/173526/173552/173617/173643/173710` · `SUMMARY cycles=6 failed=0 residue=0 temp-backups=0` |
| 2026-11-10 17:37:37 | `qa-mutate-task-0029.ps1` (GREEN sobre as fontes restauradas) | 0 | `20261110-173737-test-filter.log` → `exit: 0` · `Com falha: 0, Aprovado: 63, Total: 63` |
| 2026-11-10 17:42:18 | `sdd-test.ps1 test` (gate final AC-05) | 0 | `20261110-174218-test-gate.log` → `exit: 0` · `Com falha: 0, Aprovado: 411, Total: 411` (4 m 7 s) |
| 2026-11-10 17:44 | varredura de escopo (mtimes desde 15:27 + git vs baseline + resíduo) | 0 | 35 arquivos, **0 fora dos touches** · git **+5** só artefatos QA · resíduo `QA-MUTATION` **0** |
| 2026-11-10 18:06 | varredura final de escopo (após doc e checkers) | 0 | 37 arquivos, **0 fora da allowlist** · git **+10**, todas `QA-0029*`/`qa-*-task-0029.*`/`QaTask0029Tests.cs`, **0 removidas** · resíduo **0** |
| 2026-11-10 17:57 | `qa-check-task-0029.ps1` (2ª execução, após corrigir 2 defeitos meus) | 0 | `SUMMARY total=10 fails=0` — 1ª execução (17:52) saiu `fails=2`: regex do control block sem `\r` (arquivo CRLF) e padrão `Dns\.` casando o comentário "real DNS." |
| 2026-11-10 18:02 | `bash docs/sdd/qa/qa-check-task-0029.sh` (WSL, 3ª execução) | 0 | `SUMMARY total=10 fails=0` — 1ª execução (17:59) inverteu a comparação de exit-status (`0` vs `1`) e a 2ª (18:00) brigou com o BOM/CR do baseline; ambos corrigidos, os vermelhos ficam registrados aqui |
| 2026-11-10 18:02 | `sdd-test.sh probe` / `test` (WSL bash, gravado pelo checker) | 0 / 1 | `QA-0029-sh-twin.log` → `dotnet: command not found` · `sdd-test: no *.sln found within depth 3` · `test: exit 127 (real test failure)` → script sai **1** (Linux = `unverified`, achado 1) |

## Findings

| # | Severity | Where | What breaks | Evidence |
| --- | --- | --- | --- | --- |
| — | — | — | **Nenhum achado de produto desta task.** Os 5 ACs passaram e cada hard spot provou pode-falhar: M1/M2/M3 sobre o AC-03, M4 sobre o AC-02, M5 sobre o AC-04, M6 sobre o AC-01 | `QA-0029-mutations.log` |
| 1 | MINOR (pré-existente, re-confirmado) | `docs/sdd/_tools/sdd-test.sh:21,64` | O gêmeo Linux do gate **procura `*.sln`** (o solution é `*.slnx`), acha vazio, segue para `dotnet test ""` → `dotnet: command not found` → e imprime `test: exit 127 (real test failure)` saindo com **1**, sem a guarda de ambiente `exit 3` do `.ps1`: classifica ferramenta ausente como falha real (S-22 no traje de Linux). Mesmo achado das QA-0023..0028; não é desta task | `QA-0029-sh-twin.log` (probe exit 0 / test exit 1) |
| 2 | MINOR (contrato — **para adjudicação/consolidação**, não derruba AC) | `specs/001-squid-mvp/data-model.md:178` vs `backend/src/Squid.Api/Posts/LinkUnfurler.cs:175` | §2.6 documenta `Url` como "Servido pela instância", mas o card `Kind: Link` guarda ali o `og:image` **externo** de terceiros (sanctionado pela própria premissa da task: "og:image é guardada como URL externa no attachment Link"). Junto com o cheiro já registrado pelo ODEV (`blurHash` segurando prosa de descrição), é material para `contracts.md`/consolidação — consumidor que assumir `url` local (proxy, auth, apagar mídia própria) ou `blurHash` como placeholder de imagem erraria no card Link | `LinkUnfurler.cs:167-179`; task `## Gaps` linha 85 |

Notas (não são defeitos):

- **Gate 411 = 398 (DEV) + 13 (QA)**: os 13 são 6 casos de teoria + 7 facts novos desta QA; o
  baseline 398/398 foi medido **antes** de eu escrever qualquer teste.
- **Primeira tentativa do driver de mutação abortou por defeito meu**: um `—` (U+2014) no `.ps1`
  sem BOM é lido pelo PowerShell 5.1 como CP1252 e o byte `0x94` vira `"` → erro de parse. Script
  reescrito em ASCII puro; nada foi mutado nessa tentativa (o aborto aconteceu antes de qualquer
  edição) e a prova está no histórico desta sessão.
- **`Unfurl:TimeoutMs`/`Unfurl:MaxBytes` no rota**: os testes de roda usam os overrides do fixture
  (`500` ms / `4096` bytes) — é o mesmo caminho de código dos defaults; os defaults `3000`/`1 MB`
  são provados pela unitária de opções.
- **`og:image` nunca é baixado**: verificado estaticamente (`LinkUnfurler` tem um único fetch por
  cadeia; `ImageOf` só resolve a URL) e pela asserção de **uma** chamada do handler nos testes de
  card.

## Not covered, and why

- **Suíte do produto em Linux** — o WSL desta máquina não tem `dotnet` (instalar exige gate
  humano; aprovações desativadas nesta sessão) → `unverified`, nunca assumido igual ao Windows que
  passou. O que **foi** executado no Linux: `qa-check-task-0029.sh` (10 checks) e o platform
  probe do `sdd-test.sh` (achado 1).
- **`DnsUnfurlResolver` (resolução real)** — nunca executado, porque a AC-05 proíbe rede real no
  gate; só a costura `IUnfurlResolver` é coberta (o fake devolve todas as respostas que o guard
  valida). Risco: um resolver de produção que devolvesse lista vazia para nome existente seria
  recusado (fail-open conservador), e um nome que resolvesse para IP privado **fora** do guard
  (rebind entre check e conexão) continua sendo o shortcut declarado em `LinkUnfurler.cs:115` e
  já *parked* pelo ODEV (pinning da conexão).
- **Página real de 1 MB no default** — o AC-04 é executado no teto do fixture (4096) nos dois
  modos (declarado e streamed); o valor default `1048576` é unitário. Risco: desprezível — mesma
  linha de código.
- **Charset não-UTF-8** — shortcut declarado em `LinkUnfurler.cs:162` (latin-1 pode embaçar, nunca
  falhar); não exercitado.
- **Corpo de exatamente `Unfurl:MaxBytes`** — o shortcut de `LinkUnfurler.cs:158` (chunked de
  tamanho exato do teto perde o card) não é exercitado; declarado pelo próprio código.
- **Compose/CI** — fora desta máquina (gate `compose` exige podman + rede); fica para o run ubuntu
  do CI.

## Memory bank (rota `qa-integracao`)

- Rota lida (3 arquivos, 115 linhas): `build-run-test.md#Local tests without gate` — **S-22 e
  S-23 re-confirmados ao vivo** (todo vermelho classificado dentro do próprio log; todo restore
  com bump de mtime e hash = PRE); `contracts.md` — segue **sem** menção do unfurl/card Link (ver
  flag); `integrations.md` — ainda template `RASCUNHO`, sem conteúdo a conferir.
- `[GAP]` `docs/memory-bank/contracts.md:18` — ainda afirma que "publicação, timeline, mensageria,
  mídia … seguem sem handler", mas `POST /api/posts` publica desde a TASK-0023 e o card `Link`
  desta task não está registrado; o `507` e a mídia já foram levantados nas QA-0026..0028.
  Proposta de consolidação no fecho da P-04; o arquivo só se escreve pelo consolidador.
- Os flags `[MUDANCA_RELEVANTE: SEGURANCA]` (guarda SSRF) e `[MUDANCA_RELEVANTE: CONTRATO]`
  (card Link) já foram registrados pelo ODEV no `## Log` da task (17:03:15) — confirmados por
  execução, não duplicados aqui.
