using Godot;
using System.Collections.Generic;

/// <summary>
/// Autoload qui maintient chaque SubViewport à la résolution physique du
/// conteneur. UI scale et résolution étant verrouillés au démarrage
/// (cf. SettingsMenu), on ne réagit qu'aux ajouts de noeuds et aux changements
/// de taille du root&#160;: per-container Resized causait une boucle de
/// rétroaction (set vp.Size → container.Size grossit → set vp.Size×ratio → …).
/// On capture la taille logique du conteneur à la première observation valide
/// et on l'utilise comme cible stable ensuite.
/// </summary>
public partial class ViewportScaler : Node
{
    public static ViewportScaler Instance { get; private set; }

    private const int MaxTextureSize = 8192;
    private const bool DebugLog = true;
    private const string Tag = "[ViewportScaler]";

    private readonly Dictionary<ulong, Vector2I> _logicalCache = new();
    private readonly HashSet<ulong> _scaling = new();
    private int _tick = 0;

    public override void _Ready()
    {
        Instance = this;
        var win = DisplayServer.WindowGetSize();
        var vis = GetTree().Root.GetVisibleRect().Size;
        Log($"_Ready window={win} visibleRect={vis} csFactor={GetTree().Root.ContentScaleFactor} csSize={GetTree().Root.ContentScaleSize}");

        GetTree().Root.SizeChanged += OnRootResized;
        GetTree().NodeAdded += OnNodeAdded;
        CallDeferred(MethodName.RescaleAll);
    }

    public override void _ExitTree()
    {
        Log("_ExitTree");
        if (GetTree() is { } tree)
        {
            tree.Root.SizeChanged -= OnRootResized;
            tree.NodeAdded -= OnNodeAdded;
        }
        Instance = null;
    }

    private void OnRootResized()
    {
        var win = DisplayServer.WindowGetSize();
        var vis = GetTree().Root.GetVisibleRect().Size;
        Log($"OnRootResized window={win} visibleRect={vis}");
        RescaleAll();
    }

    private void OnNodeAdded(Node node)
    {
        if (node is SubViewportContainer container)
        {
            Log($"NodeAdded SubViewportContainer '{container.Name}' path={container.GetPath()}");
            CallDeferred(MethodName.RescaleContainerDeferred, container);
        }
    }

    private void RescaleContainerDeferred(SubViewportContainer container) =>
        RescaleContainer(container, "added");

    public void RescaleAll()
    {
        _tick++;
        int count = 0;
        foreach (var c in FindContainers(GetTree().Root))
        {
            RescaleContainer(c, $"all#{_tick}");
            count++;
        }
        Log($"RescaleAll tick={_tick} containersFound={count}");
    }

    private void RescaleContainer(SubViewportContainer container, string trigger)
    {
        if (!IsInstanceValid(container)) return;
        ulong id = container.GetInstanceId();
        if (!_scaling.Add(id))
        {
            Log($"skip re-entrance '{container.Name}' trigger={trigger}");
            return;
        }
        try
        {
            if (!_logicalCache.TryGetValue(id, out var logical))
            {
                var sz = container.Size;
                if (sz.X < 1 || sz.Y < 1)
                {
                    Log($"defer '{container.Name}' not-laid-out size={sz} trigger={trigger}");
                    CallDeferred(MethodName.RescaleContainerDeferred, container);
                    return;
                }
                logical = new Vector2I(Mathf.RoundToInt(sz.X), Mathf.RoundToInt(sz.Y));
                _logicalCache[id] = logical;
                container.TreeExited += () =>
                {
                    Log($"TreeExited '{container.Name}' id={id}");
                    _logicalCache.Remove(id);
                };
                Log($"cache '{container.Name}' logical={logical} trigger={trigger}");
            }

            var ratio = PhysicalRatio();
            var rawX = logical.X * ratio.X;
            var rawY = logical.Y * ratio.Y;
            var pixel = new Vector2I(
                Mathf.Clamp(Mathf.RoundToInt(rawX), 1, MaxTextureSize),
                Mathf.Clamp(Mathf.RoundToInt(rawY), 1, MaxTextureSize)
            );
            if (rawX > MaxTextureSize || rawY > MaxTextureSize)
                GD.PrintErr($"{Tag} CLAMPED '{container.Name}' raw=({rawX:F1},{rawY:F1}) → {pixel} trigger={trigger}");

            Log($"rescale '{container.Name}' cached={logical} ratio={ratio} pixel={pixel} containerNow={container.Size} trigger={trigger}");

            foreach (var child in container.GetChildren())
            {
                if (child is not SubViewport vp) continue;
                var prevSize = vp.Size;
                var prevOverride = vp.Size2DOverride;

                if (vp.Size2DOverride == Vector2I.Zero)
                    vp.Size2DOverride = new Vector2I((int)vp.Size.X, (int)vp.Size.Y);
                if (vp.Size != pixel) vp.Size = pixel;

                Log($"  └ vp '{vp.Name}' prevSize={prevSize} prevOverride={prevOverride} → size={vp.Size} override={vp.Size2DOverride}");
            }
        }
        finally
        {
            _scaling.Remove(id);
        }
    }

    private Vector2 PhysicalRatio()
    {
        var window = DisplayServer.WindowGetSize();
        var logical = GetTree().Root.GetVisibleRect().Size;
        if (logical.X <= 0 || logical.Y <= 0)
        {
            Log($"PhysicalRatio degenerate visibleRect={logical} → 1.0");
            return Vector2.One;
        }
        return new Vector2(window.X / logical.X, window.Y / logical.Y);
    }

    private static IEnumerable<SubViewportContainer> FindContainers(Node root)
    {
        if (root is SubViewportContainer c) yield return c;
        foreach (var child in root.GetChildren())
            foreach (var found in FindContainers(child))
                yield return found;
    }

    private static void Log(string msg)
    {
        if (DebugLog) GD.Print($"{Tag} {msg}");
    }
}
