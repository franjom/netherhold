// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using System.Runtime.CompilerServices;

namespace DK2.Presentation;

/// <summary>Compiles the methods of some assemblies before anything calls them, so the first frame that draws a panel or
/// lights the world does not stop to compile the code it runs: the first frame of Level6a spent 164 ms in its update,
/// most of it first-use work. It only compiles: no method runs, and <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>
/// runs no static constructor, so what a level does is not touched. Generic definitions are left, as the runtime
/// compiles those per instantiation; a method it cannot compile is skipped.</summary>
public static class JitWarmUp
{
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Compiles every method of <paramref name="assemblies"/> in order and returns how many it compiled; stops
    /// early, between two methods, when <paramref name="stop"/> is set.</summary>
    public static int Prepare(IEnumerable<Assembly> assemblies, Func<bool>? stop = null)
    {
        if (assemblies is null)
        {
            throw new ArgumentNullException(nameof(assemblies));
        }

        return Prepare(assemblies.SelectMany(TypesOf), stop);
    }

    /// <summary>Compiles every method of <paramref name="types"/>, as <see cref="Prepare(IEnumerable{Assembly}, Func{bool}?)"/> does.</summary>
    public static int Prepare(IEnumerable<Type> types, Func<bool>? stop = null)
    {
        if (types is null)
        {
            throw new ArgumentNullException(nameof(types));
        }

        int prepared = 0;
        foreach (Type type in types)
        {
            if (type.ContainsGenericParameters || type.IsInterface)
            {
                continue;
            }

            foreach (MethodBase method in Methods(type))
            {
                if (stop?.Invoke() == true)
                {
                    return prepared;
                }

                if (method.IsAbstract || method.ContainsGenericParameters)
                {
                    continue;
                }

                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    continue;
                }

                if ((method.GetMethodImplementationFlags() & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0)
                {
                    continue;
                }

                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    prepared++;
                }
                catch (Exception)
                {
                    // Not compilable here (a missing dependency, say): it fails when called, as it always would.
                }
            }
        }
        return prepared;
    }

    private static Type[] TypesOf(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException partial) { return [.. partial.Types.Where(t => t is not null)!]; }
    }

    private static IEnumerable<MethodBase> Methods(Type type)
    {
        MethodBase[] methods;
        MethodBase[] constructors;
        try
        {
            methods = type.GetMethods(Declared);
            constructors = type.GetConstructors(Declared);
        }
        catch (Exception)
        {
            return [];
        }
        return constructors.Concat(methods);
    }
}
