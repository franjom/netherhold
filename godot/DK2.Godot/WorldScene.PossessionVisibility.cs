// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Uploads the engine-free possession tile set and gates each existing world draw by its owning tile.</summary>
public partial class WorldScene
{
    private static readonly StringName PossessionVisibilityEnabledGlobal = "dk2_possession_visibility_enabled",
        PossessionTilesGlobal = "dk2_possession_tiles", PossessionGridGlobal = "dk2_possession_grid";
    private PossessionVisibilityField? _possessionVisibility;
    private GameDatabase? _possessionVisibilityDatabase;
    private Image? _possessionVisibilityImage;
    private ImageTexture? _possessionVisibilityTexture;
    private bool _possessionVisibilityEnabled;
    private int _possessionWidth, _possessionHeight;
    private void OpenPossessionVisibility(GameDatabase database, int width, int height)
    {
        _possessionVisibilityDatabase = database;
        (_possessionWidth, _possessionHeight) = (width, height);
        _possessionVisibility = new PossessionVisibilityField(width, height);
        _possessionVisibilityImage = Image.CreateEmpty(width, height, false, Image.Format.R8);
        _possessionVisibilityTexture = ImageTexture.CreateFromImage(_possessionVisibilityImage);
        Global(PossessionVisibilityEnabledGlobal, RenderingServer.GlobalShaderParameterType.Bool, false);
        Global(PossessionTilesGlobal, RenderingServer.GlobalShaderParameterType.Sampler2D, _possessionVisibilityTexture);
        Global(PossessionGridGlobal, RenderingServer.GlobalShaderParameterType.Ivec2, new Vector2I(width, height));
    }
    private void PreparePossessionVisibility()
    {
        if (_possessionVisibility == null || _possessionVisibilityDatabase == null || _lightWorld == null) return;
        bool enabled = Possession is { SettledPossessionCamera: true };
        if (enabled != _possessionVisibilityEnabled)
        {
            _possessionVisibilityEnabled = enabled;
            RenderingServer.GlobalShaderParameterSet(PossessionVisibilityEnabledGlobal, enabled);
        }
        if (enabled)
        {
            _possessionVisibility.Build(_lightWorld, _possessionVisibilityDatabase, _placed, _placedViewport,
                Possession!.PossessionViewDistance);
            _possessionVisibilityImage!.SetData(_possessionWidth, _possessionHeight, false, Image.Format.R8,
                _possessionVisibility.Mask);
            _possessionVisibilityTexture!.Update(_possessionVisibilityImage);
        }
        PossessionDrawVisibility visibility = DrawVisibility;
        foreach (KeyValuePair<int, CreatureSlot> entry in _creatures)
            if (_frame.Creatures.TryGetValue(new CreatureId(entry.Key), out CreatureView creature))
                entry.Value.Slot.Node.Visible = visibility.Creature(new CreatureId(entry.Key), creature.Tile);
        foreach (ModelSlot slot in _objects.Values)
            slot.Node.Visible = visibility.Object(slot.Pose.Tile, slot.Pose.Alpha);
        foreach (ModelSlot slot in _structures.Values) slot.Node.Visible = PossessionTileVisible(slot.Structure.Tile);
        foreach (TorchSlot slot in _torches.Values) slot.SetPossessionVisibility();
        _flames?.SetPossessionVisibility();
    }
    private PossessionDrawVisibility DrawVisibility => new(_possessionVisibility == null ? default : _possessionVisibility.Mask,
        _possessionWidth, _possessionVisibilityEnabled, _frame.HiddenCreature);
    private bool PossessionTileVisible(TilePos tile) => DrawVisibility.Tile(tile);
    // TorchFloorTileIndex is already an index into the map storage, unlike an interpolated flame position.
    private bool PossessionFloorVisible(int tileIndex) => DrawVisibility.Floor(tileIndex);
}
