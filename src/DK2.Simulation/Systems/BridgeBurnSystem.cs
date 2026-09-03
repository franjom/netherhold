using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Burns away wooden bridges built over lava.
///
/// <para><b>CONFIRMED</b> (owner, 2026-09-03, Q67): a wooden bridge over lava
/// does not last. <c>WoodBridgeLifeOnLavaSeconds</c> is <b>30</b> — a variable
/// that names its own mechanic — and, crucially, <b>the clock starts when the
/// bridge is built</b>. That one detail is what made the rule implementable: the
/// variable's name fits three readings equally well (from build, from first
/// footstep, or as damage to the tile) and the owner's answer picks one. See
/// <see cref="BridgeRules.LifetimeTicks"/>.</para>
///
/// <para>When it goes, the tile becomes the lava it spanned — through
/// <see cref="BridgeRules.RevertsTo"/>, the same rule that serves selling and
/// destruction, so a burnt bridge cannot leave something different from a sold
/// one (ADR-0031).</para>
///
/// <para><b>What is CONFIRMED and NOT implemented, and it is the larger half.</b>
/// The owner described what happens to whatever was standing on the bridge:</para>
/// <list type="bullet">
/// <item>everything on the tile <b>falls into the lava</b>;</item>
/// <item>a <b>dead body</b> disappears;</item>
/// <item>a <b>living creature burns</b> until it gets out of the lava or its
/// health reaches zero;</item>
/// <item>some creatures are <b>immune</b> — the owner named the Salamander and
/// the Giant, and the data agrees and extends it: seven creatures carry
/// <see cref="CreatureFlags.CanWalkOnLava"/> (Salamander, Giant, Firefly, Fairy,
/// Horny and the two unique variants). The two fliers and the Horned Reaper being
/// on that list is exactly what one would expect, which is corroboration rather
/// than contradiction;</item>
/// <item>what happens to <b>gold</b> on the tile is UNKNOWN, and blocked behind
/// objects regardless (M6) — so it is not worth asking yet.</item>
/// </list>
///
/// <para>None of that is here, because <b>creature health does not exist yet</b>:
/// <see cref="CreatureState"/> deliberately carries only the fields a built
/// mechanic reads, and nothing damages a creature. Lava's harm is not a bridge
/// rule anyway — it is <see cref="TerrainDef.Damage"/>, 100 a second, the same
/// field that makes a Hero Lair heal at −67 — so it belongs with creature health
/// and terrain damage in general, not here. What this system does is the tile
/// half, honestly and on its own.</para>
///
/// <para><b>A creature caught on a burning bridge is therefore unharmed here, and
/// it is not stranded.</b> The pathfinder enters a mover's starting tile without
/// testing walkability precisely so that a creature can leave ground that turned
/// impassable underneath it — a rule written before there was anything that could
/// do this to a tile, and which now has its first real cause.</para>
///
/// <para>Runs after the work queue drains, so a bridge somebody was repairing on
/// the tick it ran out gets the repair and burns anyway — the fire is final
/// rather than something a keeper can outrun by one tick.</para>
/// </summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/rooms-and-gold.md")]
public sealed class BridgeBurnSystem : ISimulationSystem
{
    private const string SystemName = "BridgeBurn";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;

        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState tile = ref world.TileAtIndex(i);
            if (tile.BurnTicksRemaining <= 0) continue;

            if (--tile.BurnTicksRemaining > 0) continue;

            byte was = tile.TerrainId;
            byte becomes = BridgeRules.RevertsTo(context.Database, tile);
            TerrainDef next = context.Database.TerrainById(becomes);

            tile.TerrainId = becomes;
            tile.Health = next.StartingHealth;
            tile.UpwardWork = 0;
            tile.WorkRemainder = 0;
            tile.Flags = BridgeRules.SpansWater;
            if (!next.IsTaggable) tile.TaggedBy = 0;

            int lostGold = tile.GoldHeld;
            tile.GoldHeld = 0;

            context.Emit(SystemName, "bridge_burnt",
                $"player={tile.OwnerPlayerId} at=({i % world.Width},{i / world.Width}) " +
                $"terrain={was}->{becomes} " +
                "note=anything standing on it is unharmed here (creature health is not implemented, Q67)");

            if (lostGold > 0)
            {
                context.Emit(SystemName, "gold_lost",
                    $"player={tile.OwnerPlayerId} amount={lostGold} " +
                    "reason=bridge_burnt (what the original does with it is UNKNOWN, Q67)");
            }
        }
    }
}
