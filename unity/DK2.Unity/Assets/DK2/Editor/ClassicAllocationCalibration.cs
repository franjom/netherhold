// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace DK2.Unity.Editor
{
    public static class ClassicAllocationCalibration
    {
        [Serializable] public sealed class Result
        {
            public string UnityVersion;
            public int KnownPayloadBytes = 65536;
            public long ThreadDeltaBytes;
            public bool ThreadMeterAvailable;
            public bool RecorderValid, RecorderBytesAvailable;
            public string RecorderUnit, RecorderError;
            public int RecorderSamples;
            public long RecorderCurrentValue, RecorderSampleSum;
        }

        public static Result Measure()
        {
            var result = new Result { UnityVersion = Application.unityVersion };
            ProfilerRecorder recorder = default;
            try
            {
                recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc", 128,
                    ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                result.RecorderValid = recorder.Valid;
                if (recorder.Valid) result.RecorderUnit = recorder.UnitType.ToString();
            }
            catch (Exception error) { result.RecorderError = error.Message; }
            long before = GC.GetAllocatedBytesForCurrentThread();
            var payload = new byte[result.KnownPayloadBytes];
            payload[0] = 1;
            GC.KeepAlive(payload);
            result.ThreadDeltaBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            result.ThreadMeterAvailable = result.ThreadDeltaBytes >= result.KnownPayloadBytes;
            if (recorder.Valid)
            {
                // Stop resets CurrentValue; capture it before stopping the recorder.
                result.RecorderCurrentValue = recorder.CurrentValue;
                recorder.Stop();
                result.RecorderSamples = recorder.Count;
                for (int i = 0; i < recorder.Count; i++) result.RecorderSampleSum += recorder.GetSample(i).Value;
                result.RecorderBytesAvailable = result.RecorderUnit == "Bytes"
                    && Math.Max(result.RecorderCurrentValue, result.RecorderSampleSum) >= result.KnownPayloadBytes;
            }
            recorder.Dispose();
            return result;
        }

        [MenuItem("DK2/Classic look/Calibrate managed allocation meters")]
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("DK2_ALLOCATION_CALIBRATION_OUTPUT")
                ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../build/step5-allocation-calibration/result.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            Result result = Measure();
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log($"DK2 allocation calibration: thread meter {result.ThreadMeterAvailable}, GC.Alloc byte recorder {result.RecorderBytesAvailable}; {output}");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
