using System;
using System.Collections.Generic;
using System.Linq;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Profilo del client (whitelabel): nomi exe e classi finestra.
    /// Default = PokerStars; per le skin (Sisal, Betfair, …) si imposta la
    /// sezione [Client] nell'INI:
    ///   ProcessNames=SisalPoker.exe;POKERSTARS.EXE
    ///   TableClass=SisalTableFrameClass   (opzionale: default PokerStars…)
    /// Load() rileva anche automaticamente il profilo dal nome processo.
    /// </summary>
    public static class ClientProfile
    {
        public static string[] ProcessNames = { "POKERSTARS.EXE", "POKERSTARSONBETFAIR.EXE" };
        public static string TableClass = "PokerStarsTableFrameClass";
        public static string ButtonClass = "PokerStarsButtonClass";
        public static string SliderClass = "PokerStarsSliderClass";
        public static string EditorClass = "PokerStarsSliderEditorClass";
        public static string SelectorClass = "PokerStarsSelectorClass";
        public static string FrameClass = "PokerStarsFrameClass";
        public static string ListClass = "PokerStarsListClass";
        public static string NoteEditorClass = "PokerStarsNoteEditorClass";
        public static string NoteSelectorClass = "PokerStarsNoteSelectorClass";
        public static string ToolTipClass = "PokerStarsToolTipClass";

        /// <summary>Nomi exe noti delle skin: il profilo può restare PokerStars
        /// per le classi se la whitelabel le mantiene.</summary>
        public static readonly string[] SkinHints = { "pokerstars", "pokerstarsonbetfair", "sisal" };

        /// <summary>Nomi titolo del main window da escludere dai tavoli (es. 'Sisal.IT').</summary>
        public static string[] MainTitles = { "Sisal.IT" };

        /// <summary>Modalità interazione: controls (client classico) | coords (client renderizzato).</summary>
        public static string Interaction = "controls";

        /// <summary>Classi finestra candidate a tavolo per i client renderizzati.</summary>
        public static string[] RenderTableClasses = { "GLFW30" };

        /// <summary>Punti d'azione normalizzati (0..1) per l'interazione a coordinate.</summary>
        public static Dictionary<string, (float X, float Y)> Points = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Check"] = (0.758f, 0.938f),
            ["Call"] = (0.758f, 0.938f),
            ["Fold"] = (0.590f, 0.938f),
            ["Raise"] = (0.920f, 0.938f),
            ["BetBox"] = (0.730f, 0.865f),
            ["Pot"] = (0.855f, 0.813f),
            ["Max"] = (0.945f, 0.813f)
        };

        public static void Load(IniFile ini)
        {
            var pn = ini.Get("Client", "ProcessNames");
            if (!string.IsNullOrWhiteSpace(pn))
                ProcessNames = pn.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            void Set(ref string field, string key)
            {
                var v = ini.Get("Client", key);
                if (!string.IsNullOrWhiteSpace(v)) field = v.Trim();
            }
            Set(ref TableClass, "TableClass");
            Set(ref ButtonClass, "ButtonClass");
            Set(ref SliderClass, "SliderClass");
            Set(ref EditorClass, "EditorClass");
            Set(ref SelectorClass, "SelectorClass");
            Set(ref FrameClass, "FrameClass");
            Set(ref ListClass, "ListClass");
            Set(ref NoteEditorClass, "NoteEditorClass");
            Set(ref NoteSelectorClass, "NoteSelectorClass");
            Set(ref ToolTipClass, "ToolTipClass");

            var mt = ini.Get("Client", "MainTitles");
            if (!string.IsNullOrWhiteSpace(mt))
                MainTitles = mt.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            var inter = ini.Get("Client", "Interaction");
            if (!string.IsNullOrWhiteSpace(inter)) Interaction = inter.Trim().ToLowerInvariant();

            var rtc = ini.Get("Client", "RenderTableClasses");
            if (!string.IsNullOrWhiteSpace(rtc))
                RenderTableClasses = rtc.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

            // punti d'azione dalla sezione [Sisal]: PtCheck=x,y …
            foreach (var key in new[] { "PtCheck", "PtCall", "PtFold", "PtRaise", "PtBetBox", "PtPot", "PtMax" })
            {
                var v = ini.Get("Sisal", key);
                if (string.IsNullOrWhiteSpace(v)) continue;
                var p = v.Split(',');
                if (p.Length == 2 &&
                    float.TryParse(p[0], System.Globalization.CultureInfo.InvariantCulture, out var px) &&
                    float.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, out var py))
                    Points[key.Substring(2)] = (px, py);
            }
        }

        /// <summary>Match del nome processo client (esatto o hint skin).</summary>
        public static bool IsClientProcess(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return false;
            var lower = exeName.ToLowerInvariant();
            if (ProcessNames.Any(n => string.Equals(n, exeName, StringComparison.OrdinalIgnoreCase)))
                return true;
            if (ProcessNames.Any(n => n.StartsWith("PokerStars", StringComparison.OrdinalIgnoreCase)) &&
                lower.StartsWith("pokerstars"))
                return true;
            return SkinHints.Any(h => lower.Contains(h));
        }
    }
}
