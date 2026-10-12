// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Commands;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

/// <summary>Action 26 (<c>0x00514830</c>): the possessed creature's melee pick, spell use and
/// follower arm, as keeper-spell-possession.md "Possessed actions" and possession-arms-and-inputs.md
/// record them. Unbuilt arms are named where they would run.</summary>
public static partial class PossessionRules
{
    private const string ActSystem = "possession";

    /// <summary><c>0x006BC3B0</c>: the melee pick's tiles around the pose, its own tile last.</summary>
    private static readonly (int Dx, int Dy)[] MeleeTiles =
        [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, 0)];

    /// <summary><c>0x00516950</c>'s half-width either side of the yaw, both ends strict.</summary>
    public const int AimConeHalfWidth = 0x155;

    /// <summary><c>0x00515520</c>'s squared-distance limits from the creature, strict.</summary>
    public const uint CreatureAndObjectScanLimit = 0x5B000;
    public const uint TrapAndDoorScanLimit = 0x31000;

    /// <summary>Action 15's range when the spell's <c>Range</c> or SniperMode is set (<see cref="FreeAimRangeOf"/>).</summary>
    public const int FreeAimRange = 0x3000;

    /// <summary>The shot type a free-aim cast never starts (<c>spell +0x44 == 0x1E</c>).</summary>
    public const byte HomingShotTypeId = 30;

    public const uint TeleportSpellFlag = 0x20;
    public const uint SpeedSpellFlag = 0x40;

    /// <summary>Object types the pick and the dispatch name by id, with the shipped Objects.kwd names;
    /// gold 1 and 3 are <see cref="FloorGold"/>'s and the chicken <see cref="HatcheryRules"/>'.</summary>
    private const byte GoldBagObjectId = 2;
    private const byte TrainingDummyObjectId = 14;
    private const byte AnvilObjectId = 15;
    private const byte SteelBucketObjectId = 16;
    private const byte WorkBenchObjectId = 46;
    private const byte BookCaseObjectId = 64;
    private const byte ArchPillarObjectId = 94;

    internal readonly record struct Vec3(int X, int Y, int Z)
    {
        public static Vec3 operator -(Vec3 a, Vec3 b) => new(unchecked(a.X - b.X), unchecked(a.Y - b.Y), unchecked(a.Z - b.Z));
    }

    internal enum ChainClass : byte { Creature, Object, Trap, Door, Shot, Body }

    internal readonly record struct ChainThing(ChainClass Class, object Thing, Vec3 At, byte Owner, int Order);

    public static void Act(SimulationContext context, ISightBlocker sight, PossessionCombat combat, CreatureState me, ActPossessedCreatureCommand command)
    {
        CreatureDef mine = context.Database.CreatureById(me.CreatureTypeId);
        if (command.Melee) Melee(context, sight, combat, me, mine, command.Aim);
        else UseSlot(context, sight, command.PlayerId.Value, me, mine, command.Aim, command.Slot.Value);
    }

    // ------------------------------------------------------------------ the aim

    /// <summary>The unnormalised aim: sin yaw, −cos yaw, −cos pitch, each the 16.16 table times 4096
    /// shifted 16 (an arithmetic shift), negated after the shift. The horizontal is not scaled by pitch.</summary>
    internal static Vec3 RawAim(Dk2Angle yaw, Dk2Angle pitch) =>
        new(TrapRules.Sine(yaw.Value) >> 4, -(TrapRules.Cosine(yaw.Value) >> 4), -(TrapRules.Cosine(pitch.Value) >> 4));

    /// <summary>The 4096-length direction <c>0x00516670</c>/<c>0x00515520</c> compute: squares in
    /// 32 bits shifted 12, the root of 16 times their sum (<c>0x0065FAE0</c>) shifted 4, and each
    /// component times 16 over it, truncated; unchanged at zero length.</summary>
    internal static Vec3 Normalised(Vec3 v)
    {
        int sum = unchecked((v.X * v.X >> 12) + (v.Y * v.Y >> 12) + (v.Z * v.Z >> 12));
        int length = (int)((uint)IntegerMath.SquareRoot(unchecked((uint)(sum * 16))) >> 4);
        if (length == 0) return v;
        return new(unchecked(v.X * 16) / length, unchecked(v.Y * 16) / length, unchecked(v.Z * 16) / length);
    }

    internal static int Dot(Vec3 a, Vec3 b) => unchecked((a.X * b.X >> 12) + (a.Y * b.Y >> 12) + (a.Z * b.Z >> 12));

    /// <summary><c>0x00516950</c>: the bearing from the pose to the point, moved by a turn when it is
    /// more than half a turn from the yaw, strictly inside yaw ± 0x155.</summary>
    internal static bool InAimCone(int yaw, Vec3 pose, Vec3 point)
    {
        int bearing = Bearing.Of(unchecked(point.X - pose.X), unchecked(point.Y - pose.Y)) & Bearing.Mask;
        if (yaw - bearing > Bearing.HalfTurn) bearing += Bearing.Turn;
        if (bearing - yaw > Bearing.HalfTurn) bearing -= Bearing.Turn;
        return yaw - AimConeHalfWidth < bearing && bearing < yaw + AimConeHalfWidth;
    }

    /// <summary><c>0x00516670</c>: the first survivor is taken; a later one replaces the best only when
    /// it is strictly nearer the pose horizontally and the best's aim dot is below 300/256 of its own,
    /// truncated toward zero. Order-dependent, not a nearest test.</summary>
    internal static bool Replaces(Vec3 candidate, Vec3? best, Vec3 pose, Vec3 aim)
    {
        if (best is not { } held) return true;
        if (TrapRules.DistanceSquared(candidate.X, candidate.Y, pose.X, pose.Y)
            >= TrapRules.DistanceSquared(held.X, held.Y, pose.X, pose.Y)) return false;
        int scaled = unchecked(Dot(Normalised(candidate - pose), aim) * 300);
        return Dot(Normalised(held - pose), aim) < scaled / 256;
    }

    // ------------------------------------------------------------------ the melee arm

    /// <summary>Gate <c>0x0048C9B0</c> or nothing; <c>+0x427</c> stamped before the search; the pick;
    /// then the target's arm. The begin's success stamps again, the same turn.</summary>
    private static void Melee(SimulationContext context, ISightBlocker sight, PossessionCombat combat, CreatureState me, CreatureDef mine, PossessionAim aim)
    {
        long now = context.Tick.Value;
        if (!CombatRules.SwingMayBegin(context.Database, me, mine, now)) return;
        me.TimeLastUsedMelee = now;
        ChainThing? picked = PickMeleeTarget(context.World, context.Database, me, mine, aim);
        // No target: the dig/claim arm (def +0x686 bit 0) is LIKELY by listing and not built.
        if (picked is not { } target)
        {
            context.Emit(ActSystem, "possessed_melee_miss", $"creature={me.Id}");
            return;
        }

        UnmaskAgainst(context.World, me, target.Owner);
        switch (target.Thing)
        {
            case CreatureState creature when target.Class == ChainClass.Creature:
                // A creature on a torture device (state 131) is stamped, not struck.
                if (creature.Dk2StateId == TortureRules.TorturedState) break;
                FightRules.BeginPossessedAction(context, me, SpellRules.MeleeSwing, 0, FightTargetKind.Creature, creature.Id);
                Command(context, sight, combat, me, creature);
                Record(me, FightTargetKind.Creature, creature.Id, now);
                break;
            case TrapState trap:
                Attack(context, me, FightTargetKind.Trap, trap.Id, now);
                break;
            case DoorState door:
                Attack(context, me, FightTargetKind.Door, door.Id, now);
                break;
            case ObjectState thing:
                ObjectArm(context, me, mine, thing);
                break;
            default:
                // A shot or a body: action 17 on that class is unread (Q535); only the stamps happen.
                context.Emit(ActSystem, "possessed_melee_unbuilt", $"creature={me.Id} class={target.Class}");
                break;
        }
        // DK2's second stamp. It writes the same tick as the first, so no test can tell it from its
        // absence; it is kept to mirror the binary.
        me.TimeLastUsedMelee = now;
    }

    /// <summary><c>CreatureAction_Begin(17, target)</c>, which every represented action takes; then
    /// PossessedTargetAttackTime <c>+0x4D2</c> and PossessedTargetTag <c>+0x4D0</c>, the target the
    /// followers take (<c>0x00514DF7</c>).</summary>
    private static void Attack(SimulationContext context, CreatureState me, FightTargetKind kind, int id, long now)
    {
        FightRules.BeginPossessedAction(context, me, SpellRules.MeleeSwing, 0, kind, id);
        Record(me, kind, id, now);
    }

    private static void Record(CreatureState me, FightTargetKind kind, int id, long now)
    {
        me.PossessedTargetTick = now;
        me.PossessedTargetKind = kind;
        me.PossessedTargetId = id;
    }

    /// <summary>The object dispatch: gold 1-3 is the pick-up; 15, 16 and 46 only place an impact
    /// effect (<c>0x004998A0</c>, presentation); the chicken (9), type 14's process state 18 and the
    /// attack on 64 or 94 are not built (Q535).</summary>
    private static void ObjectArm(SimulationContext context, CreatureState me, CreatureDef mine, ObjectState thing)
    {
        switch (thing.ObjectTypeId)
        {
            case FloorGold.FloorGoldObjectId or GoldBagObjectId or FloorGold.TreasuryPileObjectId:
                TakeGold(context, me, mine, thing);
                break;
            case AnvilObjectId or SteelBucketObjectId or WorkBenchObjectId:
                context.Emit(ActSystem, "possessed_impact", $"creature={me.Id} object={thing.Id}");
                break;
            default:
                context.Emit(ActSystem, "possessed_melee_unbuilt", $"creature={me.Id} object={thing.Id} type={thing.ObjectTypeId}");
                break;
        }
    }

    /// <summary>The pick-up: the least of twice what the purse lacks, the pile (an unsigned test) and
    /// 100; the pile loses it, emptied it goes, and half goes into the purse (<c>0x0048AE70</c>).</summary>
    private static void TakeGold(SimulationContext context, CreatureState me, CreatureDef mine, ObjectState pile)
    {
        int lacks = CreatureLevelRules.MaxGoldHeld(context.Database, mine, me.Level) - me.GoldHeld;
        if (lacks == 0) return;
        int take = unchecked(lacks * 2);
        if ((uint)pile.GoldHeld < (uint)take) take = pile.GoldHeld;
        if (take > 99) take = 100;
        FloorGold.Take(context.World, context.Database, pile, take);
        int landed = PurseRules.Receive(context.Database, mine, me, take / 2);
        context.Emit(ActSystem, "possessed_gold", $"creature={me.Id} pile={pile.Id} take={take} purse={landed}");
    }

    /// <summary><c>0x00515420</c>: around the pose's tile in <see cref="MeleeTiles"/> order, each
    /// tile's chain from its latest arrival, never the attacker; <see cref="Pickable"/>, the cone and
    /// <see cref="Replaces"/> decide. A tile off the map is skipped (DK2 reads past the edge, unread).</summary>
    internal static ChainThing? PickMeleeTarget(WorldState world, GameDatabase database, CreatureState me, CreatureDef mine, PossessionAim aim)
    {
        (int px, int py, int pz) = aim.PacketOrigin;
        var pose = new Vec3(px, py, pz);
        Vec3 aimVector = Normalised(RawAim(aim.Yaw, aim.Pitch));
        ChainThing? best = null;
        var onTile = new List<ChainThing>();
        foreach ((int dx, int dy) in MeleeTiles)
        {
            int x = (px >> 12) + dx, y = (py >> 12) + dy;
            if (!world.Contains(x, y)) continue;
            ThingsOnTile(world, (y * world.Width) + x, onTile);
            foreach (ChainThing thing in onTile)
            {
                if (ReferenceEquals(thing.Thing, me) || !Pickable(database, me, mine, thing)) continue;
                if (!InAimCone(aim.Yaw.Value, pose, thing.At)) continue;
                if (Replaces(thing.At, best?.At, pose, aimVector)) best = thing;
            }
        }
        return best;
    }

    /// <summary>One tile's layer-0 chain, head first: every class this simulation represents.</summary>
    internal static void ThingsOnTile(WorldState world, int tile, List<ChainThing> into)
    {
        into.Clear();
        foreach (CreatureState c in world.Creatures)
            if (c.TileLink.TileIndex == tile)
                into.Add(CreatureDeathRules.IsCorpse(c)
                    // 0x004B0F20 constructs a body owned by the neutral player (0x004B0FAA); a Graveyard
                    // that takes it makes it its owner's (0x004B1700).
                    ? new(ChainClass.Body, c, new(c.PosX, c.PosY, c.PosZ), (byte)c.BodyOwnerPlayerId, c.TileLink.Order)
                    : new(ChainClass.Creature, c, new(c.PosX, c.PosY, c.PosZ), c.OwnerPlayerId, c.TileLink.Order));
        foreach (ObjectState o in world.Objects)
            if (o.TileLink.TileIndex == tile)
                into.Add(new(ChainClass.Object, o, ObjectPosition(world, o), o.OwnerPlayerId, o.TileLink.Order));
        foreach (TrapState t in world.Traps)
            if (t.TileLink.TileIndex == tile)
                into.Add(new(ChainClass.Trap, t, new(t.PosX, t.PosY, 0), t.PlayerId, t.TileLink.Order));
        foreach (DoorState d in world.Doors)
            if (d.TileLink.TileIndex == tile)
                into.Add(new(ChainClass.Door, d, new(CreatureState.CentreOf(d.X), CreatureState.CentreOf(d.Y), 0), d.PlayerId, d.TileLink.Order));
        foreach (ShotState s in world.Shots)
            if (s.TileLink.TileIndex == tile)
                into.Add(new(ChainClass.Shot, s, new(s.PosX, s.PosY, 0), s.OwnerPlayerId, s.TileLink.Order));
        into.Sort((a, b) => b.Order.CompareTo(a.Order));
    }

    /// <summary>An object's position: its own when it keeps one (a boulder, spell gold), else its tile's
    /// centre, as the boulder's sweep takes it. Height is the floor, 0 here (Q398).</summary>
    internal static Vec3 ObjectPosition(WorldState world, ObjectState o) =>
        o.PosX != 0 || o.PosY != 0
            ? new(o.PosX, o.PosY, 0)
            : new(CreatureState.CentreOf(o.TileIndex % world.Width), CreatureState.CentreOf(o.TileIndex / world.Width), 0);

    /// <summary><c>0x00516050</c>. Same owner: objects only, in reach, of a listed type. Another owner:
    /// objects without definition flag bit 20, in reach, of a listed type or 94; traps not hidden;
    /// creatures not down; any other class.</summary>
    internal static bool Pickable(GameDatabase database, CreatureState me, CreatureDef mine, ChainThing thing)
    {
        bool sameOwner = thing.Owner == me.OwnerPlayerId;
        switch (thing.Thing)
        {
            case ObjectState o:
                if (!sameOwner && database.ObjectById(o.ObjectTypeId) is { } def && def.Flags.HasFlag(ObjectFlags.DoorKey)) return false;
                if (!InReach(me, mine, thing.At)) return false;
                return o.ObjectTypeId switch
                {
                    FloorGold.FloorGoldObjectId or GoldBagObjectId or FloorGold.TreasuryPileObjectId =>
                        (uint)me.GoldHeld < (uint)CreatureLevelRules.MaxGoldHeld(database, mine, me.Level),
                    HatcheryRules.ChickenObjectId or TrainingDummyObjectId or AnvilObjectId or SteelBucketObjectId
                        or WorkBenchObjectId or BookCaseObjectId => true,
                    ArchPillarObjectId => !sameOwner,
                    _ => false,
                };
            default:
                if (sameOwner) return false;
                if (thing.Thing is TrapState trap) return !trap.Hidden;
                if (thing.Class == ChainClass.Creature) return !CreatureDeathRules.IsDown((CreatureState)thing.Thing);
                return true;
        }
    }

    /// <summary><c>0x005162E0</c>: the bearing from the creature (not the pose) within 0x80 of its
    /// heading, no wrap, and the horizontal squared distance at most 6/5 of the summed radii (the
    /// creature's footprint, an object's 0 from <c>0x00402AD0</c>), truncated.</summary>
    internal static bool InReach(CreatureState me, CreatureDef mine, Vec3 at)
    {
        int bearing = Bearing.Of(unchecked(at.X - me.PosX), unchecked(at.Y - me.PosY)) & Bearing.Mask;
        int heading = (ushort)me.Heading;
        if ((ushort)(Math.Max(bearing, heading) - Math.Min(bearing, heading)) >= 0x80) return false;
        uint bound = (uint)(((ulong)(uint)mine.FootprintRadius.Raw * 0x4CCCCCCCDUL) >> 34);
        return TrapRules.DistanceSquared(me.PosX, me.PosY, at.X, at.Y) <= bound;
    }

    // ------------------------------------------------------------------ the spell arm

    /// <summary>Slot 3 is the follower arm (<see cref="Recruit"/>), which never stamps. Other slots: refused by <c>0x0048C800</c> (equality refuses; a slot outside the spells,
    /// negative included, has no spell) → nothing. Past the record, every path stamps the slot.</summary>
    private static void UseSlot(SimulationContext context, ISightBlocker sight, byte keeper, CreatureState me, CreatureDef mine, PossessionAim aim, int slot)
    {
        GameDatabase database = context.Database;
        long now = context.Tick.Value;
        if (slot == PossessedSlot.FollowerArm)
        {
            Recruit(context, sight, me, aim);
            return;
        }
        if (!SpellRules.CanCast(database, me, mine, slot, now)) return;
        CreatureSpellDef spell = SpellRules.SpellOf(database, mine, slot)!;

        SpellTarget target = PickSpellTarget(context.World, database, sight, me, aim, SpellRules.IsFriendly(spell));
        if (SpellRules.IsSelfOnly(spell)) target = new(FightTargetKind.Creature, me.Id, null);
        if (TargetOwner(context.World, target) is { } owner) UnmaskAgainst(context.World, me, owner);

        if ((spell.Flags & TeleportSpellFlag) != 0) TeleportHome(context, keeper, me);
        else if ((spell.Flags & SpeedSpellFlag) != 0)
            StatusEffectRules.ModifySpeed(context, me, unchecked((int)spell.ShotData1), unchecked((int)spell.ShotData2));
        else if (target.Object is { } thing)
            // Action 16 at an object (type 94) has no target kind here (Q535): stamped only.
            context.Emit(ActSystem, "possessed_cast_unbuilt", $"creature={me.Id} object={thing.Id}");
        else if (target.Kind == FightTargetKind.None)
        {
            if (spell.ShotTypeId != HomingShotTypeId)
            {
                (int x, int y, int z) = aim.PacketOrigin;
                Vec3 raw = RawAim(aim.Yaw, aim.Pitch);
                FightRules.BeginFreeAim(context, me, slot, unchecked(x + raw.X), unchecked(y + raw.Y), unchecked(z + raw.Z),
                    aim.Pitch.Value, FreeAimRangeOf(spell, me));
            }
        }
        else
            FightRules.BeginPossessedAction(context, me, SpellRules.SpellCast, slot, target.Kind, target.Id);

        me.SpellStamps[slot] = now;
    }

    /// <summary>The teleport arm: the keeper's nearest Dungeon Heart by its first tile, then action 25
    /// with a zero word at that tile's centre (effects 0x141/0x142 are presentation).</summary>
    private static void TeleportHome(SimulationContext context, byte keeper, CreatureState me)
    {
        RoomInstance? heart = StealGoldRules.NearestRoomOfType(context, keeper, RoomTypeId.DungeonHeart, me);
        if (heart is null) return;
        int anchor = heart.TileIndices[0], width = context.World.Width;
        var centre = new FpPosition3(new(CreatureState.CentreOf(anchor % width)), new(CreatureState.CentreOf(anchor / width)), new(0));
        Move(context, me, new PossessionPose(centre, new(0)));
        context.Emit(ActSystem, "possessed_teleport", $"creature={me.Id} heart_tile={anchor}");
    }

    /// <summary><c>0x00515364</c>: 0x3000 when the spell's <c>Range</c> plus SniperMode's bit is
    /// nonzero, else 0, which the shot reads as no limit. So sniper mode caps a Range-0 spell.</summary>
    public static int FreeAimRangeOf(CreatureSpellDef spell, CreatureState me) =>
        unchecked((int)spell.Range + (me.SniperMode ? 1 : 0)) != 0 ? FreeAimRange : 0;

    internal readonly record struct SpellTarget(FightTargetKind Kind, int Id, ObjectState? Object);

    /// <summary>The spell's target's owner for the unmask test (a self spell's target is the caster).
    /// With no target DK2 reads the owner of thing slot 0 unchecked (<c>0x0051513B</c>), UNKNOWN: ours
    /// does not unmask then.</summary>
    private static byte? TargetOwner(WorldState world, SpellTarget target) =>
        target.Object is { } thing ? thing.OwnerPlayerId
        : target.Kind switch
        {
            FightTargetKind.Creature => world.CreatureById(target.Id)?.OwnerPlayerId,
            FightTargetKind.Trap => world.TrapById(target.Id)?.PlayerId,
            FightTargetKind.Door => world.DoorById(target.Id)?.PlayerId,
            _ => null,
        };

    /// <summary><c>0x00515520</c>: players in list order, those hostile to the creature's owner (the
    /// friendly ones for a friendly spell); per player its creatures, traps, doors and type-94 objects,
    /// each list newest first (<c>0x004B4C30</c> prepends). Distance from the creature, cone and score
    /// from the pose, sight from the creature; the strictly lowest 4096 − dot below the accuracy
    /// threshold wins. A door is sighted one tile nearer the pose. The caster is not excluded.</summary>
    internal static SpellTarget PickSpellTarget(
        WorldState world, GameDatabase database, ISightBlocker sight, CreatureState me, PossessionAim aim, bool friendly)
    {
        (int px, int py, int pz) = aim.PacketOrigin;
        var pose = new Vec3(px, py, pz);
        Vec3 aimVector = Normalised(RawAim(aim.Yaw, aim.Pitch));
        int best = (int)(unchecked((uint)(database[VariableTypeId.FirstPersonAccuracyAngleDegrees] * 0x800)) / 360u);
        var chosen = new SpellTarget(FightTargetKind.None, CombatRules.NoTarget, null);
        int yaw = aim.Yaw.Value;

        bool Scores(Vec3 at, Vec3 sightAt, uint limit, out int score, Vec3? direction = null)
        {
            score = 0;
            if (TrapRules.DistanceSquared(me.PosX, me.PosY, at.X, at.Y) >= limit) return false;
            if (!InAimCone(yaw, pose, at)) return false;
            Vec3 d = direction ?? Normalised(at - pose);
            if (!LineOfSight.Clear(sight, me.PosX, me.PosY, sightAt.X, sightAt.Y)) return false;
            score = 4096 - Dot(d, aimVector);
            return score < best;
        }

        for (int i = 0; i < world.PlayerCount; i++)
        {
            PlayerId player = world.PlayerAtIndex(i).PlayerId;
            bool qualifies = CombatRules.IsHostile(world, me.OwnerPlayerId, player.Value);
            if (friendly) qualifies = !qualifies;
            if (!qualifies) continue;

            foreach (CreatureState c in world.OwnedCreatures(player))
            {
                if (CreatureDeathRules.IsDown(c)) continue;
                var at = new Vec3(c.PosX, c.PosY, c.PosZ);
                if (!Scores(at, at, CreatureAndObjectScanLimit, out int score)) continue;
                best = score;
                chosen = new(FightTargetKind.Creature, c.Id, null);
            }
            for (int t = world.Traps.Count - 1; t >= 0; t--)
            {
                TrapState trap = world.Traps[t];
                if (trap.PlayerId != player || trap.Hidden || trap.UnderConstruction) continue;
                var at = new Vec3(trap.PosX, trap.PosY, 0);
                if (!Scores(at, at, TrapAndDoorScanLimit, out int score)) continue;
                best = score;
                chosen = new(FightTargetKind.Trap, trap.Id, null);
            }
            for (int d = world.Doors.Count - 1; d >= 0; d--)
            {
                DoorState door = world.Doors[d];
                // The cell's secret-door bit (+0xB bit 0) is not represented (Q288).
                if (door.PlayerId != player || door.UnderConstruction) continue;
                var at = new Vec3(CreatureState.CentreOf(door.X), CreatureState.CentreOf(door.Y), 0);
                if (TrapRules.DistanceSquared(me.PosX, me.PosY, at.X, at.Y) >= TrapAndDoorScanLimit || !InAimCone(yaw, pose, at)) continue;
                Vec3 direction = Normalised(at - pose);
                if (!Scores(at, at - direction, TrapAndDoorScanLimit, out int score, direction)) continue;
                best = score;
                chosen = new(FightTargetKind.Door, door.Id, null);
            }
            for (int o = world.Objects.Count - 1; o >= 0; o--)
            {
                ObjectState thing = world.Objects[o];
                if (thing.OwnerPlayerId != player || thing.ObjectTypeId != ArchPillarObjectId) continue;
                Vec3 at = ObjectPosition(world, thing);
                if (!Scores(at, at, CreatureAndObjectScanLimit, out int score)) continue;
                best = score;
                chosen = new(FightTargetKind.None, thing.Id, thing);
            }
        }
        return chosen;
    }
}
