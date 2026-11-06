# Feature Specification: Squid — MVP de rede social federada

**Feature Branch**: `001-squid-mvp`

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "Especificar e planejar o doc 05 (`features/05-arquitetura-consolidada.md`): MVP de rede social open-source, federada e modular, com mensagens, grupos e comunidades, papéis, políticas de acesso configuráveis, compartilhamento externo por link, identidade recuperável, operação observável e backup restaurável — sem E2EE e sem sharding no MVP."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Identidade local recuperável (Priority: P1)

Uma pessoa cria uma conta em uma instância Squid, recebe um perfil público endereçável (handle) e baixa um kit de recuperação. Meses depois, com a instância perdida, ela reinstala o software em outra máquina, importa o kit e recupera a **mesma** identidade — mesmo handle, mesma reputação, mesmas relações — sem depender de e-mail, SMS ou suporte humano.

**Why this priority**: sem identidade estável e recuperável, nada mais importa: grupos, posts e seguidores são todos referências ao ator. É também o único item cuja falha é irreversível (perda de chave = perda da identidade federada).

**Independent Test**: criar conta em instância limpa, exportar o kit, apagar o diretório de dados, reinstalar, importar o kit e verificar que o handle resolve para a mesma chave pública e que os grupos da conta continuam acessíveis.

**Acceptance Scenarios**:

1. **Given** uma instância vazia, **When** uma pessoa se registra com handle válido, **Then** existe um perfil público acessível pelo handle, com chave pública publicada e o handle reservado de forma única.
2. **Given** uma conta recém-criada, **When** ela solicita o kit de recuperação, **Then** recebe um arquivo com a chave raiz cifrada por senha, um mnemônico de 24 palavras e uma credencial de recuperação independente, e o sistema confirma que o kit foi verificado.
3. **Given** um kit válido, **When** ele é importado em uma instância limpa, **Then** a identidade resultante é a mesma (mesmo handle, mesma chave pública) e a conta antiga deixa de ser a única cópia.
4. **Given** um kit com senha errada, **When** ocorre a importação, **Then** o sistema recusa sem destruir dados existentes e informa quantas tentativas restam.
5. **Given** uma conta sem kit verificado, **When** o usuário faz login, **Then** a interface mostra um aviso persistente de risco de perda irreversível.
6. **Given** uma conta, **When** o usuário edita nome, descrição ou avatar, **Then** o perfil público reflete a mudança e o handle permanece o mesmo.

---

### User Story 2 - Grupo de amigos com hierarquia e política (Priority: P1)

Um grupo de amigos cria um grupo fechado, convida pessoas, define quem pode entrar, quem pode postar e quem modera. Uma das pessoas vira "publicador único" do mural do grupo sem virar administradora. Quem não é membro não vê nada.

**Why this priority**: é a primeira camada de sociabilidade do produto e o lugar onde o modelo de permissão é validado. Errar permissão aqui contamina comunidades e federação.

**Independent Test**: criar um grupo padrão (fechado, por convite), tentar entrar sem convite (deve falhar), entrar com convite, promover alguém a moderador, ativar publicador único e comprovar que um membro comum é bloqueado e o publicador nomeado é aceito.

**Acceptance Scenarios**:

1. **Given** um grupo criado com o preset "amigos", **When** alguém sem convite tenta entrar, **Then** a entrada é negada e o motivo é registrado na auditoria.
2. **Given** um grupo com convite emitido, **When** o convidado aceita, **Then** ele se torna membro com papel inicial e passa a ver o conteúdo conforme a política.
3. **Given** um grupo com publicador único, **When** um membro comum tenta publicar, **Then** a publicação é rejeitada; **When** o publicador nomeado publica, **Then** é aceita.
4. **Given** um membro promovido a moderador, **When** ele remove um conteúdo de terceiro, **Then** a remoção é aplicada e auditada com a versão de política vigente.
5. **Given** um visitante sem vínculo, **When** ele tenta ler qualquer post do grupo, **Then** recebe "não encontrado" — nunca "não autorizado" com conteúdo vazado em cache, título ou preview.
6. **Given** um grupo cujo Proprietário ficou inativo por 90 dias, **When** o sucessor nomeado assume a propriedade, **Then** a mudança é aplicada, os administradores são notificados e a auditoria registra quem assumiu e quando.

---

