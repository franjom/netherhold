using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>
/// Pays every keeper the mana their land generated, once a second.
///
/// <para><b>Once a second, not once a tick</b>, because the rate the data gives
/// is per second and Q1 (the tick rate) is unmeasured. Scheduling it as one
/// application every <see cref="SimulationTiming.TicksPerSecond"/> ticks keeps
/// the arithmetic exact in integers and keeps the state hash independent of a
/// constant nobody has measured — the same rule
/// <see cref="SimulationTiming.WorkApplicationPeriodTicks"/> exists for. Whether
/// the original accrues smoothly or in whole seconds is UNKNOWN and it is
/// visible only as the shape of the counter, never as a total.</para>
///
/// <para>The first payment lands at the end of the first second rather than on
/// tick zero, so a level does not begin by handing out a second of mana that has
/// not elapsed.</para>
/// </summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/mana.md")]
public sealed class ManaSystem : ISimulationSystem
{
    private const string SystemName = "Mana";

    public string Name => SystemName;

    public void Execute(SimulationContext context)
    {
        if ((context.Tick.Value + 1) % SimulationTiming.TicksPerSecond != 0) return;

        WorldState world = context.World;
        int[] gain = ManaRules.GainPerSecondByPlayerIndex(world, context.Database);
        int[] upkeep = ManaRules.UpkeepPerSecondByPlayerIndex(world, context.Database);

        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            int changed = ManaRules.ApplyOneSecond(context.Database, ref player, gain[i], upkeep[i]);
            if (changed == 0) continue;

            // Both halves are reported, not just the net, because that is what
            // the original puts on screen: a green figure and a red one.
            context.Emit(SystemName, changed > 0 ? "mana_gained" : "mana_drained",
                $"player={player.PlayerId} amount={Math.Abs(changed)} " +
                $"rate={gain[i]}/s upkeep={upkeep[i]}/s total={player.Mana}");
        }
    }
}
