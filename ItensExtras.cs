using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace LiveHelmApp
{
    // Base dos itens das listas da Saúde e do chat que mudam enquanto aparecem na tela.
    public abstract class Notificavel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void Avisar(string nome) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nome)); }
    }

    // Uma medida da saúde da transmissão (CPU, FPS...). Cor vem como "#RRGGBB".
    public sealed class ItemSaude : Notificavel
    {
        string _valor = "", _cor = "#8E8BA2", _dica = "";
        public string Nome { get; set; }
        public string Valor { get { return _valor; } set { if (_valor != value) { _valor = value; Avisar("Valor"); } } }
        public string Cor { get { return _cor; } set { if (_cor != value) { _cor = value; Avisar("Cor"); } } }
        public string Dica { get { return _dica; } set { if (_dica != value) { _dica = value; Avisar("Dica"); } } }
    }

    // Marcas de plataforma compartilhadas pelas listas novas do chat.
    static class MarcaPlataforma
    {
        public static Geometry Icone(string plataforma)
        {
            var chave = plataforma == "twitch" ? "IconeTwitch" : plataforma == "kick" ? "IconeKick" : "IconeYouTube";
            return Application.Current.TryFindResource(chave) as Geometry;
        }
    }

    // Mensagem fixada no alto do chat (vale só na sessão).
    public sealed class ItemFixada
    {
        public string Plataforma { get; set; }
        public string Nome { get; set; }
        public string Texto { get; set; }
    }

    // Pergunta marcada pra responder depois: fica salva no config.json.
    public sealed class ItemPergunta : Notificavel
    {
        bool _respondida;
        public string Plataforma { get; set; }
        public string Nome { get; set; }
        public string Texto { get; set; }
        public string Hora { get; set; }
        public Geometry IconePlataforma { get { return MarcaPlataforma.Icone(Plataforma); } }
        public string CorPlataforma { get { return Alertas.CorDaPlataforma(Plataforma); } }
        public bool Respondida
        {
            get { return _respondida; }
            set { if (_respondida != value) { _respondida = value; Avisar("Respondida"); Avisar("TextoRespondida"); } }
        }
        public string TextoRespondida { get { return _respondida ? "Reabrir" : "Respondida"; } }
    }

    // Silêncio ou banimento da sessão, com o que for preciso pra desfazer.
    public sealed class ItemPunicao : Notificavel
    {
        public string Plataforma { get; set; }
        public string Nome { get; set; }
        public string Login { get; set; } = "";
        public string UsuarioId { get; set; } = "";
        public bool Banido { get; set; }
        public int Segundos { get; set; }   // duração do silêncio (0 = não informada)
        public DateTime Quando { get; set; }
        DateTime? _pedido;                  // quando o desbloqueio foi pedido ao Streamer.bot
        public DateTime? Pedido { get { return _pedido; } set { _pedido = value; Atualizar(); } }

        public Geometry IconePlataforma { get { return MarcaPlataforma.Icone(Plataforma); } }
        public string CorPlataforma { get { return Alertas.CorDaPlataforma(Plataforma); } }
        public bool Expirado { get { return !Banido && Segundos > 0 && DateTime.Now > Quando.AddSeconds(Segundos); } }
        public bool PodeReverter { get { return Plataforma != "youtube" && !Expirado; } }
        public string TextoBotao { get { return Pedido != null ? "Tentar de novo" : Banido ? "Desbanir" : "Liberar"; } }

        public string Descricao
        {
            get
            {
                var hora = Quando.ToString("HH:mm");
                var texto = Banido ? "Banido às " + hora
                    : "Silenciado" + (Segundos > 0 ? " por " + Duracao(Segundos) : "") + " às " + hora + (Expirado ? " (já acabou)" : "");
                if (Pedido != null) texto += " · desbloqueio pedido às " + Pedido.Value.ToString("HH:mm");
                return texto;
            }
        }

        public string DicaBotao
        {
            get
            {
                if (Plataforma == "youtube") return "O Streamer.bot não tem como desfazer no YouTube: faça pelo YouTube Studio.";
                if (Expirado) return "O silêncio já acabou.";
                return "Pede ao Streamer.bot para remover " + (Banido ? "o banimento." : "o silêncio.");
            }
        }

        public void Atualizar()
        {
            Avisar("Descricao");
            Avisar("TextoBotao");
            Avisar("PodeReverter");
            Avisar("DicaBotao");
        }

        static string Duracao(int s)
        {
            if (s >= 3600) return (s / 3600) + " h";
            if (s >= 60) return (s / 60) + " min";
            return s + " s";
        }
    }

    // Arquivo de momentos marcados durante a live: um .txt por dia em Documentos\LiveHelm-App.
    static class Momentos
    {
        public static string Caminho(DateTime dia)
        {
            var pasta = App.PastaDeMomentos ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LiveHelm-App");
            return Path.Combine(pasta, "Momentos " + dia.ToString("yyyy-MM-dd") + ".txt");
        }

        // Acrescenta a linha ao arquivo do dia (com um cabeçalho na primeira vez) e devolve o caminho. Lança exceção se não der pra gravar.
        public static string Registrar(string linha, DateTime agora)
        {
            var caminho = Caminho(agora);
            Directory.CreateDirectory(Path.GetDirectoryName(caminho));
            var novo = !File.Exists(caminho);
            // UTF-8 com BOM: o Bloco de Notas antigo e o Excel abrem os acentos certos.
            using (var escritor = new StreamWriter(caminho, true, new UTF8Encoding(true)))
            {
                if (novo)
                {
                    escritor.WriteLine("Momentos da live - " + agora.ToString("dd/MM/yyyy"));
                    escritor.WriteLine();
                }
                escritor.WriteLine(linha);
            }
            return caminho;
        }
    }
}
