using System;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;

namespace KeyboundBrawler;

// Keys posted to the window never count as hardware input, so the duty's idle timers keep climbing.
// Resetting them from outside is not enough: the game recomputes them in InputTimerModule.Update.
// So hook that update and zero the timers right before it runs, but only while the plugin has sent
// a key recently, which is what a real key press would have done. Idea from Jaksuhn's
// ffxiv-bundleoftweaks Anti-AFK tweak (BSD-3-Clause); written here from scratch.
//
// The minigame also keeps an idle counter of its own in its input agent (398, at 0x50), which the
// timers above never touch; that one expelled us with every timer at 0. Location found by
// XeldarAlz/FFXIV-AutoKeyboundBrawler. Only written while the agent's 0x28 points at the brawler
// HUD agent (397), so a patch that moves things leaves memory alone.
public sealed unsafe class AfkGuard : IDisposable
{
    private const string UpdateSignature = "E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 8B 01 FF 90 ?? ?? ?? ?? 84 C0";
    private const uint BrawlerHudAgent = 397, BrawlerInputAgent = 398;
    private const int HudPointerOffset = 0x28, BrawlerIdleOffset = 0x50;
    private static bool layoutWarned;
    private static readonly TimeSpan KeepAliveAfterKey = TimeSpan.FromSeconds(30);

    private delegate void UpdateDelegate(InputTimerModule* module, float delta);

    private readonly Hook<UpdateDelegate>? updateHook;
    private static DateTime lastKey = DateTime.MinValue;

    public static bool Hooked { get; private set; }

    public AfkGuard()
    {
        try
        {
            updateHook = Plugin.GameInterop.HookFromSignature<UpdateDelegate>(UpdateSignature, OnUpdate);
            updateHook.Enable();
            Hooked = true;
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[afk] could not hook InputTimerModule.Update, falling back to direct resets: {e.Message}");
        }
    }

    public void Dispose()
    {
        updateHook?.Dispose();
        Hooked = false;
    }

    // Called for every key the plugin sends.
    public static void KeySent()
    {
        lastKey = DateTime.UtcNow;
        ResetBrawlerIdle();
        if (!Hooked) Reset(InputTimerModule.Instance());
    }

    // The minigame's own idle counter in seconds, or null when the agent is missing or laid out
    // differently. The 8 bytes are two floats that count up together (seen in match logs); the
    // reset clears both.
    public static float? BrawlerIdle()
    {
        var input = BrawlerInputAgentChecked();
        return input == null ? null : Math.Max(*(float*)(input + BrawlerIdleOffset), *(float*)(input + BrawlerIdleOffset + 4));
    }

    private static void ResetBrawlerIdle()
    {
        var input = BrawlerInputAgentChecked();
        if (input != null) *(ulong*)(input + BrawlerIdleOffset) = 0;
    }

    private static byte* BrawlerInputAgentChecked()
    {
        var agents = AgentModule.Instance();
        if (agents == null) return null;
        var hud = agents->GetAgentByInternalId((AgentId)BrawlerHudAgent);
        var input = (byte*)agents->GetAgentByInternalId((AgentId)BrawlerInputAgent);
        if (hud == null || input == null) return null;
        if (*(nint*)(input + HudPointerOffset) == (nint)hud) return input;
        if (!layoutWarned)
        {
            layoutWarned = true;
            Plugin.Log.Warning("[afk] brawler input agent has an unexpected layout, its idle counter is left alone");
        }
        return null;
    }

    private void OnUpdate(InputTimerModule* module, float delta)
    {
        if (DateTime.UtcNow - lastKey < KeepAliveAfterKey)
        {
            Reset(module);
            ResetBrawlerIdle();
        }
        updateHook!.Original(module, delta);
    }

    private static void Reset(InputTimerModule* timers)
    {
        if (timers == null) return;
        timers->AfkTimer = 0;
        timers->ContentInputTimer = 0;
        timers->InputTimer = 0;
        foreach (ref var t in timers->MouseInputTimers) t.Timer = 0;
        foreach (ref var t in timers->ControllerInputTimers) t.Timer = 0;
    }
}
