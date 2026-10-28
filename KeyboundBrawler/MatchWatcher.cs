using System;
using System.Linq;
using Dalamud.Plugin.Services;

namespace KeyboundBrawler;

// Spots a Keybound Brawler match by its "[Enter]: Submit Text" hint, since chat and commands are blocked while playing.
// During each match it dumps the visible UI a few times and remembers which addon the minigame uses.
public sealed class MatchWatcher : IDisposable
{
    private const string MatchHint = "Submit Text";
    // One dump every few seconds for the whole match, so boss phases are covered; capped to keep the folder small.
    private const double DumpEverySeconds = 5;
    private const int MaxDumpsPerMatch = 80;

    private readonly Plugin plugin;
    private DateTime nextScan = DateTime.MinValue;
    private DateTime matchStart;
    private int dumpsDone;

    public bool InMatch { get; private set; }
    public string? MatchAddon { get; private set; }
    public string? LastDump { get; private set; }

    public MatchWatcher(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.UtcNow;
        if (now < nextScan) return;
        nextScan = now.AddSeconds(1);

        var addon = FindMatchAddon();
        if (addon == null)
        {
            if (InMatch)
            {
                InMatch = false;
                if (plugin.AutoPlayer.Enabled && plugin.Config.AutoStart)
                    plugin.AutoPlayer.Toggle();
            }
            return;
        }

        if (!InMatch)
        {
            InMatch = true;
            MatchAddon = addon;
            matchStart = now;
            dumpsDone = 0;
            Plugin.Log.Info($"Keybound Brawler match detected in addon {addon}");

            var cfg = plugin.Config;
            // Older builds saved the play-guide addon here; it only holds the key hint, never words.
            if (cfg.GameAddons.Remove(addon))
                cfg.Save();
            if (cfg.AutoStart && !plugin.AutoPlayer.Enabled)
                plugin.AutoPlayer.Toggle();
        }

        if (plugin.Config.AutoDump && dumpsDone < MaxDumpsPerMatch &&
            (now - matchStart).TotalSeconds >= 3 + dumpsDone * DumpEverySeconds)
        {
            dumpsDone++;
            try
            {
                LastDump = UiScanner.DumpToFile($"match{dumpsDone}").Path;
            }
            catch (Exception e)
            {
                Plugin.Log.Warning(e, "UI dump failed");
            }
        }
    }

    private static string? FindMatchAddon() =>
        UiScanner.VisibleAddons()
            .Distinct()
            .FirstOrDefault(name => UiScanner.Texts(name).Any(t => t.Text.Contains(MatchHint, StringComparison.OrdinalIgnoreCase)));
}
