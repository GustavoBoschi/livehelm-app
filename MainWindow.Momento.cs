using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LiveHelmApp
{
    // Painel Momento: marca o tempo da live com uma anotação num arquivo de texto do dia (ver Momentos em ItensExtras.cs).
    public partial class MainWindow
    {
        const int StatusMomentoMs = 6000;

        readonly DispatcherTimer _limpaStatusMomento = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StatusMomentoMs) };

        void IniciarMomento()
        {
            _limpaStatusMomento.Tick += (s, e) =>
            {
                _limpaStatusMomento.Stop();
                StatusMomento.Visibility = Visibility.Collapsed;
            };
        }

        void AvisarMomento(string texto, bool erro)
        {
            StatusMomento.Foreground = erro ? CorAviso : CorOk;
            StatusMomento.Text = texto;
            StatusMomento.Visibility = Visibility.Visible;
            _limpaStatusMomento.Stop();
            _limpaStatusMomento.Start();
        }

        void Momento_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) MarcarMomento();
        }

        void Marcar_Click(object sender, RoutedEventArgs e) { MarcarMomento(); }

        // O tempo é o da live; fora do ar vale o da gravação, e sem nenhum dos dois vale só o relógio.
        void MarcarMomento()
        {
            var nota = TxtMomento.Text.Trim();
            var agora = DateTime.Now;
            string tempo;
            if (_aoVivo)
                tempo = Duracao(DateTime.UtcNow - _inicioDaLive) + (_gravando ? "  [gravação " + Duracao(DateTime.UtcNow - _inicioDaGravacao) + "]" : "");
            else if (_gravando)
                tempo = Duracao(DateTime.UtcNow - _inicioDaGravacao) + "  [gravação]";
            else
                tempo = "--:--:--  [fora do ar, " + agora.ToString("HH:mm") + "]";
            try
            {
                Momentos.Registrar(nota == "" ? tempo : tempo + "  " + nota, agora);
                TxtMomento.Text = "";
                AvisarMomento("✓ Momento marcado em " + tempo.Split(' ')[0] + (nota == "" ? "" : ": " + nota), false);
            }
            catch (Exception ex)
            {
                AvisarMomento("Não consegui gravar o arquivo: " + ex.Message, true);
            }
        }

        void AbrirMomentos_Click(object sender, RoutedEventArgs e)
        {
            var caminho = Momentos.Caminho(DateTime.Now);
            try
            {
                if (File.Exists(caminho)) Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
                else if (Directory.Exists(Path.GetDirectoryName(caminho))) Process.Start(new ProcessStartInfo(Path.GetDirectoryName(caminho)) { UseShellExecute = true });
                else AvisarMomento("Nenhum momento marcado hoje.", true);
            }
            catch (Exception ex)
            {
                AvisarMomento("Não consegui abrir: " + ex.Message, true);
            }
        }
    }
}
