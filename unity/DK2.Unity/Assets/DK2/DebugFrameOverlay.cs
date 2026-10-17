// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using UnityEngine;

namespace DK2.Unity
{
    internal sealed class DebugFrameOverlay
    {
        private double _elapsedSeconds;
        private double _slowestSeconds;
        private int _frames;
        private bool _enabled;
        private string _label = "FPS: measuring...";
        private GUIStyle _style;

        // Sample once in Update, never in OnGUI, which can run repeatedly per frame.
        public void Sample(bool showDiagnostics)
        {
            _enabled = showDiagnostics && (Application.isEditor || Debug.isDebugBuild);
            if (!_enabled)
            {
                _elapsedSeconds = _slowestSeconds = 0;
                _frames = 0;
                _label = "FPS: measuring...";
                return;
            }

            double seconds = Time.unscaledDeltaTime;
            if (seconds <= 0) return;
            _elapsedSeconds += seconds;
            _slowestSeconds = System.Math.Max(_slowestSeconds, seconds);
            _frames++;
            if (_elapsedSeconds < 0.5) return;

            _label = $"{_frames / _elapsedSeconds:F0} FPS   {_elapsedSeconds * 1000 / _frames:F2} ms avg\n"
                + $"{_slowestSeconds * 1000:F2} ms slowest / {_elapsedSeconds:F1} s";
            _elapsedSeconds = _slowestSeconds = 0;
            _frames = 0;
        }

        public void Draw()
        {
            if (!_enabled || Event.current.type != EventType.Repaint) return;
            _style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                normal = { textColor = Color.white },
            };
            GUI.Box(new Rect(Mathf.Max(8, Screen.width - 248), 8, 240, 48), _label, _style);
        }
    }
}
