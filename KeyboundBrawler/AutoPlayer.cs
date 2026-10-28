using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;

namespace KeyboundBrawler;

// Reads the enemy word labels from the minigame UI and types them in, one character per tick.
public sealed class AutoPlayer : IDisposable
{
    // HUD text that is never a word to type.
    private static readonly Regex HudText = new(
        @"^(HP|SCORE|BONUS|Burst|X\d+(\.\d+)?|[\d,]+(/\d+)?)$|\[Enter\]|Defeat .*enem|Inputs (dis|re-en)abled|Barrier reinforced|reinforcements fade",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A word label whose text is blanked or covered up, e.g. "", "???" or "****".
    private static readonly Regex Masked = new(@"^[\?？\*＊・…_\-\s]*$", RegexOptions.CultureInvariant);

    // Holds the enemy word labels during a match (found from in-game UI dumps).
    public const string WordAddon = "KTGEWord";
    private const string InputAddon = "KTGInput";
    private const uint InputTextNode = 16;

    private readonly Plugin plugin;
    private readonly Dictionary<string, DateTime> recentlySent = [];
    private readonly Random rng = new();
    private readonly Dictionary<nint, string> remembered = [];
    private int hiddenDumps;
    private int vanishDumps;
    private DateTime? mismatchSince;
    private int castDumps;
    private bool noTypingSign;
    private bool boxClosed = true;
    private const int OpenBoxDelayMs = 120;

    private string? typing;
    private int typedCount;
    private DateTime nextAction = DateTime.MinValue;

    public bool Enabled { get; set; }
    private string status = "Off";

    // Logged on change, since the window cannot be watched easily during a match.
    public string Status
    {
        get => status;
        private set
        {
            if (value == status) return;
            status = value;
            Plugin.Log.Info($"[auto] {value}");
        }
    }
    public IReadOnlyList<UiText> LastTargets { get; private set; } = [];

    public AutoPlayer(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    public void Toggle()
    {
        Enabled = !Enabled;
        ResetTyping();
        Status = Enabled ? "Waiting for the minigame" : "Off";
    }

    // Keys posted to the window type fine but do not count as activity, so the duty kicks us
    // for "extended inactivity". Reset the game's idle timers while auto-play runs.
    private static unsafe void KeepActive()
    {
        var timers = FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance()->GetInputTimerModule();
        if (timers == null) return;
        timers->AfkTimer = 0;
        timers->ContentInputTimer = 0;
        timers->InputTimer = 0;
    }

    private void OnUpdate(IFramework framework)
    {
        if (!Enabled) return;
        KeepActive();

        var cfg = plugin.Config;
        var addons = cfg.GameAddons.Append(WordAddon).Distinct();

        var all = addons.SelectMany(UiScanner.AllTexts).ToList();
        if (!all.Any(t => t.LabelVisible))
        {
            ResetTyping();
            remembered.Clear();
            hiddenDumps = 0;
            boxClosed = true;
            Status = "Waiting for the minigame";
            return;
        }

        LastTargets = FindTargets(Remember(all), cfg);

        var now = DateTime.UtcNow;
        if (now < nextAction) return;

        // Crossed-out keyboard sign: any key press now explodes and locks input, so wait it out untouched.
        if (noTypingSign)
        {
            mismatchSince = null;
            Status = "Paused: no-typing sign";
            return;
        }

        if (!SyncWithInputBox(now)) return;

        // The word we were typing disappeared (someone else killed it): clear what we typed and pick again.
        if (typing != null && LastTargets.All(t => t.Text != typing))
        {
            // Also record the first few of these: if the boss hides its whole label, this is where it shows up.
            if (typedCount > 0 && typedCount < typing.Length && vanishDumps < 3 && cfg.AutoDump)
            {
                vanishDumps++;
                UiScanner.DumpToFile("vanish");
            }
            if (typedCount > 0)
            {
                for (var i = 0; i < typedCount; i++) KeySender.Backspace();
                ResetTyping();
                Status = "Word gone, clearing what we typed";
                Delay(OpenBoxDelayMs);
                return;
            }
            typing = null;
        }

        if (typing == null)
        {
            var next = LastTargets.FirstOrDefault(t => !RecentlySent(t.Text, now));
            if (next.Text == null)
            {
                Status = "No word to type";
                return;
            }
            typing = next.Text;
            typedCount = 0;

            // The box closes after every submit (and starts closed), so open it before the first letter.
            if (boxClosed)
            {
                KeySender.Enter();
                boxClosed = false;
                Status = "Opening the input box";
                Delay(OpenBoxDelayMs);
                return;
            }
        }

        if (typedCount < typing.Length)
        {
            KeySender.Char(typing[typedCount++]);
            Status = $"Typing \"{typing}\"";
            Delay(cfg.WordSeconds > 0 ? (int)(cfg.WordSeconds * 1000 / (typing.Length + 1)) : cfg.CharDelayMs);
            return;
        }

        KeySender.Enter();
        boxClosed = true;
        recentlySent[typing] = now;
        Status = $"Sent \"{typing}\"";
        ResetTyping();
        Delay(cfg.WordDelayMs);
    }

    // Compares the game's input box with what we think we typed. The box starts closed and needs Enter to
    // open, and keys can get lost, so fix things up before typing more. Returns false when it acted this tick.
    private bool SyncWithInputBox(DateTime now)
    {
        var input = UiScanner.Texts(InputAddon).FirstOrDefault(t => t.NodeId == InputTextNode).Text ?? "";
        var expected = typing == null ? "" : typing[..typedCount];
        if (input == expected)
        {
            mismatchSince = null;
            return true;
        }

        // The game shows a key a frame or two after we post it, so only act on a mismatch that sticks.
        mismatchSince ??= now;
        if ((now - mismatchSince.Value).TotalMilliseconds < 150) return false;
        mismatchSince = null;

        if (input.Length > 0)
        {
            // Leftover or wrong text: wipe it and start this word again. Never Esc, that quits on an empty box.
            for (var i = 0; i < input.Length; i++) KeySender.Backspace();
            Status = "Clearing the input box";
        }
        else
        {
            // We typed but nothing showed up: the box is closed.
            KeySender.Enter();
            boxClosed = false;
            Status = "Opening the input box (after a lost key)";
        }
        typedCount = 0;
        Delay(OpenBoxDelayMs);
        return false;
    }

    // The boss flashes its word and then hides it. While a label is still on screen, keep the last word we
    // saw on it, so a label whose text was hidden or replaced with "???" is still typed from memory.
    private List<UiText> Remember(List<UiText> all)
    {
        var result = new List<UiText>();
        var fromMemory = false;
        foreach (var t in all)
        {
            if (!t.LabelVisible) continue;

            var shown = t.TextVisible && !Masked.IsMatch(t.Text);
            // "Partially obscured" may swap some letters for symbols; keep the clean word we saw first.
            if (shown && remembered.TryGetValue(t.Key, out var known) && IsDegraded(known, t.Text))
            {
                result.Add(t with { Text = known, TextVisible = false });
                fromMemory = true;
            }
            else if (shown)
            {
                remembered[t.Key] = t.Text;
                result.Add(t);
            }
            else if (remembered.TryGetValue(t.Key, out var word))
            {
                result.Add(t with { Text = word, TextVisible = false });
                fromMemory = true;
            }
        }

        // Labels that are gone for good take their memory with them.
        var live = all.Where(t => t.LabelVisible).Select(t => t.Key).ToHashSet();
        foreach (var key in remembered.Keys.Where(k => !live.Contains(k)).ToList())
            remembered.Remove(key);

        // Save what the hidden word looks like in the UI, a few times per match, for tuning.
        if (fromMemory && hiddenDumps < 3 && plugin.Config.AutoDump)
        {
            hiddenDumps++;
            UiScanner.DumpToFile("hidden");
        }
        return result;
    }

    private static bool IsDegraded(string known, string now)
    {
        if (known == now || known.Length != now.Length) return false;
        for (var i = 0; i < now.Length; i++)
            if (now[i] != known[i] && char.IsLetterOrDigit(now[i])) return false;
        return true;
    }

    // Visible KTG texts that contain one of the given cast names.
    private static List<UiText> CastsNamed(List<UiText> ktgTexts, List<string> names) =>
        names.Count == 0 ? [] : ktgTexts.Where(t => names.Any(d => t.Text.Contains(d, StringComparison.OrdinalIgnoreCase))).ToList();

    // The cast bar sits just under its enemy's word label.
    private static bool NearCast(UiText word, List<UiText> casts) =>
        casts.Any(c => Math.Abs(c.X - word.X) < 160 && c.Y - word.Y is > -20 and < 120);

    private List<UiText> FindTargets(List<UiText> texts, Configuration cfg)
    {
        var ignore = cfg.IgnoreWords;
        var ktgTexts = UiScanner.VisibleAddons().Where(n => n.StartsWith("KTG")).Distinct().SelectMany(UiScanner.Texts).ToList();
        // No-entry sign: typing that enemy's word gets punished.
        var danger = CastsNamed(ktgTexts, cfg.DangerCasts);
        noTypingSign = danger.Count > 0;
        // Red / gold labels: interrupt these before anything else.
        var urgent = CastsNamed(ktgTexts, cfg.UrgentCasts);
        if ((danger.Count > 0 || urgent.Count > 0) && castDumps < 3 && cfg.AutoDump)
        {
            castDumps++;
            UiScanner.DumpToFile(danger.Count > 0 ? "danger" : "urgent");
        }
        var height = ScreenHeight();
        return texts
            .Where(t => t.FontSize >= cfg.MinFontSize)
            .Select(t => t with { Text = t.Text.Trim() })
            .Where(t => t.Text.Length > 0 && !HudText.IsMatch(t.Text) && !ignore.Contains(t.Text))
            .Where(t => t.Y > cfg.TopIgnore * height && t.Y < cfg.BottomIgnore * height)
            .Where(t => !cfg.DangerCasts.Any(d => t.Text.Contains(d, StringComparison.OrdinalIgnoreCase)))
            .Where(t => !cfg.UrgentCasts.Contains(t.Text, StringComparer.OrdinalIgnoreCase))
            // Hidden (memorised) words first, before we forget them; then lower on screen, i.e. closer enemies.
            .OrderByDescending(t => NearCast(t, urgent))
            .ThenBy(t => t.TextVisible)
            .ThenByDescending(t => t.Y)
            .ToList();
    }

    private static unsafe float ScreenHeight()
    {
        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        return device == null ? 1080f : device->Height;
    }

    private bool RecentlySent(string word, DateTime now) =>
        recentlySent.TryGetValue(word, out var at) && (now - at).TotalMilliseconds < plugin.Config.RetryAfterMs;

    private void Delay(int baseMs)
    {
        // A little jitter so the rhythm is not perfectly even.
        var jitter = baseMs / 4;
        nextAction = DateTime.UtcNow.AddMilliseconds(baseMs + rng.Next(-jitter, jitter + 1));
    }

    private void ResetTyping()
    {
        typing = null;
        typedCount = 0;
    }
}