### User Story 3 - Publicar e consumir feed (Priority: P1)

Uma pessoa publica texto, link ou imagem em um contêiner e vê o próprio feed atualizar sem recarregar a página. Colegas que estavam online recebem o post em segundos. Quem voltou depois sincroniza só o que perdeu, sem baixar o feed inteiro.

**Why this priority**: é o valor visível do produto. Sem publicação e feed, grupo e comunidade são apenas formulários.

**Independent Test**: publicar três tipos de conteúdo, observar a chegada em outra sessão autenticada sem refresh, desconectar, publicar mais itens e reconectar verificando que só o delta é entregue.

**Acceptance Scenarios**:

1. **Given** dois membros online no mesmo contêiner, **When** um publica, **Then** o outro vê o post sem recarregar, em menos de 2 segundos.
2. **Given** um cliente que ficou offline, **When** ele reconecta informando o último item que viu, **Then** recebe apenas os itens posteriores, na ordem correta.
3. **Given** um post com imagem, **When** ele é exibido, **Then** o preview aparece antes do download completo e existe descrição alternativa obrigatória para acessibilidade.
4. **Given** um post com visibilidade não listada, **When** um não membro consulta o feed do contêiner, **Then** o post não aparece.
5. **Given** um autor, **When** ele exclui o próprio post, **Then** o post desaparece do feed e das referências de leitura em até 5 segundos.

---

### User Story 4 - Comunidade de interesse (Priority: P2)

Uma comunidade pública de interesse é descoberta por busca, aceita entrada aberta ou por pedido, publica em feed cronológico e escala para milhares de membros sem degradar o tempo de abertura do feed.

**Why this priority**: é a segunda camada social e o teste de escala do agregado — mas entrega valor apenas depois que grupos e publicação funcionam.

**Independent Test**: criar comunidade pública, entrar sem convite, publicar, e medir o tempo de abertura do feed com um volume grande de conteúdo simulado.

**Acceptance Scenarios**:

1. **Given** uma comunidade com entrada aberta, **When** alguém entra, **Then** vira membro imediatamente e vê o feed.
2. **Given** uma comunidade com entrada por pedido, **When** alguém solicita, **Then** a solicitação fica pendente até aprovação de moderador.
3. **Given** uma comunidade listada, **When** alguém busca pelo nome, **Then** ela é encontrada; comunidades de amigos não listadas nunca aparecem.
4. **Given** uma comunidade com grande volume de conteúdo, **When** o feed é aberto, **Then** a primeira página chega em menos de 1 segundo no percentil 95.

---

### User Story 5 - Mensageria 1-1 e em grupo (Priority: P2)

Duas pessoas conversam em privado e criam uma conversa em grupo. Terceiros não leem, nunca. A interface deixa explícito que o operador da instância tecnicamente consegue ler as mensagens, e o usuário aceita isso antes da primeira mensagem.

**Why this priority**: é a terceira camada, central na promessa do produto, mas depende de identidade e papéis. Sem E2EE no MVP, o consentimento informado é parte do escopo, não detalhe.

**Independent Test**: dois usuários trocam mensagens; um terceiro tenta acessar a conversa por identificador direto e falha; envios simultâneos mantêm ordem estável; o aviso de ausência de E2EE é exibido e registrado.

**Acceptance Scenarios**:

1. **Given** dois usuários, **When** um inicia conversa e envia mensagem, **Then** o outro recebe em ordem e o remetente vê confirmação de entrega.
2. **Given** uma conversa entre dois, **When** um terceiro tenta abrir por identificador, **Then** o acesso é negado sem revelar participantes.
3. **Given** envios concorrentes na mesma conversa, **When** as mensagens são listadas, **Then** a ordem é única, estável e idêntica para todos os participantes.
4. **Given** o primeiro uso de mensagens, **When** o usuário abre a tela, **Then** ele vê e aceita o aviso de que não há criptografia ponta a ponta e que o operador pode ler.
5. **Given** um participante, **When** ele marca leitura, **Then** o outro vê o estado de leitura atualizado.
6. **Given** dois participantes de uma conversa em grupo, **When** um responde a uma mensagem específica, **Then** a resposta mostra a referência à original e permanece visível apenas aos participantes.
7. **Given** uma mensagem em uma conversa, **When** um participante reage com emoji, **Then** a reação aparece para os participantes e pode ser removida; quem não participa não vê a reação.

