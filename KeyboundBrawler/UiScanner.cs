using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundBrawler;

// Key is the text node's address, which stays the same for one word label while it lives.
// LabelVisible: everything above the text node is shown. TextVisible: the text node itself is shown too.
// Obscured: the label's noise overlay (#10) is up, so the label is hidden but its word is live and typeable.
public readonly record struct UiText(
    string Addon, uint NodeId, string Text, float X, float Y, byte FontSize, uint Color, int Depth,
    nint Key, bool LabelVisible, bool TextVisible, LabelTint Tint = LabelTint.None, bool Obscured = false);

// Colour of a KTGEWord label, read from the AddRGB tint on its background (node #26).
public enum LabelTint { None, Blue, Gray, Red, Gold, Green }

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
            Walk(addonName, &unit->UldManager, 0, result, LabelTint.None);
        }
        return result;
    }

    // One cast bar per casting enemy: its name (text #4) and how full it is. The fill is image #6,
    // 128 px wide when full. Node layout learned from XeldarAlz/FFXIV-AutoKeyboundBrawler.
    public readonly record struct CastBar(string Name, float Progress);

    private const uint CastNameNode = 4, CastFillNode = 6;
    private const float CastFillFullWidth = 128f;

    public static List<CastBar> CastBars(string addonName)
    {
        var result = new List<CastBar>();
        var stage = AtkStage.Instance();
        if (stage == null) return result;

        ref var list = ref stage->RaptureAtkUnitManager->AtkUnitManager.AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit == null || !unit->IsVisible || unit->NameString != addonName) continue;
            ReadCastBar(&unit->UldManager, result);
            for (var n = 0; n < unit->UldManager.NodeListCount; n++)
            {
                var node = unit->UldManager.NodeList[n];
                if (node == null || (ushort)node->Type < 1000 || !node->IsVisible()) continue;
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null) ReadCastBar(&component->UldManager, result);
            }
        }
        return result;
    }

    private static void ReadCastBar(AtkUldManager* uld, List<CastBar> result)
    {
        AtkResNode* fill = null;
        AtkTextNode* name = null;
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null) continue;
            if (node->NodeId == CastFillNode && node->Type == NodeType.Image) fill = node;
            else if (node->NodeId == CastNameNode && node->Type == NodeType.Text) name = (AtkTextNode*)node;
        }
        if (fill == null || name == null || !fill->IsVisible()) return;
        var progress = Math.Clamp(fill->Width * fill->ScaleX / CastFillFullWidth, 0f, 1f);
        result.Add(new CastBar(name->NodeText.ToString(), progress));
    }

    // Tints seen in match dumps: blue = plain enemy, gray = matches what is typed, red = "1000 Needles",
    // gold = other casts (Germinate, Tankbuster, Self-destruct), green = barrier phrase.
    private static readonly (LabelTint Tint, int R, int G, int B)[] KnownTints =
    [
        (LabelTint.Blue, -255, -255, 0),
        (LabelTint.Gray, -160, -160, -160),
        (LabelTint.Red, -128, -255, -255),
        (LabelTint.Gold, -200, -200, -255),
        (LabelTint.Green, -255, -200, -255),
    ];

    private const uint LabelBackgroundNode = 26;

    private static LabelTint TintOf(AtkUldManager* uld)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node == null || node->NodeId != LabelBackgroundNode) continue;
            var best = LabelTint.None;
            var bestDistance = int.MaxValue;
            foreach (var k in KnownTints)
            {
                var d = Math.Abs(node->AddRed - k.R) + Math.Abs(node->AddGreen - k.G) + Math.Abs(node->AddBlue - k.B);
                if (d < bestDistance) (best, bestDistance) = (k.Tint, d);
            }
            return bestDistance <= 60 ? best : LabelTint.None;
        }
        return LabelTint.None;
    }

    public static (string Path, int Count) DumpToFile(string tag)
    {
        var lines = new List<string>();
        foreach (var name in new HashSet<string>(VisibleAddons()))
            foreach (var t in name.StartsWith("KTG") ? AllTexts(name) : Texts(name))
                lines.Add($"{t.Addon}\t#{t.NodeId}\tdepth={t.Depth}\tx={t.X:0}\ty={t.Y:0}\tsize={t.FontSize}\tcolor={t.Color:X8}" +
                          $"\tkey={t.Key:X}\tlabel={(t.LabelVisible ? 1 : 0)}\ttext={(t.TextVisible ? 1 : 0)}\tobscured={(t.Obscured ? 1 : 0)}\t{t.Text.Replace('\n', ' ')}");

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

    private static void Walk(string addon, AtkUldManager* uld, int depth, List<UiText> result, LabelTint tint, bool obscured = false)
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
                    (nint)node, IsReallyVisible(node->ParentNode), node->IsVisible() && node->Alpha_2 != 0, tint, obscured));
            }
            else if ((ushort)node->Type >= 1000)
            {
                var component = ((AtkComponentNode*)node)->Component;
                if (component != null)
                    Walk(addon, &component->UldManager, depth + 1, result, TintOf(&component->UldManager), IsObscured(&component->UldManager));
            }
        }
    }

    // Cryptic Communications puts a noise overlay (#10) on a word label and hides the label, but the
    // word stays in its text node and can still be typed. A label hidden without the overlay is an
    // enemy that is gone. Overlay node learned from XeldarAlz/FFXIV-AutoKeyboundBrawler.
    private const uint ObscuringOverlayNode = 10;

    private static bool IsObscured(AtkUldManager* uld)
    {
        for (var i = 0; i < uld->NodeListCount; i++)
        {
            var node = uld->NodeList[i];
            if (node != null && node->NodeId == ObscuringOverlayNode && (ushort)node->Type < 1000)
                return IsReallyVisible(node);
        }
        return false;
    }

    // The values an addon was opened with (item names, prices, flags); buying is driven by these indexes.
    public static List<string> AtkValues(string addonName)
    {
        var lines = new List<string>();
        ForEachUnit(addonName, unit =>
        {
            lines.Add($"--- {addonName} AtkValues ({unit->AtkValuesCount}) ---");
            for (var i = 0; i < unit->AtkValuesCount; i++)
            {
                var v = unit->AtkValues[i];
                var text = v.Type switch
                {
                    AtkValueType.Int => v.Int.ToString(),
                    AtkValueType.UInt => v.UInt.ToString(),
                    AtkValueType.Bool => v.Byte.ToString(),
                    AtkValueType.String or
                    AtkValueType.ManagedString or
                    AtkValueType.ConstString => v.String.ToString(),
                    _ => "",
                };
                lines.Add($"{i}	{v.Type}	{text}");
            }
        });
        return lines;
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
