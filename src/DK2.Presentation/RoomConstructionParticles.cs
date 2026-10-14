// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A wall-clock timestamp for view effects, in milliseconds.</summary>
public readonly record struct EffectTime(long Milliseconds);

public enum TerrainDebrisStyle { Native, Crumbling }

/// <summary>How high the Dungeon Heart's mana sparkles rise. <see cref="Native"/> is the binary's
/// motion, a climb of about two tiles to the Heart Arches' apex; <see cref="PillarHeight"/> is the
/// owner's adjustment after watching DK2 (2026-10-14): the cloud rises steadily to about the Arch
/// Pillars' top and the flakes drift back down. What lowers DK2's rise is unread (Q594).</summary>
public enum HeartSparkleStyle { Native, PillarHeight }

public sealed class RoomConstructionParticles : ISimulationEventSink
{
    private readonly GameDatabase _database;
    private readonly SimRandom _random;
    private readonly List<ConstructionParticle> _particles = [];
    private readonly List<Emitter> _emitters = [];
    private readonly Dictionary<ConstructionParticle, Emitter> _parents = [];
    private readonly Func<RoomConstructionEffect, bool>? _visible;
    private readonly Func<ViewEffectRequest, bool>? _effectVisible;
    private readonly Func<FpPosition3, Fp32> _floorHeight;
    private readonly Func<EffectTime> _clock;
    private readonly HashSet<ushort> _crumblingEffects;
    private readonly HashSet<ushort> _heartManaEffects;
    private readonly Func<string, IReadOnlyList<EffectGroupPart>?>? _groupParts;

    /// <summary>The longest Next chain followed from one request (<c>0x00440350</c> loops until a
    /// zero id; shipped chains are two long). A guard against a data cycle.</summary>
    private const int LongestChain = 8;

    /// <summary>The owner's rise for a Heart sparkle, in thousandths of the binary's vertical speed
    /// (ADR-0474). Native: born 3200 above the Heart, 24 updates at 14336..16384 with friction 393
    /// climb 8237 to peak at 2.79 tiles over the pit floor, the arches' apex. The Arch Pillars top out
    /// about 1.5 tiles over the pit floor (ring floor plus half a tile, judged from screenshots):
    /// a climb of 2944, which is 357/1000 of the native one.</summary>
    private const int HeartSparkleRisePerMille = 357;
    private readonly bool _captureAtEdges;
    private readonly Func<ViewEffectRequest, CaptureEdges>? _captureEdges;
    private long _lastUpdateMilliseconds;

    public RoomConstructionParticles(GameDatabase database, ulong seed, Func<EffectTime> clock,
        Func<RoomConstructionEffect, bool>? visible = null,
        Func<ViewEffectRequest, bool>? effectVisible = null,
        Func<FpPosition3, Fp32>? floorHeight = null,
        TerrainDebrisStyle terrainDebrisStyle = TerrainDebrisStyle.Native,
        CaptureEffectStyle captureEffectStyle = CaptureEffectStyle.Native,
        Func<ViewEffectRequest, CaptureEdges>? captureEdges = null,
        HeartSparkleStyle heartSparkleStyle = HeartSparkleStyle.Native,
        Func<string, IReadOnlyList<EffectGroupPart>?>? groupParts = null)
    {
        _groupParts = groupParts;
        _heartManaEffects = heartSparkleStyle == HeartSparkleStyle.PillarHeight
            ? new HashSet<ushort>(database.Rooms.Values.Where(r => r.TypeId == RoomTypeId.DungeonHeart)
                .Select(r => r.CreatedObjectEffectId).Where(id => id != 0))
            : [];
        _database = database;
        _random = new SimRandom(seed);
        _visible = visible;
        _effectVisible = effectVisible;
        _floorHeight = floorHeight ?? (_ => new Fp32(Fp32.One));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _captureAtEdges = captureEffectStyle == CaptureEffectStyle.Edges;
        _captureEdges = captureEdges;
        _crumblingEffects = terrainDebrisStyle == TerrainDebrisStyle.Crumbling
            ? new HashSet<ushort>(database.Terrain.Values.Select(t => t.DestroyedEffectId).Where(id => id != 0))
            : [];
        _lastUpdateMilliseconds = _clock().Milliseconds;
    }

