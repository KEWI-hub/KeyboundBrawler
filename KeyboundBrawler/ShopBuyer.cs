using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundBrawler;

// Buys the wanted rewards in the MGC Trader's "Currency Exchange" (ShopExchangeCurrency) window.
// Layout from a dump of that window: value 1 = open tab, 4 = rows in the tab, 86 = MGC held,
// 90+ = row names, 456+ = row prices. Every step is checked against those values; anything
// unexpected stops the run and saves a dump instead of guessing on.
public sealed unsafe class ShopBuyer : IDisposable
{
    private const string ShopAddon = "ShopExchangeCurrency";
    private const int TabValue = 1, RowCountValue = 4, MgcValue = 86, FirstNameValue = 90;
    private static readonly string[] ConfirmAddons = ["SelectYesno", "ShopExchangeCurrencyDialog"];

    private enum Step { Idle, SwitchTab, Buy, Confirm, Verify }

    private readonly Plugin plugin;
    private readonly Queue<Reward> queue = new();
    private Step step;
    private DateTime stepStarted;
    private Reward? current;
    private uint mgcBefore;
    private bool tabRequested;

    public string Status { get; private set; } = "";
    public bool Busy => step != Step.Idle;

    public ShopBuyer(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    public static bool ShopOpen => Shop() != null;

    public void Start()
    {
        var cfg = plugin.Config;
        queue.Clear();
        foreach (var r in Rewards.All.Where(r => cfg.Wanted.Contains(r.Name) && !cfg.Owned.Contains(r.Name)).OrderBy(r => r.Cost))
            queue.Enqueue(r);
        if (queue.Count == 0) { Status = "Nothing to buy: tick Want on some rewards."; return; }
        if (Shop() == null) { Status = "Open the MGC Trader's shop first."; return; }
        Next();
    }

    public void Stop() => Finish("Stopped.");

    private void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle) return;
        var shop = Shop();
        if (shop == null) { Finish("The shop closed."); return; }
        var elapsed = (DateTime.UtcNow - stepStarted).TotalMilliseconds;
        if (elapsed < 350) return; // give the window a moment between clicks

        switch (step)
        {
            case Step.SwitchTab:
                if (UInt(shop, TabValue) == current!.Tab) { SetStep(Step.Buy); break; }
                if (elapsed > 3000) { Fail("Could not switch to the right tab."); break; }
                if (!tabRequested) { Fire(shop, 1, current.Tab); tabRequested = true; }
                break;

            case Step.Buy:
                var row = FindRow(shop, current!.Name);
                if (row < 0) { Skip($"{current.Name} is not in the shop (already owned?)."); break; }
                mgcBefore = UInt(shop, MgcValue);
                if (mgcBefore < current.Cost) { Finish($"Not enough MGC for {current.Name} ({mgcBefore}/{current.Cost})."); break; }
                Fire(shop, 0, row, 1);
                SetStep(Step.Confirm);
                break;

            case Step.Confirm:
                foreach (var name in ConfirmAddons)
                {
                    var dialog = Addon(name);
                    if (dialog == null) continue;
                    UiScanner.DumpToFile("buy_confirm");
                    Fire(dialog, 0);
                    SetStep(Step.Verify);
                    return;
                }
                if (UInt(shop, MgcValue) < mgcBefore) { SetStep(Step.Verify); break; }
                if (elapsed > 3000) Fail("No confirmation showed up after picking the item.");
                break;

            case Step.Verify:
                var mgcNow = UInt(shop, MgcValue);
                if (mgcNow < mgcBefore)
                {
                    var cfg = plugin.Config;
                    cfg.Owned.Add(current!.Name);
                    cfg.CurrentMgc = (int)mgcNow;
                    cfg.Save();
                    Plugin.Log.Info($"[shop] Bought {current.Name} for {mgcBefore - mgcNow} MGC");
                    Next();
                }
                else if (elapsed > 4000)
                {
                    Fail($"MGC did not go down after buying {current!.Name}.");
                }
                break;
        }
    }

    private void Next()
    {
        if (queue.Count == 0) { Finish("All wanted rewards bought."); return; }
        current = queue.Dequeue();
        tabRequested = false;
        Status = $"Buying {current.Name}";
        SetStep(Step.SwitchTab);
    }

    private void Skip(string why)
    {
        Plugin.Log.Info($"[shop] {why}");
        Next();
    }

    private void Fail(string why)
    {
        UiScanner.DumpToFile("buy_fail");
        Finish(why + " Saved a dump.");
    }

    private void Finish(string why)
    {
        step = Step.Idle;
        current = null;
        Status = why;
        Plugin.Log.Info($"[shop] {why}");
    }

    private void SetStep(Step next)
    {
        step = next;
        stepStarted = DateTime.UtcNow;
    }

    private static int FindRow(AtkUnitBase* shop, string name)
    {
        var rows = (int)UInt(shop, RowCountValue);
        for (var i = 0; i < rows && FirstNameValue + i < shop->AtkValuesCount; i++)
        {
            // Names carry SeString colour payloads around them, so match on containment.
            var v = shop->AtkValues[FirstNameValue + i];
            if (v.Type is AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString &&
                v.String.ToString().Contains(name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static uint UInt(AtkUnitBase* unit, int index)
    {
        if (index >= unit->AtkValuesCount) return 0;
        var v = unit->AtkValues[index];
        return v.Type == AtkValueType.Int ? (uint)v.Int : v.UInt;
    }

    private static void Fire(AtkUnitBase* unit, params int[] args)
    {
        var values = stackalloc AtkValue[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            values[i].Type = AtkValueType.Int;
            values[i].Int = args[i];
        }
        unit->FireCallback((uint)args.Length, values);
        Plugin.Log.Info($"[shop] {unit->NameString} callback {string.Join(", ", args)}");
    }

    private static AtkUnitBase* Shop() => Addon(ShopAddon);

    private static AtkUnitBase* Addon(string name)
    {
        var stage = AtkStage.Instance();
        if (stage == null) return null;
        var unit = stage->RaptureAtkUnitManager->GetAddonByName(name);
        return unit != null && unit->IsVisible ? unit : null;
    }
}
