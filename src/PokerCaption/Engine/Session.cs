using System;
using System.IO;
using System.Text;
using System.Threading;
using PokerCaption.Config;

namespace PokerCaption.Engine
{
    /// <summary>
    /// Statistiche, semantica PokerCaption (file global_stats, [GStats]):
    /// - allHandsCount: mani cumulativo su tutte le sessioni (persistito)
    /// - sessionTime: secondi cumulativi storici (persistito)
    /// - DISPLAY (GenHUD): per-sessione — 0:00 alla prima mano, mani da 0
    /// - il tempo avanza solo con il hero in mano; si ferma a sitout/uscita
    /// </summary>
    public sealed class SessionStats
    {
        public long SessionHands;                // mani di questa sessione (display)
        public long AllHands;                    // cumulativo (persistito)
        public long AllTrnys;
        public double TotalBuyin;
        public double TotalBuyinRake;
        public double TotalCashout;
        public double SitoutTables;

        public bool Started;                     // false finché prima mano
        public double RunActiveSeconds;          // secondi attivi di QUESTA sessione
        public bool InHandNow;                   // ultimo stato inHand visto dal servizio
        private double _cumSecondsAtLoad;        // cumulativo storico dal file
        private DateTime _lastUtc = DateTime.UtcNow;
        private readonly object _sessionGate = new();
        private double _handsPerHour;

        public static SessionStats Current = new SessionStats();

        // display per-sessione: 0:00 alla prima mano
        public TimeSpan SessionTime => Started ? TimeSpan.FromSeconds(RunActiveSeconds) : TimeSpan.Zero;
        public double HandsPerHour => Volatile.Read(ref _handsPerHour);
        public double Net => TotalCashout - TotalBuyin;

        internal long RecordHand()
        {
            lock (_sessionGate)
            {
                SessionHands++;
                AllHands++;
                Volatile.Write(ref _handsPerHour, RunActiveSeconds > 0 ? 3600.0 * SessionHands / RunActiveSeconds : 0);
                return SessionHands;
            }
        }

        /// <summary>Chiamato a ogni ciclo di scansione e dal timer HUD: inHand = hero in mano su almeno un tavolo.
        /// Il tempo PARTE alla prima mano conteggiata (carte effettivamente ricevute), non al primo pixel "in mano".</summary>
        public void Tick(bool inHand)
        {
            lock (_sessionGate)
            {
                var now = DateTime.UtcNow;
                if (!Started && SessionHands > 0) Started = true;
                if (Started && inHand)
                    RunActiveSeconds += (now - _lastUtc).TotalSeconds;
                _lastUtc = now;
            }
        }

        // reset dal menu del General HUD
        public void ResetSessionTime() => ResetSession(time: true, hands: false);
        public void ResetSessionHands() => ResetSession(time: false, hands: true);
        public void ResetSessionTags() => ResetSession(time: true, hands: true);

        private void ResetSession(bool time, bool hands)
        {
            lock (_sessionGate)
            {
                if (time) RunActiveSeconds = 0;
                if (hands) SessionHands = 0;
                if (time && hands) AllTrnys = 0;
                Volatile.Write(ref _handsPerHour, 0);
            }
        }

        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            var ini = IniFile.Load(path);
            AllHands = ini.GetLong("GStats", "allHandsCount", 0);
            TotalBuyin = ini.GetDouble("GStats", "totalBuyin", 0);
            TotalBuyinRake = ini.GetDouble("GStats", "totalBuyinRake", 0);
            _cumSecondsAtLoad = ini.GetDouble("GStats", "sessionTime", 0);
            // Started resta false: il tempo riparte dalla prima mano di questa sessione
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[GStats]");
            sb.AppendLine($"allHandsCount={AllHands}");
            sb.AppendLine($"totalBuyin={TotalBuyin.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"totalBuyinRake={TotalBuyinRake.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"sessionTime={(long)(_cumSecondsAtLoad + RunActiveSeconds)}");
            var h = (_cumSecondsAtLoad + RunActiveSeconds) / 3600.0;
            var wr = h > 0.02 ? Net / h : 0;
            sb.AppendLine($"wr_time={wr.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            File.WriteAllText(path, sb.ToString());
        }

        public static string FormatTime(TimeSpan t)
        {
            // m:ss (es. "0:00", "12:34")
            return $"{(int)t.TotalMinutes}:{t.Seconds:00}";
        }
    }
}