    public IReadOnlyList<ConstructionParticle> Particles => _particles;

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.EffectRequest is ViewEffectRequest request)
            Request(request);
        else if (simEvent.ConstructionEffect is RoomConstructionEffect construction)
        {
            if ((_visible != null && !_visible(construction))
                || !_database.Effects.TryGetValue(RoomConstructionEffect.DefinitionId, out EffectDef? definition)
                || definition.GenerationType != 4) return;
            AdvanceTo(_clock());
            var point = new FpPosition3(new(CreatureState.CentreOf(construction.Tile.X)),
                new(CreatureState.CentreOf(construction.Tile.Y) - 128), construction.Height);
            EmitWave(definition, new(new(RoomConstructionEffect.DefinitionId), construction.Owner, point));
        }
    }

    public void Request(in ViewEffectRequest request)
    {
        if ((_effectVisible != null && !_effectVisible(request))
            || !_database.Effects.TryGetValue(request.EffectId.Value, out EffectDef? definition)
            || definition.GenerationType != 4) return;
        AdvanceTo(_clock());
        AddEmitterChain(definition, request);
    }

    /// <summary>An effect instance and, created with it, its Next effects (<c>0x00440350</c> loops
    /// over <c>NextEffectId</c> at the same position, owner and heading), each with its group parts.
    /// Only the first id passed the visibility test; the rest never are.</summary>
    private void AddEmitterChain(EffectDef definition, ViewEffectRequest request)
    {
        EffectDef? next = definition;
        for (int link = 0; next != null && link < LongestChain; link++)
        {
            if (next.GenerationType != 4) break;
            Emitter emitter = CreateEmitter(next, request with { EffectId = new EffectId(next.EffectId) });
            _emitters.Add(emitter);
            SpawnGroupParts(emitter);
            next = next.NextEffectId != 0 && _database.Effects.TryGetValue(next.NextEffectId, out EffectDef? chained)
                ? chained : null;
        }
    }

    /// <summary>The parts of an effect with a group art (type 8; <c>0x004347E0</c>): one element of
    /// the first generated id per member, at the instance's position plus the member's offset, the
    /// instance's heading plus a half turn, spinning by the effect's spin range, linked as children.</summary>
    private void SpawnGroupParts(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        if (effect.ArtResource.Type != 8 || _groupParts == null || effect.GenerateIds.Count == 0
            || effect.GenerateIds[0] == 0
            || !_database.EffectElements.TryGetValue(effect.GenerateIds[0], out EffectElementDef? element)) return;
        IReadOnlyList<EffectGroupPart>? parts = _groupParts(effect.ArtResource.Name);
        if (parts == null) return;
        foreach (EffectGroupPart part in parts)
        {
            FpPosition3 at = emitter.Request.Position;
            ViewEffectRequest request = emitter.Request with
            {
                Position = new FpPosition3(new(at.X.Raw + part.OffsetXRaw), new(at.Y.Raw + part.OffsetYRaw),
                    new(at.Z.Raw + part.OffsetHeightRaw)),
            };
            ConstructionParticle? particle = EmitElement(element, effect, request, null, emitter, atExactPoint: true);
            if (particle == null) continue;
            particle.HeadingRaw = particle.PreviousHeadingRaw = (particle.HeadingRaw + 0x400) & 2047;
            particle.MeshEntry = part.MeshEntry;
        }
    }

    private Emitter CreateEmitter(EffectDef definition, ViewEffectRequest request)
    {
        FpPosition3 point = request.Position;
        int floor = _floorHeight(point).Raw;
        if (point.Z.Raw < floor) point = point with { Z = new(floor) };
        double direction = request.Heading.Raw * Math.PI / 1024;
        int health = DrawInclusive(definition.MinHp, definition.MaxHp);
        int speed = DrawInclusive(definition.MinSpeedXy, definition.MaxSpeedXy);
        return new Emitter(definition, request with { Position = point },
            health, velocityX: (int)(speed * Math.Sin(direction)),
            velocityY: -(int)(speed * Math.Cos(direction)),
            velocityZ: DrawInclusive(definition.MinSpeedYz, definition.MaxSpeedYz));
    }

    private void EmitWave(EffectDef effect, ViewEffectRequest request, CaptureEdges? captureBoundary = null,
        Emitter? parent = null)
    {
        CaptureEdges? edges = _captureAtEdges
            && effect.EffectId == CaptureEffectLayout.RoomEffectId
            ? captureBoundary ?? _captureEdges?.Invoke(request) ?? CaptureEdges.All : null;
        if (edges == CaptureEdges.None) return;
        // 0x004386F0: a nonzero GenerateRandomness lets a wave through one update in (value + 1).
        if (effect.GenerateRandomness != 0 && _random.NextInt(effect.GenerateRandomness + 1) != 0) return;
        int lastSlot = effect.GenerateIds.Count - 1;
        while (lastSlot >= 0 && effect.GenerateIds[lastSlot] == 0) lastSlot--;
        if (lastSlot < 0) return;
        void EmitSlot(int slot)
        {
            if ((effect.Flags & 0x40) != 0)
            {
                // Flag 0x40: the generated ids are effects, created as top-level instances at the
                // element origin draw, not linked to this one (0x004386F0 through vtable +0x94). Their
                // heading is the draw alone (DK2 draws it over def+0x43; the orientation range stands in).
                if (_database.Effects.TryGetValue(effect.GenerateIds[slot], out EffectDef? generated))
                    AddEmitterChain(generated, request with
                    {
                        Position = DrawOrigin(effect, request.Position),
                        Heading = DrawAngularOffset(effect.OrientationRange * 8),
                    });
                return;
            }
            if (_database.EffectElements.TryGetValue(effect.GenerateIds[slot], out EffectElementDef? element))
                EmitElement(element, effect, request, edges, parent);
        }
        for (int pass = 0; pass < effect.ElementsPerTurn; pass++)
        {
            if ((effect.Flags & 0x400) != 0)
                EmitSlot(_random.NextInt(lastSlot + 1));
            else
                for (int slot = lastSlot; slot >= 0; slot--)
                    EmitSlot(slot);
        }
    }

    /// <summary>The type-4 origin draw (<c>0x00435660</c>): a point within the effect's origin ring,
    /// raised by its height range, about <paramref name="around"/>.</summary>
    private FpPosition3 DrawOrigin(EffectDef effect, FpPosition3 around)
    {
        int radius = DrawExclusive(effect.InnerOriginRange * 23, effect.OuterOriginRange * 23);
        double angle = _random.NextInt(2048) * Math.PI / 1024;
        return new FpPosition3(new(around.X.Raw + (int)(Math.Sin(angle) * radius)),
            new(around.Y.Raw - (int)(Math.Cos(angle) * radius)),
            new(around.Z.Raw + DrawExclusive(effect.LowerHeightLimit * 16, effect.UpperHeightLimit * 16)));
    }

    /// <param name="atExactPoint">A group part (0x004352E0 given a ready position): no origin ring,
    /// height or orientation draw, as for a replacement.</param>
    private ConstructionParticle? EmitElement(EffectElementDef element, EffectDef effect, ViewEffectRequest request, CaptureEdges? edges,
        Emitter? parent = null, ConstructionParticle? replaced = null, bool atExactPoint = false)
    {
        bool drawn = replaced == null && !atExactPoint;
        int radius = drawn ? DrawExclusive(effect.InnerOriginRange * 23, effect.OuterOriginRange * 23) : 0;
        double angle = drawn ? _random.NextInt(2048) * Math.PI / 1024 : 0;
        int x = request.Position.X.Raw + (int)(Math.Sin(angle) * radius);
        int y = request.Position.Y.Raw - (int)(Math.Cos(angle) * radius);
        int height = request.Position.Z.Raw + (drawn
            ? DrawExclusive(effect.LowerHeightLimit * 16, effect.UpperHeightLimit * 16) : 0);
        int heading = (request.Heading.Value + (drawn
            ? DrawAngularOffset(effect.OrientationRange * 8).Value : 0)) & 2047;
        int speed = DrawInclusive(element.MinSpeedXy, element.MaxSpeedXy);
        double direction = _random.NextInt(2048) * Math.PI / 1024;
        int frames = element.ArtResource.Unknown_0044.Length >= 10
            ? BitConverter.ToUInt16(element.ArtResource.Unknown_0044.ToArray(), 8) : 0;
        int phase = (element.ArtResource.Flags & 0x24) == 0x24 && frames > 0
            ? _random.NextInt(frames) * (4096 / frames) : 0;
        int health = DrawInclusive(element.MinHp, element.MaxHp);
        int scale = DrawExclusive(element.MinScale, element.MaxScale);
        int scaleTarget = (short)(((long)scale * element.ScaleRatio) >> 12);
        int scaleIncrement = health > 0 ? (short)((scaleTarget - scale) / health) : 0;
        (byte red, byte green, byte blue) = (element.ArtResource.Flags & 0x80) != 0
            ? KeeperColour(request.Owner) : (element.Red, element.Green, element.Blue);
        var particle = new ConstructionParticle
        {
            Definition = element, XRaw = x, YRaw = y, HeightRaw = height,
            PreviousXRaw = x, PreviousYRaw = y, PreviousHeightRaw = height,
            VelocityX = (int)(speed * Math.Sin(direction)), VelocityY = -(int)(speed * Math.Cos(direction)),
            VelocityZ = DrawInclusive(element.MinSpeedYz, element.MaxSpeedYz),
            Health = health, InitialHealth = health,
            InitialScale = scale, ScaleTarget = scaleTarget, ScaleIncrement = scaleIncrement,
            InitialPhase = phase, FrameDuration = (element.ArtResource.Flags & 4) != 0 ? frames : 0,
            Red = red, Green = green, Blue = blue,
            HeadingRaw = heading, PreviousHeadingRaw = heading,
        };
        if (replaced == null && _heartManaEffects.Contains(effect.EffectId))
        {
            // Owner-requested presentation adjustment (ADR-0474, Q594); native motion remains the default.
            particle.VelocityZ = particle.VelocityZ * HeartSparkleRisePerMille / 1000;
        }
        if ((element.Flags & 16) != 0)
        {
            particle.SwayPhaseRaw = _random.NextInt(2048);
            particle.SecondSwayPhaseRaw = _random.NextInt(2048);
            particle.FallPhaseRaw = _random.NextInt(2048);
        }
        if (replaced == null && element.ArtResource.Type == 5 && _crumblingEffects.Contains(effect.EffectId))
        {
            // Owner-requested presentation adjustment; native motion remains the default (ADR-0449).
            particle.XRaw = request.Position.X.Raw + DrawExclusive(-1638, 1639);
            particle.YRaw = request.Position.Y.Raw + DrawExclusive(-1638, 1639);
            particle.HeightRaw = request.Position.Z.Raw + DrawExclusive(3072, 4097);
            particle.PreviousXRaw = particle.XRaw;
            particle.PreviousYRaw = particle.YRaw;
            particle.PreviousHeightRaw = particle.HeightRaw;
            particle.VelocityX = DrawExclusive(-1024, 1025);
            particle.VelocityY = DrawExclusive(-1024, 1025);
            particle.VelocityZ = -Math.Max(2048, Math.Abs(particle.VelocityZ));
            particle.InitialScale = particle.InitialScale * 2 / 3;
            particle.ScaleTarget = particle.ScaleTarget * 2 / 3;
            particle.ScaleIncrement = health > 0 ? (particle.ScaleTarget - particle.InitialScale) / health : 0;
        }
        if (edges.HasValue)
        {
            int count = 0;
            for (int bit = 1; bit <= 8; bit <<= 1) if (((int)edges.Value & bit) != 0) count++;
            int selected = _random.NextInt(count);
            CaptureEdges edge = CaptureEdges.None;
            for (int bit = 1; bit <= 8; bit <<= 1)
                if (((int)edges.Value & bit) != 0 && selected-- == 0) { edge = (CaptureEdges)bit; break; }
            int along = DrawExclusive(-1984, 1985);
            particle.XRaw = request.Position.X.Raw + (edge == CaptureEdges.West ? -1984
                : edge == CaptureEdges.East ? 1984 : along);
            particle.YRaw = request.Position.Y.Raw + (edge == CaptureEdges.North ? -1984
                : edge == CaptureEdges.South ? 1984 : along);
            particle.PreviousXRaw = particle.XRaw;
            particle.PreviousYRaw = particle.YRaw;
            particle.VelocityX = particle.VelocityY = 0;
            particle.InitialScale = particle.InitialScale * 4 / 5;
            particle.ScaleTarget = particle.ScaleTarget * 4 / 5;
            particle.ScaleIncrement = health > 0 ? (particle.ScaleTarget - particle.InitialScale) / health : 0;
            particle.Red = (byte)(particle.Red * 4 / 5);
            particle.Green = (byte)(particle.Green * 4 / 5);
            particle.Blue = (byte)(particle.Blue * 4 / 5);
        }
        if (!ApplyFloorContact(particle)) return null;
        int spinRange = effect.SpriteSpinRateRange * 8;
        particle.HeadingSpinRaw = replaced?.HeadingSpinRaw ?? DrawAngularOffset(spinRange).Value;
        particle.PitchSpinRaw = replaced?.PitchSpinRaw ?? DrawAngularOffset(spinRange).Value;
        particle.RollSpinRaw = replaced?.RollSpinRaw ?? DrawAngularOffset(spinRange).Value;
        particle.PreviousHeightRaw = particle.HeightRaw;
        _particles.Add(particle);
        if (parent != null) { _parents.Add(particle, parent); parent.ChildCount++; }
        return particle;
    }

    public float Advance() => AdvanceTo(_clock());

    public float AdvanceTo(EffectTime now)
    {
        long elapsedMilliseconds = now.Milliseconds - _lastUpdateMilliseconds;
        if (elapsedMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(now));
        if (elapsedMilliseconds <= 50) return elapsedMilliseconds / 50f;
        int elapsedUpdates = checked((int)(elapsedMilliseconds / 50));
        _lastUpdateMilliseconds += elapsedUpdates * 50L;
        for (int i = _particles.Count - 1; i >= 0; i--)
        {
            ConstructionParticle particle = _particles[i];
            particle.Health -= elapsedUpdates;
            if (particle.Health <= 0 && particle.Definition.DeathElementId == 0)
            { RemoveParticle(i, particle); continue; }
            particle.VisualUpdates++;
            particle.PreviousXRaw = particle.XRaw;
            particle.PreviousYRaw = particle.YRaw;
            particle.PreviousHeightRaw = particle.HeightRaw;
            particle.PreviousHeadingRaw = particle.HeadingRaw;
            particle.PreviousPitchRaw = particle.PitchRaw;
            particle.PreviousRollRaw = particle.RollRaw;
            particle.HeadingRaw = (particle.HeadingRaw + particle.HeadingSpinRaw) & 2047;
            particle.PitchRaw = (particle.PitchRaw + particle.PitchSpinRaw) & 2047;
            particle.RollRaw = (particle.RollRaw + particle.RollSpinRaw) & 2047;
            ApplyParentMotion(particle);
            int drag = 4096 - particle.Definition.AirFriction;
            particle.VelocityZ -= particle.Definition.Mass;
            particle.VelocityX = (int)((long)particle.VelocityX * drag / 4096);
            particle.VelocityY = (int)((long)particle.VelocityY * drag / 4096);
            particle.VelocityZ = (int)((long)particle.VelocityZ * drag / 4096);
            particle.XRaw += particle.VelocityX >> 4;
            particle.YRaw += particle.VelocityY >> 4;
            particle.HeightRaw += particle.VelocityZ >> 4;
            ApplyFallingSway(particle);
            if (!ApplyFloorContact(particle) || particle.Health <= 0)
            { RemoveParticle(i, particle); continue; }
            if ((particle.Definition.Flags & 8) != 0
                && Math.Max(particle.Red, Math.Max(particle.Green, particle.Blue)) >= particle.Health)
            {
                particle.Red = (byte)(particle.Red * (particle.Health - 1) / particle.Health);
                particle.Green = (byte)(particle.Green * (particle.Health - 1) / particle.Health);
                particle.Blue = (byte)(particle.Blue * (particle.Health - 1) / particle.Health);
            }
        }
        for (int i = _emitters.Count - 1; i >= 0; i--)
        {
            Emitter emitter = _emitters[i];
            int drag = 4096 - emitter.Definition.AirFriction;
            emitter.VelocityZ -= emitter.Definition.Mass;
            emitter.VelocityX = (int)((long)emitter.VelocityX * drag / 4096);
            emitter.VelocityY = (int)((long)emitter.VelocityY * drag / 4096);
            emitter.VelocityZ = (int)((long)emitter.VelocityZ * drag / 4096);
            FpPosition3 old = emitter.Request.Position;
            var point = new FpPosition3(new(old.X.Raw + (emitter.VelocityX >> 4)),
                new(old.Y.Raw + (emitter.VelocityY >> 4)), new(old.Z.Raw + (emitter.VelocityZ >> 4)));
            int floor = _floorHeight(point).Raw;
            if (point.Z.Raw < floor) point = point with { Z = new(floor) };
            emitter.Request = emitter.Request with { Position = point };
            if (emitter.Health > 0)
            {
                if (_captureAtEdges && !emitter.CaptureBoundary.HasValue
                    && emitter.Definition.EffectId == CaptureEffectLayout.RoomEffectId)
                    emitter.CaptureBoundary = _captureEdges?.Invoke(emitter.Request) ?? CaptureEdges.All;
                EmitWave(emitter.Definition, emitter.Request, emitter.CaptureBoundary, emitter);
            }
            emitter.Health -= elapsedUpdates;
            if (emitter.Health <= 0 && emitter.ChildCount == 0)
            {
                // Removed only once its HP is gone and its last element has died (0x0043C041); the
                // removal creates the death effect where the instance now is (0x004354F0).
                _emitters.RemoveAt(i);
                if (emitter.Definition.DeathEffectId != 0
                    && _database.Effects.TryGetValue(emitter.Definition.DeathEffectId, out EffectDef? death))
                    AddEmitterChain(death, emitter.Request);
            }
        }
        return (now.Milliseconds - _lastUpdateMilliseconds) / 50f;
    }

    private bool ApplyFloorContact(ConstructionParticle particle)
    {
        int floor = _floorHeight(new(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw))).Raw;
        if (particle.HeightRaw >= floor) return true;
        if ((particle.Definition.Flags & 1) != 0) return false;
        particle.HeadingSpinRaw = particle.PitchSpinRaw = particle.RollSpinRaw = 0;
        if ((particle.Definition.Flags & 16) != 0)
        {
            particle.HeightRaw = floor;
            particle.VelocityX = particle.VelocityY = particle.VelocityZ = 0;
            return true;
        }
        int elasticity = unchecked((ushort)particle.Definition.Elasticity);
        particle.HeightRaw = floor + (int)((long)(floor - particle.HeightRaw) * elasticity / 65536);
        particle.VelocityX = (int)((long)particle.VelocityX * elasticity / 65536);
        particle.VelocityY = (int)((long)particle.VelocityY * elasticity / 65536);
        int bounce = -(int)((long)particle.VelocityZ * elasticity / 65536);
        particle.VelocityZ = bounce < 8192 ? (int)((long)bounce * bounce >> 13)
            : bounce < 131072 ? (int)((long)bounce * 247 >> 8) : bounce;
        return true;
    }

    private void RemoveParticle(int index, ConstructionParticle particle)
    {
        _parents.TryGetValue(particle, out Emitter? parent);
        if (parent != null
            && _database.EffectElements.TryGetValue(particle.Definition.DeathElementId, out EffectElementDef? death))
        {
            var point = new FpPosition3(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw));
            EmitElement(death, parent.Definition,
                parent.Request with { Position = point, Heading = new((ushort)particle.HeadingRaw) },
                null, parent, particle);
        }
        _particles.RemoveAt(index);
        if (_parents.Remove(particle) && parent != null) parent.ChildCount--;
    }

    private void ApplyParentMotion(ConstructionParticle particle)
    {
        if (!_parents.TryGetValue(particle, out Emitter? parent)) return;
        if (_captureAtEdges && parent.CaptureBoundary.HasValue) return;
        int dx = particle.XRaw - parent.Request.Position.X.Raw;
        int dy = particle.YRaw - parent.Request.Position.Y.Raw;
        int whirlpool = unchecked((short)parent.Definition.WhirlpoolRate);
        if (whirlpool > 127) whirlpool -= 256;
        int circular = unchecked((short)parent.Definition.CircularPathRate);
        if (circular > 127) circular -= 256;
        int angle = whirlpool == 0 ? 0
            : (whirlpool * 16 << 7) / ((dx >> 5) * (dx >> 5) + 128 + (dy >> 5) * (dy >> 5));
        (int x, int y) first = RotateRelative((dx, dy),
            new((ushort)(Math.Max(-1024, Math.Min(1023, angle)) & 2047)));
        (int x, int y) second = RotateRelative((dx, dy),
            new((ushort)(Math.Max(-1024, Math.Min(1023, circular)) & 2047)));
        particle.XRaw += first.x - dx + second.x - dx;
        particle.YRaw += first.y - dy + second.y - dy;
    }

    private static (int X, int Y) RotateRelative((int X, int Y) offset, Dk2Angle heading)
    {
        int sin = Trig(heading.Raw), cos = Trig(heading.Raw + 512);
        return ((int)(((long)sin * offset.Y + (long)cos * offset.X) >> 16),
            (int)(((long)cos * offset.Y - (long)sin * offset.X) >> 16));
    }

    private void ApplyFallingSway(ConstructionParticle particle)
    {
        if ((particle.Definition.Flags & 16) == 0) return;
        int floor = _floorHeight(new(new(particle.PreviousXRaw), new(particle.PreviousYRaw),
            new(particle.PreviousHeightRaw))).Raw;
        if (particle.PreviousHeightRaw > floor)
        {
            particle.XRaw += (Trig(particle.SwayPhaseRaw) >> 6) >> 4;
            particle.YRaw += (Trig((particle.SecondSwayPhaseRaw & 1023) * 2) >> 6) >> 4;
            int sin = Trig(particle.FallPhaseRaw);
            particle.HeightRaw += -32 + (int)((((long)sin * sin >> 16) << 6) >> 16)
                - ((Trig(particle.FallPhaseRaw + 512) << 5) >> 16);
        }
        particle.SwayPhaseRaw = (particle.SwayPhaseRaw + 32) & 2047;
        particle.SecondSwayPhaseRaw = (particle.SecondSwayPhaseRaw + 32) & 2047;
        particle.FallPhaseRaw = (particle.FallPhaseRaw + 64) & 2047;
    }

    private static int Trig(int heading) => (int)(Math.Sin((heading & 2047) * Math.PI / 1024) * 65536);

    private static (byte Red, byte Green, byte Blue) KeeperColour(PlayerId owner)
    {
        // Player tags currently equal Numbers (Q271); the native palette uses Number.
        (byte Red, byte Green, byte Blue) colour = owner.Value switch
        {
            0 => (0, 0, 0), 1 => (191, 213, 224), 2 => (255, 255, 255),
            3 => (230, 50, 50), 4 => (50, 78, 230), 5 => (50, 230, 51),
            6 => (226, 230, 50), 7 => (230, 50, 212), _ => (191, 213, 224)
        };
        return ((byte)Math.Max(48, (int)colour.Red), (byte)Math.Max(48, (int)colour.Green),
            (byte)Math.Max(48, (int)colour.Blue));
    }

    private sealed class Emitter(EffectDef definition, ViewEffectRequest request, int health,
        int velocityX, int velocityY, int velocityZ)
    {
        public readonly EffectDef Definition = definition;
        public ViewEffectRequest Request = request;
        public CaptureEdges? CaptureBoundary;
        public int Health = health;
        public int ChildCount;
        public int VelocityX = velocityX, VelocityY = velocityY, VelocityZ = velocityZ;
    }

    private int DrawExclusive(int minimum, int maximum) => maximum <= minimum
        ? minimum : minimum + _random.NextInt(maximum - minimum);

    private Dk2Angle DrawAngularOffset(int count) => count <= 0
        ? default : new((ushort)((_random.NextInt(count) - count / 2) & 2047));

    private int DrawInclusive(int minimum, int maximum) => maximum <= minimum
        ? minimum : _random.NextInt(minimum, maximum);
}

