// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Reflection;
using Xunit.Sdk;

namespace DK2.Simulation.Tests;

/// <summary>Keeps a test's state moves out of <see cref="StateEntryLog"/>: for tests that drive
/// the transition gate with ids no mechanic enters, which would otherwise make every state
/// look exercised. xUnit runs Before, a synchronous test and After on one thread.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class StatesUnrecordedAttribute : BeforeAfterTestAttribute
{
    [ThreadStatic] private static StateEntryLog.Suspension? _scope;

    public override void Before(MethodInfo methodUnderTest)
    {
        // An async test may resume on another thread, leaving this one unrecorded for good.
        if (typeof(Task).IsAssignableFrom(methodUnderTest.ReturnType))
            throw new InvalidOperationException($"{methodUnderTest.Name}: StatesUnrecorded needs a synchronous test.");
        _scope = StateEntryLog.Unrecorded();
    }

    public override void After(MethodInfo methodUnderTest)
    {
        _scope?.Dispose();
        _scope = null;
    }
}
