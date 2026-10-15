# Instruções de instalação e configuração

Este guia leva você do zero até o LiveHelm-App funcionando. Se quiser entender primeiro o que ele faz e como as peças se
encaixam, comece pelo [README](../README.md).

O programa não fala direto com a Twitch, a Kick nem o YouTube. Quem fala é o **Streamer.bot**, que roda no seu computador; o programa
só pede coisas a ele. Por isso a maior parte da configuração é no Streamer.bot: ligar o servidor WebSocket e criar algumas **ações**
com nomes exatos. O **OBS** entra para as cenas, a saúde da transmissão e o tempo da live.

> **Já usa o LiveHelm (o dock do OBS)?** As ações do Streamer.bot são as mesmas: `Painel: Twitch`, `Painel: Kick`, `Painel: YouTube` e
> `Painel: Chat`. Se você já criou essas ações, pule a [seção 3](#3-criar-as-ações-no-streamerbot).

## Índice

1. [O que você vai precisar](#1-o-que-você-vai-precisar)
2. [Preparar o Streamer.bot](#2-preparar-o-streamerbot)
3. [Criar as ações no Streamer.bot](#3-criar-as-ações-no-streamerbot)
4. [Preparar o OBS](#4-preparar-o-obs)
5. [Baixar e abrir o programa](#5-baixar-e-abrir-o-programa)
6. [Configurar o programa](#6-configurar-o-programa)
7. [Conferir se está tudo certo](#7-conferir-se-está-tudo-certo)
8. [Problemas comuns](#8-problemas-comuns)
9. [Atualizar, mudar de computador e desinstalar](#9-atualizar-mudar-de-computador-e-desinstalar)

---

## 1. O que você vai precisar

| O quê | Para quê | Observação |
|---|---|---|
| **Windows 10 ou 11** | O programa roda nele | Usa o .NET Framework 4.8, que já vem no Windows |
| **[Streamer.bot](https://streamer.bot)** | Fala com Twitch, Kick e YouTube | Testado na versão 1.0.7 |
| **OBS Studio 28 ou mais novo** | Cenas, saúde, tempo da live | Testado no 32.2.2. Do 28 em diante o servidor WebSocket já vem dentro do OBS. Sem o OBS, o chat, o título e as enquetes funcionam |
| Contas das plataformas | O que você quer controlar | Use a conta do canal (broadcaster). Não precisa ter as três |

Nada disso é pago. O programa não precisa de chave de API nem de cadastro: ele só conversa com programas do seu próprio computador (e
baixa imagens de emotes, veja a [Privacidade](../README.md#privacidade-e-segurança)).

## 2. Preparar o Streamer.bot

### 2.1. Baixar e abrir

Baixe o Streamer.bot no [site oficial](https://streamer.bot), extraia numa pasta e abra o `Streamer.bot.exe`. Os guias oficiais
(<https://docs.streamer.bot>) cobrem a instalação em detalhes; o essencial para o LiveHelm-App está nos passos abaixo.

### 2.2. Entrar com a conta de cada plataforma

No Streamer.bot, vá em **Platforms** e entre com a conta do canal em cada plataforma que você usa:

- **Twitch:** `Platforms > Twitch > Accounts`, em **Broadcaster Account** aperte **Login** e aceite as permissões.
- **YouTube:** `Platforms > YouTube > Accounts`, em **Broadcaster Account** aperte **Sign in with Google**, aceite as permissões e
  ligue **Auto Connect**.
- **Kick:** `Platforms > Kick`, entre com a conta do canal. Depois ligue também a conta da Kick no site do Streamer.bot
  (**Account > Connections**), pois as mensagens do chat e os alertas da Kick dependem desse login do site, além do login no
  aplicativo. **Depois de conectar com o site, reinicie o Streamer.bot** (feche e abra de novo); sem isso ele não passa a receber a Kick.

A conta **Bot** é opcional. Ela só serve se você quiser que as mensagens enviadas pelo programa saiam com o nome do bot (veja
`USAR_BOT` na [seção 3](#painel-chat-chat-moderação-enquetes-e-previsões)).

### 2.3. Ligar o servidor WebSocket

É por aqui que o programa conversa com o Streamer.bot.

1. Vá em **Servers/Clients > WebSocket Server**.
2. Deixe **Address** em `127.0.0.1` e **Port** em `8080` (os valores padrão).
3. Ligue **Auto Start**: com ele, o servidor sobe sozinho sempre que o Streamer.bot abre. Se a tela tiver um botão para iniciar o
   servidor, clique nele agora (ou reinicie o Streamer.bot).
4. **Senha (recomendada):** ligue **Authentication** e preencha **Password**. Sem senha, qualquer programa do seu computador (e
   páginas abertas no navegador podem tentar) que consiga conectar no servidor pode pedir as ações do LiveHelm, como banir alguém ou
   enviar mensagem. Depois você coloca a mesma senha nas configurações do programa. Se ligar **Enforce**, todos os pedidos exigem a
   senha (o programa se autentica sozinho quando a senha está preenchida nas configurações).

> **Cuidado com o endereço:** `127.0.0.1` só aceita conexões do próprio computador, que é o que você quer. Não troque por
> `0.0.0.0` a menos que saiba o que está fazendo, porque isso abre o servidor para sua rede (use senha nesse caso).

## 3. Criar as ações no Streamer.bot

O programa pede ao Streamer.bot que execute **ações**. Cada ação é criada **uma única vez**. Sem as ações, o chat funciona só para **ler**.

> **O nome da ação precisa ser EXATAMENTE igual ao das Instruções.** O programa compara letra por letra: maiúsculas e minúsculas, o
> dois-pontos, o espaço depois dele e qualquer espaço sobrando no fim contam. `Painel: YouTube` funciona; `Painel: Youtube`,
> `Painel:YouTube`, `painel: youtube` e `Painel: YouTube ` (com um espaço no fim) **não** funcionam. O jeito mais seguro é **copiar o nome
> daqui e colar** no campo **Name**.

### Receita: criar uma ação

1. No Streamer.bot, vá em **Actions & Queues > Actions**.
2. Clique com o botão direito na área de ações e escolha **Add**.
3. No campo **Name**, cole o nome da ação **exatamente** como está nestas Instruções (por exemplo `Painel: Twitch`) e confirme.
4. Clique na ação nova para selecioná-la. O painel **Sub-Actions** (à direita) fica ativo.
5. Clique com o botão direito no painel de sub-ações e escolha a sub-ação indicada em cada receita abaixo. Para configurar uma
   sub-ação, dê dois cliques nela.

### Receita: ação com código C#

A ação `Painel: Chat` usa um código pronto, que o próprio programa mostra e copia.

1. Crie a ação com a receita acima (passos 1 a 4).
2. No painel de sub-ações, botão direito, **Core > C# > Execute C# Code**.
3. No programa, clique no **`?`** (canto de cima, à direita) e em **Copiar o código**. (O mesmo código está no repositório do
   [LiveHelm](https://github.com/GustavoBoschi/livehelm/blob/main/acoes-streamerbot/Painel-Chat.cs).)
4. Na janela do código do Streamer.bot, **apague tudo** que vier e cole.
5. Clique em **Save and Compile**. Tem que compilar sem erro. Depois **OK** para fechar.

Se o código mudar numa versão nova do programa, abra a sub-ação e cole o código de novo (o passo 4 vale para atualizar).

---

### `Painel: Twitch`: título e categoria na Twitch

Sub-ações (nessa ordem), com estes valores:

| Sub-ação | Campo | Valor |
|---|---|---|
| `Twitch > Channel > Set Channel Title` | título | `%tituloLive%` |
| `Twitch > Channel > Set Channel Game` | jogo/categoria | `%categoriaLive%` |

`%tituloLive%` e `%categoriaLive%` são os **argumentos** que o programa manda. Escreva-os exatamente assim, com os dois `%`.

### `Painel: Kick`: título e categoria na Kick

| Sub-ação | Campo | Valor |
|---|---|---|
| `Kick > Channel > Set Channel Title` | título | `%tituloLive%` |
| `Kick > Channel > Set Channel Category` | categoria | `%categoriaLive%` |

### `Painel: YouTube`: título no YouTube

| Sub-ação | Campo | Valor |
|---|---|---|
| `YouTube > Set Title` | título | `%tituloLive%` |
| `YouTube > Set Title` | **Broadcast** | **`All`** |

O YouTube não muda de categoria por aqui; só o título.

> **Atenção ao campo Broadcast:** ele precisa ficar em **All**. Ao criar a sub-ação, a janela costuma vir em **From Variable**: o Streamer.bot
> então lê a variável `broadcast.id`, que só existe quando a ação é disparada por um evento do YouTube. Como quem dispara a ação é o programa,
> essa variável não existe e **o título não muda** (sem mostrar erro). Além disso, o YouTube precisa estar conectado no Streamer.bot
> ([seção 2.2](#22-entrar-com-a-conta-de-cada-plataforma)): se a janela da sub-ação mostrar **Not Connected**, entre em
> `Platforms > YouTube > Accounts`. O Streamer.bot também avisa que mudar as informações do YouTube é uma chamada "cara" (gasta cota da
> API do YouTube), então mude o título com moderação.

> **Dica:** você não precisa criar as três. Crie só as das plataformas que usa: o botão **Confirmar** do painel Live atualiza só as
> plataformas ligadas no alto. O programa mostra o aviso "Faltam ações: …" enquanto alguma das três não existir; para quem não usa
> aquela plataforma, é só um lembrete e pode ser ignorado.

### `Painel: Chat`: chat, moderação, enquetes e previsões

Receita de **ação com código C#**. Uma ação só faz tudo: enviar mensagem, apagar, silenciar, banir, desfazer, limpar o chat e
criar/controlar enquetes e previsões. O argumento `acao` diz o que fazer:

| `acao` | O que faz | Argumentos extras |
|---|---|---|
| `enviar` | Envia uma mensagem no chat | `plataforma`, `mensagem` |
| `apagar` | Apaga uma mensagem (não existe no YouTube) | `plataforma`, `mensagemId` |
| `silenciar` | Silencia um usuário | `plataforma`, `usuario`, `usuarioId`, `segundos` |
| `banir` | Bane um usuário | `plataforma`, `usuario`, `usuarioId` |
| `reverter` | Desfaz silêncio ou banimento (Twitch e Kick) | `plataforma`, `usuario` |
| `limpar` | Limpa o chat (só Twitch) | `plataforma` |
| `enquete` / `previsao` | Cria uma enquete ou previsão (só Twitch) | `titulo`, `opcoes` (separadas por `\|`), `duracao` |
| `encerrar_enquete` | Encerra a enquete no ar | `id` |
| `travar_previsao` / `cancelar_previsao` | Trava as apostas / cancela | `id` |
| `resolver_previsao` | Diz qual opção venceu | `id`, `vencedorId` |

No alto do código há a constante **`USAR_BOT`**. Com `false` (o padrão), as mensagens e punições saem **com a sua conta**; com
`true`, saem com a conta do bot, que precisa ser **moderador** do canal.

Se você criou a `Painel: Chat` numa versão antiga, o **desbanir** e o **liberar** da lista de punições só funcionam com o código
atual: abra o `?`, copie o código de novo e cole na sub-ação (**Save and Compile**). Sem o código novo o pedido chega e nada acontece.

#### Opcional, sem código

Enviar, silenciar e banir também existem como sub-ações prontas em cada plataforma. Se preferir, crie uma ação por combinação, com a
sub-ação nativa e o nome **exatamente** igual, por exemplo:

- `Painel: Enviar Twitch`: `Twitch > Chat > Send Message` com `%mensagem%`.
- `Painel: Silenciar Kick`: o *timeout* da Kick com `%usuario%` e `%segundos%`.
- `Painel: Banir YouTube`: o banimento do YouTube com `%usuario%`.

O programa usa a ação da plataforma quando ela existe e a `Painel: Chat` quando não existe. Apagar mensagem, enquetes e previsões só
funcionam pela `Painel: Chat`.

## 4. Preparar o OBS

Esta parte é necessária para as **cenas**, a **saúde da transmissão**, o tempo ao vivo e os botões de iniciar e encerrar a live.
Sem ela, o resto funciona.

1. No OBS, abra o menu **Ferramentas** (em inglês, **Tools**) e escolha **Configurações do servidor WebSocket** (**WebSocket
   Server Settings**). Se não aparecer, confirme que o OBS é o 28 ou mais novo.
2. Marque **Ativar servidor WebSocket** (**Enable WebSocket server**).
3. Deixe a porta em `4455` (padrão).
4. Se quiser, ative a autenticação e anote a senha (o botão para mostrá-la ajuda). Você vai colocar essa senha nas configurações do
   programa. Sem autenticação, deixe a senha em branco.

O Streamer.bot e o OBS são independentes: você **não** precisa ligar o OBS dentro do Streamer.bot para o programa funcionar.

## 5. Baixar e abrir o programa

1. Na página de *Releases* do projeto, baixe o `.zip` do programa e extraia numa pasta de sua escolha (por exemplo,
   `Documentos\LiveHelm-App`). Mantenha os três arquivos juntos: o `.exe`, o `.exe.config` e a `Newtonsoft.Json.dll`.
2. Dê dois cliques em **`LiveHelm-App.exe`**. Crie um atalho na área de trabalho se quiser.
3. Se o Windows mostrar "O Windows protegeu seu computador", clique em **Mais informações** e depois em **Executar assim mesmo**: o
   programa ainda não tem assinatura digital.

Prefere compilar? Instale o .NET SDK, baixe o código e dê dois cliques em `Publicar.bat`: ele monta a pasta `LiveHelm-App` com o
programa pronto e um atalho (veja o [README](../README.md#desenvolvimento)).

O programa lembra o tamanho e a posição da janela e só abre uma de cada vez: abrir de novo traz a que já existe, mesmo escondida na
bandeja do sistema.

## 6. Configurar o programa

Clique na **engrenagem** (canto de cima, à direita) para abrir as configurações:

| O quê | O que configurar |
|---|---|
| **Streamer.bot** | **Endereço** (`ws://127.0.0.1:8080`) e **Senha** (a do passo 2.3, ou vazia) |
| **OBS** | **Endereço** (`ws://127.0.0.1:4455`) e **Senha** (a do passo 4, ou vazia). As mudanças reconectam ao fechar a gaveta |
| **Botões da transmissão** | "Botões de iniciar e encerrar a live e a gravação": vêm **desligados** de propósito; ligue só se quiser |
| **Aparência** | Cor de destaque (7 cores ou qualquer `#RRGGBB`) e fundo |
| **Tamanho da janela** | "Padrão" ou "Ao lado do OBS" (uma faixa estreita colada à direita do OBS, que liga "Manter acima das outras janelas") |
| **Segundo plano** | "Fechar (X) esconde na bandeja" e "Abrir já escondido" |

Se você deixou tudo no padrão e sem senha, normalmente não precisa mudar nada além de abrir o Streamer.bot e o OBS. As suas escolhas
ficam salvas no seu perfil do Windows (`%APPDATA%\LiveHelm-App\config.json`).

### Streamer.bot em outro computador

Abra o programa por um atalho com `--sb ws://IP-DO-OUTRO-PC:8080` (e `--sbsenha SENHA`, se houver); os dois valem no lugar do que está
nas configurações. No Streamer.bot, o servidor precisa estar escutando na rede (`Address` diferente de `127.0.0.1`), e é
**fortemente recomendado** usar senha.

## 7. Conferir se está tudo certo

1. **A bolinha de conexões está verde.** Verde = tudo conectado (Streamer.bot, as três plataformas e o OBS). Amarela = falta alguma
   dessas conexões (passe o mouse para ver qual). Vermelha = sem conexão com o Streamer.bot.
2. **O chat recebe mensagens.** Escreva algo no chat de uma das plataformas: a mensagem aparece no painel Chat.
3. **Live:** escreva um título, clique em **Confirmar** e confira o resultado por plataforma (verde = aplicado; amarelo = ficou
   diferente do digitado ou sem confirmação).
4. **Cenas e saúde:** ligue a chave **Cenas** (e **Saúde**) no alto. As cenas do OBS aparecem em botões, com a atual destacada, e o
   painel Saúde mostra CPU, FPS e quadros perdidos.
5. **Momento:** ligue a chave **Momento**, escreva uma anotação e clique em **Marcar**. Uma linha nova entra no arquivo de hoje
   (botão **Abrir**).
6. **Chat e moderação:** passe o mouse sobre uma mensagem para ver os botões; envie uma mensagem de teste pela caixa de baixo.

## 8. Problemas comuns

| Sintoma | Causa provável e solução |
|---|---|
| Bolinha **vermelha** | O Streamer.bot está fechado ou o **WebSocket Server** está desligado. Abra-o e ligue (seção 2.3). Confira também o endereço e a porta nas configurações. |
| O Streamer.bot recusa a conexão | A senha está errada ou faltando. Coloque a mesma senha do **Password** do Streamer.bot nas configurações. O painel mostra o motivo da recusa. |
| Bolinha **amarela** | Falta alguma conexão: uma das plataformas (entre com a conta dela, seção 2.2) ou o OBS (seção 4). Passe o mouse na bolinha para ver qual. A cor conta as três plataformas e o OBS, então, se você não usa alguma delas, a bolinha continua amarela e dá para ignorar. |
| Aparece "Faltam ações: …" | O Streamer.bot não tem uma ação com o nome **EXATAMENTE igual** ao da plataforma (`Painel: Twitch`, `Painel: Kick` ou `Painel: YouTube`): o programa compara letra por letra, então `Painel: Youtube`, `Painel:YouTube` ou um espaço no fim não valem. Copie o nome das Instruções, confirme com **Ok** a janela da sub-ação e espere até 15 s (o programa relê a lista a cada 15 s). O botão do aviso abre a ajuda de como criar. Se você não usa aquela plataforma, o aviso é só um lembrete. |
| O título do YouTube não muda (e não aparece erro) | Duas causas comuns: o **Broadcast** da sub-ação `YouTube > Set Title` está em **From Variable** (troque para **All**), ou o YouTube não está conectado no Streamer.bot (a janela da sub-ação mostra **Not Connected** e o YouTube aparece desconectado na bolinha de conexões; entre em `Platforms > YouTube > Accounts`, seção 2.2). |
| Cenas vazias, Saúde sem dados ou "Desconectado" no OBS | O servidor WebSocket do OBS está desligado, o endereço está errado ou a senha está faltando (seção 4). |
| Mensagens ou alertas da Kick não chegam | Ligue a conta da Kick também no site do Streamer.bot (**Account > Connections**), além do login no aplicativo, e **reinicie o Streamer.bot** depois de conectar com o site. |
| Banir, silenciar ou enviar não faz nada | A ação `Painel: Chat` não existe, não compilou ou está sem o código. Cole o código do `?` de novo e use **Save and Compile**. |
| Desbanir ou liberar não faz nada | A `Painel: Chat` é de uma versão antiga e não tem o verbo `reverter`. Cole o código do `?` de novo. |
| Os emotes de terceiros não aparecem | Eles precisam de internet e estar ligados nas configurações. Os emotes da Twitch e da Kick vêm junto das mensagens. |
| O programa sumiu | Ele pode estar na bandeja do sistema (perto do relógio). Clique no ícone, use `Ctrl+Alt+P` ou abra o programa de novo. |

Quando algo não bate, o **log do Streamer.bot** (pasta `logs`, ao lado do executável) é o melhor lugar para olhar: toda ação que o
programa pede aparece lá, com os argumentos recebidos e os erros. Erros do próprio programa vão para
`%APPDATA%\LiveHelm-App\erros.log`.

## 9. Atualizar, mudar de computador e desinstalar

- **Atualizar:** feche o programa (inclusive o ícone da bandeja), baixe o `.zip` novo e substitua os arquivos da pasta. As suas
  configurações ficam em outro lugar (`%APPDATA%\LiveHelm-App`) e continuam valendo. Se a nova versão trouxe mudanças no código da
  `Painel: Chat`, cole o código de novo na ação (seção 3).
- **Mudar de computador:** instale o Streamer.bot e o OBS, extraia o programa e crie as ações de novo. As configurações ficam no perfil
  do Windows daquele computador; copie o `config.json` se quiser levá-las.
- **Desinstalar:** feche o programa e apague a pasta dele. Para tirar também o que ele guardou, apague `%APPDATA%\LiveHelm-App` e,
  se quiser, os arquivos de momentos em `Documentos\LiveHelm-App`. As ações do Streamer.bot continuam lá; apague-as à mão se quiser.
