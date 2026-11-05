// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Steering;
using DK2.TestSupport;

namespace DK2.Behavior.Tests;

/// <summary>
/// Level 6a's own Workshop (36..38, 25..28, keeper 3) has two Anvils and a Steel Bucket, and its two Trolls
/// manufacture there once an order is queued. State 65 is the hammering (workshop-states.md section 5): the
/// Troll stands where the finder put it, <c>(objdef Width / 2 + footprint / 2)</c> from the machine's position
/// (<c>0x00470110</c>, <c>0x004F5BE0</c>), and has turned to face that position.
/// </summary>
public class Level6aWorkshopWorkTests
{
    private const byte Keeper = 3;

    [InstallFact]
    public void AWorkingTrollStandsBesideItsMachineAndFacesIt()
    {
        (GameDatabase db, GameSimulation simulation) = Level6a();
        WorldState world = simulation.World;
        ManufactureRules.Enqueue(ref world.Player(Keeper), new ManufactureOrder(ManufactureRules.DoorKind, 1, 600_000));
        CreatureState[] trolls = world.Creatures
            .Where(c => c.OwnerPlayerId == Keeper && db.CreatureById(c.CreatureTypeId).Name == "Troll").ToArray();
        Assert.NotEmpty(trolls);

        int samples = 0;
        var machines = new HashSet<byte>();
        for (int tick = 0; tick < 400; tick++)
        {
            simulation.Tick();
            foreach (CreatureState troll in trolls.Where(t => t.Dk2StateId == WorkshopStep.WorkingState))
            {
                int tile = ((troll.StateTargetY >> 12) * world.Width) + (troll.StateTargetX >> 12);
                ObjectState machine = Assert.Single(world.Objects,
                    o => o.TileIndex == tile && WorkshopStep.IsMachine(o.ObjectTypeId));
                machines.Add(machine.ObjectTypeId);

                int reach = ((ushort)db.ObjectById(machine.ObjectTypeId)!.Width.Raw >> 1)
                    + ((ushort)db.CreatureById(troll.CreatureTypeId).FootprintRadius.Raw >> 1);
                (int dx, int dy) = (troll.StateTargetX - troll.PosX, troll.StateTargetY - troll.PosY);
                double distance = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));
                Assert.InRange(distance, reach - 0x100, reach + 0x100);

                int bearing = Bearing.Of(dx, dy);
                int off = Math.Abs(((troll.Heading - bearing + 1024) & Bearing.Mask) - 1024);
                Assert.True(off <= 2, $"troll {troll.Id} faces {troll.Heading}, the machine is at {bearing}");
                samples++;
            }
        }

        Assert.True(samples > 20, $"only {samples} turns of work seen");
        Assert.Contains(WorkshopStep.Anvil, machines);
    }

    private static (GameDatabase, GameSimulation) Level6a()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase db = GameDatabaseLoader.LoadForCampaignLevel(install, "Level6a");
        GameSimulation simulation = CampaignSimulationFactory.Create(db,
            GameDatabaseLoader.LoadCampaignMap(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignLevel(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignPlayers(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignThings(install, "Level6a")!,
            GameDatabaseLoader.LoadCampaignTriggers(install, "Level6a")!, 1);
        simulation.Run(2);   // the first update furnishes the rooms
        return (db, simulation);
    }
}
