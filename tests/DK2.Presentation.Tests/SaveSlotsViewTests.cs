// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Model;
using DK2.Data.Save;

namespace DK2.Presentation.Tests;

public sealed class SaveSlotsViewTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dk2-browser-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EmptyDirectoryShowsNoInventedFixedSlots()
    {
        var view = new SaveSlotsView(_directory);
        Assert.Empty(view.Entries);
        Assert.Null(view.Selected);
        Assert.False(view.Select(0));
    }

    [Fact]
    public void SavedFileShowsItsNameAndTimeAndSelectionSeedsTheNameEditor()
    {
        var time = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var saved = new SavedSession(new("Level1", 1, 26, time, "First save"), new byte[] { 1 }, new CampaignProfile());
        SavedSessionFile.Save(SavedSessionFile.PathForName(_directory, "First save"), saved);
        var view = new SaveSlotsView(_directory);
        Assert.True(view.Select(0));
        Assert.Equal("First save", view.Name);
        Assert.Equal(time, view.Selected!.Header!.SavedAtUtc);
        view.SetName("New save");
        Assert.Equal("New save", view.Name);
    }

    [Fact]
    public void AnUnreadableFileIsListedAndSelectableUnderItsFileName()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "broken" + SavedSessionFile.Extension), new byte[] { 1, 2, 3 });
        var view = new SaveSlotsView(_directory);

        Assert.True(view.Select(0));

        Assert.False(view.Selected!.Readable);
        Assert.NotNull(view.Selected.Refusal);
        Assert.Equal("broken", view.Name);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