---

### User Story 6 - Federação com uma instância vizinha (Priority: P2)

Publicações públicas de uma instância aparecem em outra — inclusive em instâncias Mastodon e Friendica reais; conteúdo restrito a membros não vaza; entregas repetidas não duplicam nada; instância vizinha fora do ar não trava a publicação local.

**Why this priority**: é o diferencial de rede aberta, mas só faz sentido depois que o produto funciona sozinho. Depende de identidade, contêineres e publicação estarem estáveis.

**Independent Test**: subir duas instâncias isoladas, seguir uma à outra, publicar público (deve aparecer), publicar restrito a membros (não deve aparecer), derrubar a instância B e publicar em A (deve funcionar, com entrega pendente registrada); em seguida repetir a publicação pública contra uma instância Mastodon e uma Friendica reais, que devem aceitar a entrega assinada e exibir o post.

**Acceptance Scenarios**:

1. **Given** duas instâncias com perfis que se seguem, **When** A publica conteúdo público, **Then** B exibe o post com autor, data e mídia corretos.
2. **Given** conteúdo restrito a membros, **When** A publica, **Then** apenas instâncias com membros ativos recebem a entrega; nenhum payload restrito é entregue a terceiros.
3. **Given** uma entrega repetida da mesma atividade, **When** B recebe a segunda cópia, **Then** nada é duplicado e a resposta é de sucesso.
4. **Given** B indisponível, **When** A publica, **Then** a publicação local conclui normalmente e a entrega fica pendente com tentativas crescentes até esgotar, quando vai para a fila de falhas.
5. **Given** uma requisição federada com assinatura inválida, **When** B a recebe, **Then** é rejeitada, contabilizada e não consome recurso de processamento relevante.
6. **Given** uma política alterada depois do enfileiramento, **When** a entrega acontece, **Then** a política vigente é reavaliada e a entrega é cancelada se o acesso já não é permitido.
7. **Given** uma instância Mastodon e uma Friendica reais, **When** A publica conteúdo público, **Then** as duas aceitam a entrega assinada com RSA-SHA256, exibem o post com autor e mídia, e as atividades assinadas que elas enviam de volta são aceitas sem `401` por assinatura.

---

### User Story 7 - Compartilhar para fora (Priority: P3)

Uma pessoa compartilha um post no WhatsApp, Telegram ou X e o link abre com título, descrição e imagem de preview. No celular, o compartilhamento usa a folha nativa do sistema, e quem recebe o link cai direto no post.

**Why this priority**: é um requisito real de produto, mas entregável no fim do MVP e sem nenhuma integração de API com redes fechadas.

**Independent Test**: gerar link de um post público, abrir o link sem sessão e verificar metadados de preview completos com imagem; abrir no celular e confirmar a folha de compartilhamento nativa.

**Acceptance Scenarios**:

1. **Given** um post público, **When** o link é aberto por alguém sem conta, **Then** a página mostra o post com título, descrição e imagem de preview.
2. **Given** o preview em um mensageiro, **When** a imagem é carregada, **Then** existe uma miniatura adequada e o cartão não fica sem imagem.
3. **Given** um post restrito, **When** o link é aberto por não autorizado, **Then** nenhum metadado de preview do conteúdo é exposto.
4. **Given** um celular com aplicativo instalado, **When** o link do post é aberto, **Then** o aplicativo abre no post correspondente.

---

### User Story 8 - Backup cifrado e restauração comprovada (Priority: P1)

O operador da instância configura backup local e uma cópia fora do próprio provedor. Rotineiramente o sistema prova que consegue restaurar: um teste automatizado cria dados, faz backup, destrói tudo, restaura e compara os hashes. Se a restauração falhar, o pipeline quebra.

**Why this priority**: o produto promete soberania de dados; uma promessa sem restauração testada é a falha mais caras do projeto — e é barata de provar agora.

**Independent Test**: rodar o fluxo completo de backup e restauração no ambiente de integração contínua e confirmar igualdade de hashes; depois simular perda do disco local e restaurar apenas da cópia externa.

**Acceptance Scenarios**:

1. **Given** uma instância com dados, **When** o backup roda, **Then** o conteúdo é cifrado na origem e o destino não consegue ler nada sem a chave.
2. **Given** um backup concluído, **When** o operador informa o intervalo, **Then** a próxima execução envia apenas o que mudou desde o último backup.
3. **Given** um backup íntegro, **When** a instância é destruída e restaurada, **Then** dados, contadores e referências conferem por hash e o sistema volta a responder.
4. **Given** uma cópia externa, **When** o disco local é perdido, **Then** a restauração a partir da cópia externa funciona sem intervenção manual além do segredo.
5. **Given** falha de restauração no pipeline, **When** o teste roda, **Then** o pipeline falha de forma ruidosa, com relatório do que divergiu.
6. **Given** uma falha de backup, **When** ela se repete, **Then** o operador é avisado e vê o estado de redundância degradado no painel.

---

### Edge Cases

- Duas pessoas registram o mesmo handle ao mesmo tempo: apenas uma vence, a outra recebe erro claro e nenhuma chave órfã fica no armazenamento.
- Mídia com geolocalização embutida: metadados de localização são removidos antes de qualquer exibição.
- Upload com extensão de imagem e conteúdo executável: rejeitado pela verificação do conteúdo real, não pela extensão.
- Post cujo autor é banido depois da publicação: conteúdo é ocultado conforme a política da instância, sem destruir a auditoria.
- Atividade federada com campos desconhecidos, aninhamento excessivo ou tamanho acima do limite: rejeitada sem derrubar o processo, com métrica incrementada.
- Instância vizinha que envia volumes anormais: limitada por par remoto, sem afetar outros pares.
- Restauração de backup feito com uma versão anterior do software: migração aplicada e verificada, com falha explícita se incompatível.
- Usuário que apaga a conta: conteúdo local removido, aviso de que cópias federadas em outras instâncias podem permanecer.
- Duas abas editando a mesma política de contêiner: a segunda escrita é rejeitada por conflito, não aplicada em silêncio.
- Feed de comunidade com milhares de membros: abertura da primeira página não degrada de forma proporcional ao número de membros.
- Conversa em grupo com participantes removidos: histórico anterior permanece para quem ficou, e o removido perde acesso imediatamente.
- Kit de recuperação perdido e credencial de recuperação também perdida: sistema recusa qualquer atalho de recuperação e documenta o caminho de perda total.
- Backup interrompido no meio: o próximo executa a partir do último ponto íntegro, sem produzir arquivo corrompido aceito como válido.

## Requirements *(mandatory)*

### Functional Requirements

**Identidade e acesso**

- **FR-001**: O sistema MUST permitir que uma pessoa crie uma conta local com handle único e endereço de perfil estável, rejeitando duplicidade com mensagem clara.
- **FR-002**: O sistema MUST autenticar por senha com requisitos mínimos de força, manter sessão e permitir encerramento explícito em todos os dispositivos.
- **FR-003**: O perfil público MUST ser acessível por meio do handle, sem sessão, e MUST expor a chave pública de identidade para verificação por terceiros.
- **FR-004**: O sistema MUST gerar o material criptográfico de identidade no registro e MUST NOT expor a chave raiz em claro em nenhum canal (tela, log, e-mail, API).
- **FR-005**: O sistema MUST gerar um kit de recuperação exportável contendo a chave raiz cifrada por senha, um mnemônico de 24 palavras e uma credencial de recuperação independente da chave raiz.
- **FR-006**: O sistema MUST permitir importar o kit em uma instância limpa e restabelecer a mesma identidade (mesmo handle, mesma chave pública).
- **FR-007**: O sistema MUST alertar de forma persistente quando a conta não possui kit de recuperação verificado ou quando o kit tem mais de 12 meses.
- **FR-008**: O sistema MUST distinguir o operador da instância (administrador global, múltiplos operadores permitidos) e registrar toda ação administrativa com autor, alvo e justificativa.
- **FR-009**: O sistema MUST permitir que o usuário exclua a própria conta e MUST informar que cópias federadas em instâncias de terceiros podem persistir.
- **FR-062**: O sistema MUST NOT coletar e-mail ou telefone como parte da conta no MVP e MUST declarar, na UI e no termo, que não existe recuperação de senha por e-mail — a recuperação de identidade é o kit (FR-005).
- **FR-063**: O sistema MUST permitir ao usuário editar o próprio perfil — nome de exibição, descrição, avatar e cabeçalho — mantendo o handle imutável, e MUST refletir a alteração no perfil público e na federação.

