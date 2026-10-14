# LiveHelm-App: regras para quem mexe no código

Programa do Windows em C# (WPF, .NET Framework 4.8) que gerencia a live: título e categoria, cenas e saúde do OBS, momentos,
enquetes e previsões e chat com moderação de Twitch, Kick e YouTube. É a versão desktop do LiveHelm (o dock do OBS, repositório
`livehelm`): as duas usam as mesmas ações do Streamer.bot. Uso e instalação estão no [README.md](README.md) e em
[docs/INSTRUCOES.md](docs/INSTRUCOES.md); este arquivo guarda só o que é preciso saber para expandir o código sem quebrar nada. O
projeto está em estado inicial de desenvolvimento.

## Comandos

- `dotnet build LiveHelmApp.csproj -c Release` compila. `Publicar.bat` (`publicar.ps1`) compila e monta a pasta `LiveHelm-App/`
  (o `.exe`, o `.exe.config` e a `Newtonsoft.Json.dll`) e o atalho ao lado; feche o programa antes. Não copie nada de `bin` à mão.
- Não há testes automatizados ainda. A conferência é visual: rode o programa contra um Streamer.bot e um OBS falsos (servidores
  WebSocket mínimos) e use `--captura arquivo.png`. Lógica pura nova entra em classes sem WPF, para poder ganhar testes.
- Opções de linha de comando (usadas nos testes): `--sb ws://...` e `--obs ws://...` trocam os endereços, `--sbsenha` define a senha
  do Streamer.bot, `--config arquivo.json` troca o arquivo de configuração (e libera a trava de instância única, a menos que
  `--instancia nome` a isole), `--momentos pasta` troca a pasta dos arquivos de momentos e `--captura arquivo.png` salva uma imagem
  da janela e fecha, sem gravar a configuração.

## Regras

