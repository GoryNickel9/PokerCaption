using System.Text;
using System.Text.RegularExpressions;

namespace PokerCaption.Engine;

[Flags]
internal enum SitOutIntent : byte
{
    None = 0, HandClear = 1, HandSelected = 2, BlindClear = 4, BlindSelected = 8
}

internal sealed class NeverSitOut
{
    private static readonly TimeSpan EvidenceWindow = TimeSpan.FromMinutes(2);
    private static readonly Regex TableLine = new(@"^Table '(.+)'\s+\d+-max\b", RegexOptions.Multiline);
    private static readonly Regex Tournament = new(@"\bTournament\s+(\d+)\s+Table\s+(\d+)\b", RegexOptions.IgnoreCase);
    private static readonly Regex HandHeader = new(@"^(?:Sisal|PokerStars) Hand #\d+:", RegexOptions.Multiline);
    private DateTime? _lastAction, _enteredSitOut;
    private DateTime _nextHistoryCheck;
    private bool _resumed;
    private bool _manualHand, _manualBlind;
    private DateTime? _clearDecisionAt;
    private uint? _clearDecisionInput;
    internal bool EarlySignal { get; private set; }
    private static readonly TimeSpan TransitionWindow = TimeSpan.FromMilliseconds(900);
    internal int Attempts { get; private set; }

    internal bool ShouldResume(bool backVisible, bool heroAct, DateTime utcNow, Func<DateTime, bool> timedOut, int? maximum = null,
        SitOutIntent intent = SitOutIntent.None, bool heroDecision = false, uint? inputTick = null)
    {
        if (!backVisible)
        {
            if (_enteredSitOut.HasValue)
            {
                ResetObservation();
            }
            if (heroAct) _lastAction = utcNow;
            if (intent.HasFlag(SitOutIntent.HandSelected)) _manualHand = true;
            else if (intent.HasFlag(SitOutIntent.HandClear)) _manualHand = false;
            if (intent.HasFlag(SitOutIntent.BlindSelected)) _manualBlind = true;
            else if (intent.HasFlag(SitOutIntent.BlindClear)) _manualBlind = false;

            // Only an observed action with a readable, clear sit-out control can arm the fast path.
            if (_manualHand || _manualBlind || !intent.HasFlag(SitOutIntent.HandClear))
                _clearDecisionAt = null;
            else if (heroDecision && inputTick.HasValue)
            {
                _clearDecisionAt = utcNow;
                _clearDecisionInput = inputTick;
            }
            return false;
        }

        if (!_enteredSitOut.HasValue)
        {
            _enteredSitOut = utcNow;
            EarlySignal = _clearDecisionAt is DateTime clear &&
                utcNow - clear <= TransitionWindow && inputTick.HasValue && inputTick == _clearDecisionInput;
        }
        if (_manualHand || _manualBlind || _resumed || (maximum is int limit && Attempts >= limit) || _lastAction is not DateTime lastAction ||
            _enteredSitOut.Value - lastAction > EvidenceWindow ||
            utcNow - _enteredSitOut.Value > EvidenceWindow || utcNow < _nextHistoryCheck) return false;

        if (EarlySignal && utcNow - _enteredSitOut.Value <= TransitionWindow) return true;
        EarlySignal = false;
        _nextHistoryCheck = utcNow.AddMilliseconds(400);
        return timedOut(lastAction);
    }

    internal void MarkResumed()
    {
        _resumed = true;
        Attempts++;
    }

    internal void ResetObservation()
    {
        _lastAction = _enteredSitOut = null;
        _nextHistoryCheck = default;
        _resumed = false;
        _manualHand = _manualBlind = EarlySignal = false;
        _clearDecisionAt = null;
        _clearDecisionInput = null;
    }

    internal static bool HasRecentTimeout(TableInfo table, DateTime sinceUtc)
    {
        if (string.IsNullOrWhiteSpace(table.HeroNick) || string.IsNullOrWhiteSpace(table.TableName) ||
            table.HeroNick != Path.GetFileName(table.HeroNick) || table.HeroNick is "." or "..") return false;

        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sisal.IT", "HandHistory", table.HeroNick);
        try
        {
            if (!Directory.Exists(directory)) return false;
            foreach (var file in Directory.EnumerateFiles(directory, "HH*.txt")
                .Select(path => new FileInfo(path)).Where(file => file.LastWriteTimeUtc >= sinceUtc)
                .OrderByDescending(file => file.LastWriteTimeUtc))
            {
                using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 65536) stream.Seek(-65536, SeekOrigin.End);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                bool? timeout = LatestCompletedHandTimedOut(reader.ReadToEnd(), table);
                if (timeout.HasValue) return timeout.Value;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    internal static bool? LatestCompletedHandTimedOut(string history, TableInfo table)
    {
        string tableName = table.TableName;
        var tournament = Tournament.Match(table.Title);
        if (tournament.Success) tableName = $"{tournament.Groups[1].Value} {tournament.Groups[2].Value}";
        var headers = HandHeader.Matches(history);
        for (int i = headers.Count - 1; i >= 0; i--)
        {
            int end = i + 1 < headers.Count ? headers[i + 1].Index : history.Length;
            string hand = history[headers[i].Index..end];
            var title = TableLine.Match(hand);
            if (!title.Success || !title.Groups[1].Value.Equals(tableName, StringComparison.OrdinalIgnoreCase)) continue;
            if (!hand.Contains("*** SUMMARY ***", StringComparison.Ordinal)) return false;
            return hand.Split('\n').Any(line => line.TrimEnd('\r', ' ').Equals(
                table.HeroNick + " has timed out", StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }
}