**Contêineres sociais (grupos e comunidades)**

- **FR-010**: O sistema MUST permitir criar um contêiner social escolhendo entre o preset de grupo (amigos) e o de comunidade (interesses), compartilhando papéis, política e publicação.
- **FR-011**: O sistema MUST avaliar quatro políticas independentes e configuráveis: quem publica, quem vê, quem entra e quem interage (comentar, reagir, compartilhar, anexar).
- **FR-012**: O sistema MUST aplicar a hierarquia de papéis Proprietário > Administrador > Moderador > Membro > Visitante, com herança estrita de capacidades.
- **FR-013**: O sistema MUST permitir alterar o papel de um membro e MUST registrar a mudança com quem autorizou, quando e sob qual versão de política.
- **FR-014**: O sistema MUST suportar entrada por convite, por pedido, aberta e fechada, conforme a política vigente do contêiner.
- **FR-015**: O sistema MUST suportar o caso de publicador único, delegando a publicação a pessoas nomeadas sem promovê-las a administradoras.
- **FR-016**: O sistema MUST permitir descobrir comunidades listadas por busca e MUST NOT listar contêineres configurados como não listados.
- **FR-017**: O sistema MUST permitir ao Proprietário transferir a propriedade e excluir o contêiner, com salvaguarda contra exclusão acidental.
- **FR-018**: O sistema MUST negar acesso negado como "não encontrado" quando revelar a existência do recurso já for a própria violação de visibilidade.
- **FR-019**: O sistema MUST registrar toda decisão de política (ator, ação, recurso, resultado, versão de política) em trilha de auditoria consultável.
- **FR-059**: O sistema MUST permitir ao Proprietário nomear um sucessor e MUST permitir que o sucessor assuma a propriedade quando o Proprietário ficar inativo por 90 dias (configurável), com notificação aos administradores e auditoria.
- **FR-060**: O sistema MUST permitir ao operador da instância forçar a transferência de um contêiner sem proprietário ativo, exigindo justificativa e registrando a ação na auditoria.
- **FR-061**: O sistema MUST garantir que nenhum contêiner fique sem Proprietário ativo: o último Proprietário não pode sair nem ser removido, e a exclusão de conta que possua contêiner exige transferência prévia.

**Publicação e leitura**

- **FR-020**: O sistema MUST permitir publicar texto, link, imagem, GIF, figurinha, áudio e vídeo, com limites por tipo configuráveis.
- **FR-021**: O sistema MUST remover metadados de localização e demais metadados sensíveis de toda mídia enviada.
- **FR-022**: O sistema MUST exigir descrição alternativa para imagens e vídeos e MUST oferecer preview visual antes do download completo do arquivo.
- **FR-023**: O sistema MUST oferecer feed cronológico por contêiner e timeline pessoal, sem qualquer ranqueamento algorítmico no MVP.
- **FR-024**: O sistema MUST entregar atualizações de feed ao vivo para clientes conectados, sem recarregar a página.
- **FR-025**: O sistema MUST permitir sincronização incremental por marcador de posição, entregando apenas itens posteriores ao último visto.
- **FR-026**: O sistema MUST aplicar o filtro de visibilidade da política vigente em toda leitura, inclusive em listas, contadores, notificações e previews.
- **FR-027**: O sistema MUST permitir ao autor remover o próprio conteúdo e permitir a moderadores e administradores remover conteúdo de terceiros, com auditoria.
- **FR-028**: O sistema MUST permitir denúncia de conteúdo e MUST oferecer ao operador um fluxo de análise e remoção por obrigação legal.
- **FR-029**: O sistema MUST respeitar cota de armazenamento de mídia por instância e informar quando a cota é atingida.

**Mensageria**

- **FR-030**: O sistema MUST permitir conversa individual e conversa em grupo com ordem total e estável de mensagens.
- **FR-031**: O sistema MUST restringir leitura de conversa a participantes ativos e MUST NOT revelar participantes a quem não pertence à conversa.
- **FR-032**: O sistema MUST exibir aviso explícito de que as mensagens não têm criptografia ponta a ponta e são legíveis pelo operador, registrando o aceite do usuário.
- **FR-033**: O sistema MUST permitir marcar leitura, paginar histórico e informar estado de entrega.
- **FR-057**: O sistema MUST permitir responder a uma mensagem específica dentro da conversa, exibindo a referência à original, com a resposta restrita aos participantes da mesma conversa.
- **FR-058**: O sistema MUST permitir reagir a mensagens com emoji (uma reação por ator e por emoji, removível), com emoji customizado restrito a uma lista permitida, e MUST NOT expor reações ou respostas a quem não participa da conversa.

