// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

/// <summary>A wall-clock timestamp for view effects, in milliseconds.</summary>
public readonly record struct EffectTime(long Milliseconds);

public sealed class RoomConstructionParticles : ISimulationEventSink
{
    private readonly GameDatabase _database;
    private readonly SimRandom _random;
    private readonly List<ConstructionParticle> _particles = [];
    private readonly List<Emitter> _emitters = [];
    private readonly Func<RoomConstructionEffect, bool>? _visible;
    private readonly Func<ViewEffectRequest, bool>? _effectVisible;
    private readonly Func<FpPosition3, Fp32> _floorHeight;
    private readonly Func<EffectTime> _clock;
    private long _lastUpdateMilliseconds;

    public RoomConstructionParticles(GameDatabase database, ulong seed, Func<EffectTime> clock,
        Func<RoomConstructionEffect, bool>? visible = null,
        Func<ViewEffectRequest, bool>? effectVisible = null,
        Func<FpPosition3, Fp32>? floorHeight = null)
    {
        _database = database;
        _random = new SimRandom(seed);
        _visible = visible;
        _effectVisible = effectVisible;
        _floorHeight = floorHeight ?? (_ => new Fp32(Fp32.One));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lastUpdateMilliseconds = _clock().Milliseconds;
    }

    public IReadOnlyList<ConstructionParticle> Particles => _particles;

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.EffectRequest is ViewEffectRequest request)
        {
            if ((_effectVisible != null && !_effectVisible(request))
                || !_database.Effects.TryGetValue(request.EffectId.Value, out EffectDef? definition)
                || definition.GenerationType != 4 || (definition.Flags & 0x40) != 0) return;
            AdvanceTo(_clock());
            _emitters.Add(CreateEmitter(definition, request));
        }
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

    private void EmitWave(EffectDef effect, ViewEffectRequest request)
    {
        int lastSlot = effect.GenerateIds.Count - 1;
        while (lastSlot >= 0 && effect.GenerateIds[lastSlot] == 0) lastSlot--;
        if (lastSlot < 0) return;
        for (int pass = 0; pass < effect.ElementsPerTurn; pass++)
        {
            if ((effect.Flags & 0x400) != 0)
                EmitElement(effect.GenerateIds[_random.NextInt(lastSlot + 1)], effect, request);
            else
                for (int slot = lastSlot; slot >= 0; slot--)
                    EmitElement(effect.GenerateIds[slot], effect, request);
        }
    }

    private void EmitElement(ushort elementId, EffectDef effect, ViewEffectRequest request)
    {
        if (!_database.EffectElements.TryGetValue(elementId, out EffectElementDef? element)) return;
        int radius = DrawExclusive(effect.InnerOriginRange * 23, effect.OuterOriginRange * 23);
        double angle = _random.NextInt(2048) * Math.PI / 1024;
        int x = request.Position.X.Raw + (int)(Math.Sin(angle) * radius);
        int y = request.Position.Y.Raw - (int)(Math.Cos(angle) * radius);
        int height = request.Position.Z.Raw + DrawExclusive(effect.LowerHeightLimit * 16, effect.UpperHeightLimit * 16);
        int heading = (request.Heading.Value + DrawAngularOffset(effect.OrientationRange * 8).Value) & 2047;
        int speed = DrawInclusive(element.MinSpeedXy, element.MaxSpeedXy);
        double direction = _random.NextInt(2048) * Math.PI / 1024;
        int frames = element.ArtResource.Unknown_0044.Length >= 10
            ? BitConverter.ToUInt16(element.ArtResource.Unknown_0044.ToArray(), 8) : 0;
        int phase = (element.ArtResource.Flags & 0x20) != 0 && frames > 0 ? _random.NextInt(frames) * 4096 / frames : 0;
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
        if (!ApplyFloorContact(particle)) return;
        int spinRange = effect.SpriteSpinRateRange * 8;
        particle.HeadingSpinRaw = DrawAngularOffset(spinRange).Value;
        particle.PitchSpinRaw = DrawAngularOffset(spinRange).Value;
        particle.RollSpinRaw = DrawAngularOffset(spinRange).Value;
        particle.PreviousHeightRaw = particle.HeightRaw;
        _particles.Add(particle);
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
            if (particle.Health <= 0) { _particles.RemoveAt(i); continue; }
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
            int drag = 4096 - particle.Definition.AirFriction;
            particle.VelocityZ -= particle.Definition.Mass;
            particle.VelocityX = (int)((long)particle.VelocityX * drag / 4096);
            particle.VelocityY = (int)((long)particle.VelocityY * drag / 4096);
            particle.VelocityZ = (int)((long)particle.VelocityZ * drag / 4096);
            particle.XRaw += particle.VelocityX >> 4;
            particle.YRaw += particle.VelocityY >> 4;
            particle.HeightRaw += particle.VelocityZ >> 4;
            if (!ApplyFloorContact(particle)) { _particles.RemoveAt(i); continue; }
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
            if (emitter.Health > 0) EmitWave(emitter.Definition, emitter.Request);
            emitter.Health -= elapsedUpdates;
            if (emitter.Health <= 0) _emitters.RemoveAt(i);
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
        public int Health = health;
        public int VelocityX = velocityX, VelocityY = velocityY, VelocityZ = velocityZ;
    }

    private int DrawExclusive(int minimum, int maximum) => maximum <= minimum
        ? minimum : minimum + _random.NextInt(maximum - minimum);

    private Dk2Angle DrawAngularOffset(int count) => count <= 0
        ? default : new((ushort)((_random.NextInt(count) - count / 2) & 2047));

    private int DrawInclusive(int minimum, int maximum) => maximum <= minimum
        ? minimum : _random.NextInt(minimum, maximum);
}

public sealed class ConstructionParticle
{
    public required EffectElementDef Definition { get; init; }
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
    public byte Red { get; internal set; }
    public byte Green { get; internal set; }
    public byte Blue { get; internal set; }
    public int PhaseRaw => FrameDuration <= 0 ? 0
        : (InitialPhase + VisualUpdates * 4096 / FrameDuration) & 4095;
    public int ScaleRaw => (Definition.Flags & 6) == 0 ? InitialScale
        : ScaleIncrement >= 0 ? Math.Min(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates)
        : Math.Max(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates);
}
