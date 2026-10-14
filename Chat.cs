using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Pedaço do texto de uma mensagem: texto puro ou um emote (Imagem preenchida, https).
    public sealed class ParteMsg
    {
        public string Texto;
        public string Imagem;
    }

    // Uma linha do feed unificado: mensagem de chat ou alerta (seguidor, sub, banimento...).
    public sealed class ItemChat : INotifyPropertyChanged
    {
        public string Id = "";
        public string UsuarioId = "";
        public string Plataforma = "";
        public string Hora = "";
        public string Nome = "";
        public string Login = "";
        public string CorNome = "#CFCBE0";
        public List<ParteMsg> Partes = new List<ParteMsg>();
        public List<string> Insignias = new List<string>();
        public bool EhAlerta { get; set; }
        public string Rotulo = "";
        public string Detalhe = "";

        bool _apagada;
        bool _primeira;
        public bool Apagada { get { return _apagada; } set { if (_apagada != value) { _apagada = value; Avisar("Apagada"); } } }
        public bool Primeira { get { return _primeira; } set { if (_primeira != value) { _primeira = value; Avisar("Primeira"); } } }

        // Pede para a linha ser montada de novo (por exemplo quando os emotes de terceiros acabam de carregar).
        public void Redesenhar() { Avisar("Texto"); }

        public string CorPlataforma { get { return Alertas.CorDaPlataforma(Plataforma); } }
        public bool PodeApagar { get { return Plataforma != "youtube"; } } // o YouTube não permite apagar mensagem
        public string Texto { get { return string.Concat(Partes.Select(p => p.Texto)); } }

        public event PropertyChangedEventHandler PropertyChanged;
        void Avisar(string nome) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome)); }
    }

    public enum TipoEv { Chat, Alerta, Apagar, ApagarUsuario, Limpar, Primeira, Atualizacao }

    // O que um evento do Streamer.bot significa pro painel.
    public sealed class EventoSb
    {
        public TipoEv Tipo;
        public ItemChat Item;
        public string Id = "";
        public string Nome = "";
        public string Plataforma = "";
        public string Categoria = "";
        public string Titulo = "";
        public string TipoOriginal = ""; // tipo do evento do Streamer.bot (PollCreated, PredictionLocked...)
        public int Segundos;             // duração do silêncio, nos avisos de silenciado (0 = banimento ou não informada)
        public string Login = "";        // login e id do usuário punido, quando o aviso traz
        public string UsuarioId = "";
        public JObject Dados;            // dados brutos, só nos eventos de enquete e previsão
    }

    // Converte a mensagem do WebSocket do Streamer.bot em EventoSb. Formatos de ChatMessage vistos em eventos reais
    // (Twitch e Kick); os demais seguem a documentação e são lidos de forma defensiva.
    static class Eventos
    {
        static string Plataforma(string fonte)
        {
            var f = (fonte ?? "").ToLowerInvariant();
            return f == "twitch" || f == "kick" || f == "youtube" ? f : null;
        }

        static string Hora() { return DateTime.Now.ToString("HH:mm"); }

        // Cores de nome muito escuras somem no fundo escuro: clareia até ficarem legíveis.
        public static string CorLegivel(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return "#CFCBE0"; // sem cor: o mesmo resultado do catch, sem a exceção
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
                if (lum < 0.5)
                {
                    double k = (0.5 - lum) * 1.4 + 0.15;
                    c = Color.FromRgb((byte)(c.R + (255 - c.R) * k), (byte)(c.G + (255 - c.G) * k), (byte)(c.B + (255 - c.B) * k));
                }
                return c.ToString().Replace("#FF", "#");
            }
            catch (Exception) { return "#CFCBE0"; }
        }

        static List<ParteMsg> PartesDe(JObject d)
        {
            var lista = new List<ParteMsg>();
            var partes = d["parts"] as JArray;
            if (partes != null && partes.Count > 0)
            {
                foreach (var p in partes.OfType<JObject>())
                {
                    var imagem = Alertas.Texto(Alertas.Ou(p["imageUrl"], p["url"], p["emoteUrl"]));
                    var texto = Alertas.Texto(p["text"]);
                    if ((string)p["type"] == "emote" && imagem.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        lista.Add(new ParteMsg { Texto = texto, Imagem = imagem });
                    else lista.Add(new ParteMsg { Texto = texto });
                }
            }
            if (lista.Count == 0) lista.Add(new ParteMsg { Texto = Alertas.Texto(Alertas.Ou(d["text"], d["message"])) });
            return lista;
        }

        static JObject UsuarioDe(JObject d)
        {
            return Alertas.Ou(d["user"], d["targetUser"], d["raider"], d["gifter"], d["author"]) as JObject ?? new JObject();
        }

        public static EventoSb Converter(JObject msg)
        {
            var origem = msg["event"] as JObject;
            if (origem == null) return null;
            var fonte = (string)origem["source"] ?? "";
            var tipo = (string)origem["type"] ?? "";
            var plat = Plataforma(fonte);
            if (plat == null) return null;
            var d = msg["data"] as JObject ?? new JObject();
            var usuario = UsuarioDe(d);

            if (tipo == "ChatMessage" || (plat == "youtube" && tipo == "Message"))
            {
                var nome = Alertas.Texto(Alertas.Ou(usuario["name"], usuario["login"]));
                if (nome == "") return null;
                var item = new ItemChat
                {
                    Id = Alertas.Texto(Alertas.Ou(d["messageId"], d["id"])),
                    UsuarioId = Alertas.Texto(usuario["id"]),
                    Plataforma = plat,
                    Hora = Hora(),
                    Nome = nome,
                    Login = Alertas.Texto(usuario["login"]),
                    CorNome = CorLegivel(Alertas.Texto(usuario["color"])),
                    Partes = PartesDe(d),
                };
                foreach (var b in (usuario["badges"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    var url = Alertas.Texto(b["imageUrl"]);
                    if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) item.Insignias.Add(url);
                }
                return new EventoSb { Tipo = TipoEv.Chat, Item = item, Plataforma = plat };
            }

            if (tipo == "FirstWord" || tipo == "FirstWords")
            {
                var id = Alertas.Texto(Alertas.Ou(d["messageId"], d["id"]));
                return id == "" ? null : new EventoSb { Tipo = TipoEv.Primeira, Id = id, Plataforma = plat };
            }

            if (tipo == "ChatMessageDeleted" || tipo == "MessageDeleted")
            {
                var id = Alertas.Texto(Alertas.Ou(d["messageId"], d["targetMessageId"], d["id"]));
                if (id != "") return new EventoSb { Tipo = TipoEv.Apagar, Id = id, Plataforma = plat };
                return new EventoSb { Tipo = TipoEv.ApagarUsuario, Nome = Alertas.Texto(Alertas.Ou(usuario["name"], usuario["login"])), Plataforma = plat };
            }

            if (tipo == "UserBanned" || tipo == "UserTimedOut" || tipo == "UserTimedout")
            {
                var alvo = Alertas.Ou(d["user"], d["targetUser"], d["bannedUser"], d["timedoutUser"]) as JObject ?? new JObject();
                var nome = Alertas.Texto(Alertas.Ou(alvo["name"], alvo["login"]));
                if (nome == "") return null;
                var banido = tipo == "UserBanned";
                var segundos = Alertas.Numero(d["duration"], d["timeoutDuration"], d["seconds"]);
                var item = new ItemChat
                {
                    Plataforma = plat, Hora = Hora(), Nome = nome, EhAlerta = true,
                    Rotulo = banido ? "Banido" : "Silenciado",
                    Detalhe = !banido && segundos != null ? Duracao((int)segundos.Value) : "",
                };
                return new EventoSb
                {
                    Tipo = TipoEv.ApagarUsuario, Nome = nome, Plataforma = plat, Item = item, Segundos = !banido && segundos != null ? (int)segundos.Value : 0,
                    Login = Alertas.Texto(alvo["login"]), UsuarioId = Alertas.Texto(alvo["id"]),
                };
            }

            if (tipo == "ChatCleared") return new EventoSb { Tipo = TipoEv.Limpar, Plataforma = plat };

            if ((plat == "twitch" && tipo == "StreamUpdate") || (plat == "kick" && tipo == "ChannelUpdate"))
            {
                var categoria = plat == "twitch"
                    ? Alertas.Texto(Alertas.Campo(d["game"], "name"))
                    : Alertas.Texto(d["categoryName"]);
                var titulo = Alertas.Texto(plat == "twitch" ? d["status"] : d["title"]);
                return new EventoSb { Tipo = TipoEv.Atualizacao, Plataforma = plat, Categoria = categoria, Titulo = titulo };
            }

            var linha = Alertas.Converter(msg);
            if (linha == null) return null;
            var interacao = plat == "twitch" && (tipo.StartsWith("Poll") || tipo.StartsWith("Prediction"));
            return new EventoSb
            {
                Tipo = TipoEv.Alerta,
                Plataforma = plat,
                TipoOriginal = interacao ? tipo : "",
                Dados = interacao ? d : null,
                Item = new ItemChat { Plataforma = plat, Hora = Hora(), EhAlerta = true, Rotulo = linha.Rotulo, Nome = linha.Nome, Detalhe = linha.Detalhe },
            };
        }

        static string Duracao(int s)
        {
            if (s >= 3600) return (s / 3600) + " h";
            if (s >= 60) return (s / 60) + " min";
            return s + " s";
        }
    }

    // Monta o texto de cada linha do chat como um TextBlock único (hora, ícone da plataforma, insígnias, nome e mensagem com
    // emotes), de modo que a linha quebra naturalmente como num chat de verdade.
    public static class ChatTexto
    {
        public static readonly DependencyProperty ItemProperty = DependencyProperty.RegisterAttached(
            "Item", typeof(ItemChat), typeof(ChatTexto), new PropertyMetadata(null, AoMudarItem));

        public static ItemChat GetItem(DependencyObject o) { return (ItemChat)o.GetValue(ItemProperty); }
        public static void SetItem(DependencyObject o, ItemChat v) { o.SetValue(ItemProperty, v); }

        static readonly Dictionary<string, BitmapImage> Imagens = new Dictionary<string, BitmapImage>();
        static readonly HashSet<string> ImagensComFalha = new HashSet<string>();
        static readonly Dictionary<string, Brush> Pinceis = new Dictionary<string, Brush>();
        static readonly System.Text.RegularExpressions.Regex Espacos = new System.Text.RegularExpressions.Regex(@"(\s+)");

        static Brush Pincel(string cor)
        {
            Brush b;
            if (!Pinceis.TryGetValue(cor, out b))
            {
                try { b = (Brush)new BrushConverter().ConvertFromString(cor); } catch (Exception) { b = Brushes.White; }
                b.Freeze();
                Pinceis[cor] = b;
            }
            return b;
        }

        static void AoMudarItem(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var tb = o as TextBlock;
            if (tb == null) return;
            var antigo = e.OldValue as ItemChat;
            var novo = e.NewValue as ItemChat;
            if (antigo != null) antigo.PropertyChanged -= (PropertyChangedEventHandler)tb.Tag;
            if (novo == null) { tb.Inlines.Clear(); return; }
            // "Primeira" só alimenta um DataTrigger do XAML: Montar não a lê, então a linha não precisa ser refeita.
            PropertyChangedEventHandler h = (s, a) => { if (a.PropertyName != "Primeira") tb.Dispatcher.BeginInvoke(new Action(() => Montar(tb, novo))); };
            tb.Tag = h;
            novo.PropertyChanged += h;
            Montar(tb, novo);
        }

        static Inline IconeDaPlataforma(string plataforma, string cor)
        {
            var chave = plataforma == "twitch" ? "IconeTwitch" : plataforma == "kick" ? "IconeKick" : "IconeYouTube";
            var geo = Application.Current.TryFindResource(chave) as Geometry;
            var caminho = new Path { Data = geo, Fill = Pincel(cor), Stretch = Stretch.Uniform, Width = 12, Height = 12 };
            return new InlineUIContainer(caminho) { BaselineAlignment = BaselineAlignment.Center };
        }

        static Inline Imagem(string url, string alternativo, double altura)
        {
            var grade = new Grid { Height = altura, Margin = new Thickness(1, 0, 1, 0) };
            var alt = new TextBlock { Text = alternativo, Foreground = Pincel("#8E8BA2"), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            grade.Children.Add(alt);
            if (ImagensComFalha.Contains(url)) alt.Visibility = Visibility.Visible;
            else
            {
                BitmapImage bmp;
                if (!Imagens.TryGetValue(url, out bmp))
                {
                    bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(url);
                    bmp.DecodePixelHeight = (int)(altura * 2);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    // Congela quando o download termina (um handler por endereço, não por uso): imagem congelada é mais barata pro WPF.
                    if (bmp.IsDownloading) bmp.DownloadCompleted += (s, a) => { if (bmp.CanFreeze) bmp.Freeze(); };
                    else if (bmp.CanFreeze) bmp.Freeze();
                    Imagens[url] = bmp;
                }
                var img = new Image { Source = bmp, Height = altura, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                grade.Children.Insert(0, img);
                // Formatos que o WPF não decodifica (como WebP) viram o texto do emote. O aviso vem da própria Image, e não do
                // BitmapImage em cache: um handler pendurado nele a cada uso nunca soltaria a linha do chat e deixaria cada
                // novo uso mais lento.
                img.ImageFailed += (s, a) => { ImagensComFalha.Add(url); img.Visibility = Visibility.Collapsed; alt.Visibility = Visibility.Visible; };
            }
            return new InlineUIContainer(grade) { BaselineAlignment = BaselineAlignment.Center };
        }

        // Texto comum, com as palavras que são emotes de terceiros (7TV, BetterTTV, FrankerFaceZ) trocadas pela imagem.
        static void AdicionarTexto(TextBlock tb, string texto, string plataforma, Brush cor)
        {
            if (!EmotesDeTerceiros.Ativo || string.IsNullOrEmpty(texto))
            {
                tb.Inlines.Add(new Run(texto) { Foreground = cor });
                return;
            }
            var pendente = new System.Text.StringBuilder();
            Action despejar = () =>
            {
                if (pendente.Length == 0) return;
                tb.Inlines.Add(new Run(pendente.ToString()) { Foreground = cor });
                pendente.Clear();
            };
            foreach (var pedaco in Espacos.Split(texto))
            {
                var url = pedaco.Length == 0 || char.IsWhiteSpace(pedaco[0]) ? null : EmotesDeTerceiros.Resolver(plataforma, pedaco);
                if (url == null) { pendente.Append(pedaco); continue; }
                despejar();
                tb.Inlines.Add(Imagem(url, pedaco, 22));
            }
            despejar();
        }

        static void Montar(TextBlock tb, ItemChat it)
        {
            tb.Inlines.Clear();
            var suave = Pincel("#8E8BA2");
            var texto = Pincel("#E8E6F0");
            tb.Inlines.Add(new Run(it.Hora + "  ") { Foreground = suave, FontSize = 11.5 });
            tb.Inlines.Add(IconeDaPlataforma(it.Plataforma, it.CorPlataforma));
            tb.Inlines.Add(new Run("  "));

            if (it.EhAlerta)
            {
                if (it.Rotulo != "") tb.Inlines.Add(new Run(it.Rotulo + "  ") { Foreground = suave });
                tb.Inlines.Add(new Run(it.Nome) { FontWeight = FontWeights.Bold, Foreground = texto });
                if (it.Detalhe != "") tb.Inlines.Add(new Run("  " + it.Detalhe) { Foreground = suave });
                return;
            }

            foreach (var url in it.Insignias)
            {
                tb.Inlines.Add(Imagem(url, "", 16));
                tb.Inlines.Add(new Run(" "));
            }
            var nome = new Run(it.Nome) { FontWeight = FontWeights.Bold, Foreground = Pincel(it.CorNome) };
            tb.Inlines.Add(nome);
            tb.Inlines.Add(new Run(": ") { Foreground = suave });
            foreach (var p in it.Partes)
            {
                if (p.Imagem != null) tb.Inlines.Add(Imagem(p.Imagem, p.Texto, 22));
                else AdicionarTexto(tb, p.Texto, it.Plataforma, it.Apagada ? suave : texto);
            }
            if (it.Apagada)
            {
                nome.TextDecorations = TextDecorations.Strikethrough;
                foreach (var r in tb.Inlines.OfType<Run>()) r.TextDecorations = TextDecorations.Strikethrough;
            }
        }
    }

    // string -> pincel (cores das plataformas e das bolinhas, vindas como "#RRGGBB").
    public sealed class TextoParaPincel : System.Windows.Data.IValueConverter
    {
        static readonly Dictionary<string, Brush> Cache = new Dictionary<string, Brush>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var cor = (string)value;
                Brush b;
                if (Cache.TryGetValue(cor, out b)) return b;
                b = (Brush)new BrushConverter().ConvertFromString(cor);
                if (b.CanFreeze) b.Freeze();
                Cache[cor] = b;
                return b;
            }
            catch (Exception) { return Brushes.Gray; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { throw new NotSupportedException(); }
    }
}
