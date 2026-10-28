using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KeyboundBrawler;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;
    private string patternEdit;

    public MainWindow(Plugin plugin) : base("Keybound Brawler Tracker")
    {
        this.plugin = plugin;
        patternEdit = plugin.Config.MgcPattern;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 300),
            MaximumSize = new Vector2(900, 1200),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Config;

        DrawStats(cfg);
        ImGui.Separator();
        DrawAutoPlay(cfg);
        ImGui.Separator();
        if (ImGui.CollapsingHeader("Rewards", ImGuiTreeNodeFlags.DefaultOpen))
            DrawRewards(cfg);

        if (ImGui.CollapsingHeader("UI scanner"))
            DrawScanner(cfg);

        if (ImGui.CollapsingHeader("Settings"))
            DrawSettings(cfg);
    }

    private void DrawStats(Configuration cfg)
    {
        var mgc = cfg.CurrentMgc;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Current MGC", ref mgc, 10, 100))
        {
            cfg.CurrentMgc = Math.Max(0, mgc);
            cfg.Save();
        }

        ImGui.TextUnformatted($"This session: {plugin.SessionRuns} runs, {plugin.SessionMgc} MGC");
        ImGui.TextUnformatted($"All time: {cfg.TotalRuns} runs, {cfg.TotalMgcEarned} MGC, best run {cfg.BestRunMgc}");
        ImGui.TextUnformatted($"Average per run: {AveragePerRun(cfg):0.#} MGC");
    }

    private void DrawRewards(Configuration cfg)
    {
        var remaining = Rewards.All
            .Where(r => cfg.Wanted.Contains(r.Name) && !cfg.Owned.Contains(r.Name))
            .Sum(r => r.Cost);
        var shortfall = Math.Max(0, remaining - cfg.CurrentMgc);
        var avg = AveragePerRun(cfg);

        ImGui.TextUnformatted($"Wanted rewards still to buy: {remaining} MGC");
        ImGui.TextUnformatted(shortfall == 0
            ? "You have enough MGC for everything you picked."
            : avg > 0
                ? $"Short by {shortfall} MGC, about {Math.Ceiling(shortfall / avg)} more runs."
                : $"Short by {shortfall} MGC. Play a run to get an estimate.");

        if (!ImGui.BeginTable("rewards", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new Vector2(0, 260)))
            return;

        ImGui.TableSetupColumn("Want", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Owned", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("MGC", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var reward in Rewards.All)
        {
            ImGui.TableNextRow();
            ImGui.PushID(reward.Name);

            ImGui.TableNextColumn();
            var wanted = cfg.Wanted.Contains(reward.Name);
            if (ImGui.Checkbox("##want", ref wanted))
                Toggle(cfg, cfg.Wanted, reward.Name, wanted);

            ImGui.TableNextColumn();
            var owned = cfg.Owned.Contains(reward.Name);
            if (ImGui.Checkbox("##owned", ref owned))
            {
                Toggle(cfg, cfg.Owned, reward.Name, owned);
                // Ticking "owned" after buying spends the MGC; unticking refunds it.
                if (wanted)
                {
                    cfg.CurrentMgc = Math.Max(0, cfg.CurrentMgc + (owned ? -reward.Cost : reward.Cost));
                    cfg.Save();
                }
            }

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(reward.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(reward.Cost.ToString());

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawSettings(Configuration cfg)
    {
        ImGui.TextWrapped("Chat pattern for MGC rewards. Group 1 must capture the amount. Change it if your client language is not English.");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##pattern", ref patternEdit, 256);
        if (ImGui.Button("Apply pattern"))
            plugin.SetPattern(patternEdit);

        ImGui.SameLine();
        if (ImGui.Button("Reset stats"))
        {
            cfg.TotalRuns = 0;
            cfg.TotalMgcEarned = 0;
            cfg.BestRunMgc = 0;
            plugin.SessionRuns = 0;
            plugin.SessionMgc = 0;
            cfg.Save();
        }
    }

    private void DrawAutoPlay(Configuration cfg)
    {
        var auto = plugin.AutoPlayer;
        if (ImGui.Button(auto.Enabled ? "Stop auto-play" : "Start auto-play"))
            auto.Toggle();
        ImGui.SameLine();
        ImGui.TextUnformatted(auto.Status);

        ImGui.TextUnformatted("Speed:");
        foreach (var speed in new[] { TypingSpeed.Human, TypingSpeed.Programmer, TypingSpeed.Monkey, TypingSpeed.Kewi })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{speed.Label()} {speed.Wpm()}##speed", cfg.Speed == speed))
            {
                cfg.ApplySpeed(speed);
                cfg.Save();
            }
        }
        if (cfg.Speed == TypingSpeed.Custom)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(custom)");
        }

        var skipPunct = cfg.SkipOptionalPunctuation;
        if (ImGui.Checkbox("Skip optional punctuation ( ? ! - ' )", ref skipPunct)) { cfg.SkipOptionalPunctuation = skipPunct; cfg.Save(); }

        var autoStart = cfg.AutoStart;
        if (ImGui.Checkbox("Start auto-play when a match begins", ref autoStart)) { cfg.AutoStart = autoStart; cfg.Save(); }
        var autoDump = cfg.AutoDump;
        if (ImGui.Checkbox("Dump UI text automatically during matches", ref autoDump)) { cfg.AutoDump = autoDump; cfg.Save(); }
        ImGui.SameLine();
        if (ImGui.SmallButton("Open dump folder"))
        {
            var dir = System.IO.Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "dumps");
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }

        var watcher = plugin.MatchWatcher;
        ImGui.TextUnformatted(watcher.InMatch ? $"Match in progress (addon {watcher.MatchAddon})" : "Not in a match");
        if (watcher.LastDump != null)
            ImGui.TextWrapped($"Last dump: {watcher.LastDump}");

        ImGui.TextUnformatted($"Word addon: {string.Join(", ", cfg.GameAddons.Append(AutoPlayer.WordAddon).Distinct())}");

        if (auto.Enabled && auto.LastTargets.Count > 0)
            ImGui.TextWrapped("Words on screen: " + string.Join("  |  ", auto.LastTargets.Select(t => t.Text)));

        if (!ImGui.TreeNode("Auto-play settings"))
            return;

        var changed = false;
        var wordSeconds = cfg.WordSeconds;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Seconds per word (0 = use letter delay)", ref wordSeconds, 0f, 6f, "%.1f")) { cfg.WordSeconds = wordSeconds; cfg.Speed = TypingSpeed.Custom; changed = true; }

        var charDelay = cfg.CharDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Delay per letter (ms)", ref charDelay, 0, 300)) { cfg.CharDelayMs = charDelay; cfg.Speed = TypingSpeed.Custom; changed = true; }

        var wordDelay = cfg.WordDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Delay after Enter (ms)", ref wordDelay, 0, 1000)) { cfg.WordDelayMs = wordDelay; cfg.Speed = TypingSpeed.Custom; changed = true; }

        var retry = cfg.RetryAfterMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Retry same word after (ms)", ref retry, 200, 5000)) { cfg.RetryAfterMs = retry; changed = true; }

        var minFont = cfg.MinFontSize;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Min font size of a word", ref minFont, 0, 40)) { cfg.MinFontSize = minFont; changed = true; }

        var top = cfg.TopIgnore;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Ignore text above (screen %)", ref top, 0f, 0.5f)) { cfg.TopIgnore = top; changed = true; }

        var bottom = cfg.BottomIgnore;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat("Ignore text below (screen %)", ref bottom, 0.3f, 1f)) { cfg.BottomIgnore = bottom; changed = true; }

        dangerEdit ??= string.Join(", ", cfg.DangerCasts);
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("Stop all typing while any enemy casts (comma list)", ref dangerEdit, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Apply##danger"))
        {
            cfg.DangerCasts = dangerEdit.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }

        urgentEdit ??= string.Join(", ", cfg.UrgentCasts);
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("Type first when enemy casts (comma list)", ref urgentEdit, 512);
        ImGui.SameLine();
        if (ImGui.SmallButton("Apply##urgent"))
        {
            cfg.UrgentCasts = urgentEdit.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            changed = true;
        }

        if (changed) cfg.Save();
        ImGui.TreePop();
    }

    private void DrawScanner(Configuration cfg)
    {
        ImGui.TextWrapped("Open this during a match. Add the addon that holds the enemy words, then use Dump to save every visible text for debugging.");

        if (ImGui.Button("Dump visible UI text to file"))
            dumpResult = DumpAll();
        if (dumpResult != null)
            ImGui.TextWrapped(dumpResult);

        foreach (var name in UiScanner.VisibleAddons().Distinct().OrderBy(n => n))
        {
            var texts = UiScanner.Texts(name);
            if (texts.Count == 0) continue;

            ImGui.PushID(name);
            var selected = cfg.GameAddons.Contains(name);
            if (ImGui.Checkbox("##use", ref selected))
            {
                if (selected) cfg.GameAddons.Add(name); else cfg.GameAddons.Remove(name);
                cfg.Save();
            }
            ImGui.SameLine();
            if (ImGui.TreeNode($"{name} ({texts.Count} texts)"))
            {
                foreach (var t in texts.Take(40))
                    ImGui.TextUnformatted($"#{t.NodeId} y={t.Y:0} size={t.FontSize} \"{t.Text}\"");
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private string? dumpResult;
    private string? dangerEdit;
    private string? urgentEdit;

    private static string DumpAll()
    {
        var (path, count) = UiScanner.DumpToFile("manual");
        return $"Saved {count} lines to {path}";
    }

    private static double AveragePerRun(Configuration cfg) =>
        cfg.TotalRuns == 0 ? 0 : (double)cfg.TotalMgcEarned / cfg.TotalRuns;

    private static void Toggle(Configuration cfg, System.Collections.Generic.HashSet<string> set, string name, bool on)
    {
        if (on) set.Add(name); else set.Remove(name);
        cfg.Save();
    }
}
