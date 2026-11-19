// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using Godot;
namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private readonly PossessionHighlight _possessionHighlight = new();
    private StatusFlowers? _entryFlowers;
    private GameDatabase? _entryFlowerDatabase;
    private Func<string, DK2.Art.Textures.RgbaImage>? _entryFlowerArt;
    private void DrawPossessionHighlight(WorldHoverHit? pointerHit, HoverFeedback feedback, bool entering)
    {
        if (_simulation == null || _world == null || _possession == null) return;
        WorldHoverHit? target = PossessionHighlight.EntryPointerTarget(pointerHit, entering,
            feedback.Tinted, _tools.SpellArmed(_input) && feedback.Placement.Valid)
            ?? _possessionHighlight.Capture(_simulation.World, _simulation.Database, _viewer, _possession);
        if (target is { } hit)
            _world.DrawHoverOverlay(hit);
    }

    /// <summary>The status flowers over the drawn creatures while the ordinary HUD shows, including possession entry and
    /// return (<see cref="StatusFlowers"/>, drawn by <see cref="WorldScene.DrawFlowers"/> among the blended items).</summary>
    private void DrawPossessionEntryBadge(long milliseconds, WorldHoverHit? pointerHit, HoverFeedback feedback, bool ordinaryHudVisible)
    {
        if (!ordinaryHudVisible || _simulation == null || _world == null || _sources == null) return;
        CreatureQueryTarget hovered = pointerHit is { Kind: WorldHoverKind.Creature } creatureHit
            ? new CreatureQueryTarget(creatureHit.EntityId) : CreatureQueryTarget.None;
        if (_entryFlowerDatabase != _simulation.Database)
        {
            _entryFlowerDatabase = _simulation.Database;
            _entryFlowers = new(_simulation.Database, _viewer);
        }
        InstallHudSources sources = _sources;
        _entryFlowerArt ??= name => sources.Texture(StatusFlowerLayers.Folder + name);
        _world.DrawFlowers(_entryFlowers!.Capture(_simulation.World, _simulation.CurrentTick, hovered, milliseconds),
            Possessing, _entryFlowerArt);
    }
}
