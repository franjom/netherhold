// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed partial class RoomConstructionParticles
{
    private static int FlightHeading(int x, int y) =>
        (int)(Math.Atan2(x, -y) * 1024 / Math.PI + 2048) & 2047;

    private static void AddInheritedVelocity(Emitter child, Emitter? parent, ViewEffectRequest request)
    {
        FpPosition3 carried = request.CarriedVelocity ?? default;
        child.VelocityX += carried.X.Raw + (parent?.VelocityX ?? 0);
        child.VelocityY += carried.Y.Raw + (parent?.VelocityY ?? 0);
        child.VelocityZ += carried.Z.Raw + (parent?.VelocityZ ?? 0);
    }

    private static ViewEffectRequest FreeRequest(ViewEffectRequest request, EffectId id,
        FpPosition3 point, Dk2Angle heading) => new(id, request.Owner, point, heading);

    private void Generate(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        switch (effect.GenerationType)
        {
            case 4:
                EmitWave(effect, emitter.Request, emitter.CaptureBoundary, emitter);
                break;
            case 18 when (effect.Flags & 0x40) != 0:
                for (int pass = 0; pass < effect.ElementsPerTurn >> 2; pass++)
                    for (int slot = Math.Min(3, effect.GenerateIds.Count - 1); slot >= 0; slot--)
                        if (effect.GenerateIds[slot] != 0)
                            SpawnFreeEffect(effect.GenerateIds[slot], emitter.Request,
                                emitter.Request.Position, new((ushort)_random.NextInt(2048)));
                break;
            case 6:
                ushort first = effect.GenerateIds.FirstOrDefault();
                ushort last = effect.GenerateIds.LastOrDefault(id => id != 0);
                if (last < first || first == 0) break;
                for (int pass = 0; pass < effect.ElementsPerTurn; pass++)
                {
                    ushort id = (ushort)(first + _random.NextInt(last - first + 1));
                    var heading = new Dk2Angle((ushort)((_random.NextInt(64) - 32) & 2047));
                    if (_database.EffectElements.TryGetValue(id, out EffectElementDef? element))
                        EmitElement(element, effect, emitter.Request with { Heading = heading }, null, emitter, exactOrigin: true);
                }
                if (emitter.Age % 3 == 0)
                    SpawnFreeEffect(effect.OuterOriginRange, emitter.Request, emitter.Request.Position, default);
                break;
            case 3:
                GenerateTileBurst(emitter);
                break;
            case 14:
                GenerateDigits(emitter);
                break;
        }
    }

    /// <summary>A floating amount (<c>0x00439480</c>): one element per decimal digit of the request's
    /// amount (0 has one), units first, element id = first generated id + digit, at the instance's
    /// point with heading 0. The units digit sits <c>(n/2)·w</c> to the right and each following digit
    /// <c>w</c> further left, where <c>w</c> is the first element's MaxScale, so the 0.3-tile digits abut
    /// and read left to right (floating-cost-numbers.md section 4).</summary>
    private void GenerateDigits(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        ushort first = effect.GenerateIds.FirstOrDefault();
        if (first == 0 || !_database.EffectElements.TryGetValue(first, out EffectElementDef? zero)) return;
        int amount = emitter.Request.Amount ?? 0;
        int digits = 1;
        for (int rest = amount / 10; rest > 0; rest /= 10) digits++;
        int width = zero.MaxScale;
        int offset = digits / 2 * width;
        ViewEffectRequest at = emitter.Request with { Heading = default };
        for (int digit = 0; digit < digits; digit++, amount /= 10, offset -= width)
        {
            if (!_database.EffectElements.TryGetValue((ushort)(first + amount % 10), out EffectElementDef? element)) return;
            ConstructionParticle? particle = EmitElement(element, effect, at, null, emitter, exactOrigin: true);
            if (particle == null) continue;
            // The digits are alpha sprites with RGB 0,0,0, so all they show is their tile's light (Q609).
            particle.ScreenOffsetXRaw = offset;
        }
    }

    private void GenerateTileBurst(Emitter emitter)
    {
        EffectDef effect = emitter.Definition;
        ushort first = effect.GenerateIds.FirstOrDefault();
        ushort last = effect.GenerateIds.LastOrDefault(id => id != 0);
        void Burst(ushort id, int count, int range, int offset)
        {
            for (int pass = 0; pass < count; pass++)
            {
                var heading = new Dk2Angle((ushort)_random.NextInt(2048));
                FpPosition3 at = emitter.Request.Position;
                at = new(new(at.X.Raw + _random.NextInt(range) + offset),
                    new(at.Y.Raw + _random.NextInt(range) + offset), new(at.Z.Raw + 4096));
                if (!_database.EffectElements.TryGetValue(id, out EffectElementDef? element)) continue;
                ConstructionParticle? child = EmitElement(element, effect,
                    emitter.Request with { Position = at, Heading = heading }, null, emitter, exactOrigin: true);
                if (child == null) continue;
                int xy = DrawInclusive(effect.MinSpeedXy, effect.MaxSpeedXy);
                int z = DrawInclusive(effect.MinSpeedYz, effect.MaxSpeedYz);
                if (xy == 0 && z == 0) continue;
                double angle = _random.NextInt(2048) * Math.PI / 1024;
                child.VelocityX += (int)(xy * Math.Sin(angle));
                child.VelocityY -= (int)(xy * Math.Cos(angle));
                child.VelocityZ += z;
            }
        }
        Burst(first, effect.ElementsPerTurn, 2048, 1024);
        if (emitter.Age is > 8 and < 72) Burst(last, effect.ElementsPerTurn * 8, 4096, 0);
        if (emitter.Age is > 12 and < 56 && _random.NextInt(15) == 0)
        {
            FpPosition3 at = emitter.Request.Position;
            SpawnFreeEffect(effect.OuterOriginRange, emitter.Request,
                new(new(at.X.Raw + 2048), new(at.Y.Raw + 2048), new(at.Z.Raw + 4096)),
                new((ushort)_random.NextInt(2048)));
        }
    }

    private void SpawnFreeEffect(ushort id, ViewEffectRequest source, FpPosition3 at, Dk2Angle heading)
    {
        if (id == 0 || !_database.Effects.TryGetValue(id, out EffectDef? definition) || !SupportsEffect(definition)) return;
        // Generation, contact children and element NextEffect use view slot94, the ungated
        // allocator; only caller requests and teardown use slot90's visibility test.
        ViewEffectRequest request = FreeRequest(source, new(id), at, heading) with { Visibility = EffectVisibility.Ungated };
        AddEmitterChain(definition, request);
    }

    private void SpawnElementNext(ConstructionParticle particle)
    {
        if (_parents.TryGetValue(particle, out Emitter? parent))
            SpawnFreeEffect(particle.Definition.NextEffectId, parent.Request,
                new(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw)), new((ushort)particle.HeadingRaw));
    }
}
