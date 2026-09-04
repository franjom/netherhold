// Copyright (c) 2026 Franjo Misetic
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

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
