// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
namespace DK2.Simulation.Systems;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison-rats.md")]
public sealed class RatSystem : ISimulationSystem
{
    public string Name => "Rat";
    public void Execute(SimulationContext context)
    {
        if (!context.World.Objects.Any(o => o.ObjectTypeId == RatRules.RatObjectId)) return;
        context.Navigation.Refresh();
        for (int i = context.World.Objects.Count - 1; i >= 0; i--)
        {
            ObjectState rat = context.World.Objects[i];
            if (rat.ObjectTypeId == RatRules.RatObjectId) RatRules.Tick(context, rat);
        }
    }
}

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/prison-rats.md")]
public sealed class RatSpawnSystem : ISimulationSystem
{
    public string Name => "RatSpawn";
    public void Execute(SimulationContext context)
    {
        if (context.Database.ObjectById(RatRules.RatObjectId) is null) return;
        IReadOnlyList<RoomInstance> rooms = RoomIndex.Build(context.World, context.Database);
        for (int i = 0; i < context.World.PlayerCount; i++)
        {
            PlayerId owner = context.World.PlayerAtIndex(i).PlayerId;
            if (PlayerIds.IsKeeper(owner.Value)) RatRules.Spawn(context, rooms, owner);
        }
    }
}