- **Nada sensível no repositório:** `StreamerbotSenhaPadrao` e `ObsSenhaPadrao` (`Config.cs`) ficam vazios. As senhas são digitadas na
  gaveta de configurações e salvas em `%APPDATA%\LiveHelm-App\config.json`. Sem caminhos, contas ou endereços pessoais em código,
  comentários ou documentação. A única identificação pessoal permitida é o crédito de autoria (`LICENSE` e seção "Licença e
  créditos" do README), inclusive a coautoria do Claude: não os remova.
- **Tema dinâmico:** os XAMLs usam `{DynamicResource}` (não `StaticResource`) para os pincéis de acento e de fundo (`Acento`,
  `AcentoClaro`, `AcentoTexto`, `AcentoFundo`, `AcentoBorda`, `Fundo`, `Cartao`, `Campo`, `Elevado`, `ElevadoSobre`, `FundoChat`).
  `Tema.Aplicar` troca esses pincéis em `Application.Resources`; pincéis usados por estilos ficam congelados, então mutar a cor não
  funciona. Pincel novo de cor ou fundo precisa de `DynamicResource` para acompanhar o tema.
- **Erro inesperado numa tela não derruba o painel:** `App.RegistrarErro` grava em `%APPDATA%\LiveHelm-App\erros.log` e segue.
- **Ações de efeito grande ficam desligadas por padrão e pedem confirmação:** iniciar e encerrar live e gravação só existem com
  `ControlesDeTransmissao`, e encerrar a live pede confirmação; banir pede confirmação. Painéis novos de controle vêm desligados.

## Contrato com o Streamer.bot

Tudo passa por ações do Streamer.bot via `DoAction`: o `SendMessage` do WebSocket exige senha no servidor, e não existem pedidos de
moderação nem de busca de categoria. **Nomes de ação e de argumento têm que ser idênticos nos dois lados**, e iguais aos do LiveHelm.

| Ação | Argumentos |
|---|---|
| `Painel: Twitch`, `Painel: Kick`, `Painel: YouTube` | `tituloLive`, `categoriaLive` |
| `Painel: Chat` (ou o nome antigo `Painel: Enviar mensagem`) | `acao` (`enviar`, `apagar`, `silenciar`, `banir`, `reverter`, `limpar`, `enquete`, `previsao`, `encerrar_enquete`, `travar_previsao`, `cancelar_previsao`, `resolver_previsao`), `plataforma`, `mensagem`, `mensagemId`, `usuario`, `usuarioId`, `segundos`, `titulo`, `opcoes` (separadas por `\|`), `duracao`, `id`, `vencedorId` |

- O código C# da `Painel: Chat` está em `AjudaWindow.xaml.cs` (`CodigoDaAcao`) e é o mesmo de `acoes-streamerbot/Painel-Chat.cs` do
  LiveHelm: mudou um, mude o outro. Os métodos de `CPH` foram conferidos na `Streamer.bot.Plugin.Interface.dll` do 1.0.7 (não há
  sub-ação de apagar mensagem em nenhuma plataforma, e o YouTube não tem método de apagar); ao mudar, recompile contra ela.
- Enviar, silenciar e banir também aceitam ações próprias por plataforma (`Painel: Enviar Twitch`, `Painel: Silenciar Kick`...), que
  têm prioridade sobre a `Painel: Chat`.
- O resultado do título e da categoria é confirmado pelos eventos `Twitch.StreamUpdate` e `Kick.ChannelUpdate`.
- Os ids de enquete e previsão vêm dos eventos `Twitch.PollCreated` e `Twitch.PredictionCreated` (campos `id` e
  `choices`/`outcomes[].id`, lidos de forma defensiva: o formato não está documentado, confirme com um evento real).
- `Alertas.cs` espelha o mapa `ALERTAS` do dock LiveHelm (`src/streamerbot.js`): mudou um, confira o outro.

## Estrutura e decisões

- `MainWindow.xaml(.cs)` é a tela: painéis Live, Cenas, Saúde, Momento, Enquetes e previsões e Chat, recolhíveis; o botão de foco
  recolhe os outros. Bandeja do sistema com `NotifyIcon`, atalho global Ctrl+Alt+P e instância única por `EventWaitHandle`.
  `MainWindow.Saude.cs`, `MainWindow.Momento.cs` e `MainWindow.ChatExtras.cs` guardam os painéis e extras.
- `App.xaml` tem o tema escuro e os estilos (ícones das plataformas em `IconeTwitch|Kick|YouTube`). `Chat.cs` converte os eventos
  do Streamer.bot em `EventoSb` (chat, alertas, apagar, banir, `StreamUpdate`/`ChannelUpdate`) e `ChatTexto` monta cada linha do chat
  (hora, ícone, insígnias, nome e emotes) num TextBlock só.
- `StreamerBotClient.cs` e `ObsClient.cs` falam com os servidores WebSocket por cima de `ConexaoWs.cs` (reconexão automática).
  Senha errada faz o Streamer.bot recusar a inscrição (`Subscribe`) e o painel mostra o motivo (`StreamerBotClient.Recusada`).
- `Config.cs` guarda o estado em `%APPDATA%\LiveHelm-App\config.json`. Endereço e senha do Streamer.bot e do OBS são configuráveis na
  gaveta (`--sb` e `--sbsenha` valem no lugar). `AjudaWindow` explica as ações e copia o código da `Painel: Chat`.
- **Saúde:** consulta o OBS só enquanto ligada e à vista (`CarregarSaudeAsync` ao ligar ou conectar; `_timerSaude` de 2 s para
  `GetStats` e `GetStreamStatus`; as assinaturas de eventos do OBS são só Scenes e Outputs). O estado da gravação
  (`GravacaoMonitorada`) é lido se Saúde ou Momento estiver ligado. Áudio e fontes da cena ficam no OBS, não no painel.
- **Momento:** grava em `Documentos\LiveHelm-App\Momentos AAAA-MM-DD.txt` (`Momentos`, em `ItensExtras.cs`) com o tempo da live ou
  da gravação; fora do ar grava só o relógio.
- **Extras do chat:** busca e filtro por usuário entram em `PassaNoFiltro` via `PassaNaBusca`; a aba `perguntas` esconde a lista
  (`PainelPerguntas`) e a fila persiste em `Config.Perguntas`; as punições da sessão (`_punicoes`) vêm do aviso da plataforma
  (`Tratar`, `TipoEv.ApagarUsuario`) e das ações do painel (`ModerarAsync`, `Enviar_Click`), e `RegistrarPunicao` junta os dois. Desfazer
  usa o verbo `reverter`, que a ação responde ok mesmo com código antigo, então o item só ganha a hora do pedido.
- **Emotes de terceiros:** `EmotesDeTerceiros.cs` baixa os conjuntos globais e os do canal (7TV, BetterTTV, FrankerFaceZ; ids vêm do
  `GetBroadcaster`) e `ChatTexto.AdicionarTexto` troca palavras iguais ao nome de um emote pela imagem. O 7TV só entra como PNG
  (estático) ou GIF (animado), porque o WPF não abre WebP. Dá para desligar nas configurações.
- **Layout responsivo:** abaixo de `LarguraEstreita` (760) `AtualizarLargura` empilha os formulários (reposiciona por
  `Grid.SetRow/Column`) e zera `LarguraRapidas`, a coluna dos botões rápidos do chat. As abas do chat acendem pelo `BorderBrush` e
  `Background` do próprio `RadioButton` (estilo `AbaDoChat`).
- O ícone (`icone.ico` e `icone.png`) é um d20 dourado de frente. `publicar.ps1` copia o `.ico` para a pasta do programa com o hash no
  nome: o Windows guarda ícones por caminho, então trocar o arquivo sem trocar o nome mantém o ícone velho no atalho.

## Documentação

- Mudou comportamento, ação ou argumento? Atualize o README.md e as Instruções na mesma mudança.
- Passo a passo para o usuário vai em `docs/INSTRUCOES.md`; o README descreve o que o projeto faz e como estender.

## Política de comentários

O comentário descreve o código **como ele está agora**: um porquê que o código não mostra (restrição, invariante, ordem,
workaround, unidade de constante, contrato de função). Sem histórico ("antes era", "agora", "removido"), sem código comentado, sem
estado do momento, sem referência por número de linha, sem nome visível de janela ou rótulo (cite o `x:Name`), sem emojis nem
banners. Português com acentuação, presente do indicativo, uma a quatro linhas, acima do código. Mudou o código? Atualize ou apague o
comentário na mesma edição.
