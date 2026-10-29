using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace KeyboundBrawler;

// Walks to the MGC Trader «Prize Claim» with vnavmesh, talks to it, and saves a dump of the shop window.
public sealed class VendorRunner : IDisposable
{
    private enum Step { Idle, Walking, Talking, WaitShop }

    // The event's MGC Trader in the Gold Saucer event area. It does not move, so it is built in.
    public const string VendorName = "MGC Trader";
    private const uint VendorBaseId = 1058753;
    private static readonly Vector3 VendorPosition = new(8.194031f, 0f, -29.160034f);
    private const ushort VendorTerritory = 1360;

    private const float TalkRange = 3.5f;
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ShopTimeout = TimeSpan.FromSeconds(8);

    private readonly Plugin plugin;
    private Step step;
    private DateTime stepStarted;
    private string[] addonsBeforeTalk = [];

    public string Status { get; private set; } = "";
    public bool Busy => step != Step.Idle;

    public VendorRunner(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        if (Busy) Stop();
    }

    public void Start()
    {
        if ((ushort)Plugin.ClientState.TerritoryType != VendorTerritory) { Status = "Go to the Gold Saucer first."; return; }

        if (DistanceToVendor() <= TalkRange)
        {
            SetStep(Step.Talking, "Talking to the vendor");
            return;
        }
        if (plugin.Nav.CannotWalk() is { } why) { Status = why; return; }
        if (!plugin.Nav.MoveTo(VendorPosition))
        {
            Status = "vnavmesh could not path to the vendor.";
            return;
        }
        SetStep(Step.Walking, "Walking to the vendor");
    }

    public void Stop()
    {
        plugin.Nav.Stop();
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
                    plugin.Nav.Stop();
                    SetStep(Step.Talking, "Talking to the vendor");
                }
                else if (elapsed > WalkTimeout)
                {
                    Stop();
                    Status = "Took too long to reach the vendor.";
                }
                else if (elapsed > TimeSpan.FromSeconds(2) && !plugin.Nav.Moving)
                {
                    // Path finished or broke off short of the vendor: ask for it again.
                    plugin.Nav.MoveTo(VendorPosition);
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
                    if (plugin.Config.AutoBuy && plugin.ShowDebug) plugin.Shop.Start();
                }
                else if (elapsed > ShopTimeout)
                {
                    SetStep(Step.Idle, "The shop did not open.");
                }
                break;
        }
    }

    private static unsafe bool Interact()
    {
        var vendor = Plugin.ObjectTable
            .Where(o => o.BaseId == VendorBaseId)
            .OrderBy(o => Vector3.Distance(o.Position, VendorPosition))
            .FirstOrDefault();
        if (vendor == null) return false;
        Plugin.TargetManager.Target = vendor;
        TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)vendor.Address, true);
        return true;
    }

    private static float DistanceToVendor()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        return me == null ? float.MaxValue : Vector3.Distance(me.Position, VendorPosition);
    }

    private void SetStep(Step next, string status)
    {
        step = next;
        stepStarted = DateTime.UtcNow;
        Status = status;
        Plugin.Log.Info($"[vendor] {status}");
    }
}
