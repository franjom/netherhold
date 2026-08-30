using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;

namespace DK2.Simulation.Systems;

/// <summary>
/// Makes imps: free ones to keep a keeper above the floor, bought ones when a
/// keeper casts Create Imp.
///
/// <para>This closes a gap every scenario run has reported since workers
/// existed — "nothing generates imps: scenario files and tests place them" — and
/// it is the first thing in the simulation that <b>spends</b> mana rather than
/// only earning it.</para>
///
/// <para><b>The free supply is CONFIRMED and the data names it twice.</b> A
/// keeper below <c>MinimumImpThreshold</c> (4) gets one imp every
/// <c>TimeBeforeFreeImpGeneratedSeconds</c> (5) until they are back up to it.
/// The owner established the behaviour by dropping every imp into a dungeon
/// heart and watching them come back at roughly five-second intervals; the two
/// variables carry exactly those numbers and say exactly that in their names.
/// It is rare for an observation and a variable name to meet this cleanly.</para>
///
/// <para><b>What is ours:</b> that the countdown runs only while a keeper is
/// short. The alternative — a clock that ticks always and hands out an imp the
/// moment one is owed — is indistinguishable in the steady state and differs
/// only in how fast the first replacement arrives after a long spell at full
/// strength. Not observed (Q56).</para>
///
/// <para><b>Not implemented, and it is the interesting one:</b> imps cost mana
/// to keep — about 7 a second each, which is what the red figure under a
/// keeper's mana orb is. That number is in neither the balance table nor the
/// CREATURES record, so it is recorded rather than hardcoded. See Q53 and
/// docs/research/imps-and-mana.md; until it lands, a dungeon full of imps is
/// free to run here and is not in the original.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/imps-and-mana.md")]
public sealed class ImpSupplySystem : ISimulationSystem
{
    private const string SystemName = "ImpSupply";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        foreach (PlayerCommand command in context.Input.All)
        {
            if (command is CreateImpCommand create) Buy(context, create.PlayerId);
        }

        TopUp(context);
    }

    /// <summary>
    /// Hands a short-handed keeper a free imp when their countdown runs out.
    ///
    /// The countdown is per player and lives on <see cref="PlayerState"/>, so it
    /// survives a snapshot like everything else. A keeper at or above the floor
    /// holds their countdown at zero rather than banking it, which is what makes
    /// "only while short" a decision rather than an accident.
    /// </summary>
    private static void TopUp(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int minimum = ImpSupplyRules.MinimumImps(database);
        int interval = ImpSupplyRules.FreeImpIntervalTicks(database);

        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            byte playerId = player.PlayerId;

            if (ImpSupplyRules.ImpCount(world, database, playerId) >= minimum)
            {
                player.FreeImpCountdownTicks = 0;
                continue;
            }

            // Start the clock and spend a tick of it in the same pass, and put
            // it straight back on spawning. Anything else leaves a spare tick
            // between imps, which would make the gap interval+1 rather than the
            // interval the variable names.
            if (player.FreeImpCountdownTicks <= 0) player.FreeImpCountdownTicks = interval;

            player.FreeImpCountdownTicks--;
            if (player.FreeImpCountdownTicks > 0) continue;

            player.FreeImpCountdownTicks = interval;

            // No heart, nowhere to put it — so nothing happens and the clock
            // simply runs again. A keeper without a heart has lost (Q40); what
            // this avoids is searching the map for one on every tick.
            if (Spawn(context, playerId) is not { } imp) continue;

            context.Emit(SystemName, "imp_generated",
                $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
                $"free (below MinimumImpThreshold {minimum})");
        }
    }

    /// <summary>
    /// Casts Create Imp. All or nothing: a keeper who cannot afford it gets
    /// nothing and is told why, because a half-paid imp would be a worse bug
    /// than a refused one — the same rule <see cref="GoldStore.TryWithdraw"/>
    /// follows for rooms.
    /// </summary>
    private static void Buy(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        if (!world.HasPlayer(playerId))
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} reason=no_such_player");
            return;
        }

        int cost = ImpSupplyRules.CreateImpCost(world, context.Database, playerId);
        ref PlayerState player = ref world.Player(playerId);

        if (player.Mana < cost)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} mana={player.Mana} reason=not_enough_mana");
            return;
        }

        if (Spawn(context, playerId) is not { } imp)
        {
            context.Emit(SystemName, "create_imp_refused",
                $"player={playerId} cost={cost} reason=no_dungeon_heart");
            return;
        }

        player.Mana -= cost;
        context.Emit(SystemName, "imp_created",
            $"player={playerId} imp={imp.Id} at=({imp.Tile.X},{imp.Tile.Y}) " +
            $"cost={cost} mana={player.Mana}");
    }

    /// <summary>
    /// Puts a new imp on this keeper's dungeon heart, or returns null if they
    /// have not got one.
    ///
    /// The creature type is found by flag rather than by id — the same rule
    /// every other creature lookup follows — and by name among the workers,
    /// because the Dwarf is a worker too and is not what Create Imp makes.
    /// </summary>
    private static CreatureState? Spawn(SimulationContext context, byte playerId)
    {
        WorldState world = context.World;
        int heart = ImpSupplyRules.HeartTileOf(world, context.Database, playerId);
        if (heart < 0) return null;

        CreatureDef? imp = context.Database.CreatureByName("Imp");
        if (imp is null) return null;

        return world.SpawnCreature(
            imp.CreatureId, playerId, new TilePos(heart % world.Width, heart / world.Width));
    }
}
