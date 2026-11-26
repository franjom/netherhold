// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Globalization;
using DK2.Data.Campaign;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Presentation;
using Godot;

namespace DK2.GodotAdapter;

/// <summary>The front-end menu, the Godot counterpart of Unity's <c>ClassicFrontEndMenu</c> plus the menu part of
/// <c>DungeonRunner.FrontEnd</c>: <see cref="FrontEndMenuHud"/> (ADR-0805) decides every label, colour, hover and
/// page, and a <see cref="HudCanvas"/> draws its frame. This node runs the session's clock, converts the pointer to
/// 640 x 480 units and forwards it. The campaign profile and hi-score table load as in Unity
/// (<see cref="CampaignProfileStore"/>, <see cref="HiScoreStore"/>), so the hi-score and movie gallery pages show the
/// player's rows and the campaign entries open the map page. The 3D station behind the menu, the land picking, the launch
/// and the hi-score name are in <c>ClassicFrontEndMenu.Station.cs</c>, the pointer and the check runs' scripted input in
/// <c>ClassicFrontEndMenu.Input.cs</c>. The movies (the startup, the gallery, the attract and a won map's cutscene) are in
/// <c>ClassicFrontEndMenu.Movies.cs</c>. The saves and settings pages are not wired, so the model leaves their buttons disabled.
/// The pages' sounds play through the shared <see cref="ClassicSoundMixer"/> at the saved settings' volumes.</summary>
public partial class ClassicFrontEndMenu : Node
{
    private readonly HudFrame _frame = new();
    private ClassicFrontEndMenuAssets? _assets;
    private FrontEndSession? _session;
    private CampaignProfileStore? _profiles;
    private CampaignMapController? _campaign;
    private FrontEndMenuHud? _hud;
    private HudCanvas? _canvas;
    private ClassicSoundMixer? _mixer;
    private double _elapsed;
    private Vector2 _pointer;
    private int _wheel;

    private bool _useReplacement;

    public FrontEndMenuController? Model => _hud?.Model;

    /// <summary>Draw labels with the replacement TrueType face where it can set them (<see cref="Open"/> turns it on, as
    /// Unity does); without a face on this machine the labels stay the BF4 bitmap.</summary>
    public bool UseReplacementFont
    {
        get => _useReplacement;
        set
        {
            _useReplacement = value;
            if (_canvas == null || _assets == null) return;
            FontFile? face = value ? _assets.ReplacementFont : null;
            if (value && face == null) GD.Print("DK2.Godot: no replacement font found; the menu uses the BF4 bitmap font.");
            _canvas.ReplacementFont = face;
        }
    }

    /// <summary>Whether the last presented frame drew any label with the replacement font.</summary>
    public bool UsingReplacementFont => _canvas?.UsingReplacementFont ?? false;

    /// <summary>A widget's pointer region in 640 x 480 units.</summary>
    public FrontEndRect HitBounds(FrontEndWidget widget) =>
        (_hud ?? throw new InvalidOperationException("The menu is not open.")).HitBounds(widget);

