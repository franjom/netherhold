// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Diagnostics;
using System.Globalization;
using DK2.TestSupport;

namespace DK2.Oracle.Tests;

/// <summary>
/// Runs single functions of the reference install's <c>DKII.EXE</c> on an emulated i386, so a
/// test can hold our C# to the original's own answer for every input it cares to try.
///
/// <para>The emulator is Unicorn (GPLv2): the owner allows it to be used, never committed, so
/// <c>unicorn.dll</c> comes from <see cref="DllVariable"/>. It runs in
/// <c>tools/oracle/dk2oracle.py</c> because a .NET host fail-fasts (0xC0000409) under it
/// (docs/TRAPS.md). One batch is one helper process, so a test sends all its calls at once.</para>
///
/// <para>Only functions that read just their arguments and the image's own bytes belong here:
/// no imports are bound, no globals initialised, and straying outside comes back as a fault.</para>
/// </summary>
internal static class OriginalCpu
{
    /// <summary>The full path of a Windows x64 <c>unicorn.dll</c> (2.1.x).</summary>
    public const string DllVariable = "DK2_UNICORN_DLL";

    /// <summary>A Python 3 interpreter; <c>py -3</c> when unset.</summary>
    public const string PythonVariable = "DK2_PYTHON";

    /// <summary>64 KB a request may write and read, for pointer arguments.</summary>
    public const uint Scratch = 0x00300000;

    public static string? SkipReason =>
        Dk2InstallGate.SkipReason
        ?? (File.Exists(Environment.GetEnvironmentVariable(DllVariable) ?? "")
            ? null
            : $"No Unicorn: set {DllVariable} to a unicorn.dll outside the repository.");

    /// <summary>Calls each request's cdecl function in order on one emulator; memory a request
    /// writes stays written for the ones after it. With <paramref name="memoryImage"/> (a
    /// capture tick, <see cref="CapturedState"/>) every call runs against that game state.</summary>
    public static IReadOnlyList<Outcome> Run(IReadOnlyList<Request> requests, string? memoryImage = null)
    {
        OriginalExecutable.Load(); // the MD5 check: the addresses are for v1.70 only

        string exe = System.IO.Path.Combine(Dk2InstallGate.Path, "DKII.EXE");
        string[] lines = Python("dk2oracle.py", memoryImage is null ? [exe] : [exe, memoryImage],
            requests.Select(r => r.Line()));
        Assert.True(lines.Length == requests.Count, $"dk2oracle gave {lines.Length} of {requests.Count} outcomes");
        return [.. lines.Select(Outcome.Parse)];
    }

    /// <summary>Runs a <c>tools/oracle</c> script under <see cref="PythonVariable"/>, feeds it
    /// <paramref name="input"/> and returns its output lines; a nonzero exit fails the test.</summary>
    public static string[] Python(string script, IEnumerable<string> args, IEnumerable<string>? input = null)
    {
        string? python = Environment.GetEnvironmentVariable(PythonVariable);
        var start = new ProcessStartInfo(string.IsNullOrEmpty(python) ? "py" : python)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (string.IsNullOrEmpty(python)) start.ArgumentList.Add("-3");
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add(Script(script));
        foreach (string arg in args) start.ArgumentList.Add(arg);

        using Process process = Process.Start(start)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        foreach (string line in input ?? []) process.StandardInput.WriteLine(line);
        process.StandardInput.Close();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"{script} exited {process.ExitCode}:\n{error.Result}");
        return output.Result.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Script(string name)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "tools", "oracle", name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"tools/oracle/{name} is not above the test assembly.");
    }

    /// <summary>One call: the function, its 32-bit stack arguments, dwords to write first,
    /// addresses to read after, and ECX for a thiscall.</summary>
    public sealed record Request(uint Function, uint[] Args, (uint Address, uint Value)[]? Writes = null, uint[]? Reads = null,
        uint? Ecx = null)
    {
        public string Line()
        {
            string line = $"{Function:x} " + string.Join(' ', Args.Select(a => a.ToString("x")));
            IEnumerable<string> writes = (Writes ?? []).Select(w => $"{w.Address:x}={w.Value:x}");
            if (Ecx is { } ecx) writes = writes.Append($"ecx={ecx:x}");
            if (writes.Any()) line += " | " + string.Join(' ', writes);
            if (Reads is { Length: > 0 }) line += " @ " + string.Join(' ', Reads.Select(r => r.ToString("x")));
            return line;
        }
    }

    /// <summary>What the original did: EAX, EDX and the requested reads on return, or why it
    /// never returned (a CPU exception such as a divide fault, or a read outside the image).</summary>
    public readonly record struct Outcome(uint Eax, uint Edx, uint[] Reads, string? Fault)
    {
        public static Outcome Parse(string line)
        {
            if (line.StartsWith("fault ", StringComparison.Ordinal)) return new Outcome(0, 0, [], line[6..].Trim());
            uint[] words = [.. line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => uint.Parse(w, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
            return new Outcome(words[0], words[1], words[2..], null);
        }
    }
}

/// <summary><c>[Fact]</c> for a test that runs the original's code: skips without the install
/// or without Unicorn.</summary>
public sealed class OracleFactAttribute : FactAttribute
{
    public OracleFactAttribute() => Skip = OriginalCpu.SkipReason;
}
