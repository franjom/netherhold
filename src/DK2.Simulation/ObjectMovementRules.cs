// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Steering;

namespace DK2.Simulation;

public readonly record struct ObjectProcessId(byte Value);

/// <summary>The common object MoveTo block, shared by chickens and rats.</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/hatchery-roaming.md")]
public static class ObjectMovementRules
{
    public static readonly ObjectProcessId MovingProcess = new(9);
    public const int TurnRate = 0x100;
    public static void SetProcess(ObjectState thing, ObjectProcessId process)
    {
        thing.ProcessState = process.Value;
        thing.ProcessAgeTicks = 0;
    }

    public static bool StartMove(SimulationContext context, ObjectState thing, ObjectMovePoint destination,
        ObjectProcessId resume, Fp32 extraSpeed, bool interruptible)
    {
        if (!context.World.Contains(new TilePos(destination.X.Raw >> 12, destination.Y.Raw >> 12))) return false;
        SetProcess(thing, MovingProcess);
        thing.MoveResumeState = resume.Value;
        thing.MoveDestinationX = destination.X.Raw;
        thing.MoveDestinationY = destination.Y.Raw;
        thing.MoveFlags = (thing.MoveFlags & ~ObjectMoveFlags.Interruptible)
            | (interruptible ? ObjectMoveFlags.Interruptible : ObjectMoveFlags.None);
        ObjectDef definition = context.Database.ObjectById(thing.ObjectTypeId)!;
        int speed = unchecked((short)(RuntimeSpeed(definition) + extraSpeed.Raw));
        if (speed == 0) return false;
        int dx = unchecked(destination.X.Raw - thing.PosX), dy = unchecked(destination.Y.Raw - thing.PosY);
        int heading = Bearing.Of(dx, dy);
        thing.MoveVelocityX = unchecked(TrapRules.Sine(heading) * speed) >> 16;
        thing.MoveVelocityY = unchecked(-TrapRules.Cosine(heading) * speed) >> 16;
        uint distanceSquare = unchecked((uint)((int)(((long)dx * dx) >> 12) + (int)(((long)dy * dy) >> 12)));
        uint speedSquare = unchecked((uint)((long)speed * speed >> 12));
        if (speedSquare == 0) throw new DivideByZeroException("Object movement speed has zero fixed-point square.");
        int count = Math.Max(1, IntegerMath.SquareRoot(distanceSquare / speedSquare) - 1)
            + (distanceSquare % speedSquare != 0 ? 1 : 0);
        thing.MoveTicksRemaining = unchecked((ushort)count);
        thing.MoveFlags |= ObjectMoveFlags.Turning;
        return true;
    }

    public static short RuntimeSpeed(ObjectDef definition)
    {
        short speed = unchecked((short)(definition.Speed.Raw / SimulationTiming.OriginalTicksPerGameSecond));
        return definition.Speed.Raw != 0 && speed == 0 ? (short)1 : speed;
    }

    public static void StepMove(SimulationContext context, ObjectState thing)
    {
        if ((thing.MoveFlags & ObjectMoveFlags.Turning) != 0)
        {
            int heading = thing.Heading;
            int target = Bearing.Of(thing.MoveDestinationX - thing.PosX, thing.MoveDestinationY - thing.PosY);
            int remaining = Bearing.TurnToward(ref heading, target, TurnRate);
            thing.Heading = heading;
            if (remaining != 0) return;
            thing.MoveFlags &= ~ObjectMoveFlags.Turning;
        }
        thing.MoveTicksRemaining = unchecked((ushort)(thing.MoveTicksRemaining - 1));
        if (thing.MoveTicksRemaining == 0)
        {
            SetPosition(context.World, thing, new ObjectMovePoint(new Fp32(thing.MoveDestinationX), new Fp32(thing.MoveDestinationY)));
            SetProcess(thing, new ObjectProcessId(thing.MoveResumeState));
        }
        else
        {
            SetPosition(context.World, thing, new ObjectMovePoint(new Fp32(thing.PosX + thing.MoveVelocityX), new Fp32(thing.PosY + thing.MoveVelocityY)));
            if (thing.ObjectTypeId == HatcheryRules.ChickenObjectId) ChickenMovementRules.TryAvoidCreature(context, thing);
        }
    }

    private static void SetPosition(WorldState world, ObjectState thing, ObjectMovePoint position)
    {
        thing.PosX = position.X.Raw;
        thing.PosY = position.Y.Raw;
        thing.TileIndex = (position.Y.Raw >> 12) * world.Width + (position.X.Raw >> 12);
    }

}
