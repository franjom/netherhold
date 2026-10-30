// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation;

public sealed partial class RoomConstructionParticles
{
    private int _contactDepth;

    private bool CrossesWall(FpPosition3 from, FpPosition3 to)
    {
        if (_solidTile == null) return false;
        // Approximation to native grid-boundary reflection (004472F0): test the complete segment
        // at quarter-tile intervals. Blocking contacts keep the previous point (ADR-0597, Q690).
        int steps = Math.Max(1, Math.Max(Math.Abs(to.X.Raw - from.X.Raw), Math.Abs(to.Y.Raw - from.Y.Raw)) / 1024 + 1);
        for (int i = 1; i <= steps; i++)
        {
            int x = from.X.Raw + (int)((long)(to.X.Raw - from.X.Raw) * i / steps);
            int y = from.Y.Raw + (int)((long)(to.Y.Raw - from.Y.Raw) * i / steps);
            if (_solidTile(new(x >> 12, y >> 12))) return true;
        }
        return false;
    }

    private bool ApplyWallContact(ConstructionParticle particle)
    {
        FpPosition3 from = new(new(particle.PreviousXRaw), new(particle.PreviousYRaw), new(particle.PreviousHeightRaw));
        FpPosition3 to = new(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw));
        if (!CrossesWall(from, to)) return true;
        SpawnContactElement(particle, floorContact: false);
        if ((particle.Definition.Flags & 1) != 0)
        {
            particle.XRaw = from.X.Raw;
            particle.YRaw = from.Y.Raw;
            particle.HeightRaw = from.Z.Raw;
            particle.VelocityX = particle.VelocityY = particle.VelocityZ = 0;
            return false;
        }
        else
        {
            particle.XRaw = from.X.Raw;
            particle.YRaw = from.Y.Raw;
            particle.VelocityX = -particle.VelocityX;
            particle.VelocityY = -particle.VelocityY;
        }
        return true;
    }

    private FpPosition3 ApplyEmitterContact(Emitter emitter, FpPosition3 from, FpPosition3 to)
    {
        EffectDef effect = emitter.Definition;
        if (CrossesWall(from, to))
        {
            SpawnFreeEffect(effect.HitSolidEffectId, emitter.Request, from, emitter.Request.Heading);
            if ((effect.Flags & 1) != 0)
            {
                emitter.VelocityX = emitter.VelocityY = emitter.VelocityZ = 0;
                return from;
            }
            to = to with { X = from.X, Y = from.Y };
            emitter.VelocityX = -emitter.VelocityX;
            emitter.VelocityY = -emitter.VelocityY;
        }
        int floor = _floorHeight(to).Raw;
        if (to.Z.Raw >= floor) return to;
        TerrainFlags terrain = _terrainFlags?.Invoke(new(to.X.Raw >> 12, to.Y.Raw >> 12)) ?? 0;
        // Native chooses runtime +81 on bit100 (lava), +83 on bit80 (water): the file's
        // HitWater/HitLava names are reversed in this branch. Preserve that selection.
        ushort id = (terrain & TerrainFlags.Lava) != 0 ? effect.HitWaterEffectId
            : (terrain & TerrainFlags.Water) != 0 ? effect.HitLavaEffectId : effect.HitSolidEffectId;
        SpawnFreeEffect(id, emitter.Request, to, emitter.Request.Heading);
        if ((effect.Flags & 1) != 0) emitter.Health = 0;
        int elasticity = unchecked((ushort)effect.Elasticity);
        emitter.VelocityX = (int)((long)emitter.VelocityX * elasticity / 65536);
        emitter.VelocityY = (int)((long)emitter.VelocityY * elasticity / 65536);
        int bounce = -(int)((long)emitter.VelocityZ * elasticity / 65536);
        emitter.VelocityZ = bounce < 8192 ? (int)((long)bounce * bounce >> 13)
            : bounce < 131072 ? (int)((long)bounce * 247 >> 8) : bounce;
        return to with { Z = new(floor + (int)((long)(floor - to.Z.Raw) * elasticity / 65536)) };
    }

    private void SpawnContactElement(ConstructionParticle particle, bool floorContact)
    {
        if (_contactDepth >= LongestChain || !_parents.TryGetValue(particle, out Emitter? parent)) return;
        EffectElementDef definition = particle.Definition;
        ushort id = floorContact && definition.HitWaterElementId != 0 ? definition.HitWaterElementId
            : floorContact && definition.HitLavaElementId != 0 ? definition.HitLavaElementId : definition.HitSolidElementId;
        if (!_database.EffectElements.TryGetValue(id, out EffectElementDef? child)) return;
        _contactDepth++;
        try
        {
            FpPosition3 at = floorContact
                ? new(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw))
                : new(new(particle.PreviousXRaw), new(particle.PreviousYRaw),
                    _floorHeight(new(new(particle.XRaw), new(particle.YRaw), new(particle.HeightRaw))));
            EmitElement(child, parent.Definition, parent.Request with
            {
                Position = at,
                Heading = new((ushort)particle.HeadingRaw),
            }, null, parent, particle);
        }
        finally { _contactDepth--; }
    }
}
