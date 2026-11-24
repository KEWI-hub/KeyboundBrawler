using System;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GameObjectStruct = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace KeyboundBrawler;

// Plays rounds back to back: walks to the Keybound Registrar, joins, lets auto-play type, then closes the
// result screen to leave. The clicks are the exact callbacks the game sent for a manual round (see Notes.md).
// Entry NPC id and the KTGEnterDialogue / KTGResult window names: PunishXIV/Saucy (BSD-3).
public sealed unsafe class RoundRunner : IDisposable
{
    private enum Step { Idle, Walking, Talking, Entering, WaitMatch, Playing, Exiting, WaitBack, Shopping, Resting }

    private readonly record struct Click(string Addon, int Value);

    // The Keybound Registrar in the Gold Saucer event area.
    private const uint EntryNpcId = 1058751;
    private static readonly Vector3 EntryNpcPosition = new(-12.100403f, 0.33283907f, -31.296265f);
    private const ushort EntryTerritory = 1360;

    // Join button, then Commence on the duty pop. Leaving is the result screen's close button.
    private static readonly Click[] EntryClicks = [new("KTGEnterDialogue", 0), new(PopAddon, CommenceValue)];
    private static readonly Click[] ExitClicks = [new("KTGResult", -1)];

    private const float TalkRange = 3.5f;
    private static readonly TimeSpan ClickDelay = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan WalkTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(20);
    // The duty pop can take a while when the queue is slow; the result screen waits for the score to count up.
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MatchEndGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BackTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RestBetweenRounds = TimeSpan.FromSeconds(3);
    // Right after coming back from a match the Registrar can ignore the first talk; ask again this often.
    private static readonly TimeSpan TalkRetry = TimeSpan.FromSeconds(5);
    private const int TalkTries = 3;
    // A shopping trip (walk, buy everything, fill up on Coffers) should be over well within this.
    private static readonly TimeSpan ShopTimeout = TimeSpan.FromMinutes(3);
    // Room kept under the MGC cap when the best win so far is unknown.
    private const int DefaultWinMargin = 400;

    private readonly Plugin plugin;
    private Step step;
    private DateTime stepStarted;
    private int clickIndex;
    private int talks;
    // A member who withdraws after we accepted sends the duty pop again: accept that one too. If we fall
    // out of the queue anyway (e.g. the pop ran out), queue again a few times.
    private const string PopAddon = "ContentsFinderConfirm";
    private const int CommenceValue = 8;
    private static readonly TimeSpan OutOfQueueGrace = TimeSpan.FromSeconds(10);
    private const int Requeues = 3;
    private bool popAnswered;
    private DateTime? popSince;
    private DateTime? outOfQueueSince;
    private int requeues;

    public string Status { get; private set; } = "";
    public bool Busy => step != Step.Idle;
    public int RoundsDone { get; private set; }
    public int RoundsWanted { get; private set; }

    public RoundRunner(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        if (Busy) Stop();
    }

    public void Start(int rounds)
    {
        if ((ushort)Plugin.ClientState.TerritoryType != EntryTerritory) { Status = "Go to the Gold Saucer event area first."; return; }
        RoundsDone = 0;
        RoundsWanted = Math.Max(1, rounds);
        // Already near the cap: shop first so the very first win is not lost.
        if (plugin.Config.ShopWhenFull && NearCap()) { GoShopping(); return; }
        GoToNpc();
    }

    public void Stop()
    {
        if (step == Step.Shopping) { plugin.Vendor.Stop(); plugin.Shop.Stop(); }
        plugin.Nav.Stop();
        SetStep(Step.Idle, $"Stopped after {RoundsDone} of {RoundsWanted} rounds.");
    }

    private void GoToNpc()
    {
        talks = 0;
        if (DistanceToNpc() <= TalkRange) { SetStep(Step.Talking, "Talking to the entry NPC"); return; }
        if (plugin.Nav.CannotWalk() is { } why) { Fail(why); return; }
        if (!plugin.Nav.MoveTo(EntryNpcPosition)) { Fail("vnavmesh could not path to the entry NPC."); return; }
        SetStep(Step.Walking, "Walking to the entry NPC");
    }

