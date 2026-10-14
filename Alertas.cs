using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LiveHelmApp
{
    // Converte um evento do Streamer.bot na linha da lista de atividade. Espelha o mapa ALERTAS do dock LiveHelm
    // (src/streamerbot.js): Cheer, Raid, RewardRedemption e SuperChat seguem a documentação; HypeTrain,
    // HypeChat, KicksGifted, votações e previsões não têm esquema documentado, então cada campo tem vários candidatos e
    // o que falta vira vazio.
    static class Alertas
    {
        public sealed class Linha
        {
            public string Plataforma;
            public string Texto;
            public string Rotulo;
            public string Nome;
            public string Detalhe;
        }

        sealed class Def
        {
            public string Evento;
            public Func<JObject, string> Nome;
            public Func<JObject, string> Rotulo;
            public Func<JObject, string> Detalhe;
        }

        static readonly CultureInfo Pt = new CultureInfo("pt-BR");
        const int SegundosAnuncioPadrao = 60; // quando o aviso de anúncio não diz em quanto tempo ele entra

        // ---- Leitura defensiva de JSON ----

        internal static bool Verdadeiro(JToken t)
        {
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined: return false;
                case JTokenType.String: return (string)t != "";
                case JTokenType.Boolean: return (bool)t;
                case JTokenType.Integer: return (long)t != 0;
                case JTokenType.Float: return (double)t != 0;
                default: return true;
            }
        }

        // Primeiro candidato "verdadeiro" (como o || do JavaScript), ou null.
        internal static JToken Ou(params JToken[] candidatos) => candidatos.FirstOrDefault(Verdadeiro);
        internal static JToken Campo(JToken t, string chave) => (t as JObject)?[chave];

        internal static string Texto(JToken t)
        {
            var v = t as JValue;
            return v == null || v.Value == null ? "" : Convert.ToString(v.Value, CultureInfo.InvariantCulture);
        }

        internal static double? Numero(params JToken[] candidatos)
        {
            foreach (var c in candidatos)
            {
                if (c == null) continue;
                if (c.Type == JTokenType.Integer || c.Type == JTokenType.Float) return (double)c;
                if (c.Type == JTokenType.String && double.TryParse((string)c, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
            }
            return null;
        }

        static string ComUnidade(double? n, string unidade) => n == null ? "" : n.Value.ToString("#,##0.##", Pt) + " " + unidade;
        static string Juntar(string separador, params string[] partes) => string.Join(separador, partes.Where(p => !string.IsNullOrEmpty(p)));

        // ---- Votações e previsões ----

        static string TituloDe(JObject d) =>
            Texto(Ou(d["title"], d["question"], d["name"], Campo(d["poll"], "title"), Campo(d["prediction"], "title"))) is var t && t != "" ? t : "Sem título";

        internal static JArray ListaDe(JObject d) =>
            Ou(d["choices"], d["outcomes"], d["options"], d["answers"], Campo(d["poll"], "choices"), Campo(d["prediction"], "outcomes")) as JArray ?? new JArray();

        internal static string NomeDoItem(JToken i)
        {
            if (i is JObject o) return Texto(Ou(o["title"], o["name"], o["text"], o["label"]));
            return Texto(i);
        }

        static string OpcoesDe(JObject d) => string.Join(" · ", ListaDe(d).Select(NomeDoItem).Where(n => n != ""));

        static string VencedorDe(JObject d)
        {
            var lista = ListaDe(d);
            var ganhador = Ou(d["winningOutcome"], d["winningChoice"]) ?? lista.FirstOrDefault(i =>
                i is JObject o && (Verdadeiro(o["winner"]) || Verdadeiro(o["isWinner"]) || (Verdadeiro(d["winningOutcomeId"]) && Texto(o["id"]) == Texto(d["winningOutcomeId"]))));
            return ganhador == null ? "" : "Venceu: " + NomeDoItem(ganhador);
        }

        static double? SegundosDe(JObject d) => Numero(d["secondsUntilNextAd"], d["secondsUntil"], d["seconds"], d["nextAdIn"], d["duration"]);

        // ---- Mapa de eventos ----

        static Def Ev(string evento, Func<JObject, string> nome = null, Func<JObject, string> rotulo = null, Func<JObject, string> detalhe = null) =>
            new Def { Evento = evento, Nome = nome, Rotulo = rotulo, Detalhe = detalhe };

        static Func<JObject, string> Fixo(string s) => d => s;

        static readonly Dictionary<string, Def> Mapa = new Dictionary<string, Def>
        {
            ["Twitch.Follow"] = Ev("new_follower"),
            ["Kick.Follow"] = Ev("new_follower"),
            ["YouTube.NewSubscriber"] = Ev("new_follower"),
            ["Twitch.Sub"] = Ev("new_subscriber"),
            ["Twitch.ReSub"] = Ev("resub"),
            ["Twitch.GiftSub"] = Ev("subscription_gift"),
            ["Twitch.GiftBomb"] = Ev("subscription_gift"),
            ["Kick.Subscription"] = Ev("new_subscriber"),
            ["Kick.Resubscription"] = Ev("resub"),
            ["Kick.GiftSubscription"] = Ev("subscription_gift"),
            ["Kick.MassGiftSubscription"] = Ev("subscription_gift"),
            ["YouTube.NewSponsor"] = Ev("sponsorship"),
            ["YouTube.MemberMileStone"] = Ev("membermilestone"),
            ["YouTube.MembershipGift"] = Ev("giftpurchase"),
            ["YouTube.GiftMembershipReceived"] = Ev("giftredemption"),
            ["Twitch.RewardRedemption"] = Ev("reward", detalhe: d =>
                Juntar(" · ", Texto(Campo(d["reward"], "title")), ComUnidade(Numero(Campo(d["reward"], "cost")), "pontos"))),
            ["Kick.RewardRedemption"] = Ev("reward", detalhe: d =>
                Juntar(" · ", Texto(Ou(Campo(d["reward"], "title"), d["title"], d["rewardTitle"])), ComUnidade(Numero(Campo(d["reward"], "cost"), d["cost"]), "pontos"))),
            ["Twitch.Cheer"] = Ev("cheer", nome: d => Verdadeiro(d["anonymous"]) ? "Anônimo" : "", detalhe: d => ComUnidade(Numero(d["bits"]), "bits")),
            ["Kick.KicksGifted"] = Ev("gift", detalhe: d =>
                ComUnidade(Numero(d["kicks"], d["amount"], d["quantity"], d["value"], Campo(d["gift"], "amount")), "KICKs")),
            ["YouTube.JewelsGifted"] = Ev("jeweldonation", detalhe: d => ComUnidade(Numero(d["jewels"], d["amount"], d["quantity"]), "joias")),
            ["Twitch.HypeChat"] = Ev("hype_chat", detalhe: d => Juntar(" ", Texto(Ou(d["amount"], d["formattedAmount"])), Texto(d["currency"]))),
            ["YouTube.SuperChat"] = Ev("superchat", detalhe: d => Texto(d["amount"])),
            ["YouTube.SuperSticker"] = Ev("supersticker", detalhe: d => Texto(d["amount"])),
            ["Twitch.Raid"] = Ev("raid", detalhe: d => ComUnidade(Numero(d["viewers"]), "espectadores")),
            ["Twitch.HypeTrainStart"] = Ev("hype_train", nome: Fixo("Começou!"), detalhe: d => Numero(d["level"]) is double n ? "Nível " + n.ToString(Pt) : ""),
            ["Twitch.HypeTrainLevelUp"] = Ev("hype_train", nome: d => Numero(d["level"], d["newLevel"]) is double n ? "Nível " + n.ToString(Pt) + "!" : "Subiu de nível!"),
            ["Twitch.PollCreated"] = Ev("poll", TituloDe, Fixo("Votação aberta"), OpcoesDe),
            ["Twitch.PollCompleted"] = Ev("poll", TituloDe, Fixo("Votação encerrada"), d => VencedorDe(d) is var v && v != "" ? v : OpcoesDe(d)),
            ["Twitch.PollTerminated"] = Ev("poll", TituloDe, Fixo("Votação cancelada")),
            ["YouTube.PollStarted"] = Ev("poll", TituloDe, Fixo("Votação aberta"), OpcoesDe),
            ["YouTube.PollClosed"] = Ev("poll", TituloDe, Fixo("Votação encerrada"), d => VencedorDe(d) is var v && v != "" ? v : OpcoesDe(d)),
            ["Twitch.PredictionCreated"] = Ev("prediction", TituloDe, Fixo("Previsão aberta"), OpcoesDe),
            ["Twitch.PredictionLocked"] = Ev("prediction", TituloDe, Fixo("Previsão travada"), OpcoesDe),
            ["Twitch.PredictionCompleted"] = Ev("prediction", TituloDe, Fixo("Previsão concluída"), d => VencedorDe(d) is var v && v != "" ? v : OpcoesDe(d)),
            ["Twitch.PredictionCanceled"] = Ev("prediction", TituloDe, Fixo("Previsão cancelada")),
            ["Twitch.UpcomingAd"] = Ev("upcoming_ad", nome: d => SegundosDe(d) is double s ? "Em " + s.ToString(Pt) + " s" : "Em breve"),
        };

        // Texto pequeno de cada tipo de alerta (rótulos de ALERTAS em alertas.js), pelo valor de `event`.
        static readonly Dictionary<string, string> RotuloDoEvento = Montar();

        static Dictionary<string, string> Montar()
        {
            var r = new Dictionary<string, string>();
            void Grupo(string rotulo, params string[] eventos) { foreach (var e in eventos) r[e] = rotulo; }
            Grupo("Resgate de pontos", "reward");
            Grupo("Novo seguidor", "follow", "new_follower", "followed");
            Grupo("Bits", "cheer", "gift", "jeweldonation");
            Grupo("Doação", "donation", "superchat", "supersticker", "hype_chat");
            Grupo("Raid", "raid");
            Grupo("Novo sub", "subscriber", "subscription", "new_subscriber", "resub", "subscription_gift", "sponsorship",
                "membershiprenewal", "membermilestone", "giftpurchase", "giftredemption");
            Grupo("Hype train", "hype_train");
            Grupo("Votação", "poll");
            Grupo("Previsão", "prediction");
            Grupo("Anúncio chegando", "upcoming_ad");
            return r;
        }

        // Cor da bolinha de cada plataforma (plataformas.js).
        public static string CorDaPlataforma(string plataforma)
        {
            switch (plataforma)
            {
                case "twitch": return "#9146FF";
                case "youtube": return "#FF3B3B";
                case "kick": return "#53FC18";
                default: return "#8A8A99";
            }
        }

        // null se o evento não vira atividade (tipo desconhecido ou sem nome pra mostrar).
        public static Linha Converter(JObject msg)
        {
            var origem = msg["event"] as JObject;
            if (origem == null) return null;
            var fonte = (string)origem["source"] ?? "";
            var tipo = (string)origem["type"] ?? "";
            if (!Mapa.TryGetValue(fonte + "." + tipo, out var def)) return null;

            var d = msg["data"] as JObject ?? new JObject();
            var usuario = Ou(d["user"], d["targetUser"], d["raider"], d["gifter"], d["author"]) as JObject ?? new JObject();
            var nome = def.Nome?.Invoke(d);
            if (string.IsNullOrEmpty(nome)) nome = Texto(Ou(usuario["name"], usuario["login"]));
            if (string.IsNullOrEmpty(nome)) return null;

            var rotulo = def.Rotulo?.Invoke(d);
            if (string.IsNullOrEmpty(rotulo)) RotuloDoEvento.TryGetValue(def.Evento, out rotulo);
            var detalhe = def.Detalhe?.Invoke(d);
            return new Linha
            {
                Plataforma = fonte.ToLowerInvariant(),
                Texto = Juntar(" · ", rotulo ?? def.Evento, nome, detalhe),
                Rotulo = rotulo ?? def.Evento,
                Nome = nome,
                Detalhe = detalhe ?? "",
            };
        }
    }
}
