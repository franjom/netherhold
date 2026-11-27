// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>A scene that the developer overlay describes: its state at a refresh, and the state hash on request.</summary>
public interface IDevOverlayHost
{
    /// <summary>Fills the scene's part of <paramref name="facts"/>; the overlay adds the engine's monitors.</summary>
    void FillDevOverlayFacts(ref DevOverlayFacts facts);

    /// <summary>The simulation's state hash and its turn; false outside a level.</summary>
    bool TryStateHash(out ulong hash, out GameTick turn);
}

/// <summary>The developer overlay (ours, not DK2's): off by default, F3 toggles it and <c>--dev-overlay</c> starts it on;
/// it stays as it was across the process's scenes. <see cref="DevOverlayModel"/> decides the rows; this node draws them in
/// the launcher's fonts and palette at the top right, rebuilding the text about four times a second. Off, it neither
/// processes nor draws, and asks the engine for no GPU timing. On, its steady state allocates nothing: the rows are
/// reused character buffers drawn a character at a time (<see cref="Font.DrawChar(Rid, Vector2, long, int, Color?)"/>),
/// since every string-taking draw would need a new string.</summary>
public partial class DevOverlay : CanvasLayer
{
    /// <summary>Over the HUD and the screen colours (110), under the failure screen.</summary>
    public const int CanvasLayerIndex = 124;
    private const string Argument = "--dev-overlay";
    private const float Pad = 10f, Gap = 14f, Margin = 12f;

    private static bool s_on, s_argumentRead;

    private IDevOverlayHost? _host;
    private Func<byte, string>? _playerNames;
    private DevOverlayModel? _model;
    private DevOverlayFacts _facts;
    private FontFile? _regular, _medium;
    private Rid _item, _viewport;
    private bool _measuring, _hashRequested;
    private float _scale = 1f;
    /// <summary>What the shown overlay allocated after its first <see cref="WarmUpFrames"/> frames, printed at exit.</summary>
    private const int WarmUpFrames = 60;
    private int _warmUp = WarmUpFrames, _countedFrames;
    private long _allocated;
    private bool _hashCounted;

    /// <summary>The model while the overlay shows, for the host's per-frame times; null while it is off.</summary>
    public DevOverlayModel? Active
    {
        get
        {
            return s_on ? _model : null;
        }
    }

    /// <summary>Adds the overlay to <paramref name="parent"/>; the first scene of the process reads <c>--dev-overlay</c>.</summary>
    public static DevOverlay Attach(Node parent, IDevOverlayHost host, string[] args, Func<byte, string>? playerNames = null)
    {
        if (!s_argumentRead)
        {
            s_argumentRead = true;
            s_on = Array.IndexOf(args, Argument) >= 0;
        }
        var overlay = new DevOverlay { Name = "DevOverlay", Layer = CanvasLayerIndex, _host = host, _playerNames = playerNames };
        parent.AddChild(overlay);
        return overlay;
    }

