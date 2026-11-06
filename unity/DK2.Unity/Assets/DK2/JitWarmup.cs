// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DK2.Unity
{
    /// <summary>Compiles the methods of whole assemblies ahead of their first call, on a worker, so the
    /// first frame that runs a code path does not pay Mono's JIT for it: the first frame with the HUD
    /// visible after the opening camera path ran the hover, input and HUD passes for the first time
    /// and spent ~50 ms in them with no allocation and no child marker (ADR-0688). Generic methods
    /// and types are skipped, since they need an instantiation; a method that cannot be prepared is
    /// skipped. Only compiles: no type initialiser runs here, so nothing touches Unity off-thread.</summary>
    internal static class JitWarmup
    {
        public static Task Run(params Assembly[] assemblies) => Task.Run(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int prepared = 0;
            foreach (Assembly assembly in assemblies)
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                foreach (Type type in types)
                {
                    if (type == null || type.ContainsGenericParameters) continue;
                    const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    foreach (MethodBase method in type.GetMethods(all))
                        if (Prepare(method)) prepared++;
                    foreach (MethodBase constructor in type.GetConstructors(all))
                        if (Prepare(constructor)) prepared++;
                }
            }
            UnityEngine.Debug.Log($"DK2: jit warm-up prepared {prepared} methods in {clock.ElapsedMilliseconds} ms");
            return prepared;
        });

        private static bool Prepare(MethodBase method)
        {
            if (method.IsAbstract || method.ContainsGenericParameters
                || (method.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0
                || (method.Attributes & MethodAttributes.PinvokeImpl) != 0) return false;
            try
            {
                // Mono's PrepareMethod resolves without compiling (5,423 methods in 13 ms, 2026-11-06);
                // asking for the function pointer is what makes it compile.
                method.MethodHandle.GetFunctionPointer();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
