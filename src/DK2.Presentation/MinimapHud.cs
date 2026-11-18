// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>The engine-free minimap (ADR-0805): its zoom, corner controls, frame and north sprite, its hit area and
/// navigation, and the class grid and markers it captures for the engine's disc. Laid out bottom-docked in
/// DK2's 1024x768 units; one is 2.5 virtual. The disc itself (Unity's GPU pass, or <see cref="MinimapDiscImage"/>) is the
/// engine's (<see cref="Disc"/> is where it goes); the camera footprint over it is the frame's first draw.</summary>
public sealed class MinimapHud
{
    private const float Unit = 2.5f;
    /// <summary>The frame texture is 128 texels drawn 204.8 units across.</summary>
    private const float FrameTexel = 204.8f / 128, CornerSize = 32 * FrameTexel;

    public const string FrameArt = "GUI\\Map\\map", NorthArt = "GUI\\Map\\north", FootprintArt = "GUI\\Map\\mapcamerabox";
    public const string PossessionFrameArt = "GUI\\Map\\map-2";

    /// <summary>The map disc: 169.6 across, 12.8 from the left and 11.2 from the bottom (top 587.2), in 1024x768 units.</summary>
    public static readonly HudRect Disc = new(32, 1468, 424, 424);

    /// <summary>The frame's clip, 179.2 x 176 at (8, 584); the padded texture extends past it, its size
    /// interpretation STRONGLY INFERRED (minimap-rendering.md).</summary>
    private static readonly HudRect FrameClip = new(8 * Unit, 584 * Unit, 179.2f * Unit, 176 * Unit);
    private static readonly HudRect FrameRect = new(8 * Unit, 584 * Unit, 204.8f * Unit, 204.8f * Unit);

    /// <summary>Each corner's art, inactive then active. The magnifier's plain art is the "selected-" file.</summary>
    private static readonly string[] Corners = BuildCorners();

    /// <summary>The corners' actions, in <see cref="MinimapCorner"/> order.</summary>
    private static readonly ClassicHudAction[] CornerActions =
        { ClassicHudAction.SelectQuery, ClassicHudAction.SelectSell, ClassicHudAction.ToggleOptions, ClassicHudAction.ZoomIn };

    private readonly MinimapClassGrid _grid;
    private readonly List<MinimapMarker> _markers = new();
    private HudVisibility _visibility = new(true, true);
    private MinimapZoom _zoom = MinimapZoom.WholeMap;
    private ClassicHudAction _selected;
    private bool _optionsVisible;
    private WorldState? _capturedWorld;
    private long _capturedTick = -1;
    private int _capturedPreview = -1;
    private readonly Vector2[] _corners = new Vector2[4], _ribbon = new Vector2[8];
    private readonly HudVertex[] _ribbonTriangles = new HudVertex[MinimapCameraFootprint.TriangleVertices];
    private readonly HudVertex[] _footprint =
        new HudVertex[MinimapCameraFootprint.TriangleVertices / 3 * HudTriangles.MaxVerticesPerTriangle];
    private int _footprintCount;
    private readonly HudVertex[] _possessionFootprint =
        new HudVertex[MinimapCameraFootprint.TriangleVertices / 3 * HudTriangles.MaxVerticesPerTriangle];

    public MinimapHud(MinimapPalette palette, int mapWidth, int mapHeight)
    {
        Palette = palette ?? throw new ArgumentNullException(nameof(palette));
        _grid = new MinimapClassGrid(mapWidth, mapHeight);
    }

    public MinimapPalette Palette { get; }
    public MinimapClassGrid Grid => _grid;
    /// <summary>The markers of the last capture, for the GPU disc.</summary>
    public IReadOnlyList<MinimapMarker> Markers => _markers;
    public int MapWidth => _grid.Width;
    public int MapHeight => _grid.Height;
    public MinimapZoom Zoom => _zoom;
    public HudVisibility Visibility => _visibility;

