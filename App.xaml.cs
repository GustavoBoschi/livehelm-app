using System;
using System.Threading;
using System.Windows;

namespace LiveHelmApp
{
    // Linha de comando (usada nos testes): --sb ws://... e --obs ws://... trocam os endereços, --sbsenha define a senha do
    // Streamer.bot, --config arquivo.json troca o arquivo de configuração (e libera a trava de instância única, a menos que --instancia nome a isole) e
    // --captura arquivo.png salva uma imagem da janela e fecha o programa (sem gravar a configuração) e --momentos pasta
    // troca a pasta dos arquivos de momentos marcados.
    public partial class App : Application
    {
        public static string EnderecoStreamerbot; // --sb: vale no lugar do endereço das configurações
        public static string SenhaStreamerbot;    // --sbsenha: vale no lugar da senha das configurações
        public static string EnderecoObsForcado;
        public static string ArquivoDeConfig;
        public static string ArquivoDeCaptura;
        public static string PastaDeMomentos;    // null = Documentos\LiveHelm-App
        public static string SufixoDeTeste = ""; // --instancia: isola a trava de instância única nos testes

        const string NomeDoMutex = "LiveHelmApp-instancia-unica";
        const string NomeDoSinal = "LiveHelmApp-mostrar";
        Mutex _instanciaUnica;
        EventWaitHandle _sinalDeMostrar;

        // Erro inesperado numa tela: grava em %APPDATA%\LiveHelm-App\erros.log e segue, em vez de derrubar o painel.
        public static void RegistrarErro(Exception erro)
        {
            try
            {
                var pasta = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LiveHelm-App");
                System.IO.Directory.CreateDirectory(pasta);
                System.IO.File.AppendAllText(System.IO.Path.Combine(pasta, "erros.log"), DateTime.Now.ToString("s") + " " + erro + Environment.NewLine + Environment.NewLine);
            }
            catch (Exception) { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            for (int i = 0; i < e.Args.Length - 1; i++)
            {
                switch (e.Args[i])
                {
                    case "--sb": EnderecoStreamerbot = e.Args[i + 1]; break;
                    case "--sbsenha": SenhaStreamerbot = e.Args[i + 1]; break;
                    case "--obs": EnderecoObsForcado = e.Args[i + 1]; break;
                    case "--config": ArquivoDeConfig = e.Args[i + 1]; break;
                    case "--captura": ArquivoDeCaptura = e.Args[i + 1]; break;
                    case "--momentos": PastaDeMomentos = e.Args[i + 1]; break;
                    case "--instancia": SufixoDeTeste = e.Args[i + 1]; break;
                }
            }
            DispatcherUnhandledException += (o, ex) =>
            {
                RegistrarErro(ex.Exception);
                ex.Handled = true;
            };
            base.OnStartup(e);

            // Uma só janela: abrir de novo pede à que já existe (mesmo escondida na bandeja) que apareça.
            if (ArquivoDeCaptura == null && (ArquivoDeConfig == null || SufixoDeTeste != ""))
            {
                _instanciaUnica = new Mutex(true, NomeDoMutex + SufixoDeTeste, out bool primeira);
                if (!primeira)
                {
                    try { EventWaitHandle.OpenExisting(NomeDoSinal + SufixoDeTeste).Set(); } catch (Exception) { }
                    Shutdown();
                    return;
                }
                _sinalDeMostrar = new EventWaitHandle(false, EventResetMode.AutoReset, NomeDoSinal + SufixoDeTeste);
                var espera = new Thread(() =>
                {
                    while (_sinalDeMostrar.WaitOne())
                        Dispatcher.BeginInvoke(new Action(() => (MainWindow as MainWindow)?.MostrarJanela()));
                }) { IsBackground = true };
                espera.Start();
            }
            new MainWindow().Show();
        }
    }
}
