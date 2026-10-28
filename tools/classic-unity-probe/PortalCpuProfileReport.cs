// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DK2.Unity.Editor
{
    public static class PortalCpuProfileReport
    {
        private static string _profile;
        private static bool _resourcesOnly;
        public static void RunResources() { _resourcesOnly = true; Run(); }
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".classic-window-frame-probe"))
                throw new InvalidOperationException("Requires the marked isolated batch project.");
            string[] args = Environment.GetCommandLineArgs();
            _profile = args[Array.IndexOf(args, "-arrivalProfile") + 1];
            try
            {
                ProfilerDriver.ClearAllFrames();
                if (!ProfilerDriver.LoadProfile(_profile, false)) throw new InvalidOperationException("Profile load failed.");
                EditorApplication.delayCall += Analyze;
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static void Analyze()
        {
            try
            {
                string directory = Path.GetDirectoryName(_profile);
                int labelled = 0, negativeSelf = 0;
                var covered = new HashSet<int>();
                using (var csv = new StreamWriter(Path.Combine(directory, _resourcesOnly ? "cpu-resources.csv" : "cpu-samples.csv")))
                {
                    csv.WriteLine("profile_frame,unity_frame,thread,frame_ms,sample_ms,self_ms,allocation_bytes,name,parent,thread_index,sample_index,parent_index");
                    for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
                    {
                        int unityFrame = -1;
                        using (RawFrameDataView main = ProfilerDriver.GetRawFrameDataView(frame, 0))
                        {
                            if (!main.valid) continue;
                            for (int i = 0; i < main.sampleCount; i++)
                            {
                                string name = main.GetSampleName(i);
                                const string prefix = "DK2.Probe.ArrivalFrame:";
                                if (name != null && name.StartsWith(prefix, StringComparison.Ordinal))
                                { unityFrame = int.Parse(name.Substring(prefix.Length), CultureInfo.InvariantCulture); break; }
                            }
                        }
                        if (unityFrame < 0) continue;
                        labelled++;
                        covered.Add(unityFrame);
                        for (int thread = 0; thread < 256; thread++)
                        {
                            using (RawFrameDataView data = ProfilerDriver.GetRawFrameDataView(frame, thread))
                            {
                                if (!data.valid) break;
                                var parents = new Stack<(int Index, int End)>();
                                var parentIndices = new int[data.sampleCount];
                                var self = new double[data.sampleCount];
                                for (int i = 0; i < data.sampleCount; i++) self[i] = data.GetSampleTimeMs(i);
                                for (int i = 0; i < data.sampleCount; i++)
                                {
                                    while (parents.Count > 0 && parents.Peek().End < i) parents.Pop();
                                    parentIndices[i] = parents.Count > 0 ? parents.Peek().Index : -1;
                                    if (parents.Count > 0) self[parents.Peek().Index] -= data.GetSampleTimeMs(i);
                                    int children = data.GetSampleChildrenCountRecursive(i);
                                    if (children > 0) parents.Push((i, i + children));
                                }
                                for (int i = 0; i < data.sampleCount; i++)
                                {
                                    string name = data.GetSampleName(i);
                                    double duration = data.GetSampleTimeMs(i);
                                    bool allocation = name == "GC.Alloc";
                                    bool resource = name == "Gfx.CreateBufferResource" || name == "Gfx.InitializeBuffer";
                                    bool custom = name?.StartsWith("DK2.") ?? false;
                                    if (_resourcesOnly ? !resource && !custom : duration < .1 && !allocation && !custom && !resource) continue;
                                    long bytes = allocation && data.GetSampleMetadataCount(i) > 0 ? data.GetSampleMetadataAsLong(i, 0) : 0;
                                    if (self[i] < -.01) negativeSelf++;
                                    csv.WriteLine(string.Join(",", new[] { frame.ToString(), unityFrame.ToString(), Quote(data.threadName),
                                        F(data.frameTimeMs), F(duration), F(Math.Max(0, self[i])), bytes.ToString(), Quote(name),
                                        Quote(parentIndices[i] < 0 ? "" : data.GetSampleName(parentIndices[i])),
                                        thread.ToString(), i.ToString(), parentIndices[i].ToString() }));
                                }
                            }
                        }
                    }
                }
                if (labelled < 10) throw new InvalidOperationException("Missing labelled CPU frames.");
                var frameRows = File.ReadAllLines(Path.Combine(directory, "frames.csv"));
                string[] header = frameRows[0].Split(',');
                int frameColumn = Array.IndexOf(header, "unity_frame"), introColumn = Array.IndexOf(header, "intro_seconds");
                int missing = 0;
                foreach (string line in frameRows.Skip(1))
                {
                    string[] cells = line.Split(',');
                    double intro = double.Parse(cells[introColumn], CultureInfo.InvariantCulture);
                    if (intro >= 0 && intro < 6 && !covered.Contains(int.Parse(cells[frameColumn], CultureInfo.InvariantCulture))) missing++;
                }
                if (missing > 0 || negativeSelf > 0) throw new InvalidOperationException("Missing closeup frames=" + missing + "; invalid self durations=" + negativeSelf);
                File.WriteAllText(Path.Combine(directory, _resourcesOnly ? "cpu-resource-report.txt" : "cpu-report.txt"),
                    "LabelledFrames=" + labelled + "\nScope=Development CPU diagnostics; not FPS acceptance\n" +
                    "MissingCloseupFrames=" + missing + "\nNegativeSelfSamples=" + negativeSelf + "\n" +
                    "Durations inclusive; self subtracts direct children. Parallel threads must not be added.\n" +
                    "ResourcesOnly=" + _resourcesOnly + "\nSampleCutoffMs=0.1 (all DK2 markers, buffer creation and allocations retained in full reports)\n");
                Debug.Log("Arrival CPU report: " + labelled + " labelled frames.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
