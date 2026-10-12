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
    private readonly Func<RoomConstructionEffect, bool>? _visible;
    private readonly Func<EffectTime> _clock;
    private long _lastUpdateMilliseconds;

    public RoomConstructionParticles(GameDatabase database, ulong seed, Func<EffectTime> clock,
        Func<RoomConstructionEffect, bool>? visible = null)
    {
        _database = database;
        _random = new SimRandom(seed);
        _visible = visible;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _lastUpdateMilliseconds = _clock().Milliseconds;
    }

    public IReadOnlyList<ConstructionParticle> Particles => _particles;

    public void Emit(in SimEvent simEvent)
    {
        if (simEvent.ConstructionEffect is not RoomConstructionEffect request
            || (_visible != null && !_visible(request))
            || !_database.Effects.TryGetValue(RoomConstructionEffect.DefinitionId, out EffectDef? effect)) return;
        AdvanceTo(_clock());
        if (effect.GenerationType != 4) return;
        for (int pass = 0; pass < effect.ElementsPerTurn; pass++)
            for (int slot = effect.GenerateIds.Count - 1; slot >= 0; slot--)
            {
                if (!_database.EffectElements.TryGetValue(effect.GenerateIds[slot], out EffectElementDef? element)) continue;
                int radius = DrawExclusive(effect.InnerOriginRange * 23, effect.OuterOriginRange * 23);
                double angle = _random.NextInt(2048) * Math.PI / 1024;
                int x = CreatureState.CentreOf(request.Tile.X) + (int)(Math.Sin(angle) * radius);
                int y = CreatureState.CentreOf(request.Tile.Y) - 128 - (int)(Math.Cos(angle) * radius);
                int height = request.Height.Raw + DrawExclusive(effect.LowerHeightLimit * 16, effect.UpperHeightLimit * 16);
                int speed = _random.NextInt(element.MinSpeedXy, element.MaxSpeedXy);
                double direction = _random.NextInt(2048) * Math.PI / 1024;
                int frames = element.ArtResource.Unknown_0044.Length >= 10
                    ? BitConverter.ToUInt16(element.ArtResource.Unknown_0044.ToArray(), 8) : 0;
                int phase = (element.ArtResource.Flags & 0x20) != 0 && frames > 0 ? _random.NextInt(frames) * 4096 / frames : 0;
                int health = _random.NextInt(element.MinHp, element.MaxHp);
                int scale = DrawExclusive(element.MinScale, element.MaxScale);
                int scaleTarget = (short)(((long)scale * element.ScaleRatio) >> 12);
                int scaleIncrement = health > 0 ? (short)((scaleTarget - scale) / health) : 0;
                _particles.Add(new ConstructionParticle
                {
                    Definition = element, XRaw = x, YRaw = y, HeightRaw = height,
                    PreviousXRaw = x, PreviousYRaw = y, PreviousHeightRaw = height,
                    VelocityX = (int)(speed * Math.Sin(direction)), VelocityY = -(int)(speed * Math.Cos(direction)),
                    VelocityZ = _random.NextInt(element.MinSpeedYz, element.MaxSpeedYz),
                    Health = health, InitialHealth = health,
                    InitialScale = scale, ScaleTarget = scaleTarget, ScaleIncrement = scaleIncrement,
                    InitialPhase = phase, FrameDuration = (element.ArtResource.Flags & 4) != 0 ? frames : 0,
                    Red = element.Red, Green = element.Green, Blue = element.Blue,
                });
            }
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
            int drag = 4096 - particle.Definition.AirFriction;
            particle.VelocityZ -= particle.Definition.Mass;
            particle.VelocityX = (int)((long)particle.VelocityX * drag / 4096);
            particle.VelocityY = (int)((long)particle.VelocityY * drag / 4096);
            particle.VelocityZ = (int)((long)particle.VelocityZ * drag / 4096);
            particle.XRaw += particle.VelocityX >> 4;
            particle.YRaw += particle.VelocityY >> 4;
            particle.HeightRaw += particle.VelocityZ >> 4;
            // Flat room floor for this visual projection; wall contacts remain a research boundary.
            if (particle.HeightRaw < 0x1000)
            {
                if ((particle.Definition.Flags & 1) != 0) { _particles.RemoveAt(i); continue; }
                int elasticity = unchecked((ushort)particle.Definition.Elasticity);
                particle.HeightRaw = 0x1000 + (int)((long)(0x1000 - particle.HeightRaw) * elasticity >> 16);
                particle.VelocityX = (int)((long)particle.VelocityX * elasticity >> 16);
                particle.VelocityY = (int)((long)particle.VelocityY * elasticity >> 16);
                int bounce = -(int)((long)particle.VelocityZ * elasticity >> 16);
                particle.VelocityZ = bounce < 8192 ? (int)((long)bounce * bounce >> 13)
                    : bounce < 131072 ? (int)((long)bounce * 247 >> 8) : bounce;
            }
            if ((particle.Definition.Flags & 8) != 0
                && Math.Max(particle.Red, Math.Max(particle.Green, particle.Blue)) >= particle.Health)
            {
                particle.Red = (byte)(particle.Red * (particle.Health - 1) / particle.Health);
                particle.Green = (byte)(particle.Green * (particle.Health - 1) / particle.Health);
                particle.Blue = (byte)(particle.Blue * (particle.Health - 1) / particle.Health);
            }
        }
        return (now.Milliseconds - _lastUpdateMilliseconds) / 50f;
    }

    private int DrawExclusive(int minimum, int maximum) => maximum <= minimum
        ? minimum : minimum + _random.NextInt(maximum - minimum);
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
    public byte Red { get; internal set; }
    public byte Green { get; internal set; }
    public byte Blue { get; internal set; }
    public int PhaseRaw => FrameDuration <= 0 ? 0
        : (InitialPhase + VisualUpdates * 4096 / FrameDuration) & 4095;
    public int ScaleRaw => (Definition.Flags & 6) == 0 ? InitialScale
        : ScaleIncrement >= 0 ? Math.Min(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates)
        : Math.Max(ScaleTarget, InitialScale + ScaleIncrement * VisualUpdates);
}