**Federação**

- **FR-034**: O sistema MUST publicar perfis, objetos e atividades em um protocolo federado aberto, com descoberta por handle.
- **FR-035**: O sistema MUST permitir seguir e ser seguido por atores de outras instâncias e MUST receber atividades de forma idempotente.
- **FR-036**: O sistema MUST entregar atividades com tentativas, recuo progressivo, interrupção por circuito aberto e fila de falhas, sem bloquear a operação local.
- **FR-037**: O sistema MUST aplicar as regras de saída por visibilidade: público para seguidores, restrito a membros para membros ativos, privado sem federação em claro.
- **FR-038**: O sistema MUST reavaliar a política no momento da entrega e MUST cancelar entregas que deixaram de ser permitidas.
- **FR-039**: O sistema MUST assinar toda requisição de saída com RSA-SHA256 e MUST verificar, na entrada, as assinaturas RSA-SHA256 e `hs2019` usadas por Mastodon e Friendica; Ed25519 MUST ser publicado como chave adicional e MUST ser verificado quando o par remoto o enviar.
- **FR-040**: O sistema MUST limitar tamanho, profundidade e taxa de requisições federadas por par remoto, rejeitando payload malformado sem derrubar o serviço.
- **FR-041**: O sistema MUST manter a abstração de protocolo substituível, de modo que o segundo protocolo não exija mudança no domínio.

**Compartilhamento externo**

- **FR-042**: O sistema MUST gerar endereço canônico por post e MUST servir preview social completo (título, descrição, imagem, cartão de mídia) para visitantes sem sessão, respeitando a visibilidade.
- **FR-043**: O sistema MUST oferecer compartilhamento pela folha nativa do sistema em dispositivos móveis e por links diretos das redes de mensagem.
- **FR-044**: O sistema MUST abrir o post correspondente quando o link é acessado em dispositivo com o aplicativo instalado.

**Backup e recuperação**

- **FR-045**: O sistema MUST fazer backup cifrado na origem, de modo que o destino do backup não precise ser confiável.
- **FR-046**: O sistema MUST suportar destinos de backup plugáveis, com disco local no MVP e destino externo fora do perímetro do provedor.
- **FR-047**: O sistema MUST fazer backup incremental por marcador de sequência e MUST permitir rotação de chave sem reescrever backups anteriores.
- **FR-048**: O sistema MUST restaurar o estado completo a partir do backup e MUST comparar hashes de conteúdo como critério de sucesso.
- **FR-049**: O sistema MUST executar rotina automatizada de restauração que falha o pipeline quando a restauração diverge.
- **FR-050**: O sistema MUST notificar o operador em falha de backup e MUST expor estado de redundância compreensível para não especialista.

**Operação e observabilidade**

- **FR-051**: O sistema MUST expor verificação de vivacidade e de prontidão, incluindo dependências de armazenamento e de blobs.
- **FR-052**: O sistema MUST rastrear ponta a ponta os fluxos de publicação, mídia, entrega federada e backup, sem expor dados pessoais em telemetria.
- **FR-053**: O sistema MUST emitir logs estruturados com remoção de dados pessoais e MUST NOT retornar detalhes internos de erro ao cliente.
- **FR-054**: O sistema MUST expor métricas operacionais mínimas: publicações, entregas pendentes, tamanho da fila de falhas, latência de leitura e estado de backup.
- **FR-055**: O sistema MUST fornecer painel operacional de runtime com atores ativos, filas e falhas por tipo de objeto federado.
- **FR-056**: O sistema MUST documentar os procedimentos de restaurar backup, rotacionar chave, destravar entrega, esvaziar fila de falhas e tratar disco cheio.

### Key Entities *(include if data involved)*