    private void OnUpdate(IFramework framework)
    {
        if (step == Step.Idle) return;
        var elapsed = DateTime.UtcNow - stepStarted;
        var inMatch = plugin.MatchWatcher.InMatch;

        switch (step)
        {
            case Step.Walking:
                if (DistanceToNpc() <= TalkRange)
                {
                    plugin.Nav.Stop();
                    SetStep(Step.Talking, "Talking to the entry NPC");
                }
                else if (elapsed > WalkTimeout) Fail("Took too long to reach the entry NPC.");
                else if (elapsed > TimeSpan.FromSeconds(2) && !plugin.Nav.Moving)
                {
                    plugin.Nav.MoveTo(EntryNpcPosition);
                    stepStarted = DateTime.UtcNow - TimeSpan.FromSeconds(1);
                }
                break;

            case Step.Talking:
                if (!CanAct) break;
                if (!Interact()) { Fail("Could not find the entry NPC nearby."); return; }
                talks++;
                clickIndex = 0;
                SetStep(Step.Entering, "Opening the entry window");
                break;

            case Step.Entering:
                if (inMatch) { SetStep(Step.Playing, $"Round {RoundsDone + 1} of {RoundsWanted}: playing"); break; }
                if (clickIndex == 0 && Addon(EntryClicks[0].Addon) == null && elapsed > TalkRetry && talks < TalkTries)
                {
                    Plugin.Log.Info($"[rounds] Entry window not up yet, talking again ({string.Join(", ", UiScanner.VisibleAddons())})");
                    SetStep(Step.Talking, "Talking to the entry NPC");
                    break;
                }
                if (Replay(EntryClicks, elapsed))
                {
                    (popAnswered, popSince, outOfQueueSince) = (true, null, null);
                    SetStep(Step.WaitMatch, "Waiting for the match to start");
                }
                break;

            case Step.WaitMatch:
                if (inMatch)
                {
                    requeues = 0;
                    SetStep(Step.Playing, $"Round {RoundsDone + 1} of {RoundsWanted}: playing");
                }
                else WaitForMatch(elapsed);
                break;

            case Step.Playing:
                // The "Submit Text" hint goes away during the intro and the boss cutscene, so only leave once
                // the result screen shows up (Exiting waits for it, and comes back here if the hint returns).
                if (inMatch) stepStarted = DateTime.UtcNow;
                else if (elapsed > MatchEndGrace)
                {
                    clickIndex = 0;
                    SetStep(Step.Exiting, "Match over, leaving");
                }
                break;

            case Step.Exiting:
                if (inMatch && clickIndex == 0) { SetStep(Step.Playing, $"Round {RoundsDone + 1} of {RoundsWanted}: playing"); break; }
                if (Replay(ExitClicks, elapsed))
                    SetStep(Step.WaitBack, "Leaving the duty");
                break;

            case Step.WaitBack:
                if (CanAct && (ushort)Plugin.ClientState.TerritoryType == EntryTerritory)
                {
                    RoundsDone++;
                    Plugin.Log.Info($"[rounds] Round {RoundsDone} of {RoundsWanted} done");
                    if (plugin.Config.ShopWhenFull && NearCap()) GoShopping();
                    else AfterRound();
                }
                else if (elapsed > BackTimeout) Fail("Did not get back from the duty.");
                break;

            case Step.Shopping:
                if (elapsed < TimeSpan.FromSeconds(1) || plugin.Vendor.Busy || plugin.Shop.Busy)
                {
                    if (elapsed > ShopTimeout) { plugin.Vendor.Stop(); plugin.Shop.Stop(); Fail("Shopping took too long."); }
                    break;
                }
                if (!ShopBuyer.ShopOpen) { Fail($"Could not shop: {plugin.Vendor.Status}"); break; }
                Plugin.Log.Info($"[rounds] Shopping done: {plugin.Shop.Status}");
                ShopBuyer.CloseShop();
                AfterRound();
                break;

            case Step.Resting:
                if (elapsed > RestBetweenRounds && CanAct) GoToNpc();
                break;
        }
    }

    // The next win could push MGC past the cap, where it would be lost.
    private bool NearCap()
    {
        var cfg = plugin.Config;
        return cfg.CurrentMgc + Math.Max(cfg.BestRunMgc, DefaultWinMargin) > Rewards.MgcCap;
    }

    private void GoShopping()
    {
        plugin.Vendor.Start(spendAll: true);
        if (!plugin.Vendor.Busy) { Fail($"Could not go shopping: {plugin.Vendor.Status}"); return; }
        SetStep(Step.Shopping, $"MGC near the cap ({plugin.Config.CurrentMgc}), shopping");
    }

