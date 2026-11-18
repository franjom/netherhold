// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
namespace DK2.Presentation;

/// <summary>The first-person branch of Hand_UpdateFeedback: one selected action's target, independent of pointer hover.</summary>
public sealed class PossessionHighlight
{
    private WorldState? _world;
    private WorldSteering? _navigation;
    private WorldSight? _sight;
    private readonly HeartSites _hearts = new();

    public WorldHoverHit? Capture(WorldState world, GameDatabase database, PlayerId viewer, PossessionSession session)
    {
        if (!session.Possessing || !session.SettledPossessionCamera
            || !world.HasPlayer(viewer) || world.CreatureById(world.Player(viewer).PossessedCreatureId) is not { } creature)
            return null;
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _navigation = new WorldSteering(database, world);
            _sight = new WorldSight(_navigation, world, database);
        }
        _navigation!.Refresh();
        _hearts.Refresh(world, database);
        PossessionTarget target = PossessionRules.PreviewTarget(world, database, _sight!, creature,
            session.Controls.Fire(viewer, session.View), _hearts);
        WorldHoverKind kind = target.Kind switch
        {
            FightTargetKind.Creature => WorldHoverKind.Creature,
            FightTargetKind.Object => WorldHoverKind.Object,
            FightTargetKind.Door => WorldHoverKind.Door,
            FightTargetKind.Trap => WorldHoverKind.Trap,
            _ => WorldHoverKind.None,
        };
        return kind == WorldHoverKind.None ? null : new WorldHoverHit(kind, target.Tile, target.Id);
    }
}
