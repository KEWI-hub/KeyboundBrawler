using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace KeyboundBrawler;

// vnavmesh IPC, shared by the vendor walk and the round loop.
public sealed class Navmesh
{
    private readonly ICallGateSubscriber<bool> navIsReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> pathfindAndMoveTo;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<object> pathStop;

    public Navmesh()
    {
        var pi = Plugin.PluginInterface;
        navIsReady = pi.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        pathfindAndMoveTo = pi.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        pathIsRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pathStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public static bool Installed =>
        Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == "vnavmesh" && p.IsLoaded);

    public bool Ready => Try(() => navIsReady.InvokeFunc());
    public bool MoveTo(Vector3 target) => Try(() => pathfindAndMoveTo.InvokeFunc(target, false));
    public bool Moving => Try(() => pathIsRunning.InvokeFunc()) || Try(() => pathfindInProgress.InvokeFunc());
    public void Stop() => Try(() => { pathStop.InvokeAction(); return true; });

    // Says why walking cannot start, or null when it can.
    public string? CannotWalk() =>
        !Installed ? "vnavmesh is not installed or not loaded."
        : !Ready ? "vnavmesh is still building the map, try again in a moment."
        : null;

    private static bool Try(Func<bool> call)
    {
        try
        {
            return call();
        }
        catch (Exception e)
        {
            Plugin.Log.Warning($"[nav] vnavmesh IPC failed: {e.Message}");
            return false;
        }
    }
}