    public void SetVisibility(HudVisibility visibility) => _visibility = visibility;
    public void ZoomIn() => _zoom = _zoom.ZoomIn();
    public void ZoomOut() => _zoom = _zoom.ZoomOut();

    /// <summary>A corner's art file, in the engine-texture archive.</summary>
    public static string CornerArt(MinimapCorner corner, bool active)
    {
        if ((uint)corner > 3) throw new ArgumentOutOfRangeException(nameof(corner));
        return Corners[(int)corner * 2 + (active ? 1 : 0)];
    }

    /// <summary>Every image the minimap draws through the HUD, to preload.</summary>
    public static IEnumerable<HudImage> Images()
    {
        yield return FrameArt;
        yield return NorthArt;
        yield return FootprintArt;
        foreach (string corner in Corners) yield return corner;
    }

    private static string[] BuildCorners()
    {
        var corners = new string[8];
        for (int i = 0; i < 4; i++)
        {
            string suffix = "mapbf-" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            corners[i * 2] = "GUI\\Map\\" + (i == 3 ? "selected-" : "") + suffix;
            corners[i * 2 + 1] = "GUI\\Map\\" + (i == 3 ? "" : "selected-") + suffix;
        }
        return corners;
    }

    private static Vector2 Reference(float x, float y) => new(x / Unit, y / Unit);

    /// <summary>The corner action a press at a point in virtual units takes; None while the HUD or input is off.</summary>
    public ClassicHudAction CornerPress(float x, float y, bool primary, bool secondary)
    {
        if (!_visibility.ShowNormalHud || !_visibility.AllowOrdinaryInput || !primary && !secondary) return ClassicHudAction.None;
        return ClassicHudControlLayout.ActionAt(Reference(x, y), !primary);
    }

    /// <summary>The corner control under a point, for its tooltip; None off the corners.</summary>
    public ClassicHudAction HoveredControl(float x, float y) =>
        _visibility.ShowNormalHud ? ClassicHudControlLayout.ActionAt(Reference(x, y), false) : ClassicHudAction.None;

    /// <summary>A press on the magnifier zooms: in on the primary button, out on the secondary.</summary>
    public bool ZoomPress(float x, float y, bool primary, bool secondary)
    {
        if (!_visibility.ShowNormalHud || !_visibility.AllowOrdinaryInput || !primary && !secondary) return false;
        if (!MinimapControlLayout.IsMagnifier(Reference(x, y))) return false;
        if (primary) ZoomIn(); else ZoomOut();
        return true;
    }

    /// <summary>The tile a press on the map at a point in virtual units sends the camera to.</summary>
    public bool TryNavigate(float x, float y, FpPosition3 focus, Dk2Angle heading, out TilePos target)
    {
        target = default;
        if (!_visibility.ShowNormalHud || !_visibility.AllowOrdinaryInput) return false;
        return MinimapNavigation.TryResolve(Reference(x, y), new MinimapNavigationView(_grid.Width, _grid.Height, _zoom, focus, heading), out target);
    }

    /// <summary>Whether a point in virtual units is on the minimap: 8..192 across, from 584 down to the screen's
    /// bottom edge, in 1024x768 units.</summary>
    public bool ContainsPointer(float x, float y) =>
        _visibility.ShowNormalHud && _visibility.AllowOrdinaryInput
        && x >= 8 * Unit && x < 192 * Unit && y > 584 * Unit && y <= 768 * Unit;

