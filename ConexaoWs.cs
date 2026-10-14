using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Conexão WebSocket que se reabre sozinha (espera crescente entre falhas, de min a max). Entrega cada mensagem como
    // JObject, em thread do pool: quem assina precisa passar pra thread da tela. AoFechar só dispara se a conexão chegou
    // a abrir.
    sealed class ConexaoWs : IDisposable
    {
        const int ConexaoEstavelMs = 5000; // aberta por mais que isso: a próxima queda reconecta rápido de novo

        readonly string _url;
        readonly int _minMs;
        readonly int _maxMs;
        readonly CancellationTokenSource _cancelar = new CancellationTokenSource();
        readonly SemaphoreSlim _envio = new SemaphoreSlim(1, 1);
        volatile ClientWebSocket _ws;

        public event Action AoAbrir;
        public event Action AoFechar;
        public event Action<JObject> Mensagem;

        public ConexaoWs(string url, int minMs, int maxMs)
        {
            _url = url;
            _minMs = minMs;
            _maxMs = maxMs;
        }

        public void Iniciar()
        {
            Task.Run(Laco);
        }

        async Task Laco()
        {
            int falhas = 0;
            while (!_cancelar.IsCancellationRequested)
            {
                var ws = new ClientWebSocket();
                DateTime abertaEm = DateTime.MinValue;
                try
                {
                    await ws.ConnectAsync(new Uri(_url), _cancelar.Token).ConfigureAwait(false);
                    _ws = ws;
                    abertaEm = DateTime.UtcNow;
                    Avisar(AoAbrir);
                    await Receber(ws).ConfigureAwait(false);
                }
                catch (Exception) { /* sem servidor, URL inválida ou queda: tenta de novo abaixo */ }
                finally
                {
                    _ws = null;
                    ws.Dispose();
                    if (abertaEm != DateTime.MinValue)
                    {
                        Avisar(AoFechar);
                        if ((DateTime.UtcNow - abertaEm).TotalMilliseconds >= ConexaoEstavelMs) falhas = 0;
                    }
                }
                if (_cancelar.IsCancellationRequested) break;
                int espera = (int)Math.Min((long)_minMs << Math.Min(falhas, 10), _maxMs);
                falhas++;
                try { await Task.Delay(espera, _cancelar.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        async Task Receber(ClientWebSocket ws)
        {
            var buffer = new byte[16384];
            while (ws.State == WebSocketState.Open)
            {
                JObject mensagem;
                using (var acumulado = new MemoryStream())
                {
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cancelar.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false); }
                            catch (Exception) { }
                            return;
                        }
                        acumulado.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);
                    try { mensagem = JObject.Parse(Encoding.UTF8.GetString(acumulado.ToArray())); }
                    catch (Exception) { continue; }
                }
                try { Mensagem?.Invoke(mensagem); }
                catch (Exception) { /* um erro num tratador não derruba a conexão */ }
            }
        }

        static void Avisar(Action a)
        {
            try { a?.Invoke(); } catch (Exception) { }
        }

        // false se não há conexão aberta ou o envio falhou.
        public async Task<bool> EnviarAsync(string texto)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return false;
            await _envio.WaitAsync().ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(texto);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancelar.Token).ConfigureAwait(false);
                return true;
            }
            catch (Exception) { return false; }
            finally { _envio.Release(); }
        }

        public void Dispose()
        {
            _cancelar.Cancel();
            try { _ws?.Abort(); } catch (Exception) { }
        }

        // base64(sha256(texto)), como o Streamer.bot e o OBS pedem na autenticação.
        public static string Sha256Base64(string texto)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(texto)));
        }
    }
}
