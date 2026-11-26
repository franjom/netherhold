// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.


using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

public partial class WorldScene
{
    private readonly FlowerDollarClock _flowerDollarClock = new();
    private readonly List<ModelSlot> _dollarFlowers = new();
    private static readonly RenderEuler DollarTurn = new(FlowerDollarClock.YawDegrees, 0, 0);
    private ModelMesh? _dollarTemplate;
    private VertexClip? _dollarAnimation;
    private bool _lookedForDollar;
    private int _shownDollarFlowers;

    private void LoadDollarFlower()
    {
        _lookedForDollar = true;
        if (_art == null || !_art.TryResolve(StatusFlowerArt.Dollar, out ResolvedAsset asset)
            || !_art.TryLoadModel(asset, out KmfModel model) || model.Animation?.IsComplete != true
            || Model(asset, lastFrame: false) is not ModelMesh template)
        {
            return;
        }

        _dollarTemplate = template;
        _dollarAnimation = VertexAnimation(new RenderAnimation(model), template);
    }

    private bool DrawDollarFlower(int index, NVector3 anchor, double seconds)
    {
        if (!_lookedForDollar)
        {
            long loading = GC.GetAllocatedBytesForCurrentThread();
            LoadDollarFlower();
            _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - loading;
        }
        if (_dollarTemplate == null || _dollarAnimation == null)
        {
            return false;
        }

        if (index == _dollarFlowers.Count)
        {
            long loading = GC.GetAllocatedBytesForCurrentThread();
            var created = new ModelSlot(this, _dollarTemplate, "DollarFlower" + index, SlotKind.Effect);
            created.Show(_dollarTemplate, _dollarTemplate.Mesh, _dollarAnimation);
            _dollarFlowers.Add(created);
            _itemGrowthBytes += GC.GetAllocatedBytesForCurrentThread() - loading;
        }

        ModelSlot slot = _dollarFlowers[index];
        slot.Place(anchor, DollarTurn, Vector3.One);
        float phase = _flowerDollarClock.Phase(FlowerDollarClock.Fraction(seconds));
        slot.Node.SetInstanceShaderParameter(AnimationPhaseParameter, phase);
        slot.Node.Show();
        _flowerDollarClock.Advance();
        return true;
    }

    internal void HideUnusedDollarFlowers(int count)
    {
        for (int i = count; i < _shownDollarFlowers; i++)
        {
            _dollarFlowers[i].Node.Hide();
        }

        _shownDollarFlowers = count;
    }

    private void FreeDollarFlowers()
    {
        foreach (ModelSlot slot in _dollarFlowers)
        {
            slot.Free();
        }

        _dollarFlowers.Clear();
        _shownDollarFlowers = 0;
        _flowerDollarClock.Reset();
    }
}
