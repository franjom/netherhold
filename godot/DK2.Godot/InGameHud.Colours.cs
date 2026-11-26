// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;
namespace DK2.GodotAdapter;

public partial class InGameHud
{
    private Image? _screenColourImage;
    private ImageTexture? _screenColourTexture;
    private double _screenGamma = double.NaN;
    private readonly ushort[] _uploadedScreenColours = new ushort[768];
    private void OpenScreenColours()
    {
        _screenColourImage = Image.CreateEmpty(256, 1, false, Image.Format.Rgbaf);
        _screenColourTexture = ImageTexture.CreateFromImage(_screenColourImage);
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://ScreenColours.gdshader") };
        material.SetShaderParameter("ramps", _screenColourTexture);
        var rect = new ColorRect { Name = "ScreenColours", MouseFilter = Control.MouseFilterEnum.Ignore, Material = material };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Name = "ScreenColoursLayer", Layer = 110 };
        layer.AddChild(rect);
        AddChild(layer);
        UpdateScreenColours();
    }
    private void UpdateScreenColours()
    {
        if (_possession == null || _screenColourImage == null || _screenColourTexture == null)
        {
            return;
        }

        double gamma = DisplaySettings.RampGamma(_settings.Gamma);
        if (gamma != _screenGamma)
        {
            _screenGamma = gamma;
            _possession.Colours.Change.SetGamma(gamma);
        }
        // The final ramp includes gamma for the scene and every HUD layer.
        _world?.SetBrightness(1f);
        IReadOnlyList<ushort> table = _possession.Colours.Output;
        bool changed = false;
        for (int i = 0; i < _uploadedScreenColours.Length; i++)
        {
            if (_uploadedScreenColours[i] != table[i])
            {
                changed = true;
            }

            _uploadedScreenColours[i] = table[i];
        }
        if (!changed)
        {
            return;
        }

        for (int i = 0; i < 256; i++)
        {
            _screenColourImage.SetPixel(i, 0, new Color(table[i] / 65535f, table[256 + i] / 65535f, table[512 + i] / 65535f));
        }

        _screenColourTexture.Update(_screenColourImage);
    }
    private void CloseScreenColours()
    {
        _screenColourImage?.Dispose();
        _screenColourTexture?.Dispose();
        _screenColourImage = null;
        _screenColourTexture = null;
    }
}
