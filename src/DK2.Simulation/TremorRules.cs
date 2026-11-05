// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Pathfinding;

namespace DK2.Simulation;

[Dk2Behavior(Confidence.StronglyInferred, "docs/research/keeper-spell-tremor.md")]
public static partial class TremorRules
{
    public const byte SpellId = 8;
    private const ushort MaximumRadius = 0x5000;
    private const byte ReinforcedWallTerrainId = 9;
    private static readonly (int X, int Y)[] Neighbors =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, 0)];

    public static void Start(SimulationContext context, ShotState shot)
    {
        TremorState[] slots = context.World.TremorSlots;
        uint now = unchecked((uint)context.Tick.Value);
        int selected = 0;
        uint oldestAge = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Radius == 0)
            {
                selected = i;
                break;
            }
            uint age = unchecked(now - slots[i].StartedAt);
            if (age > oldestAge)
            {
                oldestAge = age;
                selected = i;
            }
        }
        ushort radius = unchecked((ushort)(shot.Data2 << 12));
        slots[selected] = new TremorState
        {
            WorldX = shot.PosX, WorldY = shot.PosY, StartedAt = now,
            DurationTicks = unchecked((ushort)((ushort)shot.Data1 * SimulationTiming.OriginalTicksPerGameSecond)),
            Radius = Math.Min(radius, MaximumRadius),
        };
        RequestSound(context, selected, ThingSoundEvent.TremorStarted, shot.PosX, shot.PosY, 0x2000);   // 0x00510355
    }

    /// <summary>A Tremor sound in the Tremor spell's category (<c>+0x6D</c> of its definition, <see cref="KeeperSpellDef.SoundCategory"/>):
    /// <c>0xFB</c> at the start and <c>0x31F</c> at the end carry the slot's handle (here the slot's voice) at the slot's
    /// place and height <c>0x2000</c>; <c>0x2EC</c> carries none, at the floor tile it shakes (docs/research/sound-effects-combat.md section 2).</summary>
    private static void RequestSound(SimulationContext context, int slot, ThingSoundEvent sound, int x, int y, int z, bool voiced = true)
    {
        if (ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        string category = context.Database.KeeperSpellById(SpellId)?.SoundCategory ?? "";
        if (category.Length == 0) return;
        ThingSoundRules.Request(context, "tremor", new SoundSourceId(SoundSourceKind.Tremor, slot), category, sound,
            new FpPosition3(new Fp32(x), new Fp32(y), new Fp32(z)), voiced);
    }

    public static void Update(SimulationContext context)
    {
        uint now = unchecked((uint)context.Tick.Value);
        TremorState[] slots = context.World.TremorSlots;
        bool refreshed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            ref TremorState tremor = ref slots[i];
            if (tremor.Radius == 0) continue;
            if (unchecked(now - tremor.StartedAt) >= tremor.DurationTicks)
            {
                tremor.Radius = 0;
                RequestSound(context, i, ThingSoundEvent.TremorEnded, tremor.WorldX, tremor.WorldY, 0x2000);   // 0x005104C6
                continue;
            }
            if (!refreshed)
            {
                context.Navigation.Refresh();
                TrapRules.RelinkCreatures(context.World);
                refreshed = true;
            }
            if (!TryChooseFloor(context, tremor, out TilePos floor)) continue;
            // 0x005105C3, after the view effect and before the tile pass, only for a floor found: no handle, the tile's centre at height 0.
            RequestSound(context, i, ThingSoundEvent.TremorShook, CreatureState.CentreOf(floor.X), CreatureState.CentreOf(floor.Y), 0, voiced: false);
            foreach (var offset in Neighbors)
            {
                var at = new TilePos(floor.X + offset.X, floor.Y + offset.Y);
                if (!context.World.Contains(at)) continue;
                if (NavBytes.IsSolid(context.Navigation.NavByte(at.X, at.Y))) DamageWall(context, at);
                else
                {
                    int tile = context.World.TileIndex(at);
                    CreatureState[] chain = context.World.Creatures
                        .Where(c => c.ChainTileIndex == tile).OrderByDescending(c => c.ChainOrder).ToArray();
                    var centre = new TilePos(tremor.WorldX >> 12, tremor.WorldY >> 12);
                    foreach (CreatureState creature in chain)
                        Respond(context, creature, centre, new Fp32(tremor.Radius));
                }
            }
        }
    }

    internal static bool TryChooseFloor(SimulationContext context, in TremorState tremor, out TilePos floor)
    {
        int radius = tremor.Radius >> 12;
        int side = radius * 2 + 1;
        int sample = context.Rng.NextInt(side * side);
        var sampled = new TilePos((tremor.WorldX >> 12) + sample % side - radius,
            (tremor.WorldY >> 12) + sample / side - radius);
        foreach (var offset in Neighbors)
        {
            var at = new TilePos(sampled.X + offset.X, sampled.Y + offset.Y);
            if (!context.World.Contains(at) || NavBytes.IsSolid(context.Navigation.NavByte(at.X, at.Y))) continue;
            floor = at;
            return true;
        }
        floor = default;
        return false;
    }

    private static void DamageWall(SimulationContext context, TilePos at)
    {
        WorldState world = context.World;
        ref TileState tile = ref world.Tile(at);
        if (tile.TerrainId != ReinforcedWallTerrainId || tile.OwnerPlayerId == PlayerIds.Neutral) return;
        int halfDamage = context.Database[VariableTypeId.TremorDamagePerTerrainTilePerSecond] / 2;
        var damage = new TileHealthDamage(halfDamage + context.Rng.NextInt(halfDamage));
        byte before = tile.TerrainId;
        PlayerId ownerBefore = tile.OwnerPlayerId;
        TileWorkResult result = TileWorkRules.ApplyDamage(context.Database, ref tile, damage);
        if (result.Outcome == TileWorkOutcome.Destroyed)
        {
            tile.FreshlyDug = false;
            SlabChain.Changed(world, context.Database, world.TileIndex(at), before, ownerBefore, context.Rng, context);
            if (tile.TerrainId != before)
                ThingSoundRules.RequestTile(context, "tremor", before, world.TileIndex(at), ThingSoundEvent.TileBroken);
            context.Navigation.Refresh();
        }
        if (result.GoldSpilled > 0)
            FloorGold.Drop(world, context.Database, world.TileIndex(at), result.GoldSpilled, PlayerIds.Neutral);
    }
}
