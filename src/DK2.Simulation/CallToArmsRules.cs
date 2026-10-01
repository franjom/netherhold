// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Navigation;

namespace DK2.Simulation;

/// <summary>A keeper's Call to Arms: DK2's player <c>+0x76</c> bit 2 (<c>CallToArmsOn</c>),
/// the flag object's tag <c>+0x138</c>, the tile <c>+0x13A/+0x13C</c> and the turn from which the
/// upkeep is charged, <c>+0x13E</c> (<c>CallToArmsManaTick</c>).</summary>
public record struct CallToArmsState(bool On, int ObjectId, ushort X, ushort Y, uint ManaTick)
{
    public readonly TilePos Tile
    {
        get
        {
            return new(X, Y);
        }
    }
}

/// <summary>Call to Arms as the binary runs it: the flag object's process states, the recruit
/// and release walks over the owner's creatures, the three heeding states and the gates other
/// mechanics read. Evidence: <c>docs/research/call-to-arms.md</c>.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/call-to-arms.md")]
public static class CallToArmsRules
{
    public const byte SpellId = 3;

    /// <summary>The flag, OBJECTS record 10, which the Create Object shot puts down.</summary>
    public const byte ObjectTypeId = 10;

    /// <summary>The heeding states: 190 walks to the flag resuming 191, 191 hands over to 192,
    /// and 192 mills about within two tiles of it.</summary>
    public const int GoingState = 190;
    public const int ArrivedState = 191;
    public const int MillingState = 192;

    /// <summary>The flag object's process states (<c>0x0049E686</c>'s table): activation,
    /// then a handover, then the per-second recall; 15 releases the creatures and destroys
    /// the flag; 20 and 19 fade it out without a release.</summary>
    public const byte Activating = 12, Activated = 13, Recalling = 14, Releasing = 15, Fading = 20, Faded = 19;

    /// <summary>Message 84: a creature could not route to the flag (<c>0x00455DA0</c>).</summary>
    public const int UnreachableMessage = 84;

    /// <summary>Whether the call is on and its flag still stands (<c>0x004C0090</c>).</summary>
    public static bool IsLive(WorldState world, in PlayerState player)
    {
        return player.CallToArms.On && world.ObjectById(player.CallToArms.ObjectId) is { } flag
        && flag.ProcessState != Releasing;
    }

    public static bool IsOn(WorldState world, byte playerId)
    {
        return world.HasPlayer(playerId) && world.Player(playerId).CallToArms.On;
    }

    /// <summary><c>0x004888C0</c>: the owner's flag, the creature's bit 20, or one of the three
    /// states through a walk.</summary>
    public static bool IsUnderCallToArms(WorldState world, CreatureState creature)
    {
        if (IsOn(world, creature.OwnerPlayerId) || creature.HeedingCallToArms)
        {
            return true;
        }

        if (!CreatureStateMachine.HasState(creature))
        {
            return false;
        }

        return NeedRules.ResolvedStateId(creature) is >= GoingState and <= MillingState;
    }

    /// <summary>The fight gate's clause (<c>0x0045A6D0</c>): with the call on and no force, a
    /// creature farther than <c>CallToArmsFightDistanceTiles</c> from the flag's tile centre may
    /// not look for a fight. Per axis: the absolute difference squared, shifted down twelve, summed
    /// and compared with the radius squared times 4096; strictly greater refuses.</summary>
    public static bool WithinFightDistance(WorldState world, GameDatabase database, CreatureState creature)
    {
        if (!IsOn(world, creature.OwnerPlayerId))
        {
            return true;
        }

        CallToArmsState call = world.Player(creature.OwnerPlayerId).CallToArms;
        uint radius = (uint)database[VariableTypeId.CallToArmsFightDistanceTiles];
        return DistanceSquared(creature.PosX, creature.PosY, call.Tile) <= radius * radius * Fp32.One;
    }

    private static uint DistanceSquared(int x, int y, TilePos tile)
    {
        long dx = Math.Abs((long)x - CreatureState.CentreOf(tile.X));
        long dy = Math.Abs((long)y - CreatureState.CentreOf(tile.Y));
        return unchecked((uint)((dy * dy) >> 12) + (uint)((dx * dx) >> 12));
    }

