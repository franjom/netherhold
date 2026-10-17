// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DK2.Presentation;
using UnityEngine;

namespace DK2.Unity
{
    public sealed class Level8MotionProbe : MonoBehaviour
    {
        private DungeonRunner _runner;
        private DungeonScene _scene;
        private Dictionary<int, GameObject> _creatures;
        private Dictionary<int, CreatureView> _poses;
        private FieldInfo _renderSeconds;
        private double _start;
        private readonly List<string> _rows = new List<string>();

        private void LateUpdate()
        {
            try
            {
                if (_runner == null)
                {
                    _runner = FindAnyObjectByType<DungeonRunner>();
                    _scene = (DungeonScene)typeof(DungeonRunner).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_runner);
                    _creatures = (Dictionary<int, GameObject>)typeof(DungeonScene).GetField("_creatures", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_scene);
                    _poses = (Dictionary<int, CreatureView>)typeof(DungeonScene).GetField("_creaturePoses", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_scene);
                    _renderSeconds = typeof(DungeonScene).GetField("_renderSeconds", BindingFlags.NonPublic | BindingFlags.Instance);
                    _start = Time.realtimeSinceStartupAsDouble;
                    Application.runInBackground = true; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 144;
                    _rows.Add("seconds,frame,render_tick,id,x,z,from_x,from_y,to_x,to_y,viewport_x,viewport_y,clip");
                }
                double now = Time.realtimeSinceStartupAsDouble - _start;
                double tick = (double)_renderSeconds.GetValue(_scene) * 4;
                for (int id = 26; id <= 32; id++)
                    if (_creatures.TryGetValue(id, out GameObject go) && _poses.TryGetValue(id, out CreatureView pose))
                    {
                        Vector3 p = go.transform.position, screen = Camera.main.WorldToViewportPoint(p);
                        _rows.Add(FormattableString.Invariant($"{now:R},{Time.frameCount},{tick:R},{id},{p.x:R},{p.z:R},{pose.PreviousPosX},{pose.PreviousPosY},{pose.PosX},{pose.PosY},{screen.x:R},{screen.y:R},{pose.Animation.Key}"));
                    }
                if (now < 12) return;
                string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-windowProbeOutput");
                File.WriteAllLines(Path.Combine(args[at + 1], "motion.csv"), _rows);
                Application.Quit(); enabled = false;
            }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); enabled = false; }
        }
    }
}
