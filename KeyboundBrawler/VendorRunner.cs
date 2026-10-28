using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace KeyboundBrawler;

// Walks to the remembered MGC vendor with vnavmesh, talks to it, and saves a dump of the shop window
// (the shop's buy flow gets automated once its addon layout is known from that dump).
public sealed class VendorRunner : IDisposable
{
    private enum Step { Idle, Walking, Talking, WaitShop }

    private const float TalkRange = 3.5f;
    private const string DefaultVendorName = "MGC Trader";
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ShopTimeout = TimeSpan.FromSeconds(8);

    private readonly Plugin plugin;
    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<object> pathStop;

    private Step step;
    private DateTime stepStarted;
    private string[] addonsBeforeTalk = [];

    public string Status { get; private set; } = "";
    public bool Busy => step != Step.Idle;

    public VendorRunner(Plugin plugin)
    {
        this.plugin = plugin;
        var pi = Plugin.PluginInterface;
        navIsReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathIsRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pathStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        if (Busy) Stop();
    }

    public static bool VnavmeshInstalled =>
        Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "vnavmesh" && p.IsLoaded);

    // Saves whatever the player has targeted as the vendor: name, base id, position and zone.
    public void RememberTarget()
    {
        // No target: fall back to the event's "MGC Trader" if one is close by.
        var target = Plugin.TargetManager.Target ?? Plugin.ObjectTable
            .Where(o => o.Name.TextValue == DefaultVendorName)
            .OrderBy(o => Vector3.Distance(o.Position, Plugin.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero))
            .FirstOrDefault();
        if (target == null)
        {
            Status = "Target the MGC vendor first.";
            return;
        }
        var cfg = plugin.Config;
        cfg.VendorName = target.Name.TextValue;
        cfg.VendorBaseId = target.BaseId;
        cfg.VendorPosition = target.Position;
        cfg.VendorTerritory = (ushort)Plugin.ClientState.TerritoryType;
        cfg.Save();
        Status = $"Vendor saved: {cfg.VendorName}";
    }

    public void Start()
    {
        var cfg = plugin.Config;
        if (cfg.VendorBaseId == 0) RememberTarget();
        if (cfg.VendorBaseId == 0) { Status = "No vendor saved yet. Target the MGC Trader once."; return; }
        if ((ushort)Plugin.ClientState.TerritoryType != cfg.VendorTerritory) { Status = "Go to the Gold Saucer first."; return; }

        if (DistanceToVendor() <= TalkRange)
        {
            SetStep(Step.Talking, "Talking to the vendor");
            return;
        }
        if (!VnavmeshInstalled) { Status = "vnavmesh is not installed or not loaded."; return; }
        if (!TryIpc(() => navIsReady.InvokeFunc())) { Status = "vnavmesh is still building the map, try again in a moment."; return; }

        if (!TryIpc(() => pathfindAndMoveTo.InvokeFunc(cfg.VendorPosition, false)))
        {
            Status = "vnavmesh could not path to the vendor.";
            return;
        }
        SetStep(Step.Walking, "Walking to the vendor");
    }

    public void Stop()
    {
        TryIpc(() => { pathStop.InvokeAction(); return true; });
        SetStep(Step.Idle, "Stopped.");
    }

    private void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle) return;
        var elapsed = DateTime.UtcNow - stepStarted;

        switch (step)
        {
            case Step.Walking:
                if (DistanceToVendor() <= TalkRange)
                {
                    TryIpc(() => { pathStop.InvokeAction(); return true; });
                    SetStep(Step.Talking, "Talking to the vendor");
                }
                else if (elapsed > WalkTimeout)
                {
                    Stop();
                    Status = "Took too long to reach the vendor.";
                }
                else if (elapsed > TimeSpan.FromSeconds(2) &&
                         !TryIpc(() => pathIsRunning.InvokeFunc()) && !TryIpc(() => pathfindInProgress.InvokeFunc()))
                {
                    // Path finished or broke off short of the vendor: ask for it again.
                    TryIpc(() => pathfindAndMoveTo.InvokeFunc(plugin.Config.VendorPosition, false));
                    stepStarted = DateTime.UtcNow - TimeSpan.FromSeconds(1);
                }
                break;

            case Step.Talking:
                addonsBeforeTalk = UiScanner.VisibleAddons().ToArray();
                if (!Interact())
                {
                    SetStep(Step.Idle, "Could not find the vendor nearby.");
                    return;
                }
                SetStep(Step.WaitShop, "Opening the shop");
                break;

            case Step.WaitShop:
                var opened = UiScanner.VisibleAddons().Except(addonsBeforeTalk).ToList();
                if (opened.Count > 0 && elapsed > TimeSpan.FromSeconds(1))
                {
                    var (path, _) = UiScanner.DumpToFile("shop");
                    System.IO.File.AppendAllLines(path, opened.SelectMany(UiScanner.AtkValues));
                    Plugin.Log.Info($"Shop opened: {string.Join(", ", opened)} (dump {path})");
                    SetStep(Step.Idle, "Shop open.");
                    if (plugin.Config.AutoBuy) plugin.Shop.Start();
                }
                else if (elapsed > ShopTimeout)
                {
                    SetStep(Step.Idle, "The shop did not open.");
                }
                break;
        }
    }

    private unsafe bool Interact()
    {
        var vendor = FindVendor();
        if (vendor == null) return false;
        Plugin.TargetManager.Target = vendor;
        TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)vendor.Address, true);
        return true;
    }

    private Dalamud.Game.ClientState.Objects.Types.IGameObject? FindVendor()
    {
        var cfg = plugin.Config;
        return Plugin.ObjectTable
            .Where(o => o.BaseId == cfg.VendorBaseId)
            .OrderBy(o => Vector3.Distance(o.Position, cfg.VendorPosition))
            .FirstOrDefault();
    }

    private float DistanceToVendor()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        return me == null ? float.MaxValue : Vector3.Distance(me.Position, plugin.Config.VendorPosition);
    }

    private void SetStep(Step next, string status)
    {
        step = next;
        stepStarted = DateTime.UtcNow;
        Status = status;
        Plugin.Log.Info($"[vendor] {status}");
    }

    private static bool TryIpc(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[vendor] vnavmesh IPC failed: {e.Message}");
            return false;
        }
    }
}
