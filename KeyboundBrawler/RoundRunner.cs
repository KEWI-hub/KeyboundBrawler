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
    private enum Step { Idle, Walking, Talking, Entering, WaitMatch, Playing, Exiting, WaitBack, Resting }

    private readonly record struct Click(string Addon, int Value);

    // The Keybound Registrar in the Gold Saucer event area.
    private const uint EntryNpcId = 1058751;
    private static readonly Vector3 EntryNpcPosition = new(-12.100403f, 0.33283907f, -31.296265f);
    private const ushort EntryTerritory = 1360;

    // Join button, then Commence on the duty pop. Leaving is the result screen's close button.
    private static readonly Click[] EntryClicks = [new("KTGEnterDialogue", 0), new("ContentsFinderConfirm", 8)];
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

    private readonly Plugin plugin;
    private Step step;
    private DateTime stepStarted;
    private int clickIndex;

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
        GoToNpc();
    }

    public void Stop()
    {
        plugin.Nav.Stop();
        SetStep(Step.Idle, $"Stopped after {RoundsDone} of {RoundsWanted} rounds.");
    }

    private void GoToNpc()
    {
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
                clickIndex = 0;
                SetStep(Step.Entering, "Opening the entry window");
                break;

            case Step.Entering:
                if (inMatch) { SetStep(Step.Playing, $"Round {RoundsDone + 1} of {RoundsWanted}: playing"); break; }
                if (Replay(EntryClicks, elapsed))
                    SetStep(Step.WaitMatch, "Waiting for the match to start");
                break;

            case Step.WaitMatch:
                if (inMatch) SetStep(Step.Playing, $"Round {RoundsDone + 1} of {RoundsWanted}: playing");
                else if (elapsed > QueueTimeout) Fail("The match did not start.");
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
                    if (RoundsDone >= RoundsWanted) SetStep(Step.Idle, $"Finished {RoundsDone} rounds.");
                    else SetStep(Step.Resting, $"Round {RoundsDone} of {RoundsWanted} done, next one soon");
                }
                else if (elapsed > BackTimeout) Fail("Did not get back from the duty.");
                break;

            case Step.Resting:
                if (elapsed > RestBetweenRounds && CanAct) GoToNpc();
                break;
        }
    }

    // Sends the clicks one by one, each once its window is up. True when all were sent.
    private bool Replay(Click[] clicks, TimeSpan elapsed)
    {
        if (clickIndex >= clicks.Length) return true;
        var click = clicks[clickIndex];
        var unit = Addon(click.Addon);
        if (unit == null)
        {
            var limit = click.Addon == "ContentsFinderConfirm" || step == Step.Exiting ? QueueTimeout : WindowTimeout;
            if (elapsed > limit) Fail($"{click.Addon} did not open.");
            return false;
        }
        if (elapsed < ClickDelay) return false;

        var value = stackalloc AtkValue[1];
        value->Type = AtkValueType.Int;
        value->Int = click.Value;
        unit->FireCallback(1, value, true);
        Plugin.Log.Info($"[rounds] sent {click.Addon} [{click.Value}]");
        clickIndex++;
        stepStarted = DateTime.UtcNow;
        return clickIndex >= clicks.Length;
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
