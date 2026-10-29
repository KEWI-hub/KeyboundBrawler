using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace KeyboundBrawler;

// "KEWI cream" look: cream paper, brown ink, orange start button, pink selection, like the bird logo.
public sealed class MainWindow : Window
{
    private static readonly Vector4 Paper = Rgb(0xFDF7EA);
    private static readonly Vector4 Panel = Rgb(0xFFFFFF);
    private static readonly Vector4 Header = Rgb(0xF6E7CC);
    private static readonly Vector4 Ink = Rgb(0x5C3A2A);
    private static readonly Vector4 InkSoft = Rgb(0x9A7B68);
    private static readonly Vector4 Orange = Rgb(0xF0A35E);
    private static readonly Vector4 OrangeHover = Rgb(0xF4B67C);
    private static readonly Vector4 Pink = Rgb(0xF6C9C9);
    private static readonly Vector4 PinkHover = Rgb(0xF9DADA);
    private static readonly Vector4 KeyCap = Rgb(0xFAEEDB);
    private static readonly Vector4 Good = Rgb(0x5E9A5A);

    private readonly Plugin plugin;
    private string patternEdit;
    private string? dangerEdit;
    private string? urgentEdit;
    private string? dumpResult;
    private int pushedColors;
    private int pushedVars;