    /// <summary>Opens the menu over an install with the player's files in <paramref name="folders"/>; call before the
    /// node enters the tree. A missing profile is created here (from the original game's registry entry on Windows); an
    /// unusable one falls back to memory (<see cref="UserFolders.LoadProfile"/>).</summary>
    /// <param name="returnPage">The page a level that ended returns to, as Unity's runner passes it to the session and the
    /// profile load (<see cref="FrontEndPage.CampaignResults"/> repairs the won map's branch row).</param>
    /// <param name="skipStartup">No title screen and intro movies, as Unity's runner's <c>skipStartup</c>.</param>
    public void Open(string install, GameFolders folders, FrontEndPage returnPage = FrontEndPage.None, bool skipStartup = false)
    {
        if (_hud != null) throw new InvalidOperationException("The menu is already open.");
        _assets = new ClassicFrontEndMenuAssets(install);
        Func<int, IReadOnlyList<KcsFrame>?> paths = EngineCameraPaths.Loader(install);
        _session = new FrontEndSession(path => paths((int)path), returnPage);
        // DungeonRunner.StartFrontEnd: the profile, the hi-scores, and the map that writes both back.
        _profiles = new CampaignProfileStore(InstalledCampaignRegistry.ReadCurrentUser, () => DateTime.Now);
        CampaignProfile profile = UserFolders.LoadProfile(_profiles, folders.Profile, GameSessionMode.FrontEnd, returnPage);
        var hiScores = new HiScoreStore(folders.HiScores, install);
        _campaign = FrontEndHost.OpenCampaign(_session, _profiles, hiScores, LoadHiScores(hiScores), _assets.Briefing, RefreshMovies);
        _hud = new FrontEndMenuHud(_session, _assets, HudFit, ClassicKeyNames.Name, _campaign);
        _hud.Model.SetMovieGallery(FrontEndHost.MovieGallery(profile, install, _assets.Text));
        _hud.Model.QuitRequested += Quit;
        _session.PageChanged += PageChanged;
        string[] args = OS.GetCmdlineUserArgs();
        GameSettings settings = InGameHud.LoadSettings(folders.Settings);
        PrepareStation(install, profile, _profiles.SecretFiveAvailable, LaunchArguments.Parse(args, "--").Seed ?? 1,
            DisplaySettings.BrightnessExponent(settings.Gamma), args);
        OpenInput(args);
        _canvas = new HudCanvas { Name = "HudCanvas", Load = _assets.Load };
        AddChild(_canvas);
        // DungeonRunner's sound player, which its front end has too, at the saved settings' volumes: the pages' sounds only
        // (no music, and the throttle never runs there).
        var output = new ClassicSoundOutput { Name = "Sound" };
        AddChild(output);
        _mixer = new ClassicSoundMixer(output, new InstallClassicSoundAssets(install), 0);
        _mixer.SoundQuality = settings.SoundQuality;
        _mixer.SetVolumes(master: settings.MasterVolume, speech: settings.SpeechVolume, music: settings.MusicVolume,
            effects: settings.SoundEffectVolume, speechOn: settings.SpeechSwitch, musicOn: settings.MusicSwitch,
            effectsOn: settings.SfxSwitch);
        // --sound-log, as the in-game scene's: each request as it starts, on the front end's clock.
        if (Array.IndexOf(args, "--sound-log") >= 0) LogSounds(_mixer);
        UseReplacementFont = true;
        OpenMovies(install, skipStartup, returnPage);
        if (_startup == null) _session.Update(TimeSpan.Zero);
    }

    private bool _soundLog;

    private void LogSounds(ClassicSoundMixer mixer)
    {
        _soundLog = true;
        mixer.Started += (request, sample) => GD.Print("DK2.Godot front-end sound: frame ", Engine.GetProcessFrames(), " ms ",
            mixer.Now, " ", request.Category.Name, " 0x", request.Id.Value.ToString("X", CultureInfo.InvariantCulture), " -> ",
            sample.Archive, ":", sample.Entry.Value);
    }