/// <summary>A member of an effect's group art: the mesh entry it draws and its offset from the
/// instance, in position units (4096 a tile; height up).</summary>
public readonly record struct EffectGroupPart(string MeshEntry, int OffsetXRaw, int OffsetYRaw, int OffsetHeightRaw);

public sealed class ConstructionParticle
{
    public required EffectElementDef Definition { get; init; }

    /// <summary>The mesh entry a group part draws instead of its definition's art (null for every
    /// other particle): the renderer keys its mesh groups by it.</summary>
    public string? MeshEntry { get; internal set; }
    public int XRaw { get; internal set; }
    public int YRaw { get; internal set; }
    public int HeightRaw { get; internal set; }
    public int VelocityX { get; internal set; }
    public int VelocityY { get; internal set; }
    public int VelocityZ { get; internal set; }
    public int Health { get; internal set; }
    public int InitialHealth { get; internal set; }
    public int VisualUpdates { get; internal set; }
    public int InitialScale { get; internal set; }
    public int ScaleTarget { get; internal set; }
    public int ScaleIncrement { get; internal set; }
    public int InitialPhase { get; internal set; }
    public int FrameDuration { get; internal set; }
    public int PreviousXRaw { get; internal set; }
    public int PreviousYRaw { get; internal set; }
    public int PreviousHeightRaw { get; internal set; }
    // Native view angles and increments wrap at 2048 units per turn.
    public int HeadingRaw { get; internal set; }
    public int PitchRaw { get; internal set; }
    public int RollRaw { get; internal set; }
    public int PreviousHeadingRaw { get; internal set; }
    public int PreviousPitchRaw { get; internal set; }
    public int PreviousRollRaw { get; internal set; }
    public int HeadingSpinRaw { get; internal set; }
    public int PitchSpinRaw { get; internal set; }
    public int RollSpinRaw { get; internal set; }
    internal int SwayPhaseRaw;
    internal int SecondSwayPhaseRaw;
    internal int FallPhaseRaw;
    public byte Red { get; internal set; }
    public byte Green { get; internal set; }
    public byte Blue { get; internal set; }
    public int PhaseRaw
    {
        get
        {
            if ((Definition.ArtResource.Flags & 4) == 0 || FrameDuration <= 0) return 0;
            int phase = InitialPhase + VisualUpdates * (4096 / FrameDuration);
            return (Definition.ArtResource.Flags & 0x80) != 0 ? Math.Min(4096, phase) : phase & 4095;
        }
    }
    public float RemainingSpriteLifetimeFraction => 1f - Math.Min(PhaseRaw, 4095) / 4096f;
    public int ScaleRaw => (Definition.Flags & 6) == 0 ? InitialScale
        : ScaleIncrement >= 0 ? Math.Min(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates)
        : Math.Max(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates);
}
