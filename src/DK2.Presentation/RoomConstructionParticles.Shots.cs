// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Pathfinding;
using DK2.Simulation;
using DK2.Simulation.Navigation;

namespace DK2.Presentation;

/// <summary>The shot's interpolated point and latest aim, or an expired shot. A null point keeps
/// the effect where it was when the view rejects the candidate point.</summary>
public readonly record struct ShotEffectAttachment(bool ShotExists, FpPosition3? Point, FpPosition3? Aim)
{
    public static ShotEffectAttachment Gone => default;
    public static ShotEffectAttachment At(FpPosition3 point, FpPosition3 aim) => new(true, point, aim);

    public static ShotEffectAttachment Resolve(WorldState world, WorldSteering navigation, ShotId id, float fraction)
    {
        ShotState? shot = world.ShotById(id.Value);
        if (shot == null) return Gone;
        FpPosition3 current = shot.ViewPosition, previous = shot.PreviousViewPosition ?? current;
        float t = Math.Clamp(fraction, 0, 1);
        var point = new FpPosition3(new Fp32(previous.X.Raw + (int)((current.X.Raw - previous.X.Raw) * t)),
            new Fp32(previous.Y.Raw + (int)((current.Y.Raw - previous.Y.Raw) * t)),
            new Fp32(previous.Z.Raw + (int)((current.Z.Raw - previous.Z.Raw) * t)));
        TilePos tile = new(point.X.Raw >> 12, point.Y.Raw >> 12);
        bool accepted = world.Contains(tile) && !NavBytes.IsSolid(navigation.NavByte(tile.X, tile.Y));
        return new(true, accepted ? point : null,
            new FpPosition3(new Fp32(shot.TargetX), new Fp32(shot.TargetY), shot.ViewHeight));
    }
}

public sealed partial class RoomConstructionParticles
{
    private void FollowShotAttachments()
    {
        if (_shotAttachment == null) return;
        foreach (Emitter emitter in _emitters)
        {
            ViewEffectRequest request = emitter.Request;
            if (request.AttachedShotId == ShotId.None) continue;
            ShotEffectAttachment found = _shotAttachment(request.AttachedShotId);
            if (!found.ShotExists)
            {
                emitter.Health = 0;
                emitter.Request = request with { AttachedShotId = ShotId.None };
                if (request.RemoveWithShot)
                {
                    for (int i = _particles.Count - 1; i >= 0; i--)
                    {
                        ConstructionParticle particle = _particles[i];
                        if (!_parents.TryGetValue(particle, out Emitter? parent) || parent != emitter) continue;
                        _parents.Remove(particle);
                        _particles.RemoveAt(i);
                        emitter.ChildCount--;
                    }
                }
                continue;
            }
            emitter.Request = request with
            {
                Position = found.Point ?? request.Position,
                SecondPoint = found.Aim ?? request.SecondPoint,
            };
        }
    }
}