- **Ator (Actor)**: identidade local, remota ou bot (`Service`); handle canônico, perfil, chave pública, contadores de publicação e estado de atividade. É a raiz de referência de todo o resto.
- **Contêiner (Container)**: agregado social único com tipo grupo ou comunidade; nome, slug, preset, política vigente, sequência de eventos, proprietário e sucessor nomeado. Não armazena o feed.
- **Vínculo (Membership)**: relação entre ator e contêiner; papel, estado (pendente, ativo, silenciado, banido), origem da entrada e datas.
- **Política de contêiner (ContainerPolicy)**: dado versionado com quem publica, quem vê, quem entra e quem interage, mais a lista de publicadores nomeados; toda decisão de acesso referencia uma versão.
- **Publicação (Object)**: post ou mensagem (`Event`, `Question` e `Objective` são a feature 003); autor, contêiner, conteúdo, anexos, visibilidade aplicada, versão de política e posição na sequência.
- **Anexo (Attachment)**: mídia ou link associado a uma publicação; tipo, hash de conteúdo, endereço, preview, descrição alternativa e dimensões.
- **Conversa (Thread)**: conjunto de participantes e ordem total de mensagens.
- **Reação (Reaction)**: vínculo entre ator, alvo (publicação ou mensagem) e emoji; uma por ator por emoji, removível, com contagem derivada e visibilidade sempre a do alvo.
- **Atividade (Activity)**: unidade de mudança de domínio; tipo, autor, objeto, sequência e payload federado associado. Fonte de sincronização incremental e de entrega.
- **Par remoto (Peer)**: instância vizinha; domínio, saúde, recuo, fila de entregas e limite de taxa.
- **Envelope de backup (BackupEnvelope)**: metadados do backup cifrado; época de chave, faixa de sequência, hash de conteúdo, tamanho e destino.
- **Decisão de política (PolicyDecision)**: registro de auditoria com ator, ação, recurso, resultado e versão de política.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uma pessoa nova completa cadastro, publicação do primeiro post e download do kit de recuperação em menos de 5 minutos, em uma única sessão.
- **SC-002**: Uma conta cuja instância foi destruída recupera a mesma identidade a partir do kit em menos de 15 minutos, sem intervenção do operador.
- **SC-003**: Publicações de um membro chegam a outro membro já conectado em menos de 2 segundos, no percentil 95.
- **SC-004**: A primeira página do feed de uma comunidade com 10.000 publicações abre em menos de 1 segundo, no percentil 95, e não piora proporcionalmente ao número de membros.
- **SC-005**: Em 100 tentativas aleatórias de acesso a conteúdo restrito por quem não é membro, nenhuma retorna conteúdo ou metadado — 0% de vazamento.
- **SC-006**: Em 100 tentativas de entrada em contêiner por convite sem convite válido, nenhuma obtém sucesso.
- **SC-007**: Uma publicação pública de uma instância aparece em instância vizinha em menos de 30 segundos, e entregas repetidas nunca produzem duplicata.
- **SC-008**: Nenhuma operação local falha por indisponibilidade de instância vizinha; 100% das entregas pendentes ficam registradas e visíveis ao operador.
- **SC-009**: 100% dos ciclos de backup executados em rotina automatizada restauram com hashes idênticos; qualquer divergência interrompe a entrega.
- **SC-010**: O operador identifica uma falha de federação, de backup ou de cota em menos de 5 minutos a partir do painel, sem ler log bruto.
- **SC-011**: 100% das decisões de acesso restritas ficam auditáveis: é possível responder "quem autorizou, sob qual versão de política e quando" para qualquer acesso negado ou concedido.
- **SC-012**: Um visitante sem conta abre o link de um post público e vê preview com imagem em WhatsApp e Telegram, sem instalar nada.
- **SC-013**: Nenhuma publicação de conteúdo restrito gera metadado público de preview, imagem ou contagem em 100% dos casos testados.
- **SC-014**: Uma troca de mensagens entre dois usuários mantém ordem idêntica e estável para ambos em 100% das execuções concorrentes testadas.
- **SC-015**: Nenhum dado pessoal identificável aparece em telemetria ou log estruturado em auditoria amostral de 1.000 registros.
- **SC-016**: 100% das entregas de saída para uma instância Mastodon e uma Friendica reais são aceitas (nenhum `401`/`403` por assinatura) e 100% das atividades assinadas recebidas delas passam na verificação; a suíte de interop roda no fechamento do Sprint 5.
- **SC-017**: Em 100 tentativas de acesso por não participante a respostas e reações de uma conversa, nenhuma retorna conteúdo, contagem ou existência.
- **SC-018**: Nenhum contêiner fica sem Proprietário ativo: em 100% das tentativas de saída, remoção ou exclusão de conta do último Proprietário, a operação é bloqueada até haver transferência.