    /// <summary>The drawn camera, whose footprint the whole-map disc shows (<see cref="MinimapCameraFootprint"/>, turned by
    /// the camera's yaw), cut to the disc's circle as Unity's footprint element was; null, or a corner ray that misses
    /// the floor, shows none.</summary>
    public void SetCamera(MinimapCameraView? view)
    {
        _footprintCount = 0;
        if (view is not MinimapCameraView camera || !camera.TryFloorCorners(_corners)
            || !MinimapCameraFootprint.TryBuild(_corners, MapWidth, MapHeight, camera.YawDegrees * (MathF.PI / 180f), _ribbon))
            return;
        MinimapCameraFootprint.Triangles(_ribbon, _ribbonTriangles);
        _footprintCount = HudTriangles.ClipToCircle(_ribbonTriangles, Disc.X + Disc.Width / 2, Disc.Y + Disc.Height / 2,
            Disc.Width / 2, _footprint);
    }

    /// <summary>The selected tool and whether Options is open: their corners stay lit.</summary>
    public void SetControls(ClassicHudAction selected, bool optionsVisible) { _selected = selected; _optionsVisible = optionsVisible; }

    /// <summary>Reads the class grid and markers once per tick or preview change; call while the simulation is joined.</summary>
    /// <param name="paletteChanged">The neutral colour cycle moved.</param>
    /// <returns>False when nothing was read: the world, tick and preview are the last capture's.</returns>
    public bool Capture(WorldState world, WorldView view, INavByteGrid navigation, out bool paletteChanged)
    {
        paletteChanged = false;
        if (_capturedWorld == world && _capturedTick == world.CurrentTick.Value && _capturedPreview == view.PreviewVersion) return false;
        _capturedWorld = world; _capturedTick = world.CurrentTick.Value; _capturedPreview = view.PreviewVersion;
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++)
            {
                var at = new TilePos(x, y);
                _grid.Set(at, view.MinimapColourAt(world, navigation, at));
            }
        paletteChanged = Palette.AdvanceNeutralCycle(world.CurrentTick, PlayerIds.Neutral);
        view.CaptureMinimapMarkers(world, Palette, _markers);
        return true;
    }

    /// <summary>What a press on the minimap does (DungeonRunner.QueueHudInput): a corner's action, else
    /// <see cref="ClassicHudAction.ToggleFullMap"/> for a secondary press on the map that resolves to a tile; None
    /// otherwise. A primary press on the map moves the camera (<see cref="TryNavigate"/>), which the host asks after its
    /// other HUD presses.</summary>
    /// <param name="focus">The camera's focus (<see cref="FixedFocus"/>) and <paramref name="heading"/>, which the map is drawn
    /// from.</param>
    public ClassicHudAction Press(float x, float y, bool primary, bool secondary, FpPosition3 focus, Dk2Angle heading)
    {
        ClassicHudAction corner = CornerPress(x, y, primary, secondary);
        if (corner != ClassicHudAction.None) return corner;
        return secondary && TryNavigate(x, y, focus, heading, out _) ? ClassicHudAction.ToggleFullMap : ClassicHudAction.None;
    }

    /// <summary>A camera focus in the adapters' world space (x east, y up, z the negated tile row) as the fixed-point map
    /// position the minimap centres on and navigates from.</summary>
    public static FpPosition3 FixedFocus(Vector3 focus) =>
        new(new Fp32((int)(focus.X * Fp32.One)), new Fp32((int)(-focus.Z * Fp32.One)), new Fp32(0));

    /// <summary>The disc's side in screen pixels for a screen height and HUD scale factor, at least 1 and at most 4096: the
    /// resolution the engine draws it at, stretched over <see cref="Disc"/>.</summary>
    public static int DiscPixels(int screenHeight, float factor) =>
        Math.Clamp((int)MathF.Ceiling(Disc.Width / 2.5f * screenHeight / 768f * factor), 1, 4096);

    /// <summary>The possession camera's disc, bottom anchored on the shared presentation content plane.</summary>
    public static HudRect PossessionDisc(HudScaleLayout layout) => PossessionRect(Disc, layout);

    /// <summary>The possession frame has empty corner panels and accepts no corner hover state.</summary>
    public void BuildPossession(HudScaleLayout layout, Dk2Angle heading, HudFrame frame)
    {
        if (!_visibility.ShowNormalHud || layout.Height <= 0) return;
        if (_zoom.IsWholeMap && _footprintCount > 0)
        {
            float scale = PossessionHud.ContentXScale(layout), shift = 1920 / layout.Factor - 1920;
            for (int i = 0; i < _footprintCount; i++)
                _possessionFootprint[i] = _footprint[i] with { X = _footprint[i].X * scale, Y = _footprint[i].Y + shift };
            frame.AddTriangles(FootprintArt, new ReadOnlySpan<HudVertex>(_possessionFootprint, 0, _footprintCount), HudColor.White);
        }
        HudQuads.Clipped(frame, PossessionFrameArt, PossessionRect(FrameRect, layout), PossessionRect(FrameClip, layout), HudColor.White);
        Vector2 north = MinimapNorth.SpriteOriginPixels(heading, layout.Height);
        HudRect northRect = new(layout.Screen.Virtual(north.X), layout.Screen.Virtual(north.Y), 8 * Unit, 8 * Unit);
        frame.Add(new(HudDrawKind.Quad, NorthArt, PossessionRect(northRect, layout), HudColor.White));
    }

    private static HudRect PossessionRect(HudRect rect, HudScaleLayout layout)
    {
        float scale = PossessionHud.ContentXScale(layout), shift = 1920 / layout.Factor - 1920;
        return new(rect.X * scale, rect.Y + shift, rect.Width * scale, rect.Height);
    }

    /// <param name="pointer">The pointer in virtual units: the corner under it shows.</param>
    /// <param name="heading">The camera's heading, which places the north sprite on the ring.</param>
    public void Build(HudScreen screen, (float X, float Y) pointer, Dk2Angle heading, HudFrame frame)
    {
        if (!_visibility.ShowNormalHud || screen.Height <= 0) return;
        // Over the disc and under the frame: the remake's choice, as in Unity's footprint element, the native z-order being
        // unread (minimap-rendering.md). Whole map only.
        if (_zoom.IsWholeMap && _footprintCount > 0)
            frame.AddTriangles(FootprintArt, new ReadOnlySpan<HudVertex>(_footprint, 0, _footprintCount), HudColor.White);
        HudQuads.Clipped(frame, FrameArt, FrameRect, FrameClip, HudColor.White);
        ClassicHudAction hover = ClassicHudControlLayout.ActionAt(Reference(pointer.X, pointer.Y), false);
        for (int i = 0; i < 4; i++)
        {
            ClassicHudAction action = CornerActions[i];
            bool active = i == 2 ? _optionsVisible : i < 2 && _selected == action;
            if (!active && hover != action) continue;
            // Measured, not read: each 32-texel corner is drawn at the frame's 1.6 units a texel over the frame's own
            // corner art, at texels (0,0), (79,0), (0,78) and (79,78), found by matching the sprites against
            // gui/map/map. A candidate sprite renderer, 0x0059B0C0, sizes sprites by resource size times render
            // scale; whether the live minimap uses it is open. The owner confirmed the result (2026-10-19).
            var rect = new HudRect(FrameClip.X + (i == 1 || i == 3 ? 79 * FrameTexel * Unit : 0),
                FrameClip.Y + (i >= 2 ? 78 * FrameTexel * Unit : 0), CornerSize * Unit, CornerSize * Unit);
            HudQuads.Clipped(frame, Corners[i * 2 + (active ? 1 : 0)], rect, FrameClip, HudColor.White);
        }
        // The native 8x8 draw descriptor, in 1024x768 units whatever the texture's resolution.
        Vector2 north = MinimapNorth.SpriteOriginPixels(heading, screen.Height);
        frame.Add(new HudDraw(HudDrawKind.Quad, NorthArt, new HudRect(screen.Virtual(north.X), screen.Virtual(north.Y), 8 * Unit, 8 * Unit), HudColor.White));
    }
}