    /// <summary>The per-second upkeep while the flag stands and the prepaid turns are spent:
    /// the record's 90, or <c>UpgradedCallToArmsCostPercentage</c> of it once the spell is
    /// upgraded (<c>0x004C17A0</c>).</summary>
    public static int UpkeepPerSecond(WorldState world, GameDatabase database, in PlayerState player, uint turn)
    {
        if (!IsLive(world, player) || player.CallToArms.ManaTick >= turn)
        {
            return 0;
        }

        KeeperSpellDef? spell = database.KeeperSpellById(SpellId);
        if (spell is null)
        {
            return 0;
        }

        int upkeep = spell.ManaUpkeepPerSecond;
        if (player.Availabilities.Exists(a => a.Kind == KeeperSpellRules.SpellAvailabilityKind && a.TypeId == SpellId && a.Upgraded))
        {
            upkeep = (database[VariableTypeId.UpgradedCallToArmsCostPercentage] * upkeep & 0xFFFF) / 100;
        }

        return upkeep;
    }

    /// <summary>The flag's activation (<c>0x004BDDA0</c>): a second flag while the first is in the
    /// hand destroys the second; otherwise the first fades. The call takes the flag's tile, the
    /// prepaid turns are set once from cost over upkeep, and the owner's creatures are recruited.</summary>
    public static void Activate(SimulationContext context, string system, ObjectState flag)
    {
        WorldState world = context.World;
        ref PlayerState player = ref world.Player(flag.OwnerPlayerId);
        CallToArmsState call = player.CallToArms;
        if (call.On && call.ObjectId != flag.Id)
        {
            if (player.Hand.Contains(new HandItem(HandItemKind.Object, call.ObjectId)))
            {
                world.RemoveObject(flag.Id);
                context.Emit(system, "call_to_arms_refused", $"player={flag.OwnerPlayerId} reason=flag_in_hand");
                return;
            }
            if (world.ObjectById(call.ObjectId) is { } old)
            {
                old.ProcessState = Fading;
                call.On = false;
            }
        }
        var tile = new TilePos(flag.TileIndex % world.Width, flag.TileIndex / world.Width);
        bool raised = !call.On || call.ObjectId != flag.Id || call.X != tile.X || call.Y != tile.Y;
        call.On = true;
        call.X = (ushort)tile.X;
        call.Y = (ushort)tile.Y;
        call.ObjectId = flag.Id;
        if (call.ManaTick == 0)
        {
            KeeperSpellDef spell = context.Database.KeeperSpellById(SpellId)!;
            int seconds = spell.ManaUpkeepPerSecond == 0 ? 0 : spell.ManaCost / spell.ManaUpkeepPerSecond;
            call.ManaTick = unchecked(Turn(context) + (uint)(seconds * SimulationTiming.OriginalTicksPerGameSecond));
        }
        player.CallToArms = call;
        if (raised)
        {
            context.Emit(system, "call_to_arms_raised", $"player={flag.OwnerPlayerId} flag={flag.Id} tile={tile.X},{tile.Y} mana_tick={call.ManaTick}");
        }

        Recruit(context, system, flag.OwnerPlayerId, tile);
    }

