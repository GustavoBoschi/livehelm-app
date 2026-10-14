using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Painel Saúde da transmissão: CPU, FPS e quadros perdidos do OBS e, se ligado nas configurações, iniciar e encerrar a
    // live e a gravação. Só consulta o OBS enquanto o painel está ligado na barra de cima e a janela está à vista.
    public partial class MainWindow
    {
        const int AtualizacaoSaudeMs = 2000;
        const int StatusSaudeMs = 6000;
        const string SaudeOk = "#4ADE80", SaudeAviso = "#FFC94A", SaudeErro = "#FF7B7B", SaudeNeutra = "#8E8BA2";

        readonly ObservableCollection<ItemSaude> _saude = new ObservableCollection<ItemSaude>();
        readonly DispatcherTimer _timerSaude = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AtualizacaoSaudeMs) };
        readonly DispatcherTimer _limpaStatusSaude = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StatusSaudeMs) };

        bool _gravando;
        DateTime _inicioDaGravacao;
        double _fpsAlvo;                    // fps configurado no OBS (0 = não lido)
        long _bytesAnteriores = -1;         // bytes enviados na última leitura, pra calcular o bitrate
        DateTime _bytesQuando;
        long _renderPerdidosAnterior = -1;  // quadros perdidos na última leitura, pra avisar quando aumentam
        long _envioPerdidosAnterior = -1;
        bool _consultandoSaude;
        bool _ocupadoTransmissao;
        string _resumoSaude = "";

        // O estado da gravação só é lido quando algum painel usa: o botão de gravar (Saúde) ou o tempo dela (Momento).
        bool GravacaoMonitorada { get { return _cfg.SaudeVisivel || _cfg.MomentoVisivel; } }

        void IniciarSaude()
        {
            ChkVerSaude.IsChecked = _cfg.SaudeVisivel;
            ChkVerMomento.IsChecked = _cfg.MomentoVisivel;
            OpControles.IsChecked = _cfg.ControlesDeTransmissao;
            ListaSaude.ItemsSource = _saude;
            _timerSaude.Tick += async (s, e) => await AtualizarSaudeAsync();
            _limpaStatusSaude.Tick += (s, e) =>
            {
                _limpaStatusSaude.Stop();
                StatusSaude.Visibility = Visibility.Collapsed;
            };
            _timerSaude.Start();
        }

        // ---- Estado da tela ----

        void AtualizarSaudeVisual()
        {
            if (BtnLive == null) return;
            SecaoTransmissao.Visibility = _cfg.ControlesDeTransmissao ? Visibility.Visible : Visibility.Collapsed;
            BtnLive.Content = _aoVivo ? "Encerrar live" : "Iniciar live";
            BtnLive.Style = (Style)FindResource(_aoVivo ? (object)"BotaoPerigo" : typeof(Button));
            BtnLive.IsEnabled = _obsAberto && !_ocupadoTransmissao;
            BtnGravacao.Content = _gravando ? "Parar gravação" : "Iniciar gravação";
            BtnGravacao.Style = (Style)FindResource(_gravando ? (object)"BotaoPerigo" : typeof(Button));
            BtnGravacao.IsEnabled = _obsAberto && !_ocupadoTransmissao;
            if (!_aoVivo)
            {
                // As medidas do envio só existem com a live no ar.
                RemoverSaude("Perdidos no envio");
                RemoverSaude("Bitrate");
                _bytesAnteriores = -1;
                _envioPerdidosAnterior = -1;
            }
            SaudeVazia.Visibility = _saude.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AtualizarResumos();
        }

        void AvisarSaude(string texto, bool erro)
        {
            StatusSaude.Foreground = erro ? CorAviso : CorOk;
            StatusSaude.Text = texto;
            StatusSaude.Visibility = Visibility.Visible;
            _limpaStatusSaude.Stop();
            _limpaStatusSaude.Start();
        }

        // Some tudo que veio do OBS quando a conexão cai ou é refeita; a próxima conexão lê de novo.
        void LimparSaude()
        {
            _saude.Clear();
            _gravando = false;
            _resumoSaude = "";
            _bytesAnteriores = -1;
            _renderPerdidosAnterior = -1;
            _envioPerdidosAnterior = -1;
            AtualizarSaudeVisual();
        }

        // ---- Leitura do OBS ----

        // Ao ligar o painel ou conectar o OBS: lê o fps configurado e já faz a primeira medição.
        async Task CarregarSaudeAsync()
        {
            var obs = _obs;
            if (obs == null || !_obsAberto || !_cfg.SaudeVisivel) return;
            var video = await obs.PedirAsync("GetVideoSettings");
            if (video != null)
            {
                double numerador = (double?)video["fpsNumerator"] ?? 0, denominador = (double?)video["fpsDenominator"] ?? 0;
                _fpsAlvo = denominador > 0 ? numerador / denominador : 0;
            }
            await CarregarGravacaoAsync();
            await AtualizarSaudeAsync();
        }

        async Task CarregarGravacaoAsync()
        {
            var obs = _obs;
            if (obs == null || !_obsAberto) return;
            var r = await obs.PedirAsync("GetRecordStatus");
            if (r == null) return;
            _gravando = (bool?)r["outputActive"] == true;
            _inicioDaGravacao = DateTime.UtcNow - TimeSpan.FromMilliseconds((double?)r["outputDuration"] ?? 0);
            AtualizarSaudeVisual();
        }

        // ---- Medidas ----

        static string CorPorFaixa(double valor, double aviso, double erro) { return valor < aviso ? SaudeOk : valor < erro ? SaudeAviso : SaudeErro; }

        void DefinirSaude(string nome, string valor, string cor, string dica)
        {
            var item = _saude.FirstOrDefault(s => s.Nome == nome);
            if (item == null)
            {
                _saude.Add(new ItemSaude { Nome = nome, Valor = valor, Cor = cor, Dica = dica });
                return;
            }
            item.Valor = valor;
            item.Cor = cor;
            item.Dica = dica;
        }

        void RemoverSaude(string nome)
        {
            var item = _saude.FirstOrDefault(s => s.Nome == nome);
            if (item != null) _saude.Remove(item);
        }

        // Percentual de quadros perdidos e a cor correspondente; subir desde a última leitura já pede atenção.
        string QuadrosPerdidos(long perdidos, long total, long anterior, out string cor)
        {
            double pct = total > 0 ? 100.0 * perdidos / total : 0;
            cor = CorPorFaixa(pct, 1, 5);
            if (anterior >= 0 && perdidos > anterior && cor == SaudeOk) cor = SaudeAviso;
            return perdidos == 0 ? "0" : perdidos + " (" + pct.ToString("0.0") + "%)";
        }

        async Task AtualizarSaudeAsync()
        {
            var obs = _obs;
            if (obs == null || !_obsAberto || !_cfg.SaudeVisivel || !IsVisible || WindowState == WindowState.Minimized || _consultandoSaude) return;
            _consultandoSaude = true;
            try
            {
                var stats = await obs.PedirAsync("GetStats", null, 3000);
                var envio = _aoVivo ? await obs.PedirAsync("GetStreamStatus", null, 3000) : null;
                if (_obs != obs) return;
                var resumo = new List<string>();
                if (stats != null)
                {
                    double cpu = (double?)stats["cpuUsage"] ?? 0;
                    DefinirSaude("CPU", cpu.ToString("0") + "%", CorPorFaixa(cpu, 60, 85), "Uso de CPU do OBS");
                    resumo.Add("CPU " + cpu.ToString("0") + "%");

                    double fps = (double?)stats["activeFps"] ?? 0;
                    string corFps = _fpsAlvo > 0 ? (fps >= _fpsAlvo * 0.97 ? SaudeOk : fps >= _fpsAlvo * 0.9 ? SaudeAviso : SaudeErro) : SaudeNeutra;
                    DefinirSaude("FPS", _fpsAlvo > 0 ? fps.ToString("0.#") + " / " + _fpsAlvo.ToString("0.##") : fps.ToString("0.#"), corFps, "Quadros por segundo reais e a meta do OBS");
                    resumo.Add(fps.ToString("0") + " fps");

                    long renderPerdidos = (long?)stats["renderSkippedFrames"] ?? 0, renderTotal = (long?)stats["renderTotalFrames"] ?? 0;
                    string corRender;
                    var textoRender = QuadrosPerdidos(renderPerdidos, renderTotal, _renderPerdidosAnterior, out corRender);
                    DefinirSaude("Quadros perdidos", textoRender, corRender, "Quadros que o OBS não conseguiu renderizar desde que abriu (placa de vídeo ou CPU no limite)");
                    if (_renderPerdidosAnterior >= 0 && renderPerdidos > _renderPerdidosAnterior) resumo.Add("perdendo quadros");
                    _renderPerdidosAnterior = renderPerdidos;

                    DefinirSaude("Memória", ((double?)stats["memoryUsage"] ?? 0).ToString("0") + " MB", SaudeNeutra, "Memória usada pelo OBS");
                }
                if (_aoVivo && envio != null)
                {
                    long enviados = (long?)envio["outputSkippedFrames"] ?? 0, envioTotal = (long?)envio["outputTotalFrames"] ?? 0;
                    string corEnvio;
                    var textoEnvio = QuadrosPerdidos(enviados, envioTotal, _envioPerdidosAnterior, out corEnvio);
                    DefinirSaude("Perdidos no envio", textoEnvio, corEnvio, "Quadros que não saíram pra plataforma desde que a live começou (internet ou codificador no limite)");
                    if (_envioPerdidosAnterior >= 0 && enviados > _envioPerdidosAnterior) resumo.Add("perdendo no envio");
                    _envioPerdidosAnterior = enviados;

                    long bytes = (long?)envio["outputBytes"] ?? 0;
                    var agora = DateTime.UtcNow;
                    string bitrate = "—";
                    if (_bytesAnteriores >= 0 && bytes >= _bytesAnteriores && agora > _bytesQuando)
                        bitrate = (8.0 * (bytes - _bytesAnteriores) / 1000 / (agora - _bytesQuando).TotalSeconds).ToString("0") + " kb/s";
                    _bytesAnteriores = bytes;
                    _bytesQuando = agora;
                    DefinirSaude("Bitrate", bitrate, SaudeNeutra, "Quanto está saindo pra plataforma agora");
                }
                _resumoSaude = string.Join(" · ", resumo);
                AtualizarSaudeVisual();
            }
            finally
            {
                _consultandoSaude = false;
            }
        }

        // ---- Iniciar e encerrar a live e a gravação ----

        async void Live_Click(object sender, RoutedEventArgs e)
        {
            var obs = _obs;
            if (obs == null || !_obsAberto || _ocupadoTransmissao) return;
            if (_aoVivo && MessageBox.Show(this, "Encerrar a transmissão no OBS agora?", "Encerrar live", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            await ComandarSaidaAsync(obs, _aoVivo ? "StopStream" : "StartStream", _aoVivo ? "encerrar a live" : "iniciar a live");
        }

        async void Gravacao_Click(object sender, RoutedEventArgs e)
        {
            var obs = _obs;
            if (obs == null || !_obsAberto || _ocupadoTransmissao) return;
            await ComandarSaidaAsync(obs, _gravando ? "StopRecord" : "StartRecord", _gravando ? "parar a gravação" : "iniciar a gravação");
        }

        // O estado novo chega pelo evento do OBS (StreamStateChanged, RecordStateChanged); aqui só se avisa se o pedido foi recusado.
        async Task ComandarSaidaAsync(ObsClient obs, string pedido, string descricao)
        {
            _ocupadoTransmissao = true;
            AtualizarSaudeVisual();
            var r = await obs.PedirAsync(pedido, null, 15000);
            _ocupadoTransmissao = false;
            AtualizarSaudeVisual();
            if (r == null) AvisarSaude("O OBS não conseguiu " + descricao + ". Confira a janela dele.", true);
        }
    }
}
