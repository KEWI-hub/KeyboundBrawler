using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundBrawler;

// Key is the text node's address, which stays the same for one word label while it lives.
// LabelVisible: everything above the text node is shown. TextVisible: the text node itself is shown too.
public readonly record struct UiText(
    string Addon, uint NodeId, string Text, float X, float Y, byte FontSize, uint Color, int Depth,
    nint Key, bool LabelVisible, bool TextVisible);

// Reads text straight out of the game's UI tree.
public static unsafe class UiScanner
{
    public static List<string> VisibleAddons()
    {
        var result = new List<string>();
        var stage = AtkStage.Instance();
        if (stage == null) return result;

        ref var list = ref stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit == null || !unit->IsVisible) continue;
            result.Add(unit->NameString);
        }
        return result;
    }

    // Visible text only.
    public static List<UiText> Texts(string addonName) =>
        AllTexts(addonName).FindAll(t => t.LabelVisible && t.TextVisible && !string.IsNullOrWhiteSpace(t.Text));

    // Every text node, hidden and empty ones included, so a word the boss hides or blanks keeps its slot.
    public static List<UiText> AllTexts(string addonName)
    {
        var result = new List<UiText>();
        var stage = AtkStage.Instance();
        if (stage == null) return result;

        // Some minigame addons can be open more than once, so walk every instance with this name.
        ref var list = ref stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit == null || !unit->IsVisible || unit->NameString != addonName) continue;
            Walk(addonName, &unit->UldManager, 0, result);
        }
        return result;
    }

    public static (string Path, int Count) DumpToFile(string tag)
    {
        var lines = new List<string>();
        foreach (var name in new HashSet<string>(VisibleAddons()))
            foreach (var t in name.StartsWith("KTG") ? AllTexts(name) : Texts(name))
                lines.Add($"{t.Addon}\t#{t.NodeId}\tdepth={t.Depth}\tx={t.X:0}\ty={t.Y:0}\tsize={t.FontSize}\tcolor={t.Color:X8}" +
                          $"\tkey={t.Key:X}\tlabel={(t.LabelVisible ? 1 : 0)}\ttext={(t.TextVisible ? 1 : 0)}\t{t.Text.Replace('\n', ' ')}");

        // Label colours and signs (e.g. the no-typing icon) are image nodes, so dump those for every minigame addon.
        foreach (var name in new HashSet<string>(VisibleAddons()))
        {
            if (!name.StartsWith("KTG")) continue;
            lines.Add($"--- {name} image nodes ---");
            ForEachUnit(name, unit => WalkImages(&unit->UldManager, 0, lines));
        }

        var dir = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "dumps");
        System.IO.Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, $"ui_dump_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}_{tag}.txt");
        System.IO.File.WriteAllLines(path, lines);
        return (path, lines.Count);
    }

    private static void Walk(string addon, AtkUldManager* uld, int depth, List<UiText> result)
    {
        if (depth > 8) return;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;

            if (node->Type == NodeType.Text)
            {
                var text = (AtkTextNode*)node;
                var c = text->TextColor;
                result.Add(new UiText(addon, node->NodeId, text->NodeText.ToString(), node->ScreenX, node->ScreenY, text->FontSize,
                    (uint)(c.R << 24 | c.G << 16 | c.B << 8 | c.A), depth,
                    (nint)node, IsReallyVisible(node->ParentNode), node->IsVisible() && node->Alpha_2 != 0));
            }
            else if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    Walk(addon, &component->UldManager, depth + 1, result);
            }
        }
    }

    private delegate void UnitAction(AtkUnitBase* unit);

    private static void ForEachUnit(string addonName, UnitAction action)
    {
        var stage = AtkStage.Instance();
        if (stage == null) return;
        ref var list = ref stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit != null && unit->IsVisible && unit->NameString == addonName) action(unit);
        }
    }

    private static void WalkImages(AtkUldManager* uld, int depth, List<string> lines)
    {
        if (depth > 8) return;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || !IsReallyVisible(node)) continue;
            if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                {
                    lines.Add($"comp	#{node->NodeId}	depth={depth}	x={node->ScreenX:0}	y={node->ScreenY:0}");
                    WalkImages(&component->UldManager, depth + 1, lines);
                }
                continue;
            }
            if (node->Type is not (NodeType.Image or NodeType.NineGrid)) continue;
            var part = node->Type == NodeType.Image ? ((AtkImageNode*)node)->PartId : ((AtkNineGridNode*)node)->PartId;
            lines.Add($"img	#{node->NodeId}	depth={depth}	type={node->Type}	part={part}	x={node->ScreenX:0}	y={node->ScreenY:0}" +
                      $"	color={node->Color.R:X2}{node->Color.G:X2}{node->Color.B:X2}{node->Color.A:X2}" +
                      $"	add={node->AddRed},{node->AddGreen},{node->AddBlue}	mul={node->MultiplyRed},{node->MultiplyGreen},{node->MultiplyBlue}");
        }
    }

    private static bool IsReallyVisible(AtkResNode* node)
    {
        for (var n = node; n != null; n = n->ParentNode)
            if (!n->IsVisible() || n->Alpha_2 == 0) return false;
        return true;
    }
}
