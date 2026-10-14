using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    public sealed class ItemCena
    {
        public string Nome { get; set; }
        public bool Atual { get; set; }
    }

    // Linha da lista de conexões (barra e popup): Cor vem como "#RRGGBB".
    public sealed class ItemConexao
    {
        public string Nome { get; set; }
        public string Detalhe { get; set; }
        public string Cor { get; set; }
    }

    // Comando da caixa do chat; Plataformas são as que o aceitam (nas outras ele nem é enviado).
    public sealed class ComandoInfo
    {
        public string Texto { get; set; }
        public string Descricao { get; set; }
        public string Insere { get; set; }
        public string Verbo { get; set; }
        public string[] Plataformas { get; set; }
    }

    // Enquete ou previsão da Twitch no ar, lida do evento do Streamer.bot.
    sealed class InteracaoAtiva
    {
        public string Id = "";
        public string Titulo = "";
        public bool Travada;
        public List<KeyValuePair<string, string>> Opcoes = new List<KeyValuePair<string, string>>(); // id, nome
    }

    // Tela única: Live (título, categoria e onde atualizar), Cenas do OBS e Chat (mensagens e alertas juntos). Cada painel
    // recolhe até o título. Os clientes de rede chamam de threads do pool; tudo que mexe na tela passa por Dispatcher.
    public partial class MainWindow : Window
    {
        sealed class PlataformaDoPainel
        {
            public string Id, Nome, Acao;
            public bool SoTitulo;        // a plataforma não muda de categoria pela API
            public bool ComConfirmacao;  // o Streamer.bot avisa o que foi aplicado (Twitch.StreamUpdate, Kick.ChannelUpdate)
            public CheckBox Marca;       // onde atualizar título e categoria
            public CheckBox MarcaChat;   // onde enviar a mensagem do chat
        }

        // Resultado do envio a uma plataforma: 0 = ok, 1 = aviso, 2 = erro.
        sealed class ResultadoEnvio
        {
            public PlataformaDoPainel Plataforma;
            public int Nivel;
            public string Texto;
        }

        const int MaxChat = 400;
        const int AtualizacaoStatusSbMs = 15000;
        const int AtualizacaoObsMs = 5000;
        const int ConfirmacaoMs = 7000;    // quanto esperar o evento que confirma o título e a categoria
        const double LarguraEstreita = 760;  // abaixo disso a janela usa o layout empilhado (cabe ao lado do preview do OBS)
        const double ZoomEmFoco = 1.4;     // escala do painel Live ou Cenas quando é o único aberto
        const int FechaPopupMs = 300;
        const int StatusChatMs = 9000;
        const int AguardoDaCapturaMs = 3000; // dá tempo dos servidores de teste responderem antes de tirar a imagem

        // A ação do Streamer.bot que envia, apaga, silencia e bane (o código fica em AjudaWindow); o primeiro nome que existir vale.
        static readonly string[] AcoesDoChat = { "Painel: Chat", "Painel: Enviar mensagem" };

        static readonly Brush CorOk = Pincel("#4ADE80");
        static readonly Brush CorAviso = Pincel("#FFC94A");
        static readonly Brush CorErro = Pincel("#FF7B7B");
        static readonly Brush CorSuave = Pincel("#8E8BA2");

        static Brush Pincel(string cor)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(cor);
            b.Freeze();
            return b;
        }

        readonly Config _cfg;
        readonly string _caminhoConfig;
        readonly bool _emCaptura = App.ArquivoDeCaptura != null;
        readonly List<PlataformaDoPainel> _plataformas;
        readonly ObservableCollection<ItemChat> _chat = new ObservableCollection<ItemChat>();
        readonly Dictionary<string, ItemChat> _porId = new Dictionary<string, ItemChat>();
        readonly HashSet<string> _primeiras = new HashSet<string>(); // ids marcados como primeira mensagem antes de a mensagem chegar
        readonly ICollectionView _vista;
        readonly ObservableCollection<ItemCena> _cenas = new ObservableCollection<ItemCena>();
        readonly ObservableCollection<string> _atalhos;
        readonly ConcurrentDictionary<string, TaskCompletionSource<EventoSb>> _confirmacoes = new ConcurrentDictionary<string, TaskCompletionSource<EventoSb>>();
        readonly DispatcherTimer _timerSb = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AtualizacaoStatusSbMs) };
        readonly DispatcherTimer _timerObs = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AtualizacaoObsMs) };
        readonly DispatcherTimer _relogio = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _fechaPopup = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FechaPopupMs) };
        readonly DispatcherTimer _limpaStatusChat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StatusChatMs) };

        static readonly ComandoInfo[] Comandos =
        {
            new ComandoInfo { Texto = "/ban nome", Descricao = "Bane na Twitch, Kick e YouTube", Insere = "/ban ", Verbo = "banir", Plataformas = new[] { "twitch", "kick", "youtube" } },
            new ComandoInfo { Texto = "/timeout nome segundos", Descricao = "Silencia (10 min se não disser o tempo)", Insere = "/timeout ", Verbo = "silenciar", Plataformas = new[] { "twitch", "kick", "youtube" } },
            new ComandoInfo { Texto = "/limpar", Descricao = "Limpa o chat (só na Twitch)", Insere = "/limpar", Verbo = "limpar", Plataformas = new[] { "twitch" } },
        };

        readonly ObservableCollection<Interacao> _interacoes;
        System.Windows.Forms.NotifyIcon _bandeja;
        bool _saindo;
        bool _estreito;
        bool _iniciando = true;  // enquanto o construtor mexe nas caixas, os eventos delas não fazem nada
        string _obsUrlEmUso;
        string _obsSenhaEmUso;
        string _sbUrlEmUso;
        string _sbSenhaEmUso;
        string _erroSb; // por que o Streamer.bot recusou a conexão (senha errada...), ou null
        InteracaoAtiva _enquete;
        InteracaoAtiva _previsao;
        StreamerBotClient _sb;
        ObsClient _obs;
        AjudaWindow _ajuda;
        ScrollViewer _rolagem;
        bool _sbAberto;
        bool _obsAberto;
        bool _enviando;
        bool _rolarAuto = true;
        bool _semSugestoes;      // true enquanto o código mexe no texto da categoria
        int _novas;
        string _filtro = "tudo";
        List<string> _conectadas = new List<string>();
        JArray _acoes; // null = ainda não carregou
        string _cenaAtual = "";
        bool _aoVivo;
        DateTime _inicioDaLive;

        public MainWindow()
        {
            InitializeComponent();
            _caminhoConfig = App.ArquivoDeConfig ?? Config.CaminhoPadrao();
            _cfg = Config.Carregar(_caminhoConfig);
            if (App.EnderecoObsForcado != null) _cfg.ObsUrl = App.EnderecoObsForcado;

            _plataformas = new List<PlataformaDoPainel>
            {
                new PlataformaDoPainel { Id = "twitch", Nome = "Twitch", Acao = "Painel: Twitch", ComConfirmacao = true, Marca = ChkTwitch, MarcaChat = ChkTwitch },
                new PlataformaDoPainel { Id = "kick", Nome = "Kick", Acao = "Painel: Kick", ComConfirmacao = true, Marca = ChkKick, MarcaChat = ChkKick },
                new PlataformaDoPainel { Id = "youtube", Nome = "YouTube", Acao = "Painel: YouTube", SoTitulo = true, Marca = ChkYouTube, MarcaChat = ChkYouTube },
            };

            TxtTitulo.Text = _cfg.Titulo;
            TxtCategoria.Text = _cfg.Categoria;
            ChkTwitch.IsChecked = _cfg.Twitch;
            ChkKick.IsChecked = _cfg.Kick;
            ChkYouTube.IsChecked = _cfg.YouTube;
            TxtSbUrl.Text = _cfg.StreamerbotUrl;
            PwdSbSenha.Password = _cfg.StreamerbotSenha ?? "";
            TxtObsUrl.Text = _cfg.ObsUrl;
            PwdObsSenha.Password = _cfg.ObsSenha ?? "";

            Tema.Aplicar(_cfg.Acento, _cfg.FundoTema);
            ListaAcentos.ItemsSource = Tema.Acentos;
            ListaFundos.ItemsSource = Tema.NomesDeFundo.ToList();
            TxtCorHex.Text = _cfg.Acento;
            OpMinimizar.IsChecked = _cfg.MinimizarParaBandeja;
            OpFechar.IsChecked = _cfg.FecharParaBandeja;
            OpEscondido.IsChecked = _cfg.IniciarEscondido;
            OpTopo.IsChecked = _cfg.SempreNoTopo;
            OpEmotes.IsChecked = _cfg.EmotesDeTerceiros;
            EmotesDeTerceiros.Ativo = _cfg.EmotesDeTerceiros;
            EmotesDeTerceiros.Carregado += () => UI(() => { foreach (var linha in _chat.Where(l => !l.EhAlerta)) linha.Redesenhar(); });
            Topmost = _cfg.SempreNoTopo;
            SizeChanged += (s, e) => AtualizarLargura();
            Loaded += (s, e) => AtualizarLargura();
            ChkVerLive.IsChecked = _cfg.LiveVisivel;
            ChkVerObs.IsChecked = _cfg.ObsVisivel;
            ChkVerInter.IsChecked = _cfg.InteracaoVisivel;
            ChkVerChat.IsChecked = _cfg.ChatVisivel;
            IniciarSaude();
            IniciarMomento();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && Gaveta.Visibility == Visibility.Visible) FecharGaveta(); };
            _atalhos = new ObservableCollection<string>(_cfg.Atalhos);
            _atalhos.CollectionChanged += (s, e) => AtualizarAtalhos();
            ListaAtalhos.ItemsSource = _atalhos;
            _interacoes = new ObservableCollection<Interacao>(_cfg.Interacoes);
            _interacoes.CollectionChanged += (s, e) => AtualizarInteracoes();
            ListaInteracoes.ItemsSource = _interacoes;
            ListaComandos.ItemsSource = Comandos;
            TxtDuracao.Text = "60";
            ListaCenas.ItemsSource = _cenas;
            ListaChat.ItemsSource = _chat;
            IniciarChatExtras();
            _vista = CollectionViewSource.GetDefaultView(_chat);
            _vista.Filter = o => PassaNoFiltro((ItemChat)o);
            _filtro = _cfg.FiltroChat;
            var abaInicial = _filtro == "twitch" ? FiltroTwitch : _filtro == "kick" ? FiltroKick : _filtro == "youtube" ? FiltroYouTube
                : _filtro == "alertas" ? FiltroAlertas : FiltroTudo;
            _filtro = (string)abaInicial.Tag; // valores antigos (como "chat") viram "tudo"
            abaInicial.IsChecked = true;
            BtnFixar.Content = _cfg.ConexoesFixas ? "Esconder da barra" : "Mostrar sempre na barra";
            ChipsFixos.Visibility = _cfg.ConexoesFixas ? Visibility.Visible : Visibility.Collapsed;

            RestaurarJanela();
            AtualizarSb();
            AtualizarObs();
            AtualizarAtalhos();
            AtualizarInteracoes();
            _iniciando = false;
            AtualizarMundo();
            AplicarLayout();

            _timerSb.Tick += async (s, e) => await CarregarStreamerbotAsync();
            _timerObs.Tick += async (s, e) => await CarregarObsAsync();
            _relogio.Tick += (s, e) => AtualizarAoVivo();
            _fechaPopup.Tick += (s, e) =>
            {
                _fechaPopup.Stop();
                if (!PontoConexoes.IsMouseOver && !CartaoConexoes.IsMouseOver) PopupConexoes.IsOpen = false;
            };
            _limpaStatusChat.Tick += (s, e) => { _limpaStatusChat.Stop(); StatusChat.Visibility = Visibility.Collapsed; };
            _timerSb.Start();
            _timerObs.Start();
            _relogio.Start();

            Loaded += (s, e) =>
            {
                ListaChat.ApplyTemplate();
                _rolagem = ListaChat.Template.FindName("Rolagem", ListaChat) as ScrollViewer;
                if (_rolagem != null) _rolagem.ScrollChanged += Rolagem_Changed;
            };

            IniciarStreamerbot();
            IniciarObs();

            if (!_emCaptura)
            {
                CriarBandeja();
                StateChanged += (s, e) => { if (WindowState == WindowState.Minimized && _cfg.MinimizarParaBandeja) Hide(); };
                if (_cfg.IniciarEscondido) Loaded += (s, e) => Hide();
            }

            if (_emCaptura) Loaded += (s, e) => Capturar();
        }

        // ---- Janela ----

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Tema.AplicarBarraEscura(this);
            if (_emCaptura) return;
            // Ctrl+Alt+P mostra ou esconde o painel de qualquer lugar.
            var fonte = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (fonte != null)
            {
                _janela = fonte.Handle;
                fonte.AddHook(AtalhoGlobal);
                RegisterHotKey(_janela, IdAtalho, ModControl | ModAlt, TeclaP);
            }
        }

        IntPtr _janela; // guardado porque, no OnClosed, a janela já não tem identificador
        const int IdAtalho = 0x5044;
        const int ModAlt = 0x1, ModControl = 0x2, TeclaP = 0x50, WmHotkey = 0x0312;
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr janela, int id, int modificadores, int tecla);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr janela, int id);

        IntPtr AtalhoGlobal(IntPtr janela, int mensagem, IntPtr w, IntPtr l, ref bool tratado)
        {
            if (mensagem == WmHotkey && w.ToInt32() == IdAtalho)
            {
                AlternarJanela();
                tratado = true;
            }
            return IntPtr.Zero;
        }

        // ---- Bandeja do sistema ----

        void CriarBandeja()
        {
            _bandeja = new System.Windows.Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location),
                Text = "LiveHelm-App",
                Visible = true,
            };
            _bandeja.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) AlternarJanela(); };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TabelaEscura());
            menu.BackColor = System.Drawing.Color.FromArgb(27, 27, 37);
            menu.ForeColor = System.Drawing.Color.FromArgb(232, 230, 240);
            menu.Items.Add("Abrir (Ctrl+Alt+P)", null, (s, e) => MostrarJanela());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Sair do painel", null, (s, e) => Sair());
            _bandeja.ContextMenuStrip = menu;
        }

        // Cores do menu da bandeja, no tema escuro do painel.
        sealed class TabelaEscura : System.Windows.Forms.ProfessionalColorTable
        {
            static readonly System.Drawing.Color Fundo = System.Drawing.Color.FromArgb(27, 27, 37);
            static readonly System.Drawing.Color Sobre = System.Drawing.Color.FromArgb(38, 38, 51);
            public override System.Drawing.Color ToolStripDropDownBackground { get { return Fundo; } }
            public override System.Drawing.Color ImageMarginGradientBegin { get { return Fundo; } }
            public override System.Drawing.Color ImageMarginGradientMiddle { get { return Fundo; } }
            public override System.Drawing.Color ImageMarginGradientEnd { get { return Fundo; } }
            public override System.Drawing.Color MenuBorder { get { return Sobre; } }
            public override System.Drawing.Color MenuItemBorder { get { return Sobre; } }
            public override System.Drawing.Color MenuItemSelected { get { return Sobre; } }
            public override System.Drawing.Color MenuItemSelectedGradientBegin { get { return Sobre; } }
            public override System.Drawing.Color MenuItemSelectedGradientEnd { get { return Sobre; } }
            public override System.Drawing.Color SeparatorDark { get { return Sobre; } }
            public override System.Drawing.Color SeparatorLight { get { return Sobre; } }
        }

        public void MostrarJanela()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true; // garante o primeiro plano mesmo vindo da bandeja
            Topmost = _cfg.SempreNoTopo;
            Focus();
        }

        void AlternarJanela()
        {
            if (IsVisible && WindowState != WindowState.Minimized && IsActive) Hide();
            else MostrarJanela();
        }

        void Sair()
        {
            _saindo = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            if (_bandeja != null)
            {
                _bandeja.Visible = false;
                _bandeja.Dispose();
            }
            if (_janela != IntPtr.Zero) UnregisterHotKey(_janela, IdAtalho);
        }

        void RestaurarJanela()
        {
            if (_cfg.Largura is double l && _cfg.Altura is double a && l >= MinWidth && a >= MinHeight)
            {
                Width = l;
                Height = a;
            }
            if (_cfg.Esquerda is double x && _cfg.Topo is double y
                && x > SystemParameters.VirtualScreenLeft - 50 && x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100
                && y > SystemParameters.VirtualScreenTop - 10 && y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = x;
                Top = y;
            }
            if (_cfg.Maximizada) WindowState = WindowState.Maximized;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!_saindo && !_emCaptura && _cfg.FecharParaBandeja)
            {
                e.Cancel = true;
                GuardarCampos();
                Hide();
                return;
            }
            if (!_emCaptura)
            {
                var limites = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                _cfg.Esquerda = limites.Left;
                _cfg.Topo = limites.Top;
                _cfg.Largura = limites.Width;
                _cfg.Altura = limites.Height;
                _cfg.Maximizada = WindowState == WindowState.Maximized;
                GuardarCampos();
            }
            _ajuda?.Close();
            _sb?.Dispose();
            _obs?.Dispose();
        }

        // Salva uma imagem do conteúdo da janela e fecha (usado só nos testes: --captura).
        void Capturar()
        {
            var espera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AguardoDaCapturaMs) };
            espera.Tick += (s, e) =>
            {
                espera.Stop();
                var raiz = (FrameworkElement)Content;
                var imagem = new RenderTargetBitmap((int)raiz.ActualWidth, (int)raiz.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var fundo = new DrawingVisual();
                using (var d = fundo.RenderOpen()) d.DrawRectangle(Background, null, new Rect(0, 0, raiz.ActualWidth, raiz.ActualHeight));
                imagem.Render(fundo);
                imagem.Render(raiz);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(imagem));
                using (var arquivo = File.Create(App.ArquivoDeCaptura)) png.Save(arquivo);
                Close();
            };
            espera.Start();
        }

        void GuardarCampos()
        {
            _cfg.Titulo = TxtTitulo.Text;
            _cfg.Categoria = TxtCategoria.Text;
            _cfg.Twitch = ChkTwitch.IsChecked == true;
            _cfg.Kick = ChkKick.IsChecked == true;
            _cfg.YouTube = ChkYouTube.IsChecked == true;
            _cfg.StreamerbotUrl = TxtSbUrl.Text.Trim();
            _cfg.StreamerbotSenha = PwdSbSenha.Password;
            _cfg.ObsUrl = TxtObsUrl.Text.Trim();
            _cfg.ObsSenha = PwdObsSenha.Password;
            _cfg.Atalhos = _atalhos.ToList();
            _cfg.Interacoes = _interacoes.ToList();
            GuardarPerguntas();
            _cfg.FiltroChat = _filtro;
            if (!_emCaptura) _cfg.Salvar(_caminhoConfig);
        }

        void UI(Action acao) { Dispatcher.BeginInvoke(acao); }

        void MostrarAjuda()
        {
            if (_ajuda == null)
            {
                _ajuda = new AjudaWindow { Owner = this };
                _ajuda.Closed += (s, e) => _ajuda = null;
                _ajuda.Show();
            }
            else
            {
                _ajuda.Activate();
            }
        }

        void Ajuda_Click(object sender, RoutedEventArgs e) { MostrarAjuda(); }

        // ---- Painéis recolhíveis ----

        void Cabecalho_Click(object sender, MouseButtonEventArgs e)
        {
            switch ((string)((FrameworkElement)sender).Tag)
            {
                case "live": _cfg.LiveRecolhido = !_cfg.LiveRecolhido; break;
                case "inter": _cfg.InteracaoRecolhido = !_cfg.InteracaoRecolhido; break;
                case "obs": _cfg.ObsRecolhido = !_cfg.ObsRecolhido; break;
                case "saude": _cfg.SaudeRecolhido = !_cfg.SaudeRecolhido; break;
                case "momento": _cfg.MomentoRecolhido = !_cfg.MomentoRecolhido; break;
                case "chat": _cfg.ChatRecolhido = !_cfg.ChatRecolhido; break;
            }
            AplicarLayout();
        }

        // Dá foco a um painel: recolhe os outros. Se ele já era o único aberto, reabre todos.
        void Focar_Click(object sender, RoutedEventArgs e)
        {
            var alvo = (string)((FrameworkElement)sender).Tag;
            var nomes = new[] { "live", "obs", "saude", "momento", "inter", "chat" };
            var recolhidos = new[] { _cfg.LiveRecolhido, _cfg.ObsRecolhido, _cfg.SaudeRecolhido, _cfg.MomentoRecolhido, _cfg.InteracaoRecolhido, _cfg.ChatRecolhido };
            bool unico = Enumerable.Range(0, 6).All(i => recolhidos[i] == (nomes[i] != alvo));
            _cfg.LiveRecolhido = !unico && alvo != "live";
            _cfg.ObsRecolhido = !unico && alvo != "obs";
            _cfg.SaudeRecolhido = !unico && alvo != "saude";
            _cfg.MomentoRecolhido = !unico && alvo != "momento";
            _cfg.InteracaoRecolhido = !unico && alvo != "inter";
            _cfg.ChatRecolhido = !unico && alvo != "chat";
            AplicarLayout();
        }

        // O chat ocupa o que sobra; recolhido, vira só o título. Painel desligado na barra de cima some por inteiro.
        void AplicarLayout()
        {
            PainelLive.Visibility = _cfg.LiveVisivel ? Visibility.Visible : Visibility.Collapsed;
            PainelObs.Visibility = _cfg.ObsVisivel ? Visibility.Visible : Visibility.Collapsed;
            PainelSaude.Visibility = _cfg.SaudeVisivel ? Visibility.Visible : Visibility.Collapsed;
            PainelMomento.Visibility = _cfg.MomentoVisivel ? Visibility.Visible : Visibility.Collapsed;
            PainelInter.Visibility = _cfg.InteracaoVisivel ? Visibility.Visible : Visibility.Collapsed;
            PainelChat.Visibility = _cfg.ChatVisivel ? Visibility.Visible : Visibility.Collapsed;
            ConteudoLive.Visibility = _cfg.LiveRecolhido ? Visibility.Collapsed : Visibility.Visible;
            ConteudoObs.Visibility = _cfg.ObsRecolhido ? Visibility.Collapsed : Visibility.Visible;
            ConteudoSaude.Visibility = _cfg.SaudeRecolhido ? Visibility.Collapsed : Visibility.Visible;
            ConteudoMomento.Visibility = _cfg.MomentoRecolhido ? Visibility.Collapsed : Visibility.Visible;
            ConteudoInter.Visibility = _cfg.InteracaoRecolhido ? Visibility.Collapsed : Visibility.Visible;
            ConteudoChat.Visibility = _cfg.ChatRecolhido ? Visibility.Collapsed : Visibility.Visible;
            SetaLive.Text = _cfg.LiveRecolhido ? "▸" : "▾";
            SetaObs.Text = _cfg.ObsRecolhido ? "▸" : "▾";
            SetaSaude.Text = _cfg.SaudeRecolhido ? "▸" : "▾";
            SetaMomento.Text = _cfg.MomentoRecolhido ? "▸" : "▾";
            SetaInter.Text = _cfg.InteracaoRecolhido ? "▸" : "▾";
            SetaChat.Text = _cfg.ChatRecolhido ? "▸" : "▾";
            bool chatAberto = _cfg.ChatVisivel && !_cfg.ChatRecolhido;
            LinhaChat.Height = chatAberto ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            PainelChat.MinHeight = chatAberto ? 170 : 0; // painéis de cima abertos demais não podem engolir o chat
            FiltrosChat.Visibility = _cfg.ChatRecolhido ? Visibility.Collapsed : Visibility.Visible;
            // Um painel de formulário em foco (chat fora e só ele aberto): amplia o conteúdo pra ocupar o espaço.
            int abertos = (_cfg.LiveVisivel && !_cfg.LiveRecolhido ? 1 : 0) + (_cfg.ObsVisivel && !_cfg.ObsRecolhido ? 1 : 0)
                + (_cfg.InteracaoVisivel && !_cfg.InteracaoRecolhido ? 1 : 0);
            double zoom = !chatAberto && abertos == 1 && !_estreito ? ZoomEmFoco : 1;
            ConteudoLive.LayoutTransform = new ScaleTransform(zoom, zoom);
            ConteudoObs.LayoutTransform = new ScaleTransform(zoom, zoom);
            ConteudoInter.LayoutTransform = new ScaleTransform(zoom, zoom);
            AtualizarObs();
            AtualizarResumos();
        }

        // O que cada título mostra quando o painel está recolhido (e também aberto, em cinza).
        void AtualizarResumos()
        {
            var titulo = TxtTitulo.Text.Trim();
            var categoria = TxtCategoria.Text.Trim();
            ResumoLive.Text = _cfg.LiveRecolhido ? Juntar(" · ", titulo, categoria) : "";
            ResumoChat.Text = _chat.Count == 0 ? "" : _chat.Count + " no feed";
            ResumoSaude.Text = _cfg.SaudeRecolhido ? _resumoSaude : "";
            ResumoInter.Text = _previsao != null ? "previsão no ar: " + _previsao.Titulo : _enquete != null ? "enquete no ar: " + _enquete.Titulo : "";
        }

        static string Juntar(string sep, params string[] partes) { return string.Join(sep, partes.Where(p => !string.IsNullOrEmpty(p))); }

        // ---- Conexões ----

        void AtualizarConexoes()
        {
            const string ligado = "#4ADE80", desligado = "#6B6580";
            var itens = new List<ItemConexao>
            {
                new ItemConexao { Nome = "Streamer.bot", Cor = _sbAberto ? ligado : desligado, Detalhe = "" },
            };
            foreach (var p in _plataformas)
                itens.Add(new ItemConexao { Nome = p.Nome, Cor = _conectadas.Contains(p.Id) ? ligado : desligado, Detalhe = "" });
            itens.Add(new ItemConexao
            {
                Nome = "OBS",
                Cor = _obsAberto ? ligado : desligado,
                Detalhe = !_obsAberto ? "" : _aoVivo ? "Live Online · " + Duracao(DateTime.UtcNow - _inicioDaLive) : "Live Offline",
            });
            ListaConexoes.ItemsSource = itens;
            ChipsFixos.ItemsSource = itens;

            int ligadas = itens.Count(i => i.Cor != desligado);
            string cor = ligadas == itens.Count ? ligado : !_sbAberto ? "#FF6B6B" : "#FFC94A";
            BolinhaGeral.Fill = Pincel(cor);
        }

        void AtualizarAoVivo()
        {
            TxtAoVivo.Text = _obsAberto && _aoVivo ? (_estreito ? "● " : "● Live Online ") + Duracao(DateTime.UtcNow - _inicioDaLive) : "";
            if (_aoVivo && (PopupConexoes.IsOpen || _cfg.ConexoesFixas)) AtualizarConexoes();
        }

        static string Duracao(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        // O popup abre com o mouse na bolinha e fecha um instante depois que ele sai (da bolinha e do popup).
        void Ponto_MouseEnter(object sender, MouseEventArgs e)
        {
            _fechaPopup.Stop();
            if (PopupConexoes.IsOpen) return;
            AtualizarConexoes();
            CartaoConexoes.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            PopupConexoes.HorizontalOffset = PontoConexoes.ActualWidth - CartaoConexoes.DesiredSize.Width;
            PopupConexoes.IsOpen = true;
        }

        void Ponto_MouseLeave(object sender, MouseEventArgs e)
        {
            _fechaPopup.Stop();
            _fechaPopup.Start();
        }

        void Fixar_Click(object sender, RoutedEventArgs e)
        {
            _cfg.ConexoesFixas = !_cfg.ConexoesFixas;
            ChipsFixos.Visibility = _cfg.ConexoesFixas ? Visibility.Visible : Visibility.Collapsed;
            BtnFixar.Content = _cfg.ConexoesFixas ? "Esconder da barra" : "Mostrar sempre na barra";
            GuardarCampos();
        }

        // ---- Streamer.bot ----

        void IniciarStreamerbot()
        {
            _sb?.Dispose();
            _sbAberto = false;
            _conectadas = new List<string>();
            _acoes = null;
            _erroSb = null;
            // --sb e --sbsenha (testes) valem no lugar do que está nas configurações.
            _sbUrlEmUso = _cfg.StreamerbotUrl;
            _sbSenhaEmUso = _cfg.StreamerbotSenha;
            var cliente = new StreamerBotClient(App.EnderecoStreamerbot ?? _cfg.StreamerbotUrl, App.SenhaStreamerbot ?? _cfg.StreamerbotSenha ?? "");
            _sb = cliente;
            cliente.ConexaoMudou += aberta => UI(async () =>
            {
                if (_sb != cliente) return;
                _sbAberto = aberta;
                if (aberta)
                {
                    _erroSb = null;
                    await CarregarStreamerbotAsync();
                }
                else
                {
                    _conectadas = new List<string>();
                    _acoes = null;
                }
                AtualizarSb();
            });
            cliente.Recusada += texto => UI(() =>
            {
                if (_sb != cliente) return;
                _erroSb = texto;
                AtualizarSb();
            });
            cliente.Evento += msg =>
            {
                var ev = Eventos.Converter(msg);
                if (ev != null) UI(() => { if (_sb == cliente) Tratar(ev); });
            };
            AtualizarSb();
        }

        async Task CarregarStreamerbotAsync()
        {
            var sb = _sb;
            if (sb == null || !_sbAberto) return;
            var quem = await sb.PedirAsync(new JObject { ["request"] = "GetBroadcaster" });
            if ((string)quem["status"] == "ok")
                _conectadas = ((quem["connected"] as JArray) ?? new JArray()).Select(x => x.ToString().ToLowerInvariant()).ToList();
            var plataformas = quem["platforms"] as JObject;
            if (plataformas != null)
                _ = EmotesDeTerceiros.CarregarAsync(
                    (string)plataformas["twitch"]?["broadcastUserId"], (string)plataformas["kick"]?["broadcasterUserId"], (string)plataformas["youtube"]?["broadcastUserId"]);
            var acoes = await sb.PedirAsync(new JObject { ["request"] = "GetActions" });
            if ((string)acoes["status"] == "ok") _acoes = acoes["actions"] as JArray ?? new JArray();
            AtualizarSb();
        }

        JObject AcaoDe(string nome) { return _acoes?.OfType<JObject>().FirstOrDefault(a => (string)a["name"] == nome); }

        void AtualizarSb()
        {
            AvisoSb.Visibility = _sbAberto ? Visibility.Collapsed : Visibility.Visible;
            AvisoSb.Text = _erroSb != null
                ? "O Streamer.bot recusou a conexão (" + _erroSb + "). Confira a senha nas configurações."
                : "Streamer.bot desconectado: abra-o e ligue o WebSocket Server.";
            BtnAtualizar.IsEnabled = _sbAberto && !_enviando;
            foreach (var p in _plataformas)
            {
                var texto = p.Nome + (_conectadas.Contains(p.Id) ? " · conectada" : " · sem conexão") + (p.SoTitulo ? " · só o título" : "");
                p.Marca.ToolTip = texto;
            }

            var faltam = _acoes == null ? new List<PlataformaDoPainel>() : _plataformas.Where(p => AcaoDe(p.Acao) == null).ToList();
            BtnFaltam.Visibility = faltam.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AvisosLive.Visibility = !_sbAberto || faltam.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnFaltam.Content = "⚠ Faltam ações: " + string.Join(", ", faltam.Select(p => p.Nome)) + " (como criar)";
            AtualizarConexoes();
        }

        // ---- Título e categoria ----

        async void Atualizar_Click(object sender, RoutedEventArgs e)
        {
            if (_enviando || !_sbAberto) return;
            _enviando = true;
            BtnAtualizar.Content = "Enviando…";
            AtualizarSb();
            PopupSugestoes.IsOpen = false;
            GuardarCampos();
            var titulo = TxtTitulo.Text.Trim();
            var categoria = TxtCategoria.Text.Trim();

            var resultados = new List<ResultadoEnvio>();
            foreach (var p in _plataformas.Where(p => p.Marca.IsChecked == true))
                resultados.Add(await EnviarParaAsync(p, titulo, categoria));
            if (resultados.Count == 0)
            {
                StatusEnvio.Inlines.Clear();
                StatusEnvio.Inlines.Add(new Run("Nenhuma plataforma ativa: ligue uma no alto da janela.") { Foreground = CorAviso });
                StatusEnvio.Visibility = Visibility.Visible;
            }

            _enviando = false;
            BtnAtualizar.Content = "Confirmar";
            AtualizarSb();
            if (resultados.Count > 0) MostrarStatusEnvio(resultados);
            GuardarCampos();
        }

        async Task<ResultadoEnvio> EnviarParaAsync(PlataformaDoPainel p, string titulo, string categoria)
        {
            var acao = AcaoDe(p.Acao);
            if (acao == null) return new ResultadoEnvio { Plataforma = p, Nivel = 2, Texto = "falta a ação “" + p.Acao + "”" };

            TaskCompletionSource<EventoSb> confirmacao = null;
            if (p.ComConfirmacao)
            {
                confirmacao = new TaskCompletionSource<EventoSb>(TaskCreationOptions.RunContinuationsAsynchronously);
                _confirmacoes[p.Id] = confirmacao;
            }
            var resposta = await _sb.PedirAsync(new JObject
            {
                ["request"] = "DoAction",
                ["action"] = new JObject { ["id"] = acao["id"], ["name"] = acao["name"] },
                ["args"] = new JObject { ["tituloLive"] = titulo, ["categoriaLive"] = categoria },
            });
            if ((string)resposta["status"] != "ok")
            {
                _confirmacoes.TryRemove(p.Id, out _);
                return new ResultadoEnvio { Plataforma = p, Nivel = 2, Texto = (string)resposta["error"] ?? "falhou" };
            }
            if (confirmacao == null) return new ResultadoEnvio { Plataforma = p, Nivel = 0, Texto = "título enviado" };

            var terminou = await Task.WhenAny(confirmacao.Task, Task.Delay(ConfirmacaoMs));
            _confirmacoes.TryRemove(p.Id, out _);
            if (terminou != confirmacao.Task)
                return new ResultadoEnvio { Plataforma = p, Nivel = 1, Texto = "enviado, sem confirmação (a categoria pode não existir ou já estava assim)" };

            var feito = confirmacao.Task.Result;
            _cfg.LembrarCategoria(feito.Categoria);
            if (categoria != "" && Normalizar(feito.Categoria) != Normalizar(categoria))
                return new ResultadoEnvio { Plataforma = p, Nivel = 1, Texto = "categoria ficou “" + feito.Categoria + "”" };
            if (titulo != "" && Normalizar(feito.Titulo) != Normalizar(titulo))
                return new ResultadoEnvio { Plataforma = p, Nivel = 1, Texto = "título ficou “" + feito.Titulo + "”" };
            return new ResultadoEnvio { Plataforma = p, Nivel = 0, Texto = feito.Categoria != "" ? feito.Categoria : "ok" };
        }

        void MostrarStatusEnvio(List<ResultadoEnvio> resultados)
        {
            StatusEnvio.Inlines.Clear();
            if (resultados.Count == 0) { StatusEnvio.Visibility = Visibility.Collapsed; return; }
            foreach (var r in resultados)
            {
                var cor = r.Nivel == 0 ? CorOk : r.Nivel == 1 ? CorAviso : CorErro;
                var marca = r.Nivel == 0 ? "✓ " : r.Nivel == 1 ? "! " : "✕ ";
                StatusEnvio.Inlines.Add(new Run(marca + r.Plataforma.Nome + ": ") { Foreground = cor, FontWeight = FontWeights.SemiBold });
                StatusEnvio.Inlines.Add(new Run(r.Texto + "     ") { Foreground = cor });
            }
            StatusEnvio.Visibility = Visibility.Visible;
        }

        // Compara sem diferenciar maiúsculas, acentos, espaços e símbolos.
        static string Normalizar(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        // Mudar o texto torna o resultado antigo enganoso: some até o próximo envio.
        void Campo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (StatusEnvio == null) return;
            StatusEnvio.Visibility = Visibility.Collapsed;
            AtualizarResumos();
        }

        void Campo_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && BtnAtualizar.IsEnabled) Atualizar_Click(sender, null);
        }

        // ---- Atalhos e sugestões de categoria ----

        void AtualizarAtalhos()
        {
            VazioAtalhos.Visibility = _atalhos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ListaAtalhos.Visibility = _atalhos.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void SalvarAtalho_Click(object sender, RoutedEventArgs e)
        {
            var nome = TxtCategoria.Text.Trim();
            if (nome == "" || _atalhos.Any(a => string.Equals(a, nome, StringComparison.OrdinalIgnoreCase))) return;
            _atalhos.Add(nome);
            GuardarCampos();
        }

        void Atalho_Click(object sender, RoutedEventArgs e)
        {
            _semSugestoes = true;
            TxtCategoria.Text = (string)((FrameworkElement)sender).Tag;
            _semSugestoes = false;
            PopupSugestoes.IsOpen = false;
        }

        void RemoverAtalho_Click(object sender, RoutedEventArgs e)
        {
            _atalhos.Remove((string)((FrameworkElement)sender).Tag);
            GuardarCampos();
        }

        // Sugere o que já foi salvo ou confirmado pelas plataformas (grafia certa) enquanto digita.
        List<string> Candidatas()
        {
            var filtro = Normalizar(TxtCategoria.Text);
            return _atalhos.Concat(_cfg.Historico)
                .Where(c => c.Trim() != "")
                .GroupBy(Normalizar).Select(g => g.First())
                .Where(c => filtro == "" || (Normalizar(c).Contains(filtro) && Normalizar(c) != filtro))
                .Take(8).ToList();
        }

        void AtualizarSugestoes()
        {
            if (_semSugestoes || !TxtCategoria.IsKeyboardFocused) return;
            var lista = Candidatas();
            ListaSugestoes.ItemsSource = lista;
            PopupSugestoes.IsOpen = lista.Count > 0;
        }

        void Categoria_Foco(object sender, KeyboardFocusChangedEventArgs e) { AtualizarSugestoes(); }

        void Categoria_TextChanged(object sender, TextChangedEventArgs e)
        {
            Campo_TextChanged(sender, e);
            AtualizarSugestoes();
        }

        void Categoria_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { PopupSugestoes.IsOpen = false; return; }
            if (e.Key == Key.Down && PopupSugestoes.IsOpen && ListaSugestoes.Items.Count > 0) { ListaSugestoes.SelectedIndex = 0; e.Handled = true; return; }
            if (e.Key == Key.Enter) PopupSugestoes.IsOpen = false;
            Campo_KeyDown(sender, e);
        }

        void Sugestao_Escolhida(object sender, SelectionChangedEventArgs e)
        {
            var escolhida = ListaSugestoes.SelectedItem as string;
            if (escolhida == null) return;
            _semSugestoes = true;
            TxtCategoria.Text = escolhida;
            TxtCategoria.CaretIndex = escolhida.Length;
            _semSugestoes = false;
            PopupSugestoes.IsOpen = false;
            TxtCategoria.Focus();
        }

        // ---- Chat e atividade ----

        // A aba Perguntas esconde a lista de mensagens, então não filtra nada.
        bool PassaNoFiltro(ItemChat it)
        {
            if (_filtro == "alertas") { if (!it.EhAlerta) return false; }
            else if (_filtro != "tudo" && _filtro != "perguntas" && (it.EhAlerta || it.Plataforma != _filtro)) return false; // aba de plataforma: só mensagens dela
            return PassaNaBusca(it);
        }

        void Filtro_Checked(object sender, RoutedEventArgs e)
        {
            _filtro = (string)((RadioButton)sender).Tag;
            AtualizarDestinoDoChat();
            if (PainelPerguntas != null) PainelPerguntas.Visibility = _filtro == "perguntas" ? Visibility.Visible : Visibility.Collapsed;
            if (_vista == null) return;
            _vista.Refresh();
            _rolarAuto = true;
            _rolagem?.ScrollToEnd();
        }

        // Aba de plataforma: envia só para ela (o alto da janela não conta). Em Tudo e em Alertas, vale o que está ativo no alto.
        List<PlataformaDoPainel> DestinosDoChat()
        {
            var aba = _plataformas.FirstOrDefault(p => p.Id == _filtro);
            return aba != null ? new List<PlataformaDoPainel> { aba } : _plataformas.Where(p => p.MarcaChat.IsChecked == true).ToList();
        }

        void AtualizarDestinoDoChat()
        {
            if (TxtMensagem == null || _plataformas == null) return;
            var aba = _plataformas.FirstOrDefault(p => p.Id == _filtro);
            TxtMensagem.Tag = aba != null ? "Mensagem para " + (aba.Nome == "Twitch" ? "a " : aba.Nome == "Kick" ? "a " : "o ") + aba.Nome : "Mensagem";
        }

        static string Chave(string plataforma, string id) { return plataforma + "|" + id; }

        void Tratar(EventoSb e)
        {
            switch (e.Tipo)
            {
                case TipoEv.Chat:
                case TipoEv.Alerta:
                    AdicionarChat(e.Item);
                    if (e.Dados != null) AtualizarInteracao(e);
                    break;
                case TipoEv.Apagar:
                    ItemChat apagada;
                    if (_porId.TryGetValue(Chave(e.Plataforma, e.Id), out apagada)) apagada.Apagada = true;
                    break;
                case TipoEv.ApagarUsuario:
                    foreach (var it in _chat.Where(i => !i.EhAlerta && i.Plataforma == e.Plataforma && string.Equals(i.Nome, e.Nome, StringComparison.OrdinalIgnoreCase)))
                        it.Apagada = true;
                    if (e.Item != null)
                    {
                        RegistrarPunicao(e.Plataforma, e.Nome, e.Item.Rotulo == "Banido", e.Segundos, e.Login, e.UsuarioId);
                        AdicionarChat(e.Item);
                    }
                    break;
                case TipoEv.Limpar:
                    foreach (var it in _chat.Where(i => !i.EhAlerta && i.Plataforma == e.Plataforma)) it.Apagada = true;
                    AdicionarChat(new ItemChat { Plataforma = e.Plataforma, Hora = DateTime.Now.ToString("HH:mm"), EhAlerta = true, Rotulo = "Chat limpo", Nome = "" });
                    break;
                case TipoEv.Primeira:
                    ItemChat primeira;
                    if (_porId.TryGetValue(Chave(e.Plataforma, e.Id), out primeira)) primeira.Primeira = true;
                    else _primeiras.Add(Chave(e.Plataforma, e.Id));
                    break;
                case TipoEv.Atualizacao:
                    TaskCompletionSource<EventoSb> espera;
                    if (_confirmacoes.TryGetValue(e.Plataforma, out espera)) espera.TrySetResult(e);
                    break;
            }
        }

        void AdicionarChat(ItemChat it)
        {
            if (!it.EhAlerta && it.Id != "")
            {
                var chave = Chave(it.Plataforma, it.Id);
                if (_primeiras.Remove(chave)) it.Primeira = true;
                _porId[chave] = it;
            }
            _chat.Add(it);
            while (_chat.Count > MaxChat)
            {
                var velho = _chat[0];
                if (velho.Id != "") _porId.Remove(Chave(velho.Plataforma, velho.Id));
                _chat.RemoveAt(0);
            }
            ChatVazio.Visibility = Visibility.Collapsed;
            if (!_rolarAuto && PassaNoFiltro(it))
            {
                _novas++;
                BtnNovas.Content = _novas == 1 ? "1 nova ↓" : _novas + " novas ↓";
                BtnNovas.Visibility = Visibility.Visible;
            }
            AtualizarResumos();
        }

        // Acompanha o fim do chat enquanto o usuário não rolar pra cima.
        void Rolagem_Changed(object sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            {
                _rolarAuto = _rolagem.VerticalOffset >= _rolagem.ScrollableHeight - 4;
                if (_rolarAuto) { _novas = 0; BtnNovas.Visibility = Visibility.Collapsed; }
            }
            else if (_rolarAuto)
            {
                _rolagem.ScrollToEnd();
            }
        }

        void Novas_Click(object sender, RoutedEventArgs e)
        {
            _rolarAuto = true;
            _novas = 0;
            BtnNovas.Visibility = Visibility.Collapsed;
            _rolagem?.ScrollToEnd();
        }

        // ---- Moderação e envio (por ações do Streamer.bot) ----

        // null se deu certo; senão o motivo. `verbo` é o argumento `acao`: enviar, apagar, silenciar, banir ou limpar.
        async Task<string> ExecutarChatAsync(string verbo, JObject args)
        {
            if (!_sbAberto) return "Streamer.bot desconectado";
            // Se você criou a ação própria da plataforma (por exemplo "Painel: Banir Kick", só com a sub-ação nativa), ela vale; senão a Painel: Chat.
            JObject acao = null;
            var rotulo = verbo == "enviar" ? "Enviar" : verbo == "silenciar" ? "Silenciar" : verbo == "banir" ? "Banir" : null;
            var plataforma = (string)args["plataforma"];
            if (rotulo != null && plataforma != null)
                acao = AcaoDe("Painel: " + rotulo + " " + (plataforma == "youtube" ? "YouTube" : plataforma == "kick" ? "Kick" : "Twitch"));
            if (acao == null) acao = AcoesDoChat.Select(AcaoDe).FirstOrDefault(a => a != null);
            if (acao == null) return "falta a ação “" + AcoesDoChat[0] + "” no Streamer.bot (veja o botão ? no alto)";
            args["acao"] = verbo;
            var resposta = await _sb.PedirAsync(new JObject
            {
                ["request"] = "DoAction",
                ["action"] = new JObject { ["id"] = acao["id"], ["name"] = acao["name"] },
                ["args"] = args,
            });
            return (string)resposta["status"] == "ok" ? null : ((string)resposta["error"] ?? "falhou");
        }

        void AvisarChat(string texto, bool erro = true)
        {
            StatusChat.Foreground = erro ? CorAviso : CorSuave;
            StatusChat.Text = texto;
            StatusChat.Visibility = Visibility.Visible;
            _limpaStatusChat.Stop();
            _limpaStatusChat.Start();
        }

        static ItemChat ItemDoMenu(object sender)
        {
            var menu = ((MenuItem)sender).Parent as ContextMenu;
            var alvo = menu == null ? null : menu.PlacementTarget as FrameworkElement;
            return alvo == null ? null : alvo.DataContext as ItemChat;
        }

        void MenuDaMensagem_Opened(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)sender;
            var alvo = menu.PlacementTarget as FrameworkElement;
            var item = alvo == null ? null : alvo.DataContext as ItemChat;
            foreach (var mi in menu.Items.OfType<MenuItem>())
            {
                mi.IsEnabled = item != null && (!item.EhAlerta || (string)mi.Tag == "copiar");
                // O Streamer.bot não oferece apagar mensagem do YouTube.
                if (item != null && item.Plataforma == "youtube" && (string)mi.Header == "Apagar mensagem")
                {
                    mi.IsEnabled = false;
                    mi.ToolTip = "O YouTube não permite apagar mensagens por aqui";
                }
                else if ((string)mi.Header == "Apagar mensagem") mi.ToolTip = null;
            }
        }

        void MenuMencionar_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null) return;
            TxtMensagem.Text = (TxtMensagem.Text + " @" + it.Nome + " ").TrimStart();
            TxtMensagem.Focus();
            TxtMensagem.CaretIndex = TxtMensagem.Text.Length;
        }

        void MenuCopiar_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null) return;
            try { Clipboard.SetText(it.EhAlerta ? Juntar(" ", it.Rotulo, it.Nome, it.Detalhe) : it.Nome + ": " + it.Texto); }
            catch (Exception) { /* a área de transferência pode estar em uso por outro programa */ }
        }

        async Task ModerarAsync(string verbo, ItemChat it, int segundos, string infinitivo)
        {
            var args = new JObject
            {
                ["plataforma"] = it.Plataforma, ["usuario"] = it.Login != "" ? it.Login : it.Nome, ["usuarioId"] = it.UsuarioId,
                ["mensagemId"] = it.Id, ["segundos"] = segundos,
            };
            var erro = await ExecutarChatAsync(verbo, args);
            if (erro != null) { AvisarChat("Não consegui " + infinitivo + ": " + erro); return; }
            if (verbo == "apagar") it.Apagada = true;
            else RegistrarPunicao(it.Plataforma, it.Nome, verbo == "banir", segundos, it.Login, it.UsuarioId);
        }

        async void MenuApagar_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it != null) await ModerarAsync("apagar", it, 0, "apagar");
        }

        async void MenuSilenciar1_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it != null) await ModerarAsync("silenciar", it, 60, "silenciar");
        }

        async void MenuSilenciar10_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it != null) await ModerarAsync("silenciar", it, 600, "silenciar");
        }

        async void MenuBanir_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null) return;
            if (MessageBox.Show(this, "Banir " + it.Nome + " (" + it.Plataforma + ")?", "Banir", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ModerarAsync("banir", it, 0, "banir");
        }

        void Mensagem_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Enviar_Click(sender, null);
        }

        // Comando digitado na caixa do chat (/ban nome, /timeout nome 600, /limpar): devolve o comando e os argumentos (sem a
        // plataforma), ou null se não for um comando conhecido.
        static Tuple<ComandoInfo, JObject> InterpretarComando(string texto, out string erro)
        {
            erro = null;
            var partes = texto.Substring(1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var palavra = partes.Length > 0 ? partes[0].ToLowerInvariant() : "";
            var nome = partes.Length > 1 ? partes[1].TrimStart('@') : "";
            switch (palavra)
            {
                case "ban":
                case "banir":
                    if (nome == "") { erro = "Use /ban nome"; return null; }
                    return Tuple.Create(Comandos[0], new JObject { ["usuario"] = nome });
                case "timeout":
                case "silenciar":
                    if (nome == "") { erro = "Use /timeout nome segundos"; return null; }
                    int segundos;
                    if (partes.Length < 3 || !int.TryParse(partes[2], out segundos) || segundos <= 0) segundos = 600;
                    return Tuple.Create(Comandos[1], new JObject { ["usuario"] = nome, ["segundos"] = segundos });
                case "limpar":
                case "clear":
                    return Tuple.Create(Comandos[2], new JObject());
            }
            erro = "Comando desconhecido. Digite / para ver os comandos.";
            return null;
        }

        // Digitando "/" abre a lista de comandos (filtrada pelo que já foi escrito).
        void Mensagem_TextChanged(object sender, TextChangedEventArgs e)
        {
            var t = TxtMensagem.Text;
            if (!TxtMensagem.IsKeyboardFocused || !t.StartsWith("/") || t.Contains(" "))
            {
                PopupComandos.IsOpen = false;
                return;
            }
            var lista = Comandos.Where(c => c.Insere.StartsWith(t, StringComparison.OrdinalIgnoreCase) || c.Texto.StartsWith(t, StringComparison.OrdinalIgnoreCase)).ToList();
            ListaComandos.ItemsSource = lista;
            PopupComandos.IsOpen = lista.Count > 0;
        }

        void Comando_Escolhido(object sender, SelectionChangedEventArgs e)
        {
            var c = ListaComandos.SelectedItem as ComandoInfo;
            if (c == null) return;
            PopupComandos.IsOpen = false;
            TxtMensagem.Text = c.Insere;
            TxtMensagem.Focus();
            TxtMensagem.CaretIndex = TxtMensagem.Text.Length;
        }

        async void Enviar_Click(object sender, RoutedEventArgs e)
        {
            var texto = TxtMensagem.Text.Trim();
            if (texto == "") return;
            PopupComandos.IsOpen = false;
            var destinos = DestinosDoChat();
            if (destinos.Count == 0) { AvisarChat("Ative uma plataforma no alto da janela ou escolha a aba de uma delas."); return; }

            string verbo = "enviar";
            JObject base_ = new JObject { ["mensagem"] = texto };
            if (texto.StartsWith("/"))
            {
                string erroComando;
                var comando = InterpretarComando(texto, out erroComando);
                if (comando == null) { AvisarChat(erroComando); return; }
                verbo = comando.Item1.Verbo;
                base_ = comando.Item2;
                // Plataforma que não tem o comando fica de fora, sem aviso de erro.
                destinos = destinos.Where(p => comando.Item1.Plataformas.Contains(p.Id)).ToList();
                if (destinos.Count == 0) { AvisarChat("Nenhuma das plataformas marcadas aceita esse comando (" + comando.Item1.Descricao + ")."); return; }
            }

            BtnEnviar.IsEnabled = false;
            var erros = new List<string>();
            foreach (var p in destinos)
            {
                var args = (JObject)base_.DeepClone();
                args["plataforma"] = p.Id;
                var erro = await ExecutarChatAsync(verbo, args);
                if (erro != null) erros.Add(p.Nome + ": " + erro);
                else if (verbo == "banir" || verbo == "silenciar") RegistrarPunicao(p.Id, (string)args["usuario"], verbo == "banir", (int?)args["segundos"] ?? 0, "", "");
            }
            BtnEnviar.IsEnabled = true;
            if (erros.Count == 0)
            {
                TxtMensagem.Text = "";
                if (verbo != "enviar") AvisarChat("Comando enviado: " + texto + " (" + string.Join(", ", destinos.Select(p => p.Nome)) + ")", false);
            }
            else AvisarChat("Não enviei — " + string.Join("; ", erros.Distinct()));
        }

        // ---- Enquetes e previsões (Twitch) ----

        void AtualizarInteracoes()
        {
            ListaInteracoes.Visibility = _interacoes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void AvisarInter(string texto, bool erro = true)
        {
            StatusInter.Foreground = erro ? CorAviso : CorSuave;
            StatusInter.Text = texto;
            StatusInter.Visibility = Visibility.Visible;
        }

        string TipoDoForm { get { return TipoPrevisao.IsChecked == true ? "previsao" : "enquete"; } }

        // Cada tipo tem sua duração padrão: 1 minuto na enquete e 2 minutos pra apostar na previsão.
        void TipoInter_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtDuracao == null) return;
            int atual;
            if (!int.TryParse(TxtDuracao.Text, out atual) || atual == 60 || atual == 120) TxtDuracao.Text = TipoDoForm == "previsao" ? "120" : "60";
        }

        void Inter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Iniciar_Click(sender, null);
        }

        static List<string> OpcoesDe(string texto)
        {
            var separador = texto.Contains("|") ? '|' : ',';
            return texto.Split(separador).Select(o => o.Trim()).Where(o => o != "").ToList();
        }

        // Limites da Twitch: enquete de 2 a 5 opções, previsão de 2 a 10; título de 60 (enquete) ou 45 (previsão); opção até 25.
        string ValidarInteracao(string tipo, string pergunta, List<string> opcoes, int duracao)
        {
            bool previsao = tipo == "previsao";
            if (pergunta == "") return "Escreva a pergunta.";
            if (pergunta.Length > (previsao ? 45 : 60)) return "A pergunta passa de " + (previsao ? 45 : 60) + " letras (limite da Twitch).";
            if (opcoes.Count < 2) return "Coloque pelo menos 2 opções, separadas por | (barra).";
            if (opcoes.Count > (previsao ? 10 : 5)) return "No máximo " + (previsao ? 10 : 5) + " opções.";
            if (opcoes.Any(o => o.Length > 25)) return "Cada opção tem no máximo 25 letras.";
            if (duracao < (previsao ? 30 : 15) || duracao > 1800) return "A duração vai de " + (previsao ? 30 : 15) + " a 1800 segundos.";
            return null;
        }

        async Task IniciarInteracaoAsync(string tipo, string pergunta, string opcoesTexto, int duracao)
        {
            if (ChkTwitch.IsChecked != true) { AvisarInter("A Twitch está desativada (ícone no alto). Ative para criar."); return; }
            var opcoes = OpcoesDe(opcoesTexto);
            var invalido = ValidarInteracao(tipo, pergunta, opcoes, duracao);
            if (invalido != null) { AvisarInter(invalido); return; }
            BtnIniciar.IsEnabled = false;
            var erro = await ExecutarChatAsync(tipo, new JObject
            {
                ["plataforma"] = "twitch", ["titulo"] = pergunta, ["opcoes"] = string.Join("|", opcoes), ["duracao"] = duracao,
            });
            BtnIniciar.IsEnabled = true;
            if (erro != null) AvisarInter("Não consegui iniciar: " + erro);
            else AvisarInter((tipo == "previsao" ? "Previsão" : "Enquete") + " enviada à Twitch. Se ela não aparecer aqui em instantes, confira se já não há uma no ar.", false);
        }

        async void Iniciar_Click(object sender, RoutedEventArgs e)
        {
            int duracao;
            if (!int.TryParse(TxtDuracao.Text, out duracao)) duracao = 0;
            await IniciarInteracaoAsync(TipoDoForm, TxtPergunta.Text.Trim(), TxtOpcoes.Text, duracao);
        }

        void CarregarInteracao_Click(object sender, RoutedEventArgs e)
        {
            var i = (Interacao)((FrameworkElement)sender).Tag;
            (i.Tipo == "previsao" ? TipoPrevisao : TipoEnquete).IsChecked = true;
            TxtPergunta.Text = i.Pergunta;
            TxtOpcoes.Text = i.Opcoes;
            TxtDuracao.Text = i.Duracao.ToString();
        }

        async void IniciarSalva_Click(object sender, RoutedEventArgs e)
        {
            var i = (Interacao)((FrameworkElement)sender).Tag;
            await IniciarInteracaoAsync(i.Tipo, i.Pergunta, i.Opcoes, i.Duracao);
        }

        void RemoverInteracao_Click(object sender, RoutedEventArgs e)
        {
            _interacoes.Remove((Interacao)((FrameworkElement)sender).Tag);
            GuardarCampos();
        }

        static string IdDe(JObject d)
        {
            return Alertas.Texto(Alertas.Ou(d["id"], d["pollId"], d["predictionId"], Alertas.Campo(d["poll"], "id"), Alertas.Campo(d["prediction"], "id")));
        }

        static InteracaoAtiva LerInteracao(JObject d)
        {
            var i = new InteracaoAtiva
            {
                Id = IdDe(d),
                Titulo = Alertas.Texto(Alertas.Ou(d["title"], d["question"], Alertas.Campo(d["poll"], "title"), Alertas.Campo(d["prediction"], "title"))),
            };
            foreach (var o in Alertas.ListaDe(d)) i.Opcoes.Add(new KeyValuePair<string, string>(Alertas.Texto((o as JObject) == null ? null : ((JObject)o)["id"]), Alertas.NomeDoItem(o)));
            return i;
        }

        // Acompanha a enquete e a previsão que estão no ar (as criadas aqui e as criadas direto na Twitch).
        void AtualizarInteracao(EventoSb e)
        {
            switch (e.TipoOriginal)
            {
                case "PollCreated": _enquete = LerInteracao(e.Dados); break;
                case "PollCompleted":
                case "PollTerminated":
                case "PollArchived": _enquete = null; break;
                case "PredictionCreated": _previsao = LerInteracao(e.Dados); break;
                case "PredictionLocked":
                    _previsao = _previsao ?? LerInteracao(e.Dados);
                    _previsao.Travada = true;
                    break;
                case "PredictionCompleted":
                case "PredictionCanceled": _previsao = null; break;
            }
            RenderizarInteracao();
        }

        Button BotaoDeInteracao(string texto, string verbo, InteracaoAtiva i, string vencedorId = "")
        {
            var botao = new Button { Content = texto, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 5, 12, 5) };
            if (i.Id == "" || (verbo == "resolver_previsao" && vencedorId == ""))
            {
                botao.IsEnabled = false;
                botao.ToolTip = "O Streamer.bot não mandou o identificador no evento; controle esta pela Twitch.";
            }
            botao.Click += async (s, ev) =>
            {
                var erro = await ExecutarChatAsync(verbo, new JObject { ["plataforma"] = "twitch", ["id"] = i.Id, ["vencedorId"] = vencedorId });
                if (erro != null) AvisarInter("Não consegui: " + erro);
                else StatusInter.Visibility = Visibility.Collapsed;
            };
            return botao;
        }

        void RenderizarInteracao()
        {
            BotoesInteracao.Children.Clear();
            var linhas = new List<string>();
            if (_enquete != null)
            {
                linhas.Add("Enquete no ar: " + _enquete.Titulo);
                BotoesInteracao.Children.Add(BotaoDeInteracao("Encerrar enquete", "encerrar_enquete", _enquete));
            }
            if (_previsao != null)
            {
                linhas.Add("Previsão " + (_previsao.Travada ? "travada" : "aberta") + ": " + _previsao.Titulo);
                if (!_previsao.Travada) BotoesInteracao.Children.Add(BotaoDeInteracao("Travar apostas", "travar_previsao", _previsao));
                foreach (var o in _previsao.Opcoes)
                    BotoesInteracao.Children.Add(BotaoDeInteracao("Venceu: " + o.Value, "resolver_previsao", _previsao, o.Key));
                BotoesInteracao.Children.Add(BotaoDeInteracao("Cancelar previsão", "cancelar_previsao", _previsao));
            }
            TxtInteracao.Text = string.Join("\n", linhas);
            CartaoInteracao.Visibility = linhas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AtualizarResumos();
        }

        // ---- Plataformas globais e painéis ----

        void Plataforma_Changed(object sender, RoutedEventArgs e)
        {
            if (_iniciando) return;
            AtualizarMundo();
            GuardarCampos();
        }

        // O globo acende (cor de destaque) quando todas as plataformas estão ativas.
        void AtualizarMundo()
        {
            if (_plataformas == null) return;
            bool todas = _plataformas.All(p => p.Marca.IsChecked == true);
            BtnMundo.Foreground = (Brush)FindResource(todas ? "AcentoClaro" : "Suave");
            BtnMundo.ToolTip = todas ? "Desativar todas as plataformas" : "Ativar todas as plataformas";
        }

        void Mundo_Click(object sender, RoutedEventArgs e)
        {
            bool todas = _plataformas.All(p => p.Marca.IsChecked == true);
            foreach (var p in _plataformas) p.Marca.IsChecked = !todas;
        }

        void Visivel_Changed(object sender, RoutedEventArgs e)
        {
            if (_iniciando) return;
            _cfg.LiveVisivel = ChkVerLive.IsChecked == true;
            _cfg.ObsVisivel = ChkVerObs.IsChecked == true;
            bool saudeLigou = ChkVerSaude.IsChecked == true && !_cfg.SaudeVisivel;
            bool momentoLigou = ChkVerMomento.IsChecked == true && !_cfg.MomentoVisivel;
            _cfg.SaudeVisivel = ChkVerSaude.IsChecked == true;
            _cfg.MomentoVisivel = ChkVerMomento.IsChecked == true;
            _cfg.InteracaoVisivel = ChkVerInter.IsChecked == true;
            _cfg.ChatVisivel = ChkVerChat.IsChecked == true;
            AplicarLayout();
            GuardarCampos();
            // Saúde e Momento só consultam o OBS enquanto estão ligados.
            if (saudeLigou) _ = CarregarSaudeAsync();
            else if (momentoLigou) _ = CarregarGravacaoAsync();
        }

        // ---- Tamanho da janela (escalas prontas) ----

        const double LarguraAoLadoDoObs = 420; // a faixa do painel ao lado do preview do OBS
        [StructLayout(LayoutKind.Sequential)]
        struct RetanguloNativo { public int Esquerda, Topo, Direita, Base; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr janela, out RetanguloNativo retangulo);

        void PosicionarEm(double esquerda, double topo, double largura, double altura)
        {
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = esquerda;
            Top = topo;
            Width = largura;
            Height = altura;
        }

        // Janela comum (a de sempre), centralizada na tela onde o painel está.
        void EscalaPadrao_Click(object sender, RoutedEventArgs e)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            double largura = Math.Min(1120, area.Width / dpi.DpiScaleX - 40);
            double altura = Math.Min(780, area.Height / dpi.DpiScaleY - 40);
            PosicionarEm((area.Left + area.Width / 2.0) / dpi.DpiScaleX - largura / 2, (area.Top + area.Height / 2.0) / dpi.DpiScaleY - altura / 2, largura, altura);
            OpTopo.IsChecked = false;
        }

        // Faixa estreita na altura da tela: colada à direita da janela do OBS (se ele estiver aberto e sobrar espaço ali) ou no canto
        // direito da tela dele. Fica acima das outras janelas pra não sumir quando você clica no OBS.
        void EscalaObs_Click(object sender, RoutedEventArgs e)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var tela = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
            RetanguloNativo obs = new RetanguloNativo();
            bool achouObs = false;
            try
            {
                var processo = System.Diagnostics.Process.GetProcessesByName("obs64").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                if (processo != null && GetWindowRect(processo.MainWindowHandle, out obs))
                {
                    achouObs = true;
                    tela = System.Windows.Forms.Screen.FromHandle(processo.MainWindowHandle);
                }
            }
            catch (Exception) { /* sem acesso ao processo do OBS: usa a tela atual */ }

            var area = tela.WorkingArea;
            int largura = (int)(LarguraAoLadoDoObs * dpi.DpiScaleX);
            int esquerda = area.Right - largura;
            // Sobrou espaço de verdade à direita da janela do OBS (ele não está em tela cheia): cola nela.
            if (achouObs && area.Right - obs.Direita >= 360 * dpi.DpiScaleX)
            {
                esquerda = obs.Direita;
                largura = Math.Min(largura, area.Right - obs.Direita);
            }
            PosicionarEm(esquerda / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, largura / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
            OpTopo.IsChecked = true;
        }

        // ---- Emojis ----

        [DllImport("user32.dll")] static extern void keybd_event(byte tecla, byte varredura, uint opcoes, UIntPtr extra);
        const byte TeclaWindows = 0x5B, TeclaPonto = 0xBE;
        const uint SoltarTecla = 0x2;

        // O seletor de emojis do Windows (Win + .) escreve no campo que está em foco; aqui só se foca o título e se aperta o atalho.
        void Emoji_Click(object sender, RoutedEventArgs e)
        {
            TxtTitulo.Focus();
            TxtTitulo.CaretIndex = TxtTitulo.Text.Length;
            keybd_event(TeclaWindows, 0, 0, UIntPtr.Zero);
            keybd_event(TeclaPonto, 0, 0, UIntPtr.Zero);
            keybd_event(TeclaPonto, 0, SoltarTecla, UIntPtr.Zero);
            keybd_event(TeclaWindows, 0, SoltarTecla, UIntPtr.Zero);
        }

        // ---- Layout estreito ----

        static void Pos(UIElement el, int linha, int coluna, int colunas = 1, double margemTopo = 0)
        {
            Grid.SetRow(el, linha);
            Grid.SetColumn(el, coluna);
            Grid.SetColumnSpan(el, colunas);
            var fe = el as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, margemTopo, el is Button && ((Button)el).Name == "BtnEmoji" ? 3 : 0, 0);
        }

        // Janela estreita (por exemplo ao lado do preview do OBS): barra de cima em duas linhas e formulários empilhados.
        void AtualizarLargura()
        {
            bool estreito = ActualWidth > 0 && ActualWidth < LarguraEstreita;
            if (estreito == _estreito) return;
            _estreito = estreito;
            Application.Current.Resources["LarguraRapidas"] = new GridLength(estreito ? 0 : 70);

            // O tempo ao vivo vai pra segunda linha da barra (à direita das chaves) pra não empurrar os botões pra fora.
            var pai = TxtAoVivo.Parent as Panel;
            if (pai != null) pai.Children.Remove(TxtAoVivo);
            if (estreito)
            {
                BarraTopo.Children.Add(TxtAoVivo);
                Grid.SetRow(TxtAoVivo, 1);
                Grid.SetColumn(TxtAoVivo, 3);
                TxtAoVivo.Margin = new Thickness(0, 8, 2, 0);
                TxtAoVivo.HorizontalAlignment = HorizontalAlignment.Right;
            }
            else
            {
                BarraTopo.Children.Remove(TxtAoVivo);
                GrupoDireita.Children.Insert(0, TxtAoVivo);
                TxtAoVivo.Margin = new Thickness(0, 0, 10, 0);
            }
            Grid.SetRow(GrupoPaineis, estreito ? 1 : 0);
            Grid.SetColumn(GrupoPaineis, estreito ? 0 : 2);
            Grid.SetColumnSpan(GrupoPaineis, estreito ? 3 : 1); // a coluna da direita fica pro tempo ao vivo
            GrupoPaineis.Margin = estreito ? new Thickness(0, 8, 0, 0) : new Thickness(18, 0, 0, 0);
            GrupoPlataformas.Margin = new Thickness(0);
            // Cabeçalho do chat: no layout estreito os botões de busca e de punições encolhem pro botão de foco caber.
            BtnBuscar.Width = estreito ? 24 : 28;
            BtnPunicoes.Padding = new Thickness(estreito ? 3 : 6, 0, estreito ? 3 : 6, 0);
            SetaChat.Margin = estreito ? new Thickness(10, 0, 6, 1) : new Thickness(14, 0, 8, 1);
            foreach (var aba in new[] { FiltroTudo, FiltroTwitch, FiltroKick, FiltroYouTube, FiltroAlertas, FiltroPerguntas })
                aba.Padding = new Thickness(estreito ? 6 : 9, 4, estreito ? 6 : 9, 4);

            if (estreito)
            {
                Pos(TxtTitulo, 0, 0, 5);
                Pos(BtnEmoji, 0, 0, 5);
                Pos(TxtCategoria, 1, 0, 5, 8);
                Pos(BtnAtualizar, 2, 0, 5, 8);
                Pos(TxtPergunta, 0, 0, 9);
                Pos(TxtOpcoes, 1, 0, 9, 8);
                Pos(SegInter, 2, 0, 1, 8);
                Pos(TxtDuracao, 2, 6, 1, 8);
                Pos(BtnIniciar, 2, 8, 1, 8);
            }
            else
            {
                Pos(TxtTitulo, 0, 0);
                Pos(BtnEmoji, 0, 0);
                Pos(TxtCategoria, 0, 2);
                Pos(BtnAtualizar, 0, 4);
                Pos(SegInter, 0, 0);
                Pos(TxtPergunta, 0, 2);
                Pos(TxtOpcoes, 0, 4);
                Pos(TxtDuracao, 0, 6);
                Pos(BtnIniciar, 0, 8);
            }
            AplicarLayout();
            AtualizarAoVivo();
        }

        // ---- Gaveta de configurações ----

        void Config_Click(object sender, RoutedEventArgs e) { AbrirGaveta(); }

        void AbrirGaveta()
        {
            if (Gaveta.Visibility == Visibility.Visible) return;
            AtualizarObs();
            CartaoGaveta.Width = Math.Min(390, Math.Max(280, ActualWidth - 24));
            Gaveta.Visibility = Visibility.Visible;
            var animacao = new DoubleAnimation(CartaoGaveta.Width, 0, TimeSpan.FromMilliseconds(190)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            DeslocaGaveta.BeginAnimation(TranslateTransform.XProperty, animacao);
        }

        void FecharGaveta_Click(object sender, RoutedEventArgs e) { FecharGaveta(); }

        void FecharGaveta()
        {
            if (Gaveta.Visibility != Visibility.Visible) return;
            var animacao = new DoubleAnimation(0, CartaoGaveta.Width, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            animacao.Completed += (s, e) => Gaveta.Visibility = Visibility.Collapsed;
            DeslocaGaveta.BeginAnimation(TranslateTransform.XProperty, animacao);
            GuardarCampos();
            // Endereço ou senha do OBS mudaram: reconecta com os novos.
            if (_cfg.ObsAtivo && (_cfg.ObsUrl != _obsUrlEmUso || _cfg.ObsSenha != _obsSenhaEmUso)) IniciarObs();
            // Endereço ou senha do Streamer.bot mudaram: reconecta com os novos.
            if (_cfg.StreamerbotUrl != _sbUrlEmUso || _cfg.StreamerbotSenha != _sbSenhaEmUso) IniciarStreamerbot();
        }

        void AplicarTema()
        {
            Tema.Aplicar(_cfg.Acento, _cfg.FundoTema);
            Tema.AplicarBarraEscura(this);
            if (_ajuda != null) Tema.AplicarBarraEscura(_ajuda);
            AtualizarMundo();
            GuardarCampos();
        }

        void Acento_Click(object sender, RoutedEventArgs e)
        {
            _cfg.Acento = (string)((FrameworkElement)sender).Tag;
            TxtCorHex.Text = _cfg.Acento;
            AvisoCor.Visibility = Visibility.Collapsed;
            AplicarTema();
        }

        void AplicarCor_Click(object sender, RoutedEventArgs e)
        {
            var hex = TxtCorHex.Text.Trim();
            if (!hex.StartsWith("#")) hex = "#" + hex;
            if (!Tema.HexValido(hex) || hex.Length != 7)
            {
                AvisoCor.Text = "Use o formato #RRGGBB (por exemplo #2F6FEB).";
                AvisoCor.Visibility = Visibility.Visible;
                return;
            }
            AvisoCor.Visibility = Visibility.Collapsed;
            _cfg.Acento = hex.ToUpperInvariant();
            TxtCorHex.Text = _cfg.Acento;
            AplicarTema();
        }

        void Fundo_Click(object sender, RoutedEventArgs e)
        {
            _cfg.FundoTema = (string)((FrameworkElement)sender).Tag;
            AplicarTema();
        }

        void Opcao_Changed(object sender, RoutedEventArgs e)
        {
            if (_iniciando) return;
            _cfg.MinimizarParaBandeja = OpMinimizar.IsChecked == true;
            _cfg.FecharParaBandeja = OpFechar.IsChecked == true;
            _cfg.IniciarEscondido = OpEscondido.IsChecked == true;
            _cfg.SempreNoTopo = OpTopo.IsChecked == true;
            _cfg.EmotesDeTerceiros = OpEmotes.IsChecked == true;
            _cfg.ControlesDeTransmissao = OpControles.IsChecked == true;
            AtualizarSaudeVisual();
            EmotesDeTerceiros.Ativo = _cfg.EmotesDeTerceiros;
            Topmost = _cfg.SempreNoTopo;
            GuardarCampos();
        }

        // ---- Ações direto na linha do chat ----

        static ItemChat ItemDaLinha(object sender)
        {
            var el = sender as FrameworkElement;
            return el == null ? null : el.DataContext as ItemChat;
        }

        async void RapidaBanir_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDaLinha(sender);
            if (it == null) return;
            if (MessageBox.Show(this, "Banir " + it.Nome + " (" + it.Plataforma + ")?", "Banir", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ModerarAsync("banir", it, 0, "banir");
        }

        // Silêncio padrão da linha: 5 minutos.
        async void RapidaTempo_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDaLinha(sender);
            if (it != null) await ModerarAsync("silenciar", it, 300, "silenciar");
        }

        async void RapidaApagar_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDaLinha(sender);
            if (it != null && it.PodeApagar) await ModerarAsync("apagar", it, 0, "apagar");
        }

        // Os três pontinhos abrem o mesmo menu do botão direito.
        void Mais_Click(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)FindResource("MenuDaMensagem");
            menu.PlacementTarget = (UIElement)sender;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ---- OBS ----

        void IniciarObs()
        {
            _obs?.Dispose();
            _obs = null;
            _obsAberto = false;
            _aoVivo = false;
            _cenas.Clear();
            LimparSaude();
            if (_cfg.ObsAtivo)
            {
                _obsUrlEmUso = _cfg.ObsUrl;
                _obsSenhaEmUso = _cfg.ObsSenha;
                var cliente = new ObsClient(_cfg.ObsUrl, _cfg.ObsSenha);
                _obs = cliente;
                cliente.ConexaoMudou += aberta => UI(async () =>
                {
                    if (_obs != cliente) return;
                    _obsAberto = aberta;
                    if (aberta)
                    {
                        await CarregarObsAsync();
                        await CarregarSaudeAsync();
                    }
                    else { _cenas.Clear(); _aoVivo = false; LimparSaude(); }
                    AtualizarObs();
                });
                cliente.Evento += (nome, dados) => UI(async () =>
                {
                    if (_obs != cliente) return;
                    if (nome == "CurrentProgramSceneChanged")
                    {
                        _cenaAtual = (string)dados["sceneName"] ?? "";
                        MarcarCenaAtual();
                        AtualizarObs();
                    }
                    else if (nome == "SceneListChanged" || nome == "StreamStateChanged" || (nome == "RecordStateChanged" && GravacaoMonitorada))
                    {
                        await CarregarObsAsync();
                    }
                });
            }
            AtualizarObs();
        }

        async Task CarregarObsAsync()
        {
            var obs = _obs;
            if (obs == null || !_obsAberto) return;
            var lista = await obs.PedirAsync("GetSceneList");
            if (lista != null)
            {
                _cenaAtual = (string)lista["currentProgramSceneName"] ?? "";
                var nomes = ((lista["scenes"] as JArray) ?? new JArray()).Select(c => (string)c["sceneName"]).Reverse().ToList();
                // Só refaz os botões quando a lista ou a cena no ar mudou (a consulta roda a cada 5 s): refazê-los à toa
                // zera o hover e pode perder um clique no meio do caminho.
                var igual = _cenas.Count == nomes.Count && _cenas.Select((c, i) => c.Nome == nomes[i] && c.Atual == (nomes[i] == _cenaAtual)).All(x => x);
                if (!igual)
                {
                    _cenas.Clear();
                    foreach (var n in nomes) _cenas.Add(new ItemCena { Nome = n, Atual = n == _cenaAtual });
                }
            }
            var estado = await obs.PedirAsync("GetStreamStatus");
            if (estado != null)
            {
                _aoVivo = (bool?)estado["outputActive"] == true;
                _inicioDaLive = DateTime.UtcNow - TimeSpan.FromMilliseconds((double?)estado["outputDuration"] ?? 0);
            }
            if (GravacaoMonitorada) await CarregarGravacaoAsync();
            AtualizarObs();
        }

        void MarcarCenaAtual()
        {
            var nomes = _cenas.Select(c => c.Nome).ToList();
            _cenas.Clear();
            foreach (var n in nomes) _cenas.Add(new ItemCena { Nome = n, Atual = n == _cenaAtual });
        }

        void AtualizarObs()
        {
            BtnObs.Content = _cfg.ObsAtivo ? "Desconectar" : "Conectar";
            EstadoObs.Text = _obsAberto ? "Conectado" : _cfg.ObsAtivo ? "Conectando…" : "Desconectado";
            ListaCenas.Visibility = _obsAberto ? Visibility.Visible : Visibility.Collapsed;
            ObsVazio.Visibility = _obsAberto ? Visibility.Collapsed : Visibility.Visible;
            BtnConfigObs.Visibility = _obsAberto ? Visibility.Collapsed : Visibility.Visible;
            ObsVazio.Text = _cfg.ObsAtivo
                ? "Conectando ao OBS… Confira se o servidor WebSocket está ligado (Ferramentas > Configurações do servidor WebSocket)."
                : "OBS desconectado.";
            AtualizarConexoes();
            AtualizarAoVivo();
            AtualizarSaudeVisual();
        }

        void Obs_Click(object sender, RoutedEventArgs e)
        {
            _cfg.ObsAtivo = !_cfg.ObsAtivo;
            GuardarCampos();
            IniciarObs();
        }

        async void Cena_Click(object sender, RoutedEventArgs e)
        {
            var nome = (string)((FrameworkElement)sender).Tag;
            if (_obs != null) await _obs.PedirAsync("SetCurrentProgramScene", new JObject { ["sceneName"] = nome });
        }
    }
}
