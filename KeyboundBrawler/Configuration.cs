using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace KeyboundBrawler;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 5;

    // MGC the player currently holds. Entered by hand once, then grows with every win.
    public int CurrentMgc { get; set; }

    public int TotalRuns { get; set; }
    public int TotalMgcEarned { get; set; }
    public int BestRunMgc { get; set; }

    // Reward names the player wants, and the ones already bought.
    public HashSet<string> Wanted { get; set; } = [];
    public HashSet<string> Owned { get; set; } = [];

    // Matched against system/loot chat lines. Group 1 must capture the amount.
    public string MgcPattern { get; set; } = @"You obtain ([\d,]+) MGC";

    // Auto-play. Addon names come from the UI scanner while the minigame is on screen.
    public List<string> GameAddons { get; set; } = [];
    public bool AutoStart { get; set; }
    public bool AutoDump { get; set; } = true;
    public HashSet<string> IgnoreWords { get; set; } = [];

    // While any enemy casts one of these (crossed-out keyboard sign), pressing any key gets punished, so stop typing.
    public List<string> DangerCasts { get; set; } = ["Cursed Gaze", "Sensory Jammer"];

    // Enemies casting these go first: red label "1000 Needles", gold label "Burst".
    public List<string> UrgentCasts { get; set; } = ["Needles", "Burst"];
    public int CharDelayMs { get; set; } = 194;
    // When above 0, letters are spread so each whole word takes about this long; CharDelayMs is then ignored.
    public float WordSeconds { get; set; } = 0f;
    public int WordDelayMs { get; set; } = 75;
    public int RetryAfterMs { get; set; } = 1000;
    public int MinFontSize { get; set; } = 14;

    // Word labels live between these fractions of the screen height; the typing box sits below.
    public float TopIgnore { get; set; } = 0.08f;
    public float BottomIgnore { get; set; } = 0.62f;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
