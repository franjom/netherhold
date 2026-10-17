// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DK2.Unity.Editor
{
    public static class ClassicRotationProfileReport
    {
        [Serializable] private sealed class Allocation
        {
            public string Phase, Stack;
            public long Bytes, Count;
        }
        [Serializable] private sealed class Report
        {
            public bool Passed;
            public int Frames, AllocationSamples, UnresolvedStacks;
            public long CalibrationBytes;
            public Allocation[] Allocations;
            public PhaseFrames[] PhaseCounts;
            public string Error;
        }
        [Serializable] private sealed class PhaseFrames
        {
            public string Phase;
            public int Frames;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".classic-window-frame-probe"))
                throw new InvalidOperationException("Requires the marked isolated batch project.");
            try
            {
                string path = Argument("-rotationProfile");
                ProfilerDriver.ClearAllFrames();
                if (!ProfilerDriver.LoadProfile(path, false)) throw new InvalidOperationException("Profile load failed.");
                EditorApplication.delayCall += Analyze;
            }
            catch (Exception error) { Write(new Report { Error = error.ToString() }); }
        }

        private static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, key);
            if (at < 0 || at + 1 >= args.Length) throw new ArgumentException(key + " required.");
            return args[at + 1];
        }

        private static void Analyze()
        {
            var report = new Report();
            try
            {
                var totals = new Dictionary<string, Allocation>();
                var phaseCounts = new Dictionary<string, int>();
                var pointers = new List<ulong>();
                var parents = new List<(int End, string Name)>();
                for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
                {
                    string phase = "Unlabelled";
                    using (RawFrameDataView main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                    {
                        if (!main.valid) continue;
                        for (int i = 0; i < main.sampleCount; i++)
                        {
                            string name = main.GetSampleName(i);
                            if (name != null && name.StartsWith("DK2.Probe.") && !name.Contains("AllocationCalibration"))
                            { phase = name.Substring("DK2.Probe.".Length); break; }
                        }
                    }
                    report.Frames++;
                    phaseCounts.TryGetValue(phase, out int count);
                    phaseCounts[phase] = count + 1;
                    for (int thread = 0; thread < 128; thread++)
                    {
                        using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                        {
                            if (!data.valid) break;
                            parents.Clear();
                            for (int i = 0; i < data.sampleCount; i++)
                            {
                                while (parents.Count > 0 && parents[parents.Count - 1].End < i) parents.RemoveAt(parents.Count - 1);
                                string name = data.GetSampleName(i);
                                int children = data.GetSampleChildrenCountRecursive(i);
                                if (name == "GC.Alloc" && data.GetSampleMetadataCount(i) > 0)
                                {
                                    long bytes = data.GetSampleMetadataAsLong(i, 0);
                                    if (parents.Any(p => p.Name == "DK2.Probe.AllocationCalibration")) report.CalibrationBytes += bytes;
                                    else
                                    {
                                        pointers.Clear(); data.GetSampleCallstack(i, pointers);
                                        string stack = string.Join("\n", pointers.Select(p => data.ResolveMethodInfo(p).methodName));
                                        if (string.IsNullOrWhiteSpace(stack))
                                        { report.UnresolvedStacks++; stack = string.Join("/", parents.Select(p => p.Name)); }
                                        string key = phase + "\n" + stack;
                                        if (!totals.TryGetValue(key, out Allocation allocation))
                                            totals.Add(key, allocation = new Allocation { Phase = phase, Stack = stack });
                                        allocation.Bytes += bytes; allocation.Count++; report.AllocationSamples++;
                                    }
                                }
                                if (children > 0) parents.Add((i + children, name));
                            }
                        }
                    }
                }
                report.Allocations = totals.Values.OrderByDescending(a => a.Bytes).ToArray();
                report.PhaseCounts = phaseCounts.Select(p => new PhaseFrames { Phase = p.Key, Frames = p.Value }).ToArray();
                foreach (string phase in new[] { "IdleFirst", "RotateFirst", "IdleMiddle", "RotateWarm", "IdleLast" })
                    if (!phaseCounts.TryGetValue(phase, out int count) || count != 40)
                        throw new InvalidOperationException("Expected 40 labelled frames for " + phase);
                if (report.CalibrationBytes < 65536 || report.AllocationSamples == 0)
                    throw new InvalidOperationException("Allocation byte calibration or sample collection failed.");
                report.Passed = true;
            }
            catch (Exception error) { report.Error = error.ToString(); }
            Write(report);
        }

        private static void Write(Report report)
        {
            string output = Argument("-rotationReport");
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("Allocation report: " + output + "; passed=" + report.Passed);
            EditorApplication.Exit(report.Passed ? 0 : 1);
        }
    }
}