    public override void _Ready()
    {
        _viewport = GetViewport().GetViewportRid();
        Apply();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.F3)
        {
            s_on = !s_on;
            Apply();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.F4 && s_on)
        {
            _hashRequested = true;
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Shows or hides the overlay as <see cref="s_on"/> says; the model and the canvas item are made on the first show.</summary>
    private void Apply()
    {
        SetProcess(s_on);
        if (s_on && _model == null)
        {
            _model = new DevOverlayModel();
            if (_playerNames != null)
            {
                _model.UsePlayerNames(_playerNames);
            }

            _regular = GD.Load<FontFile>("res://Launcher/AlegreyaSans-Regular.ttf");
            _medium = GD.Load<FontFile>("res://Launcher/AlegreyaSans-Medium.ttf");
            _item = RenderingServer.CanvasItemCreate();
            RenderingServer.CanvasItemSetParent(_item, GetCanvas());
        }
        if (_item.IsValid)
        {
            RenderingServer.CanvasItemSetVisible(_item, s_on);
        }
        // The engine times the GPU only when asked; the performance harness asks for itself and keeps it on.
        bool measure = s_on || GetParent()?.GetNodeOrNull("PerformanceReport") != null;
        if (measure != _measuring)
        {
            _measuring = measure;
            RenderingServer.ViewportSetMeasureRenderTime(_viewport, measure);
        }
        if (s_on)
        {
            Refresh();
        }
    }

    public override void _Process(double delta)
    {
        if (_model == null)
        {
            return;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        double gpu = _measuring ? RenderingServer.ViewportGetMeasuredRenderTimeGpu(_viewport) : -1;
        bool due = _model.EndFrame(delta, gpu);
        if (_hashRequested)
        {
            _hashRequested = false;
            if (_host != null && _host.TryStateHash(out ulong hash, out GameTick turn))
            {
                _model.SetHash(hash, turn);
            }

            _hashCounted = true;
            due = true;
        }
        if (due)
        {
            Refresh();
        }
        // The exit line's check of the zero-allocation claim; the hash a key asks for is the simulation's, not counted.
        if (_warmUp > 0)
        {
            _warmUp--;
        }
        else if (!_hashCounted)
        {
            _allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            _countedFrames++;
        }
        _hashCounted = false;
    }

    private void Refresh()
    {
        if (_model == null || _host == null || _regular == null || _medium == null)
        {
            return;
        }

        _facts = default;
        _host.FillDevOverlayFacts(ref _facts);
        _facts.GpuMeasured = _measuring;
        _facts.DrawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        _facts.Objects = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        _facts.VideoMemoryMegabytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);
        _model.Compose(_facts);
        Draw(_model);
    }

    private int Size(DevOverlayRowKind kind)
    {
        return Mathf.RoundToInt((kind switch
        {
            DevOverlayRowKind.Heading => 18f,
            DevOverlayRowKind.Hint => 14f,
            _ => 17f,
        }) * _scale);
    }

    /// <summary>Records the panel: a measuring pass for its size, then the backdrop and every row.</summary>
    private void Draw(DevOverlayModel model)
    {
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        _scale = Mathf.Max(1f, screen.Y / 1080f);
        float labelColumn = 0, valueColumn = 0, height = 0;
        for (int i = 0; i < model.RowCount; i++)
        {
            DevOverlayRowKind kind = model.Kind(i);
            int size = Size(kind);
            FontFile font = kind == DevOverlayRowKind.Heading ? _medium! : _regular!;
            if (kind == DevOverlayRowKind.Row)
            {
                labelColumn = Mathf.Max(labelColumn, Measure(font, model.Label(i), size));
                valueColumn = Mathf.Max(valueColumn, Measure(font, model.Value(i), size));
            }
            else
            {
                valueColumn = Mathf.Max(valueColumn, Measure(font, model.Label(i), size) - labelColumn - Gap * _scale);
            }

            height += LineHeight(font, size, kind);
        }
        float pad = Pad * _scale, width = pad * 2 + labelColumn + Gap * _scale + valueColumn;
        var origin = new Vector2(screen.X - width - Margin * _scale, Margin * _scale);
        RenderingServer.CanvasItemClear(_item);
        RenderingServer.CanvasItemAddRect(_item, new Rect2(origin, new Vector2(width, height + pad * 2)),
            LauncherTheme.Fade(LauncherTheme.DeepEarth, .86f));
        RenderingServer.CanvasItemAddRect(_item, new Rect2(origin, new Vector2(2f * _scale, height + pad * 2)), LauncherTheme.KeeperGold);
        float y = origin.Y + pad, left = origin.X + pad, valueLeft = left + labelColumn + Gap * _scale;
        Color label = LauncherTheme.Fade(LauncherTheme.Bone, .62f), hint = LauncherTheme.Fade(LauncherTheme.Bone, .5f);
        for (int i = 0; i < model.RowCount; i++)
        {
            DevOverlayRowKind kind = model.Kind(i);
            int size = Size(kind);
            FontFile font = kind == DevOverlayRowKind.Heading ? _medium! : _regular!;
            float baseline = y + font.GetAscent(size) + (kind == DevOverlayRowKind.Heading && i > 0 ? 6f * _scale : 0f);
            switch (kind)
            {
                case DevOverlayRowKind.Heading:
                    Write(font, model.Label(i), new Vector2(left, baseline), size, LauncherTheme.KeeperGold);
                    break;
                case DevOverlayRowKind.Hint:
                    Write(font, model.Label(i), new Vector2(left, baseline + 4f * _scale), size, hint);
                    break;
                default:
                    Write(font, model.Label(i), new Vector2(left, baseline), size, label);
                    Write(font, model.Value(i), new Vector2(valueLeft, baseline), size, LauncherTheme.Bone);
                    break;
            }
            y += LineHeight(font, size, kind);
        }
    }

    private float LineHeight(FontFile font, int size, DevOverlayRowKind kind)
    {
        return font.GetHeight(size) + (kind == DevOverlayRowKind.Heading ? 6f * _scale : kind == DevOverlayRowKind.Hint ? 4f * _scale : 1f * _scale);
    }

    private static float Measure(FontFile font, ReadOnlySpan<char> text, int size)
    {
        float width = 0;
        foreach (char c in text)
        {
            width += font.GetCharSize(c, size).X;
        }

        return width;
    }

    private void Write(FontFile font, ReadOnlySpan<char> text, Vector2 at, int size, Color colour)
    {
        foreach (char c in text)
        {
            at.X += font.DrawChar(_item, at, c, size, colour);
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete)
        {
            return;
        }

        if (_item.IsValid)
        {
            RenderingServer.FreeRid(_item);
        }

        _item = default;
    }

    public override void _ExitTree()
    {
        if (_countedFrames > 0)
        {
            GD.Print("DK2.Godot developer overlay: ", _countedFrames, " frames shown after the first ", WarmUpFrames, " allocated ", _allocated, " B");
        }
        // The next scene's overlay asks again; the harness keeps its own request.
        if (_measuring && GetParent()?.GetNodeOrNull("PerformanceReport") == null)
        {
            RenderingServer.ViewportSetMeasureRenderTime(_viewport, false);
        }

        _measuring = false;
    }
}
