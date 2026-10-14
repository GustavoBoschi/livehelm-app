using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Cliente do OBS WebSocket v5: pedidos com resposta e eventos de cena, de transmissão e de gravação. A senha (se houver) segue o
    // desafio do protocolo: base64(sha256(senha + salt)) e depois base64(sha256(segredo + challenge)). Os eventos chegam
    // em thread do pool.
    sealed class ObsClient : IDisposable
    {
        const int EventosCenas = 1 << 2;   // eventSubscriptions: Scenes
        const int EventosSaidas = 1 << 6;  // eventSubscriptions: Outputs
        const int ReconexaoMs = 5000;

        readonly ConexaoWs _conexao;
        readonly string _senha;
        readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> _pendentes = new ConcurrentDictionary<string, TaskCompletionSource<JObject>>();
        int _contador;
        volatile bool _pronta;

        public event Action<bool> ConexaoMudou;
        public event Action<string, JObject> Evento;

        public ObsClient(string url, string senha)
        {
            _senha = senha ?? "";
            _conexao = new ConexaoWs(url, ReconexaoMs, ReconexaoMs);
            _conexao.Mensagem += AoMensagem;
            _conexao.AoFechar += AoFechar;
            _conexao.Iniciar();
        }

        void AoMensagem(JObject m)
        {
            var op = (int?)m["op"];
            var d = m["d"] as JObject ?? new JObject();
            if (op == 0)
            {
                var identificar = new JObject { ["rpcVersion"] = 1, ["eventSubscriptions"] = EventosCenas | EventosSaidas };
                var auth = d["authentication"] as JObject;
                if (auth != null)
                {
                    var segredo = ConexaoWs.Sha256Base64(_senha + (string)auth["salt"]);
                    identificar["authentication"] = ConexaoWs.Sha256Base64(segredo + (string)auth["challenge"]);
                }
                _ = _conexao.EnviarAsync(new JObject { ["op"] = 1, ["d"] = identificar }.ToString(Formatting.None));
            }
            else if (op == 2)
            {
                _pronta = true;
                ConexaoMudou?.Invoke(true);
            }
            else if (op == 5)
            {
                Evento?.Invoke((string)d["eventType"] ?? "", d["eventData"] as JObject ?? new JObject());
            }
            else if (op == 7)
            {
                var id = (string)d["requestId"];
                if (id != null && _pendentes.TryRemove(id, out var espera))
                {
                    var deu = (bool?)(d["requestStatus"] as JObject)?["result"] == true;
                    espera.TrySetResult(deu ? d["responseData"] as JObject ?? new JObject() : null);
                }
            }
        }

        void AoFechar()
        {
            foreach (var chave in _pendentes.Keys)
                if (_pendentes.TryRemove(chave, out var espera)) espera.TrySetResult(null);
            if (_pronta)
            {
                _pronta = false;
                ConexaoMudou?.Invoke(false);
            }
        }

        // Resposta do OBS (objeto, possivelmente vazio) ou null se falhou, demorou ou não há conexão.
        public async Task<JObject> PedirAsync(string tipo, JObject dados = null, int limiteMs = 8000)
        {
            if (!_pronta) return null;
            var id = "obs-" + Interlocked.Increment(ref _contador);
            var espera = new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendentes[id] = espera;
            var pedido = new JObject
            {
                ["op"] = 6,
                ["d"] = new JObject { ["requestType"] = tipo, ["requestId"] = id, ["requestData"] = dados ?? new JObject() },
            };
            if (!await _conexao.EnviarAsync(pedido.ToString(Formatting.None)).ConfigureAwait(false))
            {
                _pendentes.TryRemove(id, out _);
                return null;
            }
            if (await Task.WhenAny(espera.Task, Task.Delay(limiteMs)).ConfigureAwait(false) != espera.Task)
            {
                _pendentes.TryRemove(id, out _);
                return null;
            }
            return espera.Task.Result;
        }

        public void Dispose() => _conexao.Dispose();
    }
}
