using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Cliente do WebSocket Server do Streamer.bot: se inscreve nos eventos de alerta e responde pedidos (GetBroadcaster,
    // GetActions, DoAction). Os eventos chegam em thread do pool. O Kick exige o login na conta do Streamer.bot e a
    // conta do Kick ligada no site dele.
    sealed class StreamerBotClient : IDisposable
    {
        // Chat, moderação e alertas (o feed unificado), mais a atualização de título e categoria (confirma o que as
        // plataformas aplicaram de fato).
        static JObject EventosAssinados() => new JObject
        {
            ["Twitch"] = new JArray(
                "ChatMessage", "ChatMessageDeleted", "ChatCleared", "UserBanned", "UserTimedOut", "FirstWord", "StreamUpdate",
                "Follow", "Sub", "ReSub", "GiftSub", "GiftBomb", "Cheer", "Raid", "RewardRedemption", "HypeChat", "HypeTrainStart",
                "HypeTrainLevelUp", "PollCreated", "PollCompleted", "PollTerminated", "PredictionCreated", "PredictionLocked",
                "PredictionCompleted", "PredictionCanceled", "UpcomingAd"),
            ["Kick"] = new JArray(
                "ChatMessage", "UserBanned", "UserTimedOut", "FirstWords", "ChannelUpdate",
                "Follow", "Subscription", "Resubscription", "GiftSubscription", "MassGiftSubscription", "KicksGifted", "RewardRedemption"),
            ["YouTube"] = new JArray(
                "Message", "MessageDeleted", "UserBanned", "UserTimedout", "FirstWords",
                "NewSubscriber", "NewSponsor", "MemberMileStone", "MembershipGift", "GiftMembershipReceived", "SuperChat",
                "SuperSticker", "JewelsGifted", "PollStarted", "PollClosed"),
        };

        readonly ConexaoWs _conexao;
        readonly string _senha;
        readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendentes = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();
        int _contador;
        volatile bool _anunciada;

        public event Action<bool> ConexaoMudou;
        public event Action<JObject> Evento;
        public event Action<string> Recusada; // o servidor não aceitou a senha ou a inscrição (texto do erro)

        public StreamerBotClient(string url, string senha)
        {
            _senha = senha;
            _conexao = new ConexaoWs(url, 1000, 15000);
            _conexao.Mensagem += AoMensagem;
            _conexao.AoFechar += AoFechar;
            _conexao.Iniciar();
        }

        void AoMensagem(JObject m)
        {
            var requisicao = (string)m["request"];
            var id = (string)m["id"];
            if (requisicao == "Hello")
            {
                // Com senha, o servidor manda um desafio; sem senha, já dá pra se inscrever.
                var auth = m["authentication"] as JObject;
                if (auth != null && !string.IsNullOrEmpty(_senha))
                {
                    var segredo = ConexaoWs.Sha256Base64(_senha + (string)auth["salt"]);
                    var assinatura = ConexaoWs.Sha256Base64(segredo + (string)auth["challenge"]);
                    var pedido = new JObject { ["request"] = "Authenticate", ["id"] = "autenticar", ["authentication"] = assinatura };
                    _ = _conexao.EnviarAsync(pedido.ToString(Formatting.None));
                }
                else
                {
                    _ = InscreverAsync();
                }
            }
            else if (id == "autenticar")
            {
                if ((string)m["status"] == "ok") _ = InscreverAsync();
                else Recusada?.Invoke((string)m["error"] ?? "senha recusada");
            }
            else if (id != null && _pendentes.TryRemove(id, out var espera))
            {
                espera.TrySetResult(m);
            }
            else if (m["event"] != null)
            {
                Evento?.Invoke(m);
            }
        }

        async Task InscreverAsync()
        {
            var espera = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendentes["inscrever"] = espera;
            var pedido = new JObject { ["request"] = "Subscribe", ["id"] = "inscrever", ["events"] = EventosAssinados() };
            if (!await _conexao.EnviarAsync(pedido.ToString(Formatting.None)).ConfigureAwait(false))
            {
                _pendentes.TryRemove("inscrever", out _);
                return;
            }
            // O servidor recusa a inscrição quando a senha está errada ou falta (o painel não pode se dizer conectado nesse caso);
            // sem resposta nenhuma, segue como antes.
            var terminou = await Task.WhenAny(espera.Task, Task.Delay(8000)).ConfigureAwait(false);
            _pendentes.TryRemove("inscrever", out _);
            if (terminou == espera.Task && (string)espera.Task.Result["status"] == "error")
            {
                Recusada?.Invoke((string)espera.Task.Result["error"] ?? "inscrição recusada");
                return;
            }
            _anunciada = true;
            ConexaoMudou?.Invoke(true);
        }

        void AoFechar()
        {
            foreach (var chave in _pendentes.Keys)
                if (_pendentes.TryRemove(chave, out var espera)) espera.TrySetResult(Erro("conexão fechada"));
            if (_anunciada)
            {
                _anunciada = false;
                ConexaoMudou?.Invoke(false);
            }
        }

        static JObject Erro(string texto) => new JObject { ["status"] = "error", ["error"] = texto };

        // Manda um pedido e devolve a resposta; { status: "error" } se não há conexão ou o servidor demora demais.
        public async Task<JObject> PedirAsync(JObject pedido, int limiteMs = 8000)
        {
            var id = "pedido-" + Interlocked.Increment(ref _contador);
            pedido["id"] = id;
            var espera = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendentes[id] = espera;
            if (!await _conexao.EnviarAsync(pedido.ToString(Formatting.None)).ConfigureAwait(false))
            {
                _pendentes.TryRemove(id, out _);
                return Erro("sem conexão com o Streamer.bot");
            }
            if (await Task.WhenAny(espera.Task, Task.Delay(limiteMs)).ConfigureAwait(false) != espera.Task)
            {
                _pendentes.TryRemove(id, out _);
                return Erro("sem resposta");
            }
            return espera.Task.Result;
        }

        public void Dispose() => _conexao.Dispose();
    }
}
