// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Data.Settings;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

public partial class ClassicFrontEndMenu
{
    private OptionsPagesController? _options;
    private ClassicSaveLoadPanel? _savePanel;
    private SessionLoader? _saveLoader;
    private Button? _importButton;
    private FileDialog? _importDialog;
    private string? _scriptedImportPath;
    private bool _loadPrimaryHeld, _importDialogOpen, _waitForPickerRelease;

    private void OpenSaveBrowser(string install, GameFolders folders, string[] args)
    {
        ClassicFrontEndMenuAssets assets = _assets!;
        FrontEndMenuController menu = _hud!.Model;
        menu.SetSaveBrowser(new SaveSlotsView(folders.Saves));
        menu.LoadRequested += LoadMenuSave;
        _saveLoader = new SessionLoader(_profiles!,
            () => UserFolders.LoadProfile(_profiles!, folders.Profile, GameSessionMode.FrontEnd, FrontEndPage.Load),
            name => CampaignLevelData.Load(install, name))
        { TerrainImportsSupported = true };

        _savePanel = new ClassicSaveLoadPanel { Name = "SaveBrowser" };
        _savePanel.Open(assets.Text, assets.LegibleFont);
        AddChild(_savePanel);

        _scriptedImportPath = LaunchArguments.Value(args, "--front-end-import-path");
        _importButton = new Button { Name = "ImportDk2Save", Text = "Import DK2 Save… (terrain only)", Visible = false };
        _importButton.Pressed += BrowseDk2Save;
        AddChild(_importButton);
    }

    private void UpdateSaveBrowser(int width, int height, bool primaryHeld)
    {
        bool primaryDown = primaryHeld && !_loadPrimaryHeld;
        _loadPrimaryHeld = primaryHeld;
        bool shown = _hud!.Model.Page == FrontEndPage.Load && !_session!.PathPlaying && width > 0 && height > 0;
        _savePanel!.ShowFrontEnd(_hud.Model.Saves, shown, _wheel);
        Button button = _importButton!;
        button.Visible = shown;
        if (!shown) return;

        float scale = FrontEndBox.Scale(width, height);
        (float left, float top) = FrontEndBox.Origin(width, height);
        button.Position = new Vector2(left + 70 * scale, top + 408 * scale);
        button.Size = new Vector2(174 * scale, 22 * scale);
        if (!primaryDown) return;
        if (button.GetRect().HasPoint(_pointer))
        {
            if (_pointerScript != null) BrowseDk2Save();
            return;
        }
        _savePanel.FrontEndPointerDown(_pointer);
    }

    private void LoadMenuSave(string path)
    {
        try { _pendingLaunch = _saveLoader!.Load(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            _savePanel?.Error(ex.Message);
        }
    }

    private void BrowseDk2Save()
    {
        if (_pointerScript != null && _scriptedImportPath != null)
        {
            ImportDk2Save(_scriptedImportPath);
            return;
        }
        if (_importDialog == null)
        {
            _importDialog = new FileDialog
            {
                Name = "Dk2SavePicker", Title = "Import DK2 save terrain",
                Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenFile,
                UseNativeDialog = true, Filters = ["*.sav, *.qsav; Dungeon Keeper 2 saves"],
            };
            _importDialog.FileSelected += ImportDk2Save;
            _importDialog.Canceled += ImportPickerClosed;
            AddChild(_importDialog);
        }
        _importDialogOpen = _waitForPickerRelease = true;
        _importDialog.PopupCenteredRatio(.7f);
    }

    private void ImportPickerClosed() => _importDialogOpen = false;

    private bool ImportPickerOwnsPointer(bool primaryHeld)
    {
        if (_importDialogOpen) return true;
        if (!_waitForPickerRelease) return false;
        if (!primaryHeld) _waitForPickerRelease = false;
        return true;
    }

    private void ImportDk2Save(string path)
    {
        ImportPickerClosed();
        if (_install == null) return;
        try { _pendingLaunch = GameSessionRequest.LoadDk2Save(_install, path); }
        catch (Exception ex) when (InGameHud.IsLegacyImportError(ex)) { _savePanel?.Error(ex.Message); }
    }

    private void CloseSaveBrowser()
    {
        if (_hud != null) _hud.Model.LoadRequested -= LoadMenuSave;
        if (_importButton != null) _importButton.Pressed -= BrowseDk2Save;
        if (_importDialog != null)
        {
            _importDialog.FileSelected -= ImportDk2Save;
            _importDialog.Canceled -= ImportPickerClosed;
        }
    }

    private void ApplyMenuSettings(GameSettings settings)
    {
        if (_mixer != null)
        {
            _mixer.SoundQuality = settings.SoundQuality;
            _mixer.SetVolumes(master: settings.MasterVolume, speech: settings.SpeechVolume, music: settings.MusicVolume,
                effects: settings.SoundEffectVolume, speechOn: settings.SpeechSwitch, musicOn: settings.MusicSwitch,
                effectsOn: settings.SfxSwitch);
        }
        _world?.SetBrightness(DisplaySettings.BrightnessExponent(settings.Gamma));
        _world?.SetRenderQuality(settings);
    }

    private void PollKeyCapture()
    {
        if (_options is { CapturingAction: not null } options && _hud!.Model.Page == options.Page)
            options.CapturePressedKey(_keyboard.Read(options.Current));
    }
}
