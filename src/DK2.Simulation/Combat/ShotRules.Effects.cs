// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Combat;

public static partial class ShotRules
{
    /// <summary>The constructor's trail, delivered after launch in the same caller so its initial
    /// carried velocity is available. It keeps the constructor heading when launch changes the body.</summary>
    public static void RequestTrail(SimulationContext context, ShotState shot, Dk2Angle initialHeading,
        Fp32 height = default)
    {
        shot.ViewHeight = height;
        shot.PreviousViewPosition = shot.ViewPosition;
        if (!shot.HasLaunchHeading) shot.ViewHeading = initialHeading;
        if (!context.Database.Shots.TryGetValue(shot.ShotTypeId, out ShotDef? definition)) return;
        RequestConstructionSounds(context, shot, definition);
        context.RequestEffect(SystemName, new ViewEffectRequest(new EffectId(definition.GeneralEffectId),
            shot.OwnerPlayerId, shot.ViewPosition, initialHeading)
        {
            AttachedShotId = new ShotId(shot.Id),
            Visibility = EffectVisibility.Ungated,
            SecondPoint = AimPoint(shot),
            CarriedVelocity = shot.HasLaunchHeading ? LaunchEffectVelocity(shot, SessionTurnRate) : null,
            RemoveWithShot = (definition.Flags & 0x40) != 0,
        });
    }

    /// <summary>The constructor's two sounds (<c>Shot_InitialiseFromDefinition</c>, <c>0x004AAF1E</c> and <c>0x004AAF40</c>): <c>0xEF</c> with
    /// the shot's first handle and <c>0xF2</c> with its second, in the shot's own category (definition <c>+0x72</c>) at the shot's
    /// position. Fourteen of the fifteen callers of <c>Shot_Create</c> pass a nonzero last argument, which gates the <c>0xEF</c>; the fifteenth,
    /// the saved-game loader (<c>0x004B6CB0</c>), passes 0 and asks only <c>0xF2</c>, which a simulation never loads through here. The category
    /// is empty for most keeper-spell and trap shots, which then ask nothing. Asked with the trail, each caller's construction hook; the
    /// height is the observation's, not a stored one.</summary>
    internal static void RequestConstructionSounds(SimulationContext context, ShotState shot, ShotDef definition, FpPosition3? at = null)
    {
        RequestShotSound(context, shot, definition, ThingSoundEvent.SpellCast, at: at);
        RequestShotSound(context, shot, definition, ThingSoundEvent.ShotMadeSecond, SoundSourceKind.ShotSecond, at: at);
    }

    /// <summary>A request in the shot's own category (<c>+0x72</c>, <see cref="ShotDef.SoundCategory"/>) with one of its two handles
    /// (<c>+0x2E</c>, <paramref name="handle"/> <see cref="SoundSourceKind.Shot"/>, or <c>+0x32</c>, <see cref="SoundSourceKind.ShotSecond"/>)
    /// at the shot's position; nothing for a shot whose category is empty, which DK2's lookup answers with no group.
    /// <paramref name="viewerOnlyFor"/> names the keeper whose screen alone hears it (the Sight of Evil's).</summary>
    internal static void RequestShotSound(SimulationContext context, ShotState shot, ShotDef definition, ThingSoundEvent sound,
        SoundSourceKind handle = SoundSourceKind.Shot, PlayerId? viewerOnlyFor = null, FpPosition3? at = null)
    {
        if (definition.SoundCategory.Length == 0 || ReferenceEquals(context.Events, NullEventSink.Instance)) return;
        ThingSoundRules.Request(context, SystemName, new SoundSourceId(handle, shot.Id), definition.SoundCategory, sound,
            at ?? shot.ViewPosition, viewerOnlyFor: viewerOnlyFor);
    }

    // [[world]+4]+8, initialized by 0x00526020, independent of level variable19.
    private const int SessionTurnRate = 4;

    private static FpPosition3 LaunchEffectVelocity(ShotState shot, int ticksPerSecond) =>
        new(new Fp32(unchecked(shot.VelX * 16 * ticksPerSecond) / 20),
            new Fp32(unchecked(shot.VelY * 16 * ticksPerSecond) / 20), default);

    internal static FpPosition3 AimPoint(ShotState shot) =>
        new(new Fp32(shot.TargetX), new Fp32(shot.TargetY), shot.ViewHeight);

    internal static void RequestTimedEffect(SimulationContext context, ShotState shot, ShotDef definition,
        FpPosition3 point)
    {
        if (definition.TimedEffectId != 0 && shot.Age % definition.TimedDelay == 0)
            RequestShotEffect(context, shot, new EffectId(definition.TimedEffectId), point, aimed: true);
    }

    internal static void RequestEffectsAfterProcessKill(SimulationContext context, ShotState shot,
        ShotDef definition, FpPosition3 start)
    {
        // Process34 returns1 after killing. Project its native tail without altering the legacy
        // early gameplay removal; vertical flight retains the existing planar approximation.
        bool stopped = shot.VelX == 0 && shot.VelY == 0;
        FpPosition3 point = shot.ViewPosition;
        int halfX = shot.VelX / 2, halfY = shot.VelY / 2;
        for (int step = 0; !stopped && step < 2; step++)
        {
            int x = point.X.Raw + (step == 0 ? halfX : shot.VelX - halfX);
            int y = point.Y.Raw + (step == 0 ? halfY : shot.VelY - halfY);
            if (!Passable(context.World, context.Navigation, x, y)) { stopped = true; point = shot.ViewPosition; }
            else point = new FpPosition3(new Fp32(x), new Fp32(y), point.Z);
        }
        if (stopped) RequestShotEffect(context, shot, new EffectId(definition.HitSolidEffectId), start);
        if (stopped && (definition.Flags & DiesOnSolid) != 0) return;
        RequestShotEffect(context, shot, new EffectId(definition.DeathEffectId), point);
        RequestTimedEffect(context, shot, definition, point);
    }

    internal static void RequestShotEffect(SimulationContext context, ShotState shot, EffectId effect,
        FpPosition3 position, CreatureId attachedCreatureId = default, bool aimed = false, bool carryVelocity = false) =>
        context.RequestEffect(SystemName, new ViewEffectRequest(effect, shot.OwnerPlayerId, position, shot.ViewHeading,
            AttachedCreatureId: attachedCreatureId.Value)
        {
            SecondPoint = aimed ? AimPoint(shot) : position,
            CarriedVelocity = carryVelocity ? LaunchEffectVelocity(shot,
                context.Database.Level?.TicksPerSecond ?? SimulationTiming.OriginalTicksPerGameSecond) : null,
        });
}