    /// <summary>The hi-score table; one that cannot be read for an IO reason warns and shows empty rows.</summary>
    private static IReadOnlyList<HiScoreEntry> LoadHiScores(HiScoreStore store)
    {
        try { return store.Load(path => GD.PushWarning("DK2.Godot: unreadable hi-score table " + path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PushWarning($"DK2.Godot: cannot read the hi-score table ({ex.Message}); showing empty rows.");
            return HiScoreTable.Empty;
        }
    }

    public override void _Input(InputEvent @event)
    {
        NoteKeyPress(@event);
        if (@event is InputEventKey key) _keyboard.Feed(key);
        // Only the wheel comes from events; the position is polled each frame, as Unity reads Input.mousePosition.
        if (@event is InputEventMouseButton button)
        {
            if (!button.Pressed) return;
            if (button.ButtonIndex == MouseButton.WheelUp) _wheel++;
            else if (button.ButtonIndex == MouseButton.WheelDown) _wheel--;
        }
    }

    public override void _Process(double delta)
    {
        if (_hud == null || _session == null || _canvas == null) return;
        InjectKeys();
        // Engine time rather than the wall clock, so a fixed-fps Movie Maker capture is repeatable.
        _elapsed += delta;
        long milliseconds = (long)(_elapsed * 1000);
        bool primaryHeld;
        (_pointer, primaryHeld) = SamplePointer();
        // DungeonRunner.Update's first step, the front end's too.
        _mixer?.PlayDecoded(milliseconds);
        _mixer?.UpdateSpeech(milliseconds);
        // The startup, a won map's cutscene, or a movie holds the screen; the menu runs once they are over.
        if (UpdateMovies()) { _wheel = 0; _keyboard.EndFrame(); return; }
        _session.Update(FrontEndTime);
        TypeHiScoreName();
        Vector2 size = GetViewport().GetVisibleRect().Size;
        int width = (int)size.X, height = (int)size.Y;
        bool sized = width > 0 && height > 0;
        _hud.Sync();
        FrontEndPointer? pointer = null;
        if (sized)
        {
            FrontEndPoint at = FrontEndBox.ToUnits(_pointer.X, _pointer.Y, width, height);
            pointer = new FrontEndPointer(at.X, at.Y, primaryHeld);
        }
        _hud.Advance(pointer, sized ? _wheel : 0, _elapsed);
        _wheel = 0;
        // DungeonRunner.UpdateFrontEnd: the menu's sounds, the station (which may leave for a level), then the map's sounds.
        if (_mixer != null) _mixer.Play(_hud.Model.Sounds); else _hud.Model.Sounds.Clear();
        bool inside = sized && _pointer.X >= 0 && _pointer.Y >= 0 && _pointer.X < width && _pointer.Y < height;
        if (UpdateStation(_pointer, primaryHeld, inside, milliseconds)) return;
        if (_campaign != null)
        {
            if (_mixer != null) _mixer.Play(_campaign.Sounds); else _campaign.Sounds.Clear();
        }
        _hud.Sync();
        if (!sized) return;
        _frame.Clear();
        _hud.Build(_frame);
        float pixelsPerUnit = FrontEndBox.Scale(width, height);
        (float left, float top) = FrontEndBox.Origin(width, height);
        var origin = new Vector2(MathF.Floor(left + .5f), MathF.Floor(top + .5f));
        _canvas.Present(_frame, origin, 1f / FrontEndBox.VirtualPerUnit, pixelsPerUnit);
    }

    /// <summary>The HUD scale the window fits for a requested percent; read only by the graphics settings page.</summary>
    private float HudFit(int percent)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        return new HudScaleLayout((int)size.X, (int)size.Y, percent).EffectivePercent;
    }

    private static void PageChanged(FrontEndPage page) =>
        GD.Print("DK2.Godot front end: page ", page.ToString(), " at process frame ", Engine.GetProcessFrames());

    private void Quit() => GetTree().Quit();

    /// <summary>A profile the map changed re-reads the gallery's unlocks, as Unity's <c>ProfileChanged</c> handler does.</summary>
    private void RefreshMovies(CampaignProfile profile) => _hud?.Model.Movies?.Refresh(profile);

    public override void _ExitTree()
    {
        if (_session != null) _session.PageChanged -= PageChanged;
        if (_profiles != null) _profiles.Changed -= RefreshMovies;
        CloseStation();
        CloseMovies();
        _hud?.Dispose(); _hud = null;
        _mixer?.Dispose(); _mixer = null;
        _campaign?.Dispose(); _campaign = null; _profiles = null;
        _assets?.Dispose(); _assets = null;
        _session = null; _canvas = null;
    }
}
