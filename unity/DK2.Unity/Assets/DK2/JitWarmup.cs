// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DK2.Unity
{
    /// <summary>Compiles the methods of whole assemblies on a worker, ahead of their first call, so
    /// no frame pays Mono's JIT for a path it runs for the first time (ADR-0688). Generic methods
    /// and types are skipped, since they need an instantiation. Only compiles: no type initialiser
    /// runs here, so nothing touches Unity off the main thread. Nothing to do under IL2CPP.</summary>
    internal static class JitWarmup
    {
        public static Task Run(params Assembly[] assemblies) => Task.Run(() =>
        {
#if !ENABLE_IL2CPP
            var clock = Stopwatch.StartNew();
            int prepared = 0;
            foreach (Assembly assembly in assemblies)
                foreach (Type type in Types(assembly))
                    foreach (MethodBase method in Methods(type))
                        if (Compile(method)) prepared++;
            UnityEngine.Debug.Log($"DK2: jit warm-up compiled {prepared} methods in {clock.ElapsedMilliseconds} ms");
#endif
        });

        private static IEnumerable<Type> Types(Assembly assembly)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            return types.Where(type => type != null && !type.ContainsGenericParameters);
        }

        private static IEnumerable<MethodBase> Methods(Type type)
        {
            const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            return type.GetMethods(declared).Cast<MethodBase>().Concat(type.GetConstructors(declared))
                .Where(method => !method.IsAbstract && !method.ContainsGenericParameters
                    && (method.MethodImplementationFlags & MethodImplAttributes.InternalCall) == 0
                    && (method.Attributes & MethodAttributes.PinvokeImpl) == 0);
        }

        /// <summary>Mono's <c>RuntimeHelpers.PrepareMethod</c> resolves without compiling; asking for
        /// the function pointer is what compiles.</summary>
        private static bool Compile(MethodBase method)
        {
            try
            {
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
