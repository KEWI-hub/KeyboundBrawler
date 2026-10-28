using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace KeyboundBrawler;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/kbb";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;

    public Configuration Config { get; }
    public int SessionRuns { get; set; }
    public int SessionMgc { get; set; }
    public AutoPlayer AutoPlayer { get; }
    public MatchWatcher MatchWatcher { get; }
    public VendorRunner Vendor { get; }
    public ShopBuyer Shop { get; }

    // "/kbb debug" shows the UI scanner, which normal players do not need.
    public bool ShowDebug { get; set; }

    private readonly WindowSystem windowSystem = new("KeyboundBrawler");
    private readonly MainWindow mainWindow;
    private Regex mgcRegex;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        if (Config.Version < 2)
        {
            // v1 typed one word per 3 s, which turned out far too slow.
            Config.WordSeconds = 0.3f;
            Config.WordDelayMs = 100;
            Config.Version = 2;
            Config.Save();
        }
        if (Config.Version < 3)
        {
            // Red "1000 Needles" labels must be typed fast, not skipped.
            Config.DangerCasts = ["Cursed Gaze"];
            Config.UrgentCasts = ["Needles", "Burst"];
            Config.Version = 3;
            Config.Save();
        }
        if (Config.Version < 4)
        {
            // The no-typing sign also comes with the boss's "Dynamic Sensory Jammer".
            if (!Config.DangerCasts.Contains("Sensory Jammer")) Config.DangerCasts.Add("Sensory Jammer");
            Config.Version = 4;
            Config.Save();
        }
        if (Config.Version < 5)
        {
            // Type like a normal person: about 62 WPM (5 letters per word) = 194 ms per key.
            // Enter is a key too; with the 120 ms box-open wait, 75 ms more keeps the same pace.
            Config.WordSeconds = 0;
            Config.CharDelayMs = 194;
            Config.WordDelayMs = 75;
            Config.RetryAfterMs = 1000;
            Config.Version = 5;
            Config.Save();
        }
        if (Config.Version < 6)
        {
            // Speed presets replace hand-tuned sliders; Cursed Gaze / Sensory Jammer are now built in.
            Config.ApplySpeed(Config.Speed);
            Config.Version = 6;
            Config.Save();
        }
        if (Config.Version < 7)
        {
            // 0.6.1 crashed the game on the first tab click; buying waits for an explicit press again.
            Config.AutoBuy = false;
            Config.Version = 7;
            Config.Save();
        }
        // Loading appends saved list items onto the defaults, so drop the repeats.
        Config.DangerCasts = Config.DangerCasts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Config.UrgentCasts = Config.UrgentCasts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        mgcRegex = BuildRegex(Config.MgcPattern);
        AutoPlayer = new AutoPlayer(this);
        MatchWatcher = new MatchWatcher(this);
        Vendor = new VendorRunner(this);
        Shop = new ShopBuyer(this);

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Keybound Brawler tracker. /kbb auto toggles auto-play.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Shop.Dispose();
        Vendor.Dispose();
        MatchWatcher.Dispose();
        AutoPlayer.Dispose();
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        windowSystem.RemoveAllWindows();
        CommandManager.RemoveHandler(Command);
    }

    public void SetPattern(string pattern)
    {
        Config.MgcPattern = pattern;
        mgcRegex = BuildRegex(pattern);
        Config.Save();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            ShowDebug = !ShowDebug;
            mainWindow.IsOpen = true;
            return;
        }
        if (args.Trim().Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            AutoPlayer.Toggle();
            ChatGui.Print($"[Keybound Brawler] Auto-play {(AutoPlayer.Enabled ? "on" : "off")}");
            return;
        }
        mainWindow.Toggle();
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        // MGC rewards arrive as system or loot lines; ignore everything players can type.
        var channel = (XivChatType)((int)message.LogKind & 0x7F);
        if (channel is not (XivChatType.SystemMessage or XivChatType.LootNotice or XivChatType.Notice or (XivChatType)62))
            return;

        var match = mgcRegex.Match(message.Message.TextValue);
        if (!match.Success || match.Groups.Count < 2)
            return;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var amount))
            return;

        RecordRun(amount);
    }

    public void RecordRun(int amount)
    {
        SessionRuns++;
        SessionMgc += amount;
        Config.TotalRuns++;
        Config.TotalMgcEarned += amount;
        Config.CurrentMgc += amount;
        Config.BestRunMgc = Math.Max(Config.BestRunMgc, amount);
        Config.Save();
        Log.Debug($"Keybound Brawler: +{amount} MGC (run {Config.TotalRuns})");
    }

    private static Regex BuildRegex(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return new Regex(@"You obtain ([\d,]+) MGC", RegexOptions.IgnoreCase);
        }
    }
}