    /// <summary>The recruiting walk over the owner's list: no worker, no Reaper on its timer,
    /// nobody joining, fighting, reporting danger or held captive, nobody already heeding; a
    /// route to the flag and the transition to 190 set the bit.</summary>
    private static void Recruit(SimulationContext context, string system, byte playerId, TilePos tile)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        foreach (CreatureState creature in world.OwnedCreatures(playerId).ToArray())
        {
            CreatureDef definition = database.CreatureById(creature.CreatureTypeId);
            if (definition.Flags.HasFlag(CreatureFlags.IsWorker) || creature.HeedingCallToArms)
            {
                continue;
            }

            // DK2's pickup holds state 78, which refuses 190; ours holds idle, so the hand is tested here.
            if (HandRules.IsHeld(creature))
            {
                continue;
            }

            CreatureStateCategory category = NeedRules.ResolvedCategory(creature);
            if (creature.Dk2StateId == PortalRules.JoiningState || category is CreatureStateCategory.Combat or CreatureStateCategory.ReportingDanger
                || PrisonRules.IsImprisoned(creature))
            {
                continue;
            }

            if (!CanRoute(context, creature, definition, tile))
            {
                continue;
            }

            if (!CreatureStateMachine.TrySetState(context.Hooks, creature, GoingState, context.Tick.Value))
            {
                continue;
            }

            creature.HeedingCallToArms = true;
            context.Emit(system, "call_to_arms_heeded", $"player={playerId} creature={creature.Id} tile={tile.X},{tile.Y}");
        }
    }

    /// <summary>The route test the recruit makes (<c>0x004D0E60</c>) without loading a route.</summary>
    private static bool CanRoute(SimulationContext context, CreatureState creature, CreatureDef definition, TilePos tile)
    {
        if (creature.Tile == tile)
        {
            return true;
        }

        var from = new Steering.Fixed16Vector(creature.PosX << 4, creature.PosY << 4);
        var to = new Steering.Fixed16Vector(CreatureState.CentreOf(tile.X) << 4, CreatureState.CentreOf(tile.Y) << 4);
        return context.Navigation.Route(from, to, WorldSteering.ProfileOf(definition, creature.OwnerPlayerId, 0), RouteScratch);
    }

    private static readonly List<Steering.Fixed16Vector> RouteScratch = new();

    /// <summary>The release (<c>0x004BDFC0</c>): the clock and the flag bit go, and every owned
    /// non-worker under the call that is neither captive nor joining is idled and loses its bit
    /// only when idle took it.</summary>
    public static void Deactivate(SimulationContext context, string system, byte playerId)
    {
        WorldState world = context.World;
        ref PlayerState player = ref world.Player(playerId);
        CallToArmsState call = player.CallToArms;
        call.ManaTick = 0;
        call.On = false;
        player.CallToArms = call;
        foreach (CreatureState creature in world.OwnedCreatures(playerId).ToArray())
        {
            if (context.Database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker))
            {
                continue;
            }

            if (!creature.HeedingCallToArms && !IsUnderCallToArms(world, creature))
            {
                continue;
            }

            if (PrisonRules.IsImprisoned(creature) || creature.Dk2StateId == PortalRules.JoiningState)
            {
                continue;
            }

            if (!CreatureStateMachine.TrySetState(context.Hooks, creature, CreatureStateMachine.Idle, context.Tick.Value))
            {
                continue;
            }

            creature.HeedingCallToArms = false;
            creature.StopMoving(); // DK2 moves only a walking state (0x0048A23B), so the route goes with the state
        }
        context.Emit(system, "call_to_arms_removed", $"player={playerId}");
    }

    /// <summary>After a keeper's payment (<c>0x004C10E0</c>): with the flag standing and the
    /// prepaid turns spent, an upkeep the mana left cannot cover fades the flag. The fade releases
    /// nobody and leaves the call on with a dead flag: the release the update holds for a missing
    /// flag sits behind a guard that requires the flag, so it never runs (call-to-arms.md §5).
    /// True when the flag was sent fading, which ends DK2's update early.</summary>
    public static bool AfterPayment(SimulationContext context, string system, ref PlayerState player)
    {
        WorldState world = context.World;
        if (!IsLive(world, player) || player.CallToArms.ManaTick >= Turn(context)) return false;
        int upkeep = UpkeepPerSecond(world, context.Database, player, Turn(context));
        if (upkeep == 0 || upkeep <= player.Mana) return false;
        ObjectState flag = world.ObjectById(player.CallToArms.ObjectId)!;
        flag.ProcessState = Fading;
        context.Emit(system, "call_to_arms_unaffordable", $"player={player.PlayerId} upkeep={upkeep} mana={player.Mana}");
        return true;
    }

    /// <summary>DK2's first game turn is 1; our next-tick clock starts at 0.</summary>
    public static uint Turn(SimulationContext context)
    {
        return unchecked((uint)(context.Tick.Value + 1));
    }

    /// <summary>The slap that removes the flag (action 63 through <c>0x0049C2A0</c>): refused while
    /// activating, releasing or fading; otherwise the flag goes to its releasing state.</summary>
    public static bool Remove(SimulationContext context, string system, byte playerId)
    {
        WorldState world = context.World;
        if (!world.HasPlayer(playerId))
        {
            return false;
        }

        CallToArmsState call = world.Player(playerId).CallToArms;
        ObjectState? flag = call.On ? world.ObjectById(call.ObjectId) : null;
        if (flag is null || flag.OwnerPlayerId != playerId || flag.ObjectTypeId != ObjectTypeId)
        {
            return false;
        }

        if (flag.ProcessState is Releasing or Activating or Faded or Fading || flag.ProcessEnding)
        {
            return false;
        }

        flag.ProcessState = Releasing;
        context.Emit(system, "call_to_arms_slapped", $"player={playerId} flag={flag.Id}");
        return true;
    }
}
