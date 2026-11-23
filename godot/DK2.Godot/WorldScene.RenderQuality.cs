// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The 3D view's anti-aliasing and render scale (modern-rendering audit item 17), from the settings file's
/// <see cref="GameSettings.RenderMsaa"/>, <see cref="GameSettings.RenderSmaa"/>, <see cref="GameSettings.RenderScale"/> and
/// <see cref="GameSettings.RenderScaleFsr"/>, all off by default. MSAA and SMAA apply to the main view and the full map's
/// preview, the scale to the main view only; the torch-shadow views never take them (distance maps must not blend).
/// A value equal to the viewport's is not written, so the defaults leave the project's viewports untouched.</summary>
public partial class WorldScene
{
    private Viewport.Msaa _msaa = Viewport.Msaa.Disabled;
    private Viewport.ScreenSpaceAAEnum _screenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
    private float _renderScale = 1f;
    private Viewport.Scaling3DModeEnum _scalingMode = Viewport.Scaling3DModeEnum.Bilinear;
    private SubViewport? _previewRenderViewport;

    /// <summary>The full map's preview viewport (<see cref="ClassicFullMap"/>); it takes the view's MSAA and SMAA.</summary>
    public SubViewport? PreviewRenderViewport
    {
        get => _previewRenderViewport;
        set
        {
            _previewRenderViewport = value;
            if (value != null) ApplyAntiAliasing(value);
        }
    }

    /// <summary>Applies the settings' anti-aliasing and render scale to the main view and the preview.</summary>
    public void SetRenderQuality(GameSettings settings)
    {
        settings = settings.Clamped();
        _msaa = settings.RenderMsaa switch
        {
            8 => Viewport.Msaa.Msaa8X,
            4 => Viewport.Msaa.Msaa4X,
            2 => Viewport.Msaa.Msaa2X,
            _ => Viewport.Msaa.Disabled,
        };
        _screenSpaceAA = settings.RenderSmaa ? Viewport.ScreenSpaceAAEnum.Smaa : Viewport.ScreenSpaceAAEnum.Disabled;
        // At full scale FSR 1 would still sharpen the image, so it takes effect only below 100%.
        _renderScale = settings.RenderScale / 100f;
        _scalingMode = settings.RenderScale < 100 && settings.RenderScaleFsr
            ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        Viewport view = GetViewport();
        ApplyAntiAliasing(view);
        if (view.Scaling3DMode != _scalingMode) view.Scaling3DMode = _scalingMode;
        if (view.Scaling3DScale != _renderScale) view.Scaling3DScale = _renderScale;
        if (_previewRenderViewport != null) ApplyAntiAliasing(_previewRenderViewport);
    }

    private void ApplyAntiAliasing(Viewport viewport)
    {
        if (viewport.Msaa3D != _msaa) viewport.Msaa3D = _msaa;
        if (viewport.ScreenSpaceAA != _screenSpaceAA) viewport.ScreenSpaceAA = _screenSpaceAA;
    }
}
