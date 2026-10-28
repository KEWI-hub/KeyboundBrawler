using System;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundBrawler;

// Debug aid: while on, logs every callback and UI event the shop windows receive, so a purchase the
// player makes by hand shows exactly what the plugin has to send. Nothing is changed, only logged.
public sealed unsafe class ShopRecorder : IDisposable
{
    private static readonly string[] Watched = ["ShopExchangeCurrency", "ShopExchangeCurrencyDialog", "SelectYesno", "InputNumeric"];

    private delegate bool FireCallbackDelegate(AtkUnitBase* unit, uint count, AtkValue* values, bool close);

    private readonly Hook<FireCallbackDelegate>? callbackHook;

    public bool Recording { get; private set; }

    public ShopRecorder()
    {
        try
        {
            callbackHook = Plugin.GameInterop.HookFromAddress<FireCallbackDelegate>(
                (nint)AtkUnitBase.MemberFunctionPointers.FireCallback, OnFireCallback);
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[rec] could not hook FireCallback: {e.Message}");
        }
    }

    public void Toggle()
    {
        Recording = !Recording;
        if (Recording)
        {
            callbackHook?.Enable();
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreReceiveEvent, Watched, OnReceiveEvent);
        }
        else
        {
            callbackHook?.Disable();
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreReceiveEvent, Watched, OnReceiveEvent);
        }
        Plugin.Log.Info($"[rec] recording {(Recording ? "on" : "off")}");
    }

    public void Dispose()
    {
        if (Recording) Toggle();
        callbackHook?.Dispose();
    }

    private bool OnFireCallback(AtkUnitBase* unit, uint count, AtkValue* values, bool close)
    {
        try
        {
            var name = unit->NameString;
            if (Watched.Contains(name))
            {
                var args = Enumerable.Range(0, (int)count).Select(i => Describe(values[i]));
                Plugin.Log.Info($"[rec] callback {name} close={close} [{string.Join(", ", args)}]");
            }
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[rec] {e.Message}");
        }
        return callbackHook!.Original(unit, count, values, close);
    }

    private static void OnReceiveEvent(AddonEvent type, AddonArgs args)
    {
        if (args is not AddonReceiveEventArgs e) return;
        var data = e.AtkEventData == 0 ? "null" : Convert.ToHexString(new ReadOnlySpan<byte>((void*)e.AtkEventData, 0x30));
        Plugin.Log.Info($"[rec] event {args.AddonName} type={e.AtkEventType} param={e.EventParam} data={data}");
    }

    private static string Describe(AtkValue v) => v.Type switch
    {
        AtkValueType.Int => $"int {v.Int}",
        AtkValueType.UInt => $"uint {v.UInt}",
        AtkValueType.Bool => $"bool {v.Byte}",
        AtkValueType.String or AtkValueType.ManagedString or AtkValueType.ConstString => $"str \"{v.String}\"",
        _ => v.Type.ToString(),
    };
}
