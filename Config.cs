using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace LiveHelmApp
{
    // Formato antigo dos atalhos (título e categoria); só é lido pra migrar pro formato de categorias.
    public sealed class Predefinicao
    {
        public string Nome { get; set; }
        public string Titulo { get; set; }
        public string Categoria { get; set; }
    }

    // Pergunta do chat marcada pra responder depois (a fila de perguntas sobrevive ao fechar o painel).
    public sealed class PerguntaSalva
    {
        public string Plataforma { get; set; } = "";
        public string Nome { get; set; } = "";
        public string Texto { get; set; } = "";
        public string Hora { get; set; } = "";
        public bool Respondida { get; set; }
    }

    // Enquete ou previsão salva pra iniciar com um clique.
    public sealed class Interacao
    {
        public string Tipo { get; set; } = "enquete"; // enquete ou previsao
        public string Pergunta { get; set; } = "";
        public string Opcoes { get; set; } = "";      // separadas por |
        public int Duracao { get; set; } = 60;        // segundos
        [JsonIgnore]
        public string Rotulo { get { return (Tipo == "previsao" ? "Previsão · " : "Enquete · ") + Pergunta; } }
    }

    // O que o painel lembra entre as aberturas: último título e categoria, plataformas marcadas, atalhos de categoria,
    // dados do OBS, painéis recolhidos e posição da janela. Fica em %APPDATA%\LiveHelm-App\config.json.
    public sealed class Config
    {
        public const string ObsUrlPadrao = "ws://127.0.0.1:4455";
        public const string ObsSenhaPadrao = "";
        public const string StreamerbotUrlPadrao = "ws://127.0.0.1:8080";
        public const string StreamerbotSenhaPadrao = "";
        const int MaxHistorico = 60;

        public string Titulo { get; set; } = "";
        public string Categoria { get; set; } = "";
        public bool Twitch { get; set; } = true;
        public bool Kick { get; set; } = true;
        public bool YouTube { get; set; } = true;
        public List<string> Atalhos { get; set; } = new List<string>();      // categorias fixadas pelo usuário
        public List<string> Historico { get; set; } = new List<string>();    // nomes que as plataformas confirmaram (sugestões)
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<Predefinicao> Predefinicoes { get; set; }
        public string StreamerbotUrl { get; set; } = StreamerbotUrlPadrao;
        public string StreamerbotSenha { get; set; } = StreamerbotSenhaPadrao;
        public string ObsUrl { get; set; } = ObsUrlPadrao;
        public string ObsSenha { get; set; } = ObsSenhaPadrao;
        public bool ObsAtivo { get; set; } = true;
        public bool ConexoesFixas { get; set; }
        public string Acento { get; set; } = "#2F6FEB";      // cor de destaque do app
        public string FundoTema { get; set; } = "Grafite";   // nome do conjunto de cores de fundo
        public bool LiveVisivel { get; set; } = true;
        public bool ObsVisivel { get; set; } = true;
        public bool InteracaoVisivel { get; set; } = true;
        public bool ChatVisivel { get; set; } = true;
        public bool SaudeVisivel { get; set; }         // painéis Saúde e Momento: começam desligados, as chaves ficam na barra de cima
        public bool SaudeRecolhido { get; set; }
        public bool MomentoVisivel { get; set; }
        public bool MomentoRecolhido { get; set; }
        public bool ControlesDeTransmissao { get; set; } // botões de iniciar e encerrar a live e a gravação: desligados até ligar nas configurações
        public List<PerguntaSalva> Perguntas { get; set; } = new List<PerguntaSalva>();
        public bool MinimizarParaBandeja { get; set; } = true; // minimizar esconde da barra de tarefas e deixa o ícone na bandeja
        public bool FecharParaBandeja { get; set; }            // o X esconde na bandeja em vez de fechar
        public bool IniciarEscondido { get; set; }
        public bool SempreNoTopo { get; set; }
        public bool EmotesDeTerceiros { get; set; } = true; // 7TV, BetterTTV e FrankerFaceZ no chat
        public bool InteracoesPadraoAplicadas { get; set; } // os atalhos de exemplo são criados uma vez só
        public bool InteracaoRecolhido { get; set; } = true; // começa recolhido pra dar espaço ao chat
        public List<Interacao> Interacoes { get; set; } = new List<Interacao>();
        public bool LiveRecolhido { get; set; }
        public bool ObsRecolhido { get; set; }
        public bool ChatRecolhido { get; set; }
        public string FiltroChat { get; set; } = "tudo"; // tudo, chat ou alertas
        public bool ChatTwitch { get; set; } = true;
        public bool ChatKick { get; set; } = true;
        public bool ChatYouTube { get; set; } = true;
        public double? Esquerda { get; set; }
        public double? Topo { get; set; }
        public double? Largura { get; set; }
        public double? Altura { get; set; }
        public bool Maximizada { get; set; }

        public static string CaminhoPadrao() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveHelm-App", "config.json");

        public static Config Carregar(string caminho)
        {
            try
            {
                if (File.Exists(caminho))
                {
                    var lida = JsonConvert.DeserializeObject<Config>(File.ReadAllText(caminho));
                    if (lida != null)
                    {
                        if (lida.Atalhos == null) lida.Atalhos = new List<string>();
                        if (lida.Historico == null) lida.Historico = new List<string>();
                        if (lida.Interacoes == null) lida.Interacoes = new List<Interacao>();
                        if (lida.Perguntas == null) lida.Perguntas = new List<PerguntaSalva>();
                        if (string.IsNullOrWhiteSpace(lida.StreamerbotUrl)) lida.StreamerbotUrl = StreamerbotUrlPadrao;
                        lida.AplicarPadroes();
                        if (lida.Predefinicoes != null)
                        {
                            foreach (var c in lida.Predefinicoes.Select(p => (p.Categoria ?? "").Trim()).Where(c => c != ""))
                                if (!lida.Atalhos.Contains(c)) lida.Atalhos.Add(c);
                            lida.Predefinicoes = null;
                        }
                        if (string.IsNullOrWhiteSpace(lida.ObsUrl)) lida.ObsUrl = ObsUrlPadrao;
                        return lida;
                    }
                }
            }
            catch (Exception) { /* arquivo corrompido: começa do zero */ }
            var nova = new Config();
            nova.AplicarPadroes();
            return nova;
        }

        // Atalhos de enquete e previsão de exemplo (só na primeira vez, e só se você ainda não salvou nenhum).
        void AplicarPadroes()
        {
            if (InteracoesPadraoAplicadas) return;
            InteracoesPadraoAplicadas = true;
            if (Interacoes.Count > 0) return;
            Interacoes.Add(new Interacao { Tipo = "previsao", Pergunta = "Passa no teste?", Opcoes = "Sim | Não", Duracao = 120 });
            Interacoes.Add(new Interacao { Tipo = "previsao", Pergunta = "O ataque acerta?", Opcoes = "Acerta | Erra", Duracao = 90 });
            Interacoes.Add(new Interacao { Tipo = "previsao", Pergunta = "Quem vence o combate?", Opcoes = "Heróis | Monstros", Duracao = 180 });
            Interacoes.Add(new Interacao { Tipo = "previsao", Pergunta = "Próxima rolagem do mestre?", Opcoes = "Nat 20 | Nat 1 | Nenhum", Duracao = 60 });
            Interacoes.Add(new Interacao { Tipo = "enquete", Pergunta = "O que o grupo faz agora?", Opcoes = "Atacar | Negociar | Fugir", Duracao = 60 });
            Interacoes.Add(new Interacao { Tipo = "enquete", Pergunta = "Hora de uma pausa?", Opcoes = "Sim | Não", Duracao = 60 });
        }

        // Guarda o nome no começo do histórico (sem repetir, comparando sem diferenciar maiúsculas).
        public void LembrarCategoria(string nome)
        {
            nome = (nome ?? "").Trim();
            if (nome == "") return;
            Historico.RemoveAll(h => string.Equals(h, nome, StringComparison.OrdinalIgnoreCase));
            Historico.Insert(0, nome);
            if (Historico.Count > MaxHistorico) Historico.RemoveRange(MaxHistorico, Historico.Count - MaxHistorico);
        }

        public void Salvar(string caminho)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(caminho));
                File.WriteAllText(caminho, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception) { /* sem permissão de escrita: só não lembra */ }
        }
    }
}
