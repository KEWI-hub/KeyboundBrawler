using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace KeyboundBrawler;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 7;

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
    public TypingSpeed Speed { get; set; } = TypingSpeed.Human;
    public bool SkipOptionalPunctuation { get; set; } = true;

    // Words per minute counts 5 letters as a word, so one letter takes 12000 / WPM ms. Enter is a key too,
    // but the 120 ms box-open wait already covers most of its share.
    public void ApplySpeed(TypingSpeed speed)
    {
        Speed = speed;
        if (speed == TypingSpeed.Custom) return;
        var perKey = 12000 / speed.Wpm();
        WordSeconds = 0;
        CharDelayMs = perKey;
        WordDelayMs = Math.Max(20, perKey - 120);
    }

    public int CharDelayMs { get; set; } = 200;
    // When above 0, letters are spread so each whole word takes about this long; CharDelayMs is then ignored.
    public float WordSeconds { get; set; } = 0f;
    public int WordDelayMs { get; set; } = 80;
    public int RetryAfterMs { get; set; } = 1000;
    public int MinFontSize { get; set; } = 14;

    // Word labels live between these fractions of the screen height; the typing box sits below.
    public float TopIgnore { get; set; } = 0.08f;
    public float BottomIgnore { get; set; } = 0.62f;

    // How many rounds "Play rounds" plays back to back.
    public int LoopRounds { get; set; } = 5;

    // Buy the wanted rewards as soon as the walk opens the shop.
    public bool AutoBuy { get; set; }
    public bool HideOwnedRewards { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

public enum TypingSpeed { Human, Dev, Monkey, Kewi, Custom }

public static class TypingSpeedInfo
{
    public static int Wpm(this TypingSpeed speed) => speed switch
    {
        TypingSpeed.Human => 60,
        TypingSpeed.Dev => 100,
        TypingSpeed.Monkey => 120,
        TypingSpeed.Kewi => 180,
        _ => 0,
    };

    public static string Label(this TypingSpeed speed) => speed switch
    {
        TypingSpeed.Human => "Human",
        TypingSpeed.Dev => "DEV",
        TypingSpeed.Monkey => "Monkey",
        TypingSpeed.Kewi => "KEWI",
        _ => "Custom",
    };
}
