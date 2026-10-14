using System;
using System.Windows;

namespace LiveHelmApp
{
    // Janela de ajuda: como criar no Streamer.bot as ações que o painel usa.
    public partial class AjudaWindow : Window
    {
        // Código da sub-ação "Execute C# Code" da ação Painel: Chat. Os métodos de CPH foram conferidos nas DLLs do
        // Streamer.bot 1.0.7 (Streamer.bot.Plugin.Interface.dll).
        const string CodigoDaAcao = @"using System;
using System.Collections.Generic;

public class CPHInline
{
    // false: mensagens e punicoes saem com a sua conta; true: com a conta do bot (que precisa ser moderador)
    const bool USAR_BOT = false;

    string Texto(string nome)
    {
        object valor;
        return args.TryGetValue(nome, out valor) && valor != null ? valor.ToString() : """";
    }

    int Numero(string nome, int padrao)
    {
        int n;
        return int.TryParse(Texto(nome), out n) && n > 0 ? n : padrao;
    }

    List<string> Opcoes()
    {
        List<string> lista = new List<string>();
        foreach (string o in Texto(""opcoes"").Split('|'))
            if (o.Trim() != """") lista.Add(o.Trim());
        return lista;
    }

    public bool Execute()
    {
        string acao = Texto(""acao"");
        string plataforma = Texto(""plataforma"");
        string mensagem = Texto(""mensagem"");
        string mensagemId = Texto(""mensagemId"");
        string usuario = Texto(""usuario"");
        string usuarioId = Texto(""usuarioId"");
        int segundos = Numero(""segundos"", 600);

        switch (acao)
        {
            case ""enviar"":
                if (plataforma == ""twitch"") CPH.SendMessage(mensagem, USAR_BOT);
                else if (plataforma == ""kick"") CPH.SendKickMessage(mensagem, USAR_BOT);
                else if (plataforma == ""youtube"") CPH.SendYouTubeMessage(mensagem, USAR_BOT);
                break;
            case ""apagar"":
                if (plataforma == ""twitch"") CPH.TwitchDeleteChatMessage(mensagemId, USAR_BOT);
                else if (plataforma == ""kick"") CPH.KickDeleteChatMessage(mensagemId);
                else CPH.LogWarn(""Painel: o YouTube nao permite apagar mensagens."");
                break;
            case ""silenciar"":
                if (plataforma == ""twitch"") CPH.TwitchTimeoutUser(usuario, segundos, ""Painel"", USAR_BOT);
                else if (plataforma == ""kick"") CPH.KickTimeoutUser(usuario, segundos, ""Painel"", USAR_BOT);
                else if (plataforma == ""youtube"")
                {
                    if (usuarioId != """") CPH.YouTubeTimeoutUserById(usuarioId, segundos);
                    else CPH.YouTubeTimeoutUserByName(usuario, segundos);
                }
                break;
            case ""banir"":
                if (plataforma == ""twitch"") CPH.TwitchBanUser(usuario, ""Painel"", USAR_BOT);
                else if (plataforma == ""kick"") CPH.KickBanUser(usuario, ""Painel"", USAR_BOT);
                else if (plataforma == ""youtube"")
                {
                    if (usuarioId != """") CPH.YouTubeBanUserById(usuarioId);
                    else CPH.YouTubeBanUserByName(usuario);
                }
                break;
            case ""reverter"":
                if (plataforma == ""twitch"") CPH.TwitchUnbanUser(usuario, USAR_BOT);
                else if (plataforma == ""kick"") CPH.KickUnbanUser(usuario, USAR_BOT);
                else CPH.LogWarn(""Painel: o YouTube nao tem como desfazer banimento ou silencio por aqui."");
                break;
            case ""limpar"":
                if (plataforma == ""twitch"") CPH.TwitchClearChatMessages(USAR_BOT);
                break;
            case ""enquete"":
                CPH.TwitchPollCreate(Texto(""titulo""), Opcoes(), Numero(""duracao"", 60));
                break;
            case ""previsao"":
                CPH.TwitchPredictionCreate(Texto(""titulo""), Opcoes(), Numero(""duracao"", 120));
                break;
            case ""encerrar_enquete"":
                CPH.TwitchPollTerminate(Texto(""id""));
                break;
            case ""travar_previsao"":
                CPH.TwitchPredictionLock(Texto(""id""));
                break;
            case ""cancelar_previsao"":
                CPH.TwitchPredictionCancel(Texto(""id""));
                break;
            case ""resolver_previsao"":
                CPH.TwitchPredictionResolve(Texto(""id""), Texto(""vencedorId""));
                break;
        }
        return true;
    }
}
";

        public AjudaWindow()
        {
            InitializeComponent();
            TxtCodigo.Text = CodigoDaAcao.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            Tema.AplicarBarraEscura(this);
        }

        void Copiar_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtCodigo.Text); }
            catch (Exception) { /* a área de transferência pode estar em uso por outro programa */ }
            ((System.Windows.Controls.Button)sender).Content = "Copiado ✓";
        }
    }
}
