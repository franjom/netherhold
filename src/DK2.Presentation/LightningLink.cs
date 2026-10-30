// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Presentation;

public readonly record struct LightningTarget(bool Exists, bool IsRoom, FpPosition3 BasePoint);
public readonly record struct LightningCaster(bool HasLink, FpPosition3? Point);
public readonly record struct LightningEndpoints(FpPosition3 Start, FpPosition3 End, int BoltCount);

/// <summary>The procedural art's render link, independent of the simulation's target word.</summary>
public sealed class LightningLink(ProceduralArtKind kind)
{
    private bool _linked = true;
    private bool _casterLinkCleared;
    public LightningEndpoints? Resolve(FpPosition3 shotPoint, LightningTarget target, LightningCaster casterLink = default)
    {
        if (kind == ProceduralArtKind.FixedLightning)
            return new(shotPoint, shotPoint with { Z = new(Fp32.One) }, 1);
        if (kind is not (ProceduralArtKind.Lightning or ProceduralArtKind.TripleLightning) || !_linked) return null;
        if (!target.Exists || target.IsRoom) { _linked = false; return null; }
        if (!_casterLinkCleared)
        {
            if (casterLink.Point is FpPosition3 caster) shotPoint = shotPoint with { X = caster.X, Y = caster.Y };
            else if (casterLink.HasLink) _casterLinkCleared = true;
        }
        return new(shotPoint, target.BasePoint, kind == ProceduralArtKind.TripleLightning ? 3 : 1);
    }
}

public sealed class CreatureLightningVisual
{
    private GameTick? _lastChooserTick;
    private GameTick _expiresAt;
    private bool _crackle;
    public void Ignite(GameTick tick) => _expiresAt = new(tick.Value + 20);
    public bool Choose(GameTick tick, bool animationInstalled)
    {
        if (_lastChooserTick != tick)
        {
            _lastChooserTick = tick;
            _crackle = false;
        }
        if (tick.Value < _expiresAt.Value && animationInstalled) _crackle = true;
        return _crackle;
    }
}