## Assumptions

- **Instância única e operador humano**: o MVP é operado por uma pessoa ou time pequeno, em 1 VM, sem multi-tenancy, sem SLA contratual e sem federação com SLA.
- **Sem E2EE no MVP, declarado**: mensagens são protegidas em trânsito e em repouso, e o usuário é informado de que o operador pode ler. Não existe promessa parcial de criptografia ponta a ponta.
- **Feed cronológico**: não há ranqueamento, recomendação ou algoritmo de relevância no MVP; qualquer personalização fica para depois da federação.
- **Interop real é critério de aceite, não verificação posterior**: duas instâncias Squid são o mínimo de desenvolvimento, mas o Sprint 5 só é aceito quando uma instância Mastodon e uma Friendica reais aceitam nossas entregas assinadas e suas atividades assinadas passam na verificação.
- **Sharding mútuo e Reed-Solomon estão fora do MVP**, mas os contratos preparados (destino de backup plugável, hash de conteúdo, marcador de sequência, cota em inteiro) existem desde já.
- **Mobile é web responsivo primeiro**; aplicativo nativo (Capacitor) é fase posterior e não bloqueia nenhum critério do MVP.
- **Idioma**: interface e documentos em português do Brasil; o modelo de dados é neutro e aceita conteúdo em qualquer idioma e escrita.
- **Autenticação em degraus**: senha (hash resistente a GPU) + sessão atendem o MVP; **não há e-mail, telefone nem reset por e-mail** — a recuperação é o kit. Passkey entra na fase 2; OIDC só como SSO de instância na fase 3, nunca login social obrigatório (ADR-36).
- **Continuidade de identidade após recuperação**: recuperar o kit restaura a mesma chave, portanto seguidores não precisam re-autenticar a identidade.
- **Moderação de conteúdo é obrigação do operador**: o produto fornece denúncia, remoção e trilha de auditoria; a decisão legal é humana.
- **Descoberta de perfis remotos pode ser aproximada**: cache de atores remotos é aceitável; consistência forte entre instâncias não é requisito do MVP.

## Out of Scope (Non-Goals)

Os itens abaixo são **não-objetivos registrados** — existem para resistir à expansão de escopo, que é o risco dominante deste projeto.

- Integração de postagem automática em X, Instagram, Facebook, WhatsApp ou Tinder; ingestão de conteúdo dessas redes; scraping.
- Bot API pública, loja de bots e automação de primeira classe — feature `002-bots-e-automacao`; o MVP apenas reserva o ator `Service` e a superfície de token/webhook documentada como reservada.
- Eventos, listas de metas, enquetes e outros modos de interação rica em contêineres — feature `003-eventos-metas-enquetes`; o MVP apenas reserva `Object.Kind`.
- Criptografia ponta a ponta e MLS.
- Federação multi-protocolo ativa (apenas a abstração; segundo protocolo é fase 2).
- Mutual Sharding, Reed-Solomon, guardiões SSS reais, escrow com 2FA, threshold KMS/OPRF.
- Algoritmo de recomendação de feed, busca federada avançada, notificações push.
- Multi-tenancy, Kubernetes, microsserviços além do Worker de mídia/backup, fila gerenciada.
- Aplicativo iOS publicado em loja e aplicativo nativo Android no MVP.
- Reputação, gamificação, anúncios, monetização, criptomoeda ou tokens.

## Dependencies

- Domínio público próprio (handle) apontando para a instância, necessário para descoberta federada e para o link canônico dos posts.
- Certificado TLS válido por domínio, requisito para federação e para preview social.
- Destino de armazenamento fora do perímetro do provedor da instância (bucket de terceiro ou disco físico) para a cópia de sobrevivência.
- Acesso a instâncias Mastodon e Friendica reais (contêineres locais ou hospedadas) com domínio e TLS válidos, para o critério de interoperabilidade do Sprint 5.
- Documento mestre `05-arquitetura-consolidada.md` como fonte da verdade: desvio exige atualização do documento antes do código.