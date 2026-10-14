using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LiveHelmApp
{
    // Opção de cor na tela de configurações.
    public sealed class OpcaoDeCor
    {
        public string Nome { get; set; }
        public string Hex { get; set; }
    }

    // Cores do app: o acento e o conjunto de fundo mudam em tempo de execução trocando os pincéis de App.xaml
    // (os XAMLs usam DynamicResource pra essas chaves, então a tela inteira acompanha).
    static class Tema
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr janela, int atributo, ref int valor, int tamanho);

        public static readonly OpcaoDeCor[] Acentos =
        {
            new OpcaoDeCor { Nome = "Safira", Hex = "#2F6FEB" },
            new OpcaoDeCor { Nome = "Violeta", Hex = "#8B6CFF" },
            new OpcaoDeCor { Nome = "Esmeralda", Hex = "#10B981" },
            new OpcaoDeCor { Nome = "Dourado", Hex = "#E0A526" },
            new OpcaoDeCor { Nome = "Rosa", Hex = "#EC4899" },
            new OpcaoDeCor { Nome = "Vermelho", Hex = "#EF4444" },
            new OpcaoDeCor { Nome = "Ciano", Hex = "#06B6D4" },
        };

        // Fundo, Cartao, Campo, Elevado, ElevadoSobre, FundoChat.
        static readonly Dictionary<string, string[]> Fundos = new Dictionary<string, string[]>
        {
            { "Grafite", new[] { "#0B0B10", "#13131A", "#0D0D13", "#1B1B25", "#262633", "#0A0A0F" } },
            { "Preto", new[] { "#000000", "#0C0C0E", "#050506", "#16161A", "#212127", "#050506" } },
            { "Azul-noite", new[] { "#070B14", "#0D1322", "#080C18", "#151D33", "#1F2A47", "#060A12" } },
            { "Vinho", new[] { "#110A0D", "#1A1014", "#0E080A", "#25161C", "#321F27", "#0D0709" } },
            { "Floresta", new[] { "#090E0C", "#101915", "#0A110E", "#182620", "#22362D", "#080D0B" } },
        };
        static readonly string[] ChavesDoFundo = { "Fundo", "Cartao", "Campo", "Elevado", "ElevadoSobre", "FundoChat" };

        public static IEnumerable<string> NomesDeFundo { get { return Fundos.Keys; } }

        static int _barra = 0x100B0B; // COLORREF (BGR) da barra de título, só no Windows 11

        static Color Cor(string hex) { return (Color)ColorConverter.ConvertFromString(hex); }

        static void Definir(string chave, Color cor)
        {
            // Pincel novo no dicionário: quem usa DynamicResource se atualiza (os pincéis dos estilos ficam congelados).
            var novo = new SolidColorBrush(cor);
            novo.Freeze();
            Application.Current.Resources[chave] = novo;
        }

        static Color Misturar(Color a, Color b, double k)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
        }

        public static bool HexValido(string hex)
        {
            try { Cor(hex); return hex != null && hex.StartsWith("#"); } catch (Exception) { return false; }
        }

        public static void Aplicar(string acentoHex, string fundo)
        {
            var acento = Cor(HexValido(acentoHex) ? acentoHex : "#2F6FEB");
            Definir("Acento", acento);
            Definir("AcentoClaro", Misturar(acento, Colors.White, 0.35));
            Definir("AcentoFundo", Color.FromArgb(0x14, acento.R, acento.G, acento.B));
            Definir("AcentoBorda", Color.FromArgb(0x55, acento.R, acento.G, acento.B));
            Definir("AcentoAba", Color.FromArgb(0x38, acento.R, acento.G, acento.B));
            // Texto sobre o acento: escuro nos acentos claros (dourado), branco nos demais.
            double luz = (0.299 * acento.R + 0.587 * acento.G + 0.114 * acento.B) / 255;
            Definir("AcentoTexto", luz > 0.6 ? Cor("#14110A") : Colors.White);

            string[] cores;
            if (fundo == null || !Fundos.TryGetValue(fundo, out cores)) cores = Fundos["Grafite"];
            for (int i = 0; i < ChavesDoFundo.Length; i++) Definir(ChavesDoFundo[i], Cor(cores[i]));
            var f = Cor(cores[0]);
            _barra = (f.B << 16) | (f.G << 8) | f.R;
        }

        // Barra de título escura na cor do fundo (Windows 10 20H1+ e 11); onde não há suporte, a chamada só falha e a barra
        // fica como o sistema define.
        public static void AplicarBarraEscura(Window janela)
        {
            var handle = new WindowInteropHelper(janela).Handle;
            if (handle == IntPtr.Zero) return;
            int sim = 1;
            DwmSetWindowAttribute(handle, 20, ref sim, sizeof(int));
            int fundo = _barra;
            DwmSetWindowAttribute(handle, 35, ref fundo, sizeof(int));
        }
    }
}
