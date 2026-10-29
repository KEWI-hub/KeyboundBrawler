using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboundBrawler;

public enum WordResult { Waiting, Hit, Miss, Dropped }

// One phrase auto-play typed. Fixes = times a wrong or lost key had to be cleared before it went in.
public sealed class WordEntry
{
    public DateTime Time { get; init; }
    public string Word { get; init; } = "";
    public nint Key { get; init; }
    public bool Hidden { get; init; }
    public int Fixes { get; init; }
    public WordResult Result { get; set; }
}

// Every phrase sent this session and whether it landed. A phrase landed when its label shows another
// word (or is gone) shortly after Enter; still the same word after a while means the game refused it.
public sealed class WordLog
{
    private const int MaxEntries = 500;
    // The old phrase lingers for a moment after a hit, so only call it a miss after this long.
    private static readonly TimeSpan MissAfter = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan CheckAfter = TimeSpan.FromMilliseconds(150);

    private readonly List<WordEntry> entries = [];
    // Session totals, kept apart from the list because old entries fall off it.
    private readonly Dictionary<WordResult, int> counts = [];

    public IReadOnlyList<WordEntry> Entries => entries;
    public int Count(WordResult result) => counts.GetValueOrDefault(result);
    public int Fixes { get; private set; }

    public void Clear()
    {
        entries.Clear();
        counts.Clear();
        Fixes = 0;
    }

    public void Sent(string word, nint key, bool hidden, int fixes) =>
        Add(new WordEntry { Time = DateTime.Now, Word = word, Key = key, Hidden = hidden, Fixes = fixes, Result = WordResult.Waiting });

    // The phrase left the screen before it was finished.
    public void Dropped(string word, nint key, bool hidden, int fixes) =>
        Add(new WordEntry { Time = DateTime.Now, Word = word, Key = key, Hidden = hidden, Fixes = fixes, Result = WordResult.Dropped });

    // `onScreen` maps each live label to the word it shows now (hidden labels from memory).
    public void Check(IReadOnlyDictionary<nint, string> onScreen)
    {
        var now = DateTime.Now;
        foreach (var e in entries)
        {
            if (e.Result != WordResult.Waiting || now - e.Time < CheckAfter) continue;
            if (!onScreen.TryGetValue(e.Key, out var shown) || shown != e.Word) Settle(e, WordResult.Hit);
            else if (now - e.Time > MissAfter)
            {
                Settle(e, WordResult.Miss);
                Plugin.Log.Info($"[words] Miss \"{e.Word}\" still on screen");
            }
        }
    }

    // The match ended: every label is gone, so whatever was still waiting finished its enemy.
    public void MatchOver()
    {
        var waiting = entries.Where(e => e.Result == WordResult.Waiting).ToList();
        if (waiting.Count == 0) return;
        foreach (var e in waiting) Settle(e, WordResult.Hit);
        Plugin.Log.Info($"[words] totals: {Count(WordResult.Hit)} hit, {Count(WordResult.Miss)} missed, {Count(WordResult.Dropped)} dropped, {Fixes} typos fixed");
    }

    private void Settle(WordEntry e, WordResult result)
    {
        counts[e.Result] = Count(e.Result) - 1;
        e.Result = result;
        counts[result] = Count(result) + 1;
    }

    private void Add(WordEntry entry)
    {
        counts[entry.Result] = Count(entry.Result) + 1;
        Fixes += entry.Fixes;
        entries.Add(entry);
        if (entries.Count > MaxEntries) entries.RemoveAt(0);
        Plugin.Log.Info($"[words] {entry.Result} \"{entry.Word}\"{(entry.Hidden ? " (hidden)" : "")}{(entry.Fixes > 0 ? $" fixes={entry.Fixes}" : "")}");
    }
}
