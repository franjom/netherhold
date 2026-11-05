// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace DK2.Unity.Editor
{
    /// <summary>
    /// Reads a probe run's <c>capture.raw</c> in a batchmode Editor and writes <c>capture-report.txt</c>:
    /// the main thread's self time and self GC allocation per sample path, averaged over the frames
    /// with a tick and over those without, heaviest first. Copied into the probe's scratch project
    /// by <c>report-capture.ps1</c>; never part of the game. A capture adds overhead, so read the
    /// shape, not the numbers.
    /// </summary>
    public static class CaptureReport
    {
        private sealed class Total
        {
            public double SelfMs, GcBytes;
            public long Calls;
        }

        public static void Run()
        {
            string[] args = Environment.GetCommandLineArgs();
            string dir = args[Array.IndexOf(args, "-captureDir") + 1];
            if (!ProfilerDriver.LoadProfile(Path.Combine(dir, "capture.raw"), false))
                throw new InvalidOperationException("could not load " + dir);
            var totals = new[] { new Dictionary<string, Total>(), new Dictionary<string, Total>() };
            var frames = new int[2];
            var children = new List<int>();
            var worst = new List<(int Frame, double Ms, List<(string Path, double SelfMs, double Gc, long Calls)> Paths)>();
            for (int frame = ProfilerDriver.firstFrameIndex; frame <= ProfilerDriver.lastFrameIndex; frame++)
            {
                using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(frame, 0,
                    HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false);
                if (view == null || !view.valid) continue;
                var paths = new List<(string Path, double SelfMs, double Gc, long Calls)>();
                bool ticked = false;
                Walk(view, view.GetRootItemID(), "", paths, children, ref ticked);
                int kind = ticked ? 1 : 0;
                frames[kind]++;
                double frameMs = paths.Where(p => !p.Path.EndsWith("WaitForTargetFPS", StringComparison.Ordinal)
                    && !p.Path.EndsWith("Idle", StringComparison.Ordinal)).Sum(p => p.SelfMs);
                worst.Add((frame, frameMs, paths));
                worst = worst.OrderByDescending(w => w.Ms).Take(8).ToList();
                foreach (var (path, self, gc, calls) in paths)
                {
                    if (!totals[kind].TryGetValue(path, out Total total)) totals[kind][path] = total = new Total();
                    total.SelfMs += self;
                    total.GcBytes += gc;
                    total.Calls += calls;
                }
            }

            var text = new StringBuilder();
            for (int kind = 1; kind >= 0; kind--)
            {
                int n = Math.Max(1, frames[kind]);
                text.AppendLine(kind == 1 ? $"FRAMES WITH A TICK ({frames[1]})" : $"FRAMES WITHOUT A TICK ({frames[0]})");
                text.AppendLine("  by self GC per frame (KB), self ms, calls per frame, path");
                foreach (var entry in totals[kind].OrderByDescending(e => e.Value.GcBytes).Take(40))
                    text.AppendLine(FormattableString.Invariant(
                        $"  {entry.Value.GcBytes / n / 1024,9:F1} {entry.Value.SelfMs / n,8:F3} {entry.Value.Calls / (double)n,8:F1}  {entry.Key}"));
                text.AppendLine("  by self ms per frame, GC KB, calls per frame, path");
                foreach (var entry in totals[kind].OrderByDescending(e => e.Value.SelfMs).Take(40))
                    text.AppendLine(FormattableString.Invariant(
                        $"  {entry.Value.SelfMs / n,8:F3} {entry.Value.GcBytes / n / 1024,9:F1} {entry.Value.Calls / (double)n,8:F1}  {entry.Key}"));
            }
            text.AppendLine("WORST FRAMES (main-thread self time, excluding the frame-rate wait): self ms, GC KB, calls, path");
            foreach (var (frame, ms, paths) in worst)
            {
                text.AppendLine(FormattableString.Invariant($"  frame {frame}: {ms:F1} ms"));
                foreach (var (path, self, gc, calls) in paths.OrderByDescending(p => p.SelfMs).Take(14))
                    text.AppendLine(FormattableString.Invariant($"    {self,8:F2} {gc / 1024,9:F1} {calls,6}  {path}"));
            }
            File.WriteAllText(Path.Combine(dir, "capture-report.txt"), text.ToString());
            Debug.Log("DK2 capture report written");
        }

        private static void Walk(HierarchyFrameDataView view, int id, string parent,
            List<(string, double, double, long)> into, List<int> scratch, ref bool ticked)
        {
            view.GetItemChildren(id, scratch);
            int[] kids = scratch.ToArray();
            foreach (int child in kids)
            {
                string name = view.GetItemName(child);
                if (name == "DK2.Simulation.Tick") ticked = true;
                string path = parent.Length == 0 ? name : parent + " > " + name;
                double gc = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnGcMemory);
                double self = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                long calls = (long)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                view.GetItemChildren(child, scratch);
                double childGc = 0;
                foreach (int grandchild in scratch)
                    childGc += view.GetItemColumnDataAsFloat(grandchild, HierarchyFrameDataView.columnGcMemory);
                into.Add((path, self, gc - childGc, calls));
                Walk(view, child, path, into, scratch, ref ticked);
            }
        }
    }
}