    private void WaitForMatch(TimeSpan elapsed)
    {
        var now = DateTime.UtcNow;
        var pop = Addon(PopAddon);
        if (pop == null) (popAnswered, popSince) = (false, null);
        else if (!popAnswered)
        {
            popSince ??= now;
            if (now - popSince.Value < ClickDelay) return;
            Fire(pop, PopAddon, CommenceValue);
            popAnswered = true;
            stepStarted = now;
            Plugin.Log.Info("[rounds] Duty popped again (a member withdrew), accepted it");
            return;
        }

        var queued = pop != null || Plugin.Condition[ConditionFlag.InDutyQueue] || Plugin.Condition[ConditionFlag.BoundByDuty] || !CanAct;
        if (queued) outOfQueueSince = null;
        else if ((outOfQueueSince ??= now) + OutOfQueueGrace < now)
        {
            if (requeues++ >= Requeues) { Fail("Dropped out of the duty queue."); return; }
            Plugin.Log.Info($"[rounds] Not in the duty queue any more, queueing again ({requeues} of {Requeues})");
            GoToNpc();
            return;
        }

        if (elapsed > QueueTimeout) Fail("The match did not start.");
    }

    private void AfterRound()
    {
        if (RoundsDone >= RoundsWanted) SetStep(Step.Idle, $"Finished {RoundsDone} rounds.");
        else SetStep(Step.Resting, $"Round {RoundsDone} of {RoundsWanted} done, next one soon");
    }

    // Sends the clicks one by one, each once its window is up. True when all were sent.
    private bool Replay(Click[] clicks, TimeSpan elapsed)
    {
        if (clickIndex >= clicks.Length) return true;
        var click = clicks[clickIndex];
        var unit = Addon(click.Addon);
        if (unit == null)
        {
            var limit = click.Addon == PopAddon || step == Step.Exiting ? QueueTimeout : WindowTimeout;
            if (elapsed > limit) Fail($"{click.Addon} did not open.");
            return false;
        }
        if (elapsed < ClickDelay) return false;

        Fire(unit, click.Addon, click.Value);
        clickIndex++;
        stepStarted = DateTime.UtcNow;
        return clickIndex >= clicks.Length;
    }

    private static void Fire(AtkUnitBase* unit, string name, int callback)
    {
        var value = stackalloc AtkValue[1];
        value->Type = AtkValueType.Int;
        value->Int = callback;
        unit->FireCallback(1, value, true);
        Plugin.Log.Info($"[rounds] sent {name} [{callback}]");
    }

    private static AtkUnitBase* Addon(string name)
    {
        var stage = AtkStage.Instance();
        if (stage == null) return null;
        var unit = stage->RaptureAtkUnitManager->GetAddonByName(name);
        return unit != null && unit->IsVisible && unit->IsReady ? unit : null;
    }

    private static bool CanAct =>
        Plugin.ObjectTable.LocalPlayer != null &&
        !Plugin.Condition[ConditionFlag.BetweenAreas] && !Plugin.Condition[ConditionFlag.BetweenAreas51] &&
        !Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent];

    private static bool Interact()
    {
        var npc = Plugin.ObjectTable
            .Where(o => o.BaseId == EntryNpcId)
            .OrderBy(o => Vector3.Distance(o.Position, EntryNpcPosition))
            .FirstOrDefault();
        if (npc == null) return false;
        Plugin.TargetManager.Target = npc;
        TargetSystem.Instance()->InteractWithObject((GameObjectStruct*)npc.Address, true);
        return true;
    }

    private static float DistanceToNpc()
    {
        var me = Plugin.ObjectTable.LocalPlayer;
        return me == null ? float.MaxValue : Vector3.Distance(me.Position, EntryNpcPosition);
    }

    private void Fail(string why)
    {
        Plugin.Log.Info($"[rounds] Open windows: {string.Join(", ", UiScanner.VisibleAddons())}");
        plugin.Nav.Stop();
        SetStep(Step.Idle, $"Stopped: {why}");
    }

    private void SetStep(Step next, string status)
    {
        step = next;
        stepStarted = DateTime.UtcNow;
        Status = status;
        Plugin.Log.Info($"[rounds] {status}");
    }
}
