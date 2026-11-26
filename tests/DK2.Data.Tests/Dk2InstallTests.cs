// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Tests;

// These tests check the lookup rules on temporary folders and never read the real install (InstallGateTests allows
// Check and Resolve; it flags Find and Require).

public sealed class Dk2InstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dk2-install-tests-" + Guid.NewGuid().ToString("N"));

    public Dk2InstallTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Folder(string name, bool exe, bool data)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        if (exe)
        {
            File.WriteAllBytes(Path.Combine(dir, "DKII.EXE"), []);
        }

        if (data)
        {
            Directory.CreateDirectory(Path.Combine(dir, "Data"));
        }

        return dir;
    }

    [Fact]
    public void AnInstallHoldsTheExecutableAndTheDataFolder()
    {
        Assert.Equal(Dk2InstallCheck.Empty, Dk2Install.Check(null));
        Assert.Equal(Dk2InstallCheck.Empty, Dk2Install.Check("  "));
        Assert.Equal(Dk2InstallCheck.NoGame, Dk2Install.Check(Path.Combine(_root, "missing")));
        Assert.Equal(Dk2InstallCheck.NoGame, Dk2Install.Check(Folder("data-only", exe: false, data: true)));
        Assert.Equal(Dk2InstallCheck.NoData, Dk2Install.Check(Folder("exe-only", exe: true, data: false)));
        Assert.Equal(Dk2InstallCheck.Found, Dk2Install.Check(Folder("game", exe: true, data: true)));
    }

    [Fact]
    public void TheEnvironmentThenTheCommandLineDecideAlone()
    {
        string a = Folder("a", true, true), b = Folder("b", true, true), c = Folder("c", true, true), d = Folder("d", true, true);
        string broken = Folder("broken", true, false);
        Assert.Equal(a, Dk2Install.Resolve(a, b, c, [d]));
        Assert.Null(Dk2Install.Resolve(broken, b, c, [d]));
        Assert.Equal(b, Dk2Install.Resolve(" ", b, c, [d]));
        Assert.Null(Dk2Install.Resolve(null, broken, c, [d]));
    }

    [Fact]
    public void TheSavedFolderComesBeforeTheDefaultsAndFallsThroughWhenMoved()
    {
        string saved = Folder("saved", true, true), fallback = Folder("fallback", true, true);
        Assert.Equal(saved, Dk2Install.Resolve(null, null, saved, [fallback]));
        Assert.Equal(fallback, Dk2Install.Resolve(null, null, Path.Combine(_root, "moved"), [Path.Combine(_root, "x"), fallback]));
        Assert.Null(Dk2Install.Resolve(null, null, null, []));
    }
}