    public MainWindow(Plugin plugin) : base("Keybound Brawler Helper###kbb")
    {
        this.plugin = plugin;
        patternEdit = plugin.Config.MgcPattern;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 360),
            MaximumSize = new Vector2(900, 1400),
        };
    }

    public override void PreDraw()
    {
        (ImGuiCol, Vector4)[] colors =
        [
            (ImGuiCol.WindowBg, Paper), (ImGuiCol.ChildBg, Panel), (ImGuiCol.PopupBg, Panel),
            (ImGuiCol.TitleBg, Header), (ImGuiCol.TitleBgActive, Header), (ImGuiCol.TitleBgCollapsed, Header),
            (ImGuiCol.Text, Ink), (ImGuiCol.TextDisabled, InkSoft), (ImGuiCol.Border, Ink),
            (ImGuiCol.FrameBg, Panel), (ImGuiCol.FrameBgHovered, KeyCap), (ImGuiCol.FrameBgActive, Pink),
            (ImGuiCol.Button, KeyCap), (ImGuiCol.ButtonHovered, PinkHover), (ImGuiCol.ButtonActive, Pink),
            (ImGuiCol.Header, Header), (ImGuiCol.HeaderHovered, KeyCap), (ImGuiCol.HeaderActive, Pink),
            (ImGuiCol.CheckMark, Ink), (ImGuiCol.SliderGrab, Orange), (ImGuiCol.SliderGrabActive, OrangeHover),
            (ImGuiCol.Separator, InkSoft), (ImGuiCol.TableRowBg, Panel), (ImGuiCol.TableRowBgAlt, Paper),
            (ImGuiCol.TableHeaderBg, Header), (ImGuiCol.TableBorderLight, KeyCap),
            (ImGuiCol.ScrollbarBg, Paper), (ImGuiCol.ScrollbarGrab, KeyCap), (ImGuiCol.ScrollbarGrabHovered, Pink),
            (ImGuiCol.PlotHistogram, Orange), (ImGuiCol.ResizeGrip, KeyCap),
        ];
        foreach (var (col, value) in colors) ImGui.PushStyleColor(col, value);
        pushedColors = colors.Length;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 12f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 8));
        pushedVars = 8;
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(pushedVars);
        ImGui.PopStyleColor(pushedColors);
    }

    public override void Draw()
    {
        var cfg = plugin.Config;

        DrawHeader();
        DrawTypingCard();
        DrawSpeedChips(cfg);
        DrawStartButton();
        DrawQuickToggles(cfg);
        DrawMgcProgress(cfg);

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Play rounds"))
            DrawRounds(cfg);
        if (ImGui.CollapsingHeader("Rewards"))
        {
            DrawVendor(cfg);
            DrawRewards(cfg);
        }
        if (ImGui.CollapsingHeader("Word log"))
            DrawWordLog();
        if (ImGui.CollapsingHeader("Settings"))
            DrawSettings(cfg);
        if (plugin.ShowDebug && ImGui.CollapsingHeader("UI scanner (debug)"))
            DrawScanner(cfg);
    }

    private void DrawHeader()
    {
        var iconPath = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "icon.png");
        if (File.Exists(iconPath))
        {
            var icon = Plugin.TextureProvider.GetFromFile(iconPath).GetWrapOrEmpty();
            ImGui.Image(icon.Handle, new Vector2(52, 52));
            ImGui.SameLine();
        }

        // The title bar already names the plugin, so the header says what is going on.
        var auto = plugin.AutoPlayer;
        var watcher = plugin.MatchWatcher;
        var headline = auto.Enabled ? (watcher.InMatch ? "Typing for you" : "Ready, waiting for a match") : "Auto-play is off";
        var sub = watcher.InMatch ? "In a Keybound Brawler match" : "At the Gold Saucer";
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.SetWindowFontScale(1.3f);
        ImGui.TextColored(auto.Enabled ? Orange : Ink, headline);
        ImGui.SetWindowFontScale(1f);
        ImGui.TextDisabled(sub);
        ImGui.EndGroup();
    }

    private void DrawTypingCard()
    {
        var auto = plugin.AutoPlayer;
        if (!auto.Enabled || auto.CurrentWord == null)
        {
            var next = auto.LastTargets.Take(4).Select(t => t.Text).ToList();
            ImGui.TextDisabled(next.Count > 0 ? "On screen: " + string.Join(" · ", next)
                : auto.Enabled ? auto.Status : "Press Start, or tick \"Start when a match begins\".");
            return;
        }
        ImGui.BeginChild("card", new Vector2(-1, 70), true);

        var word = auto.CurrentWord;
        if (auto.Enabled && word != null)
        {
            ImGui.TextUnformatted("Typing");
            ImGui.SameLine();
            ImGui.TextColored(Orange, word);
            DrawKeyCaps(auto.CurrentKeys, auto.TypedCount);
        }
        else
        {
            ImGui.TextUnformatted(auto.Enabled ? auto.Status : "Auto-play is off");
            var next = auto.LastTargets.Take(5).Select(t => t.Text).ToList();
            if (next.Count > 0)
                ImGui.TextDisabled("On screen: " + string.Join(" · ", next));
            else
                ImGui.TextDisabled("Tick \"Start when a match begins\" and queue up.");
        }

        ImGui.EndChild();
    }

    // Letters as little key caps: sent ones solid, the rest faded.
    private static void DrawKeyCaps(string keys, int typed)
    {
        var draw = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var max = ImGui.GetContentRegionAvail().X;
        const float h = 26f, pad = 7f, gap = 4f;
        var x = 0f;
        var shown = 0;
        for (var i = 0; i < keys.Length; i++)
        {
            var text = keys[i] == ' ' ? "_" : keys[i].ToString();
            var w = Math.Max(h - 4, ImGui.CalcTextSize(text).X + pad * 2);
            if (x + w > max) break;
            var min = start + new Vector2(x, 0);
            var alpha = i < typed ? 1f : 0.35f;
            draw.AddRectFilled(min, min + new Vector2(w, h), ImGui.GetColorU32(KeyCap with { W = alpha }), 5f);
            draw.AddRect(min, min + new Vector2(w, h), ImGui.GetColorU32(Ink with { W = alpha }), 5f, ImDrawFlags.None, 1.5f);
            var size = ImGui.CalcTextSize(text);
            draw.AddText(min + new Vector2((w - size.X) / 2, (h - size.Y) / 2), ImGui.GetColorU32(Ink with { W = alpha }), text);
            x += w + gap;
            shown++;
        }
        ImGui.Dummy(new Vector2(max, h));
    }

    private static void DrawSpeedChips(Configuration cfg)
    {
        foreach (var speed in new[] { TypingSpeed.Human, TypingSpeed.Dev, TypingSpeed.Monkey, TypingSpeed.Kewi })
        {
            var selected = cfg.Speed == speed;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, Pink);
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f);
            if (ImGui.Button($"{speed.Label()} {speed.Wpm()}##speed"))
            {
                cfg.ApplySpeed(speed);
                cfg.Save();
            }
            ImGui.PopStyleVar();
            if (selected) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.TextDisabled(cfg.Speed == TypingSpeed.Custom ? "custom" : "wpm");
    }

    private void DrawStartButton()
    {
        var auto = plugin.AutoPlayer;
        ImGui.PushStyleColor(ImGuiCol.Button, auto.Enabled ? Pink : Orange);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, auto.Enabled ? PinkHover : OrangeHover);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 12f);
        if (ImGui.Button(auto.Enabled ? "Stop typing" : "Start typing", new Vector2(-1, 40)))
            auto.Toggle();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawQuickToggles(Configuration cfg)
    {
        var autoStart = cfg.AutoStart;
        if (ImGui.Checkbox("Start when a match begins", ref autoStart)) { cfg.AutoStart = autoStart; cfg.Save(); }
        ImGui.SameLine();
        var skip = cfg.SkipOptionalPunctuation;
        if (ImGui.Checkbox("Skip ? ! - '", ref skip)) { cfg.SkipOptionalPunctuation = skip; cfg.Save(); }
    }

    // Shows the next reward to save for (cheapest wanted one), then the whole wish list in one line.
    private void DrawMgcProgress(Configuration cfg)
    {
        var goal = Rewards.All.Where(r => cfg.Wanted.Contains(r.Name) && !cfg.Owned.Contains(r.Name)).OrderBy(r => r.Cost).ToList();
        if (goal.Count == 0)
        {
            ImGui.TextDisabled($"{cfg.CurrentMgc} MGC. Tick Want on rewards to set a goal.");
            return;
        }

        var next = goal[0];
        var avg = AveragePerRun(cfg);
        var amount = $"{cfg.CurrentMgc} / {next.Cost}";
        ImGui.TextUnformatted($"Next: {next.Name}");
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(amount).X);
        ImGui.TextUnformatted(amount);
        ImGui.ProgressBar(Math.Min(1f, cfg.CurrentMgc / (float)next.Cost), new Vector2(-1, 10), "");

        var shortfall = next.Cost - cfg.CurrentMgc;
        var nextLine = shortfall <= 0 ? "You can buy it now." : avg > 0 ? $"{shortfall} MGC to go, about {Math.Ceiling(shortfall / avg)} runs." : $"{shortfall} MGC to go.";
        ImGui.TextDisabled($"{nextLine} Wish list: {goal.Count} rewards, {goal.Sum(r => r.Cost)} MGC.");
    }

    // Round loop: walks to the Keybound Registrar, plays, leaves, and goes again.
    private void DrawRounds(Configuration cfg)
    {
        var rounds = plugin.Rounds;
        if (rounds.Busy)
        {
            if (ImGui.Button($"Stop (round {Math.Min(rounds.RoundsDone + 1, rounds.RoundsWanted)} of {rounds.RoundsWanted})", new Vector2(-1, 30)))
                rounds.Stop();
        }
        else
        {
            var count = cfg.LoopRounds;
            ImGui.SetNextItemWidth(120);
            if (ImGui.InputInt("rounds", ref count)) { cfg.LoopRounds = Math.Clamp(count, 1, 999); cfg.Save(); }
            ImGui.SameLine();
            if (ImGui.Button($"Play {cfg.LoopRounds} rounds", new Vector2(-1, 0)))
                rounds.Start(cfg.LoopRounds);
        }
        if (!Navmesh.Installed)
            ImGui.TextDisabled("Needs the vnavmesh plugin to walk to the Keybound Registrar.");
        if (rounds.Status.Length > 0)
            ImGui.TextDisabled(rounds.Status);
    }

    private void DrawVendor(Configuration cfg)
    {
        var vendor = plugin.Vendor;
        ImGui.TextUnformatted($"Vendor: {VendorRunner.VendorName}");

        var shop = plugin.Shop;
        if (vendor.Busy || shop.Busy)
        {
            if (ImGui.Button("Stop", new Vector2(-1, 30))) { vendor.Stop(); shop.Stop(); }
        }
        else if (ShopBuyer.ShopOpen)
        {
            if (ImGui.Button("Buy wanted rewards now", new Vector2(-1, 30))) shop.Start();
        }
        else
        {
            if (ImGui.Button(cfg.AutoBuy ? "Walk to vendor and buy" : "Walk to vendor and open shop", new Vector2(-1, 30))) vendor.Start();
        }
        var autoBuy = cfg.AutoBuy;
        if (ImGui.Checkbox("Buy wanted rewards when the shop opens", ref autoBuy)) { cfg.AutoBuy = autoBuy; cfg.Save(); }
        if (!Navmesh.Installed)
            ImGui.TextDisabled("Needs the vnavmesh plugin to walk (talking works without it when you stand close).");
        if (vendor.Status.Length > 0)
            ImGui.TextDisabled(vendor.Status);
        if (shop.Status.Length > 0)
            ImGui.TextDisabled(shop.Status);
    }

    // Every phrase auto-play sent this session, newest first, and whether the game took it.
    private void DrawWordLog()
    {
        var log = plugin.AutoPlayer.Words;
        var hits = log.Count(WordResult.Hit);
        var misses = log.Count(WordResult.Miss);
        var dropped = log.Count(WordResult.Dropped);
        var judged = hits + misses;
        var rate = judged > 0 ? $"{100.0 * hits / judged:0}% landed" : "nothing judged yet";
        ImGui.TextUnformatted($"{hits} hit · {misses} missed · {dropped} dropped · {log.Fixes} typos fixed · {rate}");
        ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize("Clear").X - ImGui.GetStyle().FramePadding.X * 2);
        if (ImGui.SmallButton("Clear##words")) log.Clear();
        ImGui.TextDisabled("Hit: the enemy took it. Missed: still on screen after Enter. Dropped: gone before it was finished.");

        if (log.Entries.Count == 0)
        {
            ImGui.TextDisabled("No words yet. Start auto-play in a match.");
            return;
        }

        if (!ImGui.BeginTable("words", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY, new Vector2(0, 240)))
            return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Word", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        for (var i = log.Entries.Count - 1; i >= 0; i--)
        {
            var e = log.Entries[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled(e.Time.ToString("HH:mm:ss"));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.Word);
            if (e.Hidden || e.Fixes > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(string.Join(", ", new[] { e.Hidden ? "hidden" : null, e.Fixes > 0 ? $"{e.Fixes} typo{(e.Fixes > 1 ? "s" : "")} fixed" : null }.Where(s => s != null)));
            }
            ImGui.TableNextColumn();
            switch (e.Result)
            {
                case WordResult.Hit: ImGui.TextColored(Good, "Hit"); break;
                case WordResult.Miss: ImGui.TextColored(Orange, "Missed"); break;
                case WordResult.Dropped: ImGui.TextDisabled("Dropped"); break;
                default: ImGui.TextDisabled("..."); break;
            }
        }
        ImGui.EndTable();
    }

    private void DrawRewards(Configuration cfg)
    {
        var hideOwned = cfg.HideOwnedRewards;
        if (ImGui.Checkbox("Hide rewards I already have", ref hideOwned)) { cfg.HideOwnedRewards = hideOwned; cfg.Save(); }

        // No inner scroll: the whole list shows and the window scrolls, so no row hides under the header.
        if (!ImGui.BeginTable("rewards", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Want", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Got", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Reward", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("MGC", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var reward in Rewards.All)
        {
            if (cfg.HideOwnedRewards && cfg.Owned.Contains(reward.Name)) continue;
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
                // Ticking "got" after buying spends the MGC; unticking refunds it.
                if (wanted)
                {
                    cfg.CurrentMgc = Math.Max(0, cfg.CurrentMgc + (owned ? -reward.Cost : reward.Cost));
                    cfg.Save();
                }
            }

            ImGui.TableNextColumn();
            if (owned) ImGui.TextDisabled(reward.Name);
            else ImGui.TextUnformatted(reward.Name);
            ImGui.TableNextColumn();
            // Orange price: wanted and affordable right now.
            if (!owned && wanted && reward.Cost <= cfg.CurrentMgc) ImGui.TextColored(Orange, reward.Cost.ToString());
            else ImGui.TextDisabled(reward.Cost.ToString());

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawSettings(Configuration cfg)
    {
        var changed = false;

        Section("MGC");
        var mgc = cfg.CurrentMgc;
        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("held (updates from the shop)", ref mgc, 10, 100)) { cfg.CurrentMgc = Math.Max(0, mgc); changed = true; }
        ImGui.TextDisabled($"This session {plugin.SessionRuns} runs / {plugin.SessionMgc} MGC");
        ImGui.TextDisabled($"All time {cfg.TotalRuns} runs / {cfg.TotalMgcEarned} MGC, best run {cfg.BestRunMgc}");

        Section("Typing speed (moving these makes it custom)");
        var charDelay = cfg.CharDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Delay per letter (ms)", ref charDelay, 0, 300)) { cfg.CharDelayMs = charDelay; cfg.WordSeconds = 0; cfg.Speed = TypingSpeed.Custom; changed = true; }
        var wordDelay = cfg.WordDelayMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Pause after Enter (ms)", ref wordDelay, 0, 1000)) { cfg.WordDelayMs = wordDelay; cfg.Speed = TypingSpeed.Custom; changed = true; }
        var retry = cfg.RetryAfterMs;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("Retry same word after (ms)", ref retry, 200, 5000)) { cfg.RetryAfterMs = retry; changed = true; }

        Section("Casts");
        ImGui.TextDisabled("Cursed Gaze / Sensory Jammer: keeps typing while the bar fills, then holds every key while it lands.");
        dangerEdit ??= string.Join(", ", cfg.DangerCasts);
        ImGui.TextUnformatted("Also treat these casts as no-typing");
        ImGui.SetNextItemWidth(-70);
        ImGui.InputText("##danger", ref dangerEdit, 512);
        ImGui.SameLine();
        if (ImGui.Button("Apply##danger"))
        {
            cfg.DangerCasts = SplitList(dangerEdit);
            changed = true;
        }
        urgentEdit ??= string.Join(", ", cfg.UrgentCasts);
        ImGui.TextUnformatted("Type enemies casting these first");
        ImGui.SetNextItemWidth(-70);
        ImGui.InputText("##urgent", ref urgentEdit, 512);
        ImGui.SameLine();
        if (ImGui.Button("Apply##urgent"))
        {
            cfg.UrgentCasts = SplitList(urgentEdit);
            changed = true;
        }

        Section("Other");
        ImGui.TextUnformatted("MGC chat line (group 1 = amount)");
        ImGui.SetNextItemWidth(-70);
        ImGui.InputText("##pattern", ref patternEdit, 256);
        ImGui.SameLine();
        if (ImGui.Button("Apply##pattern"))
            plugin.SetPattern(patternEdit);

        var autoDump = cfg.AutoDump;
        if (ImGui.Checkbox("Save UI dumps during matches (for bug reports)", ref autoDump)) { cfg.AutoDump = autoDump; changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("Open folder"))
        {
            var dir = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "dumps");
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }

        if (ImGui.Button("Reset stats"))
        {
            cfg.TotalRuns = 0;
            cfg.TotalMgcEarned = 0;
            cfg.BestRunMgc = 0;
            plugin.SessionRuns = 0;
            plugin.SessionMgc = 0;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Support on Ko-fi"))
            Dalamud.Utility.Util.OpenLink("https://ko-fi.com/kewiv");

        if (changed) cfg.Save();
    }

    private void DrawScanner(Configuration cfg)
    {
        // Logs the clicks on the shop and on the brawler entry / exit windows as [rec] lines.
        if (ImGui.Button(plugin.Recorder.Recording ? "Stop recording clicks" : "Record clicks (shop, entry, exit)"))
            plugin.Recorder.Toggle();
        ImGui.SameLine();
        if (ImGui.Button("Dump visible UI text to file"))
        {
            var (path, count) = UiScanner.DumpToFile("manual");
            dumpResult = $"Saved {count} lines to {path}";
        }
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
                    ImGui.TextUnformatted($"#{t.NodeId} y={t.Y:0} size={t.FontSize} {t.Tint} \"{t.Text}\"");
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    private static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Orange, title);
        ImGui.Separator();
    }

    private static List<string> SplitList(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static double AveragePerRun(Configuration cfg) =>
        cfg.TotalRuns == 0 ? 0 : (double)cfg.TotalMgcEarned / cfg.TotalRuns;

    private static void Toggle(Configuration cfg, HashSet<string> set, string name, bool on)
    {
        if (on) set.Add(name); else set.Remove(name);
        cfg.Save();
    }

    private static Vector4 Rgb(uint hex) =>
        new(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);
}
