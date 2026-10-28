using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundBrawler;

// Game-side checks around sending keys: where focus is, the idle timers, and which casts lock input.
public static unsafe class GameInput
{
    private const string InputAddon = "KTGInput";

    // Cursed Gaze (Action 49529) shows the crossed-out keyboard sign; read its name from game data so
    // it matches in any client language. Dynamic Sensory Jammer shows the same sign (seen on video).
    private const uint CursedGazeAction = 49529;
    private static readonly string[] ExtraLetterlockNames = ["Sensory Jammer"];
    private static List<string>? letterlockNames;

    // Keys go to whatever text box has focus. Only type when no text box has it (the minigame box
    // opens on Enter) or when it is the minigame's own box, never chat or a search field.
    public static bool SafeToType()
    {
        var stage = AtkStage.Instance();
        if (stage == null) return false;

        var focus = stage->GetFocus();
        if (focus != null)
        {
            var input = stage->RaptureAtkUnitManager->GetAddonByName(InputAddon);
            if (input != null && Contains(&input->UldManager, focus, 0)) return true;
        }

        var module = RaptureAtkModule.Instance();
        return module == null || !module->AtkModule.IsTextInputActive();
    }

    // Component nodes keep their own node lists, so search those too.
    private static bool Contains(AtkUldManager* uld, AtkResNode* target, int depth)
    {
        if (depth > 8) return false;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;
            if (node == target) return true;
            if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null && Contains(&component->UldManager, target, depth + 1)) return true;
            }
        }
        return false;
    }

    // Every key we post keeps the duty's idle timers from running out (see AfkGuard). Every 15 s,
    // log where they stand so a kick can be traced.
    public static void MarkActive()
    {
        // Read before this key's reset, so the brawler counter shows how far it climbed since the last one.
        var brawler = AfkGuard.BrawlerIdle();
        AfkGuard.KeySent();
        if (DateTime.UtcNow <= nextTimerLog) return;
        nextTimerLog = DateTime.UtcNow.AddSeconds(15);
        var timers = InputTimerModule.Instance();
        if (timers != null)
            Plugin.Log.Info($"[afk] hooked={AfkGuard.Hooked} brawler={brawler?.ToString("0.00") ?? "?"} afk={timers->AfkTimer:0.0} content={timers->ContentInputTimer:0.0} input={timers->InputTimer:0.0} limit={timers->InstanceContentAfkTimeLimit:0}");
    }

    private static DateTime nextTimerLog = DateTime.MinValue;

    public static bool IsLetterlock(string castName)
    {
        if (castName.Length == 0) return false;
        letterlockNames ??= LoadLetterlockNames();
        foreach (var name in letterlockNames)
            if (castName.Contains(name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static List<string> LoadLetterlockNames()
    {
        var names = new List<string>(ExtraLetterlockNames);
        var sheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet.GetRowOrDefault(CursedGazeAction) is { } row && row.Name.ExtractText() is { Length: > 0 } name)
            names.Add(name);
        else
            names.Add("Cursed Gaze");
        return names;
    }
}
