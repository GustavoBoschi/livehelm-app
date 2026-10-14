using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Emotes de terceiros (7TV, BetterTTV e FrankerFaceZ): o Streamer.bot só manda como emote o que a plataforma conhece, então
    // aqui se baixam os conjuntos globais e os do seu canal e as palavras do chat que batem com o nome de um emote viram imagem.
    // As imagens são PNG ou GIF (o WPF não abre WebP, que é o formato padrão do 7TV).
    static class EmotesDeTerceiros
    {
        public static bool Ativo = true;
        public static event Action Carregado; // os conjuntos terminaram de baixar: mensagens já na tela podem ser redesenhadas

        static readonly object Trava = new object();
        static Dictionary<string, string> _globais = new Dictionary<string, string>(StringComparer.Ordinal);
        static Dictionary<string, Dictionary<string, string>> _porPlataforma = new Dictionary<string, Dictionary<string, string>>();
        static readonly HttpClient Http = CriarCliente();
        static string _chaveCarregada = "";
        static bool _carregando;

        static HttpClient CriarCliente()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Sem a descompressão automática o .NET Framework não pede gzip, e os JSON do 7TV passam de 1 MB.
            var c = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate }) { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("LiveHelmApp/1.0");
            return c;
        }

        // Endereço da imagem do emote que tem esse nome nessa plataforma (o do canal vale mais que o global), ou null.
        public static string Resolver(string plataforma, string palavra)
        {
            if (!Ativo) return null;
            lock (Trava)
            {
                Dictionary<string, string> canal;
                string url;
                if (_porPlataforma.TryGetValue(plataforma, out canal) && canal.TryGetValue(palavra, out url)) return url;
                return _globais.TryGetValue(palavra, out url) ? url : null;
            }
        }

        // Baixa os conjuntos uma vez por combinação de canais; falhas de rede só deixam o chat sem esses emotes.
        public static async Task CarregarAsync(string twitchId, string kickId, string youtubeId)
        {
            var chave = twitchId + "|" + kickId + "|" + youtubeId;
            if (_carregando || chave == _chaveCarregada) return;
            _carregando = true;
            try
            {
                var globais = new Dictionary<string, string>(StringComparer.Ordinal);
                var porPlataforma = new Dictionary<string, Dictionary<string, string>>();

                // Os downloads saem todos juntos; as respostas são aplicadas na mesma ordem de sempre (quem vem depois sobrescreve).
                var tBttvGlobal = Pegar("https://api.betterttv.net/3/cached/emotes/global");
                var tFfzGlobal = Pegar("https://api.frankerfacez.com/v1/set/global");
                var tSeteGlobal = Pegar("https://7tv.io/v3/emote-sets/global");
                bool comTwitch = !string.IsNullOrEmpty(twitchId), comKick = !string.IsNullOrEmpty(kickId), comYouTube = !string.IsNullOrEmpty(youtubeId);
                var tBttvTwitch = comTwitch ? Pegar("https://api.betterttv.net/3/cached/users/twitch/" + twitchId) : null;
                var tFfzTwitch = comTwitch ? Pegar("https://api.frankerfacez.com/v1/room/id/" + twitchId) : null;
                var tSeteTwitch = comTwitch ? Pegar("https://7tv.io/v3/users/twitch/" + twitchId) : null;
                var tSeteKick = comKick ? Pegar("https://7tv.io/v3/users/kick/" + kickId) : null;
                var tSeteYouTube = comYouTube ? Pegar("https://7tv.io/v3/users/youtube/" + youtubeId) : null;

                AdicionarBttv(globais, await tBttvGlobal);
                AdicionarFfz(globais, (await tFfzGlobal) as JObject);
                AdicionarSete(globais, ((await tSeteGlobal) as JObject)?["emotes"]);

                if (comTwitch)
                {
                    var twitch = new Dictionary<string, string>(StringComparer.Ordinal);
                    var bttv = await tBttvTwitch as JObject;
                    if (bttv != null) { AdicionarBttv(twitch, bttv["channelEmotes"]); AdicionarBttv(twitch, bttv["sharedEmotes"]); }
                    AdicionarFfz(twitch, await tFfzTwitch as JObject);
                    AdicionarSete(twitch, ((await tSeteTwitch) as JObject)?["emote_set"]?["emotes"]);
                    porPlataforma["twitch"] = twitch;
                }
                if (comKick)
                {
                    var kick = new Dictionary<string, string>(StringComparer.Ordinal);
                    AdicionarSete(kick, ((await tSeteKick) as JObject)?["emote_set"]?["emotes"]);
                    porPlataforma["kick"] = kick;
                }
                if (comYouTube)
                {
                    var youtube = new Dictionary<string, string>(StringComparer.Ordinal);
                    AdicionarSete(youtube, ((await tSeteYouTube) as JObject)?["emote_set"]?["emotes"]);
                    porPlataforma["youtube"] = youtube;
                }

                lock (Trava)
                {
                    _globais = globais;
                    _porPlataforma = porPlataforma;
                }
                _chaveCarregada = chave;
                Carregado?.Invoke();
            }
            catch (Exception erro) { App.RegistrarErro(erro); }
            finally { _carregando = false; }
        }

        static async Task<JToken> Pegar(string url)
        {
            try { return JToken.Parse(await Http.GetStringAsync(url).ConfigureAwait(false)); }
            catch (Exception) { return null; }
        }

        static void Por(Dictionary<string, string> d, string nome, string url)
        {
            if (!string.IsNullOrEmpty(nome) && !string.IsNullOrEmpty(url)) d[nome] = url;
        }

        static void AdicionarSete(Dictionary<string, string> d, JToken emotes)
        {
            var lista = emotes as JArray;
            if (lista == null) return;
            foreach (var e in lista)
            {
                var host = (string)e["data"]?["host"]?["url"];
                // Animado: GIF. Estático: PNG (o 7TV só tem WebP e AVIF além disso, e o WPF não abre esses).
                var animado = (bool?)e["data"]?["animated"] == true;
                if (host != null) Por(d, (string)e["name"], "https:" + host + (animado ? "/2x.gif" : "/2x.png"));
            }
        }

        static void AdicionarBttv(Dictionary<string, string> d, JToken emotes)
        {
            var lista = emotes as JArray;
            if (lista == null) return;
            foreach (var e in lista)
            {
                var id = (string)e["id"];
                if (id != null) Por(d, (string)e["code"], "https://cdn.betterttv.net/emote/" + id + "/2x");
            }
        }

        static void AdicionarFfz(Dictionary<string, string> d, JObject raiz)
        {
            var conjuntos = raiz?["sets"] as JObject;
            if (conjuntos == null) return;
            foreach (var conjunto in conjuntos.Properties())
                foreach (var e in (conjunto.Value["emoticons"] as JArray) ?? new JArray())
                {
                    var urls = e["urls"] as JObject;
                    var url = (string)(urls?["2"] ?? urls?["1"]);
                    if (url != null) Por(d, (string)e["name"], url.StartsWith("//") ? "https:" + url : url);
                }
        }
    }
}
