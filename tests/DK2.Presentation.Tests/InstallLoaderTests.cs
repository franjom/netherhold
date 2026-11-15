// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The engine-neutral loaders both adapters share (migration plan step 1a). The install-reading tests skip
/// without an install; PNG decoding is the engine's, so a stub decoder stands in for it.</summary>
public class InstallLoaderTests
{
    [Theory]
    [InlineData(640, 480, 0f, 0f)]
    [InlineData(1920, 1080, 240f, 0f)]
    [InlineData(1024, 1024, 0f, 128f)]
    public void FrontEndBoxOriginCentresTheBox(int width, int height, float left, float top)
    {
        (float actualLeft, float actualTop) = FrontEndBox.Origin(width, height);
        Assert.Equal(left, actualLeft, 3);
        Assert.Equal(top, actualTop, 3);
    }

    [InstallFact]
    public void CameraPathsComeFromTheInstallAndAreReadOnce()
    {
        Func<int, IReadOnlyList<DK2.Data.Model.KcsFrame>?> load = EngineCameraPaths.Loader(Dk2InstallGate.Path);
        IReadOnlyList<DK2.Data.Model.KcsFrame>? path = load((int)FrontEndPath.EntryToOptions);
        Assert.NotNull(path);
        Assert.NotEmpty(path);
        Assert.Same(path, load((int)FrontEndPath.EntryToOptions));
    }

    [InstallFact]
    public void FrontEndImagesAreFoundByNameDecodedOnceAndMissingOnesHaveNoSize()
    {
        int decoded = 0;
        using var sources = new InstallFrontEndSources(Dk2InstallGate.Path, _ =>
        {
            decoded++;
            return new DK2.Art.Textures.RgbaImage(1, 1, new byte[4]);
        });
        DK2.Art.Textures.RgbaImage image = sources.Load("Cancel");
        Assert.Same(image, sources.Load("Cancel"));
        Assert.Equal(1, decoded);
        Assert.Equal((1, 1), sources.ImageSize("Cancel"));
        Assert.Null(sources.ImageSize("NoSuchFrontEndImage"));
        Assert.NotEqual(string.Empty, sources.Text(1));
    }

    [Fact]
    public void FrontEndBoxOriginMapsToTheBoxCorner()
    {
        (float left, float top) = FrontEndBox.Origin(1920, 1080);
        FrontEndPoint corner = FrontEndBox.ToUnits(left, top, 1920, 1080);
        Assert.Equal(0f, corner.X, 3);
        Assert.Equal(0f, corner.Y, 3);
    }

    [Fact]
    public void CameraPathLoaderReturnsNullWithoutPathsWad()
    {
        string install = Path.Combine(Path.GetTempPath(), "dk2-paths-" + Guid.NewGuid().ToString("N"));
        Func<int, IReadOnlyList<DK2.Data.Model.KcsFrame>?> load = EngineCameraPaths.Loader(install);
        Assert.Null(load(1));
        Assert.Null(load(1));
    }

    [Fact]
    public void LoadersRejectNoInstall()
    {
        Assert.Throws<ArgumentNullException>(() => EngineCameraPaths.Loader(null!));
        Assert.Throws<ArgumentNullException>(() => new InstallHudSources(null!));
        Assert.Throws<ArgumentNullException>(() => new InstallFrontEndSources("x", null!));
    }
}
