// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

public readonly record struct TileHighlightSlot(ActionPointId ActionPoint, short RemainingTicks, TilePos Min, TilePos Max);

/// <summary>The world's four action-16 rectangles, independent of dig orders (tag-highlight.md).</summary>
public sealed class TileHighlightState
{
    public const int Capacity = 4;
    private readonly TileHighlightSlot[] _slots = new TileHighlightSlot[Capacity];
    public ReadOnlySpan<TileHighlightSlot> Slots => _slots;
    public bool HasActive
    {
        get
        {
            foreach (TileHighlightSlot slot in _slots)
                if (slot.ActionPoint != ActionPointId.None) return true;
            return false;
        }
    }

    public void Set(WorldState world, ActionPointId actionPoint, short durationTicks)
    {
        for (int i = 0; i < Capacity; i++)
        {
            TileHighlightSlot slot = _slots[i];
            if (durationTicks == 0 ? slot.ActionPoint != actionPoint
                : slot.ActionPoint != ActionPointId.None && slot.ActionPoint != actionPoint) continue;
            var point = durationTicks != 0 ? world.ActionPoints.FirstOrDefault(p => p.Id == actionPoint.Value) : null;
            _slots[i] = new(durationTicks == 0 ? ActionPointId.None : actionPoint, durationTicks,
                point is null ? slot.Min : new TilePos(point.StartX, point.StartY),
                point is null ? slot.Max : new TilePos(point.EndX, point.EndY));
            return;
        }
    }

    public void AdvanceTick()
    {
        for (int i = 0; i < Capacity; i++)
        {
            TileHighlightSlot slot = _slots[i];
            if (slot.RemainingTicks <= 0) continue;
            short remaining = (short)(slot.RemainingTicks - 1);
            _slots[i] = slot with { RemainingTicks = remaining,
                ActionPoint = remaining == 0 ? ActionPointId.None : slot.ActionPoint };
        }
    }

    public bool Contains(TilePos tile)
    {
        foreach (TileHighlightSlot slot in _slots)
            if (slot.ActionPoint != ActionPointId.None && tile.X >= slot.Min.X && tile.X <= slot.Max.X
                && tile.Y >= slot.Min.Y && tile.Y <= slot.Max.Y) return true;
        return false;
    }

    internal void HashInto(ref Hash64 hash)
    {
        foreach (TileHighlightSlot slot in _slots)
        {
            hash.Add(slot.ActionPoint.Value); hash.Add(slot.RemainingTicks);
            hash.Add(slot.Min.X); hash.Add(slot.Min.Y); hash.Add(slot.Max.X); hash.Add(slot.Max.Y);
        }
    }

    internal void Write(BinaryWriter writer)
    {
        foreach (TileHighlightSlot slot in _slots)
        {
            writer.Write(slot.ActionPoint.Value); writer.Write(slot.RemainingTicks);
            writer.Write(slot.Min.X); writer.Write(slot.Min.Y); writer.Write(slot.Max.X); writer.Write(slot.Max.Y);
        }
    }

    internal void Read(BinaryReader reader)
    {
        for (int i = 0; i < Capacity; i++)
            _slots[i] = new(new ActionPointId(reader.ReadByte()), reader.ReadInt16(),
                new TilePos(reader.ReadInt32(), reader.ReadInt32()), new TilePos(reader.ReadInt32(), reader.ReadInt32()));
    }
}
