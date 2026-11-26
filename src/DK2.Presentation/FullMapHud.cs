// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A mouse press on the open full map (<see cref="FullMapHud.Press"/>); both acting ones close the map
/// (<see cref="ClassicHudAction.ToggleFullMap"/>).</summary>
public enum FullMapPress : byte { None, Close, GoToTile }

/// <summary>The engine-free full map's overlay (ADR-0805): its black backdrop, the hovered room's icon, a square per
/// visible creature and the keepers' legend, over the map image and the camera preview that the engine draws where
/// <see cref="Layout"/> and <see cref="ClassicFullMapLayout.Preview"/> put them. Laid out in screen pixels
/// (DK2's 640x480 fractions of the screen), drawn in virtual units.</summary>
public sealed class FullMapHud
{
    private readonly MinimapPalette _palette;
    private readonly int _mapWidth, _mapHeight;
    private readonly List<MinimapMarker> _markers = new();
    private readonly List<PlayerId> _players = new();
    private readonly List<(PlayerId Player, HudText Text, int Width, int Height)> _legend = new();
    private MinimapMarker? _room;
    private Func<int, BitmapFont>? _font;
    private Func<byte, string>? _playerName;
    private bool _roomIcons, _visible;
    private int _legendHeight;

    public FullMapHud(MinimapPalette palette, int mapWidth, int mapHeight)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        if (mapWidth <= 0 || mapHeight <= 0) throw new ArgumentOutOfRangeException(nameof(mapWidth));
        _mapWidth = mapWidth; _mapHeight = mapHeight;
    }

    public bool Visible => _visible;
    public void SetVisible(bool visible) => _visible = visible;

    /// <summary>Turns on the legend, in the legible font for a screen height, when both its font and the players'
    /// names are given, and the hovered room's icon when <paramref name="roomIcons"/>.</summary>
    public void SetLegend(Func<int, BitmapFont>? font, Func<byte, string>? playerName, bool roomIcons)
    {
        bool legend = font != null && playerName != null;
        _font = legend ? font : null; _playerName = legend ? playerName : null;
        _roomIcons = roomIcons; _legendHeight = 0;
    }

    public ClassicFullMapLayout Layout(int screenWidth, int screenHeight) =>
        ClassicFullMapLayout.Fit(screenWidth, screenHeight, _mapWidth, _mapHeight);

    /// <summary>What a mouse press does while the map is open: the left
    /// button sends the camera to the tile under the pointer and closes the map, else the right button closes it. The
    /// open map takes every press, acted on or not.</summary>
    public static FullMapPress Press(bool primary, bool secondary) =>
        primary ? FullMapPress.GoToTile : secondary ? FullMapPress.Close : FullMapPress.None;

    /// <summary>The HUD action a press applies: every acting press closes the map.</summary>
    public static ClassicHudAction Action(FullMapPress press) =>
        press == FullMapPress.None ? ClassicHudAction.None : ClassicHudAction.ToggleFullMap;

    /// <summary>The tile under a screen pixel, measured from the top-left.</summary>
    public TilePos ResolvePointer(int screenWidth, int screenHeight, Vector2 pixel) =>
        Layout(screenWidth, screenHeight).ResolveTile(pixel);

    /// <summary>The hovered room and every visible creature; call while the simulation is joined.</summary>
    public void Capture(WorldState world, GameDatabase database, PlayerId viewer, TilePos hovered)
    {
        if (!_visible) return;
        _markers.Clear();
        _room = null;
        if (hovered.X > 0 && hovered.Y > 0 && hovered.X < world.Width - 1 && hovered.Y < world.Height - 1
            && RoomIndex.RoomAt(world, database, world.TileIndex(hovered)) is RoomInstance room)
        {
            var centre = new TilePos(room.CentreFieldX, room.CentreFieldY);
            if (room.Definition.TypeId is RoomTypeId type
                && ClassicFullMapMarkers.TryRoom(type, centre, Explored(world, centre, viewer), out MinimapMarker marker)) _room = marker;
        }
        foreach (CreatureState creature in world.Creatures)
        {
            var tile = new TilePos(creature.PosX >> 12, creature.PosY >> 12);
            var position = new FpPosition3(new Fp32(creature.PosX), new Fp32(creature.PosY), new Fp32(creature.PosZ));
            if (ClassicFullMapMarkers.TryCreature(position, creature.OwnerPlayerId, creature.InHand, Explored(world, tile, viewer),
                _palette, out MinimapMarker marker)) _markers.Add(marker);
        }
        _players.Clear();
        for (int i = 0; i < world.PlayerCount; i++)
        {
            PlayerId player = world.PlayerAtIndex(i).PlayerId;
            if (player.Value >= PlayerIds.FirstKeeper) _players.Add(player);
        }
    }

    private static bool Explored(WorldState world, TilePos tile, PlayerId viewer) =>
        world.Contains(tile) && (!world.FogOfWarEnabled || world.Tile(tile).IsExploredBy(viewer));

    /// <param name="screen">The screen in pixels; its virtual units are what the frames hold.</param>
    /// <param name="back">Under the engine's map image and preview: the black backdrop.</param>
    /// <param name="front">Over them: the room icon, the creature squares and the legend.</param>
    public void Build(HudScreen screen, HudFrame back, HudFrame front)
    {
        if (!_visible || screen.Height <= 0 || screen.Width <= 0) return;
        float s = screen.Scale;
        back.Add(new HudDraw(HudDrawKind.Fill, default, new HudRect(0, 0, screen.VirtualWidth, 1920), new HudColor(0, 0, 0)));
        int width = screen.Width, height = screen.Height;
        ClassicFullMapLayout layout = Layout(width, height);
        if (_roomIcons && _room is MinimapMarker room && ClassicFullMapMarkers.RoomArt(room.RoomType) is string art)
        {
            // A 32x32 icon of 640x480, centred on the room's centre tile.
            float w = width * 32 / 640, h = height * 32 / 480;
            float x = (int)(room.Position.X.Raw / (float)Fp32.One * layout.Size.X / layout.MapWidth - w / 2) + layout.Origin.X;
            float y = (int)(room.Position.Y.Raw / (float)Fp32.One * layout.Size.Y / layout.MapHeight - h / 2) + layout.Origin.Y;
            front.Add(new HudDraw(HudDrawKind.Quad, art, new HudRect(x / s, y / s, w / s, h / s), HudColor.White));
        }
        float size = 3 * height / 768f;
        foreach (MinimapMarker marker in _markers)
        {
            float x = (int)(marker.Position.X.Raw / (float)Fp32.One * layout.Size.X / layout.MapWidth - 1) + layout.Origin.X;
            float y = (int)(marker.Position.Y.Raw / (float)Fp32.One * layout.Size.Y / layout.MapHeight - 1) + layout.Origin.Y;
            front.Add(new HudDraw(HudDrawKind.Fill, default, new HudRect(x / s, y / s, size / s, size / s), Colour(marker.ColourArgb)));
        }
        if (_font == null) return;
        if (_legendHeight != height || !SamePlayers()) SetLegend(height);
        foreach ((PlayerId player, HudText text, int w, int h) in _legend)
        {
            int left = width * 474 / 640;
            int top = height * 274 / 480 + (player.Value - 3) * (h + height * 4 / 480);
            // 0x00404C14: legend names use Number + 0x1A; creature squares use + 0x11.
            front.Add(new HudDraw(HudDrawKind.Quad, HudImage.Of(text), new HudRect(left / s, top / s, w / s, h / s),
                Colour(_palette.Colours[26 + player.Value])));
        }
    }

    private bool SamePlayers()
    {
        if (_players.Count != _legend.Count) return false;
        for (int i = 0; i < _players.Count; i++) if (_players[i] != _legend[i].Player) return false;
        return true;
    }

    private void SetLegend(int height)
    {
        _legendHeight = height; _legend.Clear();
        BitmapFont font = _font!(height);
        foreach (PlayerId player in _players)
        {
            var text = new HudText(font, _playerName!(player.Value), 0);
            (int w, int h) = HudTextPixels.Measure(text);
            _legend.Add((player, text, w, h));
        }
    }

    private static HudColor Colour(uint argb) => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));
}
