using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Extras do chat: busca e filtro por usuário, mensagens fixadas, fila de perguntas e lista de silenciados e banidos.
    public partial class MainWindow
    {
        const int MaxFixadas = 3;
        const int MaxPerguntas = 100;
        const int MaxPunicoes = 50;
        const int JanelaDuplicadaS = 20;   // o mesmo silêncio ou banimento visto duas vezes (pelo painel e pelo aviso da plataforma) dentro disso vale um só
        const double LarguraDoPopupPunicoes = 400;
        const int ReaberturaPopupMs = 250; // clicar no botão com o popup aberto fecha (StaysOpen=false) e o clique reabriria logo em seguida

        readonly ObservableCollection<ItemFixada> _fixadas = new ObservableCollection<ItemFixada>();
        readonly ObservableCollection<ItemPergunta> _perguntas = new ObservableCollection<ItemPergunta>();
        readonly ObservableCollection<ItemPunicao> _punicoes = new ObservableCollection<ItemPunicao>();
        string _busca = "";          // texto da busca sem acento nem maiúsculas
        string _usuarioFiltro;       // "plataforma|nome em minúsculas" do usuário em foco, ou null
        DateTime _popupPunicoesFechou = DateTime.MinValue;

        // Chamado no construtor, antes de a tela passar a reagir às caixas.
        void IniciarChatExtras()
        {
            ListaFixadas.ItemsSource = _fixadas;
            ListaPerguntas.ItemsSource = _perguntas;
            ListaPunicoes.ItemsSource = _punicoes;
            foreach (var p in _cfg.Perguntas)
                _perguntas.Add(new ItemPergunta { Plataforma = p.Plataforma, Nome = p.Nome, Texto = p.Texto, Hora = p.Hora, Respondida = p.Respondida });
            _fixadas.CollectionChanged += (s, e) => AtualizarFixadas();
            _perguntas.CollectionChanged += (s, e) => AtualizarPerguntas();
            _punicoes.CollectionChanged += (s, e) => AtualizarPunicoes();
            PopupPunicoes.Closed += (s, e) => _popupPunicoesFechou = DateTime.UtcNow;
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && _cfg.ChatVisivel && !_cfg.ChatRecolhido)
                {
                    MostrarBusca();
                    e.Handled = true;
                }
            };
            AtualizarFixadas();
            AtualizarPerguntas();
            AtualizarPunicoes();
        }

        // Guarda a fila de perguntas no config (chamado por GuardarCampos).
        void GuardarPerguntas()
        {
            _cfg.Perguntas = _perguntas.Select(p => new PerguntaSalva { Plataforma = p.Plataforma, Nome = p.Nome, Texto = p.Texto, Hora = p.Hora, Respondida = p.Respondida }).ToList();
        }

        // ---- Busca e filtro por usuário ----

        // Sem acento e sem maiúsculas; a pontuação fica (a busca por "?" acha as perguntas).
        static string Simplificar(string s)
        {
            var sb = new StringBuilder();
            foreach (var c in (s ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        bool PassaNaBusca(ItemChat it)
        {
            if (_usuarioFiltro != null && it.Plataforma + "|" + it.Nome.ToLowerInvariant() != _usuarioFiltro) return false;
            if (_busca == "") return true;
            return Simplificar(it.EhAlerta ? Juntar(" ", it.Rotulo, it.Nome, it.Detalhe) : it.Nome + " " + it.Texto).Contains(_busca);
        }

        void AplicarBusca()
        {
            if (_vista == null) return;
            _vista.Refresh();
            _rolarAuto = true;
            _rolagem?.ScrollToEnd();
        }

        void MostrarBusca()
        {
            if (FiltroPerguntas.IsChecked == true) FiltroTudo.IsChecked = true; // a lista de perguntas não passa pela busca
            BarraBusca.Visibility = Visibility.Visible;
            TxtBusca.Focus();
            TxtBusca.SelectAll();
        }

        void Buscar_Click(object sender, RoutedEventArgs e)
        {
            if (BarraBusca.Visibility == Visibility.Visible) FecharBusca();
            else MostrarBusca();
        }

        // Fecha a barra e solta a busca e o usuário em foco.
        void FecharBusca()
        {
            bool tinhaFiltro = _busca != "" || _usuarioFiltro != null;
            TxtBusca.Text = "";
            _busca = "";
            _usuarioFiltro = null;
            ChipUsuario.Visibility = Visibility.Collapsed;
            BarraBusca.Visibility = Visibility.Collapsed;
            if (tinhaFiltro) AplicarBusca();
        }

        void FecharBusca_Click(object sender, RoutedEventArgs e) { FecharBusca(); }

        void Busca_TextChanged(object sender, TextChangedEventArgs e)
        {
            var novo = Simplificar(TxtBusca.Text.Trim());
            if (novo == _busca) return;
            _busca = novo;
            AplicarBusca();
        }

        void Busca_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            FecharBusca();
            e.Handled = true;
        }

        void MenuUsuario_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null || it.EhAlerta) return;
            if (FiltroPerguntas.IsChecked == true) FiltroTudo.IsChecked = true;
            _usuarioFiltro = it.Plataforma + "|" + it.Nome.ToLowerInvariant();
            TxtChipUsuario.Text = it.Nome;
            ChipUsuario.Visibility = Visibility.Visible;
            BarraBusca.Visibility = Visibility.Visible;
            AplicarBusca();
        }

        void LimparUsuario_Click(object sender, RoutedEventArgs e)
        {
            _usuarioFiltro = null;
            ChipUsuario.Visibility = Visibility.Collapsed;
            AplicarBusca();
        }

        // ---- Mensagens fixadas ----

        void AtualizarFixadas()
        {
            BarraFixadas.Visibility = _fixadas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void MenuFixar_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null || it.EhAlerta) return;
            var texto = it.Texto;
            if (_fixadas.Any(f => f.Plataforma == it.Plataforma && f.Nome == it.Nome && f.Texto == texto)) return;
            _fixadas.Add(new ItemFixada { Plataforma = it.Plataforma, Nome = it.Nome, Texto = texto });
            while (_fixadas.Count > MaxFixadas) _fixadas.RemoveAt(0);
        }

        void Desafixar_Click(object sender, RoutedEventArgs e)
        {
            var f = ((FrameworkElement)sender).DataContext as ItemFixada;
            if (f != null) _fixadas.Remove(f);
        }

        // ---- Fila de perguntas ----

        void AtualizarPerguntas()
        {
            int pendentes = _perguntas.Count(p => !p.Respondida);
            int respondidas = _perguntas.Count - pendentes;
            TxtContaPerguntas.Text = pendentes.ToString();
            TxtContaPerguntas.Visibility = pendentes > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(FiltroPerguntas, pendentes > 0 ? "Perguntas " + pendentes : "Perguntas");
            TxtResumoPerguntas.Text = Juntar(" · ", pendentes + " na fila", respondidas > 0 ? respondidas + (respondidas == 1 ? " respondida" : " respondidas") : "");
            PerguntasVazias.Visibility = _perguntas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void MenuPergunta_Click(object sender, RoutedEventArgs e)
        {
            var it = ItemDoMenu(sender);
            if (it == null || it.EhAlerta) return;
            var texto = it.Texto;
            if (_perguntas.Any(p => p.Plataforma == it.Plataforma && p.Nome == it.Nome && p.Texto == texto && p.Hora == it.Hora))
            {
                AvisarChat("Essa mensagem já está na fila de perguntas.", false);
                return;
            }
            _perguntas.Add(new ItemPergunta { Plataforma = it.Plataforma, Nome = it.Nome, Texto = texto, Hora = it.Hora });
            while (_perguntas.Count > MaxPerguntas)
                _perguntas.Remove(_perguntas.FirstOrDefault(p => p.Respondida) ?? _perguntas[0]);
            GuardarCampos();
            AvisarChat("Pergunta de " + it.Nome + " na fila (aba Perguntas).", false);
        }

        void PerguntaRespondida_Click(object sender, RoutedEventArgs e)
        {
            var p = ((FrameworkElement)sender).DataContext as ItemPergunta;
            if (p == null) return;
            p.Respondida = !p.Respondida;
            AtualizarPerguntas();
            GuardarCampos();
        }

        void PerguntaMencionar_Click(object sender, RoutedEventArgs e)
        {
            var p = ((FrameworkElement)sender).DataContext as ItemPergunta;
            if (p == null) return;
            TxtMensagem.Text = (TxtMensagem.Text + " @" + p.Nome + " ").TrimStart();
            TxtMensagem.Focus();
            TxtMensagem.CaretIndex = TxtMensagem.Text.Length;
        }

        void PerguntaRemover_Click(object sender, RoutedEventArgs e)
        {
            var p = ((FrameworkElement)sender).DataContext as ItemPergunta;
            if (p == null) return;
            _perguntas.Remove(p);
            GuardarCampos();
        }

        void LimparRespondidas_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _perguntas.Where(x => x.Respondida).ToList()) _perguntas.Remove(p);
            GuardarCampos();
        }

        // ---- Silenciados e banidos da sessão ----

        void AtualizarPunicoes()
        {
            TxtPunicoes.Text = _punicoes.Count.ToString();
            TxtPunicoes.Visibility = _punicoes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            PunicoesVazias.Visibility = _punicoes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ListaPunicoes.Visibility = _punicoes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // Anota um silêncio ou banimento (feito pelo painel ou avisado pela plataforma). Os dois caminhos podem trazer o mesmo caso:
        // o que já está na lista é completado em vez de repetido.
        void RegistrarPunicao(string plataforma, string nome, bool banido, int segundos, string login, string usuarioId)
        {
            if (string.IsNullOrEmpty(nome)) return;
            var agora = DateTime.Now;
            Func<ItemPunicao, bool> mesmoUsuario = p => p.Plataforma == plataforma && string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase);
            var repetida = _punicoes.FirstOrDefault(p => mesmoUsuario(p) && p.Banido == banido && (agora - p.Quando).TotalSeconds < JanelaDuplicadaS);
            if (repetida != null)
            {
                if (repetida.Segundos == 0) repetida.Segundos = segundos;
                if (repetida.Login == "") repetida.Login = login ?? "";
                if (repetida.UsuarioId == "") repetida.UsuarioId = usuarioId ?? "";
                repetida.Atualizar();
                return;
            }
            var antiga = _punicoes.FirstOrDefault(mesmoUsuario);
            if (antiga != null) _punicoes.Remove(antiga);
            if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(usuarioId))
            {
                var mensagem = _chat.LastOrDefault(i => !i.EhAlerta && i.Plataforma == plataforma && string.Equals(i.Nome, nome, StringComparison.OrdinalIgnoreCase));
                if (mensagem != null)
                {
                    if (string.IsNullOrEmpty(login)) login = mensagem.Login;
                    if (string.IsNullOrEmpty(usuarioId)) usuarioId = mensagem.UsuarioId;
                }
            }
            _punicoes.Insert(0, new ItemPunicao
            {
                Plataforma = plataforma, Nome = nome, Login = login ?? "", UsuarioId = usuarioId ?? "", Banido = banido, Segundos = segundos, Quando = agora,
            });
            while (_punicoes.Count > MaxPunicoes) _punicoes.RemoveAt(_punicoes.Count - 1);
        }

        void Punicoes_Click(object sender, RoutedEventArgs e)
        {
            if (PopupPunicoes.IsOpen) { PopupPunicoes.IsOpen = false; return; }
            if ((DateTime.UtcNow - _popupPunicoesFechou).TotalMilliseconds < ReaberturaPopupMs) return;
            foreach (var p in _punicoes) p.Atualizar(); // os silêncios que acabaram desde a última vez
            // O cartão termina na borda direita do botão (a margem de 8 dos dois lados entra na conta).
            CartaoPunicoes.Width = Math.Min(LarguraDoPopupPunicoes, Math.Max(260, ActualWidth - 60));
            PopupPunicoes.HorizontalOffset = BtnPunicoes.ActualWidth - (CartaoPunicoes.Width + 16);
            PopupPunicoes.IsOpen = true;
        }

        // Pede ao Streamer.bot para liberar o usuário (verbo reverter da ação Painel: Chat). A ação responde ok mesmo se o código
        // dela for o antigo, então o item fica na lista marcado com o pedido, e dá pra tentar de novo.
        async void ReverterPunicao_Click(object sender, RoutedEventArgs e)
        {
            var p = ((FrameworkElement)sender).DataContext as ItemPunicao;
            if (p == null || !p.PodeReverter) return;
            var erro = await ExecutarChatAsync("reverter", new JObject
            {
                ["plataforma"] = p.Plataforma, ["usuario"] = p.Login != "" ? p.Login : p.Nome, ["usuarioId"] = p.UsuarioId,
            });
            if (erro != null) { AvisarChat("Não consegui reverter: " + erro); return; }
            p.Pedido = DateTime.Now;
            AvisarChat("Pedi ao Streamer.bot para liberar " + p.Nome + ". Se nada mudar, atualize o código da ação Painel: Chat (botão ? no alto).", false);
        }
    }
}
