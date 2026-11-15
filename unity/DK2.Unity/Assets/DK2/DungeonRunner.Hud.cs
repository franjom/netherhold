// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.Collections.Generic;
using System.IO;
using DK2.Art;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Settings;
using DK2.Data.Text;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using UnityEngine;

namespace DK2.Unity
{
    public sealed partial class DungeonRunner : IHudHoverSources
    {
        private readonly ClassicHudControls _hudControls = new ClassicHudControls();
        private BottomPanelView _bottomView;
        private ClassicBottomPanel _bottomPanel;
        private CreatureOverview _overviewView;
        private ClassicCreatureOverview _overviewPanel;
        private RoomNavigation _roomNavigation;
        private ThingNavigation _doorNavigation, _trapNavigation;
        private readonly System.Collections.Generic.List<ThingNavigationEntry> _thingEntries = new();
        private ClassicOptionsPanel _optionsPanel;
        private ClassicFullMap _fullMap;
        private BottomPanelChoiceKey? _panelChoice;
        private bool _queryTool;
        private ClassicHudAction _selectedCorner;
        private KeeperResourceSnapshot _resources;
        private IClassicCreatureNames _identityNames;
        private Dk2StringTable _hudStrings;
        private HudHoverText _hudHover;
        private readonly HandCursorFeedback _cursorFeedback = new HandCursorFeedback();
        // Where the hand points in the world (interface +0x1078/+0x107C): the last pick, kept while the pointer is over the HUD.
        private FpPosition2? _pointerWorld;
        private CreatureQueryView _queryView;
        private ClassicCreatureQueryPanel _queryPanel;
        private StatusFlowers _statusFlowers;
        private ClassicStatusFlowers _statusFlowerArt;
        private TransparentTerrain _statusFlowerBlended;
        private CreatureQueryTarget _hoveredCreature;
        private InGameOptionsController _gameOptions;
        private BattleNavigation _battleNavigation;
        private ClassicGameOptionsPanel _gameOptionsPanel;
        private MessageTabsView _messageView;
        private ClassicMessageTabs _messageTabs;
        private ClassicBattleTab _battleTab;
        private readonly BattleFlyIn _battleFlyIn = new BattleFlyIn();
        private readonly System.Random _flyInRandom = new System.Random(1);
        private int _flyInEntries;
        private bool _tabletArtTried;

        private HudVisibility CurrentHud => _hudWarmFrames > 0
            ? new HudVisibility(ShowNormalHud: true, AllowOrdinaryInput: false)
            : PlayedHud;

        /// <summary>The visibility the game is actually in: what the message tabs slide on and the
        /// sounds play for, whatever the warm frames below draw.</summary>
        private HudVisibility PlayedHud => _hudControls.Visibility(_scriptedView.HudFor(cameraPreset));

        // The HUD's first draw, and the camera's first frame without the letterbox, build pipelines
        // and targets on the render thread: 74 ms where the fly-by ends. The first frames of play draw
        // both instead, the panels at zero opacity, while the first frame is loading art anyway.
        private int _hudWarmFrames;
        private UnityEngine.UIElements.UIDocument[] _warmDocuments;

        private void BeginHudWarmUp(int frames)
        {
            _hudWarmFrames = frames;
            _warmDocuments = FindObjectsByType<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (UnityEngine.UIElements.UIDocument document in _warmDocuments)
                if (document.rootVisualElement != null) document.rootVisualElement.style.opacity = 0;
        }

        private void EndHudWarmFrame()
        {
            if (_hudWarmFrames == 0 || --_hudWarmFrames > 0) return;
            foreach (UnityEngine.UIElements.UIDocument document in _warmDocuments)
                if (document != null && document.rootVisualElement != null)
                    document.rootVisualElement.style.opacity = UnityEngine.UIElements.StyleKeyword.Null;
            _warmDocuments = null;
        }
        private bool HudModal => _hudControls.Modal;

        private void InitializeHudControls(string install, GameDatabase database, bool campaignLevel)
        {
            var bottomAssets = new InstallClassicBottomPanelAssets(_hudSources, database);
            _bottomView = new BottomPanelView(database, new PlayerId((byte)viewingPlayerId), bottomAssets.Name, bottomAssets.Order, campaignLevel);
            _bottomPanel = new ClassicBottomPanel(transform, bottomAssets, _bottomView);
            // The cursor's spell icon is the panel's own resource (0x0040D310 reads the same art).
            _placementOverlay.SpellIconName = id => bottomAssets.Icon(new BottomPanelChoiceKey(BottomPanelChoiceKind.Spell, id));
            // The Hand's held-item icons (0x0040D450): a creature's Icon1Resource, an object's GuiIconResource.
            Dk2ArtCatalogue heldArt = _hudSources.Catalogue;
            _placementOverlay.CreatureIconName = id => heldArt.CreatureIcons.TryGetValue(id, out Dk2ArtResource icon) ? icon.Name : null;
            _placementOverlay.ObjectIconName = id => heldArt.Objects.TryGetValue(id, out IReadOnlyList<Dk2ArtResource> art) && art.Count > 1 ? art[1].Name : null;
            _bottomPanel.OpenAtLevelStart();
            _bottomPanel.Selected += SelectPanelChoice;
            _bottomPanel.SpecialChosen += ActivateStoredSpecial;
            _roomNavigation = new RoomNavigation(new PlayerId((byte)viewingPlayerId));
            _bottomPanel.NavigateRoom += NavigateRoom;
            _doorNavigation = new ThingNavigation(new PlayerId((byte)viewingPlayerId));
            _trapNavigation = new ThingNavigation(new PlayerId((byte)viewingPlayerId));
            _bottomPanel.NavigateDoor += NavigateDoor;
            _bottomPanel.NavigateTrap += NavigateTrap;
            _bottomPanel.GuiButton += PostGuiButton;
            _bottomPanel.CollapsedChanged += collapsed =>
            {
                if (_classicCamera == null) return;
                _classicCamera.ProjectionCentre = ClassicCamera.ProjectionCentreFromTop(
                    collapsed ? ClassicCamera.CollapsedPanelTop : ClassicCamera.ExpandedPanelTop);
                if (!frameWholeMap && !_scriptedView.PathPlaying) ApplyClassicCamera();
            };
            _startup.Step("hud.bottom");
            var overviewAssets = new InstallCreatureOverviewAssets(install, database, bottomAssets);
            _overviewView = new CreatureOverview(database, new PlayerId((byte)viewingPlayerId), overviewAssets.Name, overviewAssets.Order);
            _overviewPanel = new ClassicCreatureOverview(transform, overviewAssets, _overviewView);
            _startup.Step("hud.overview");
            _optionsPanel = new ClassicOptionsPanel(transform, new InstallClassicOptionsAssets(_hudSources));
            _optionsPanel.SetSupported(ClassicOptionsAction.Save, true);
            _optionsPanel.SetSupported(ClassicOptionsAction.Load, true);
            _optionsPanel.SetSupported(ClassicOptionsAction.GameOptions, true);
            _gameOptions = new InGameOptionsController(_hudControls, _optionsPages);
            _battleNavigation = new BattleNavigation(new PlayerId((byte)viewingPlayerId));
            _gameOptionsPanel = new ClassicGameOptionsPanel(transform, _hudSources, _gameOptions);
            InitializeSaves(install);
            _startup.Step("hud.options");
            _fullMap = new ClassicFullMap(transform, _minimap);
            _fullMap.SetLegendAssets(new InstallClassicOptionsAssets(_hudSources),
                PlayerNames.From(campaignLevel ? GameDatabaseLoader.LoadCampaignPlayers(install, levelName) : null),
                new InstallClassicResourceAssets(_hudSources));
            _startup.Step("hud.fullmap");
            _identityNames = new InstallClassicCreatureNames(install);
            _hudStrings = _hudSources.Strings;
            _hudHover = new HudHoverText(_hudStrings.Resolve);
            _queryView = new CreatureQueryView(database, _hudStrings.Resolve, new PlayerId((byte)viewingPlayerId), _identityNames);
            _startup.Step("hud.names");
            _queryPanel = new ClassicCreatureQueryPanel(transform, new InstallClassicOptionsAssets(_hudSources), _hudSources.Texture);
            _startup.Step("hud.query");
            _statusFlowers = new StatusFlowers(database, new PlayerId((byte)viewingPlayerId));
            _statusFlowerArt = new ClassicStatusFlowers(new InstallClassicResourceAssets(_hudSources), _installArt, _classic);
            _startup.Step("hud.flowers");
            // Reuse the model already observing events before the initial simulation tick.
            var messageText = new MessageTextTables(_hudSources, database.Level?.TextTableId ?? 0, Debug.LogWarning);
            _messageTabs = new ClassicMessageTabs(transform, bottomAssets, new InstallClassicOptionsAssets(_hudSources), _messageView,
                messageText.Resolve, messageText.Briefing,
                message => _messageView.LocationOncePerTick(World, message),
                // 0x00415B30 submits game action 4 with the point's tile (x >> 12, y >> 12): the focus
                // glides to that tile's centre (0x00442650), as room navigation does. x and y are tiles;
                // DK2 first drops an off-map point (World_ContainsPosition), ours clamps in StartGlide.
                (x, y) => StartGlide(new TilePos((int)Math.Floor(x), (int)Math.Floor(y))),
                PlayerNames.From(campaignLevel ? GameDatabaseLoader.LoadCampaignPlayers(install, levelName) : null));
            _startup.Step("hud.messages");
            _battleTab = new ClassicBattleTab(transform, bottomAssets);
            _battleTab.Clicked += BattleTabClicked;
        }

        /// <summary>Captures the flowers from the settled world (before the worker tick starts) and
        /// draws them over the posed creatures; hidden with the HUD, except on a camera path, where
        /// DK2 still draws them (Level 1's Lord wears his star through the flyover, SHOT0075).</summary>
        private void DrawStatusFlowers()
        {
            if (_statusFlowers == null || _statusFlowerArt == null) return;
            WorldState world = World;
            bool possessing = world.Player(new PlayerId((byte)viewingPlayerId)).PossessedCreatureId != 0;
            // Unity's null: a rebuilt scene destroyed the previous blended terrain.
            if (_statusFlowerBlended == null) _statusFlowerBlended = GetComponentInChildren<TransparentTerrain>();
            _statusFlowerArt.Draw(Camera.main, _scene, _statusFlowers.Capture(world, world.CurrentTick, _hoveredCreature,
                EffectClock?.Invoke().Milliseconds ?? (long)(Time.unscaledTimeAsDouble * 1000)),
                CurrentHud.ShowNormalHud || _scriptedView.PathPlaying, possessing, _statusFlowerBlended);
        }

        private void DisposeHudControls()
        {
            _gameOptionsPanel?.Dispose(); _overviewPanel?.Dispose(); _bottomPanel?.Dispose(); _messageTabs?.Dispose(); _battleTab?.Dispose(); _optionsPanel?.Dispose(); _fullMap?.Dispose(); _queryPanel?.Dispose();
            _statusFlowerArt?.Dispose();
            _savePanel?.Dispose();
            _hudSources?.Dispose();
        }

        private void SelectPanelChoice(BottomPanelSelection selection)
        {
            _input.CancelPointerGesture();
            _input.CancelTool();
            _panelChoice = selection.Choice;
            _queryView?.Clear();
            _queryTool = false; _selectedCorner = ClassicHudAction.None;
            tool = selection.Tool == PlayerToolMode.BuildRoom ? DungeonToolMode.BuildRoom
                : selection.Tool == PlayerToolMode.CreateImp ? DungeonToolMode.CreateImp : DungeonToolMode.Hand;
            if (selection.Tool == PlayerToolMode.BuildRoom) selectedRoomId = selection.Room;
        }

        /// <summary>A stored special's slot (<see cref="PlayerInputTranslator.ChooseStoredSpecial(PlayerId, BottomPanelChoice, BottomPanelView)"/>):
        /// action 88 is posted, or targeting began and the runner's tool returns to the hand with the panel's.</summary>
        private void ActivateStoredSpecial(BottomPanelChoice special)
        {
            // 0x00411140 ignores a special's slot while the Options menu is open (+0x2722).
            if (_hudControls.OptionsVisible) return;
            if (_input.ChooseStoredSpecial(new PlayerId((byte)viewingPlayerId), special, _bottomView) is PlayerCommand command)
            {
                _pendingCommands.Add(command);
                return;
            }
            _panelChoice = null;
            _queryTool = false; _selectedCorner = ClassicHudAction.None;
            tool = DungeonToolMode.Hand;
        }

        private void NavigateRoom(RoomTypeId type)
        {
            var rooms = RoomIndex.Build(World, _simulation.Database);
            var entries = new System.Collections.Generic.List<RoomNavigationEntry>(rooms.Count);
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomInstance room = rooms[i];
                if (room.Definition.TypeId is not RoomTypeId roomType) continue;
                TilePos identity = World.TilePosition(room.TileIndices[0]);
                entries.Add(new RoomNavigationEntry(identity, new PlayerId(room.OwnerPlayerId), roomType,
                    new TilePos(room.CentreFieldX, room.CentreFieldY)));
            }
            if (_roomNavigation.Next(entries, type) is TilePos tile) StartGlide(tile);
        }

        // 0x00416150 / 0x004162A0: the viewer's doors or traps of one kind, blueprints included,
        // newest first: Thing_LinkToOwner (0x004B4C30) links each new thing at the list's head.
        private void NavigateDoor(byte kind)
        {
            _thingEntries.Clear();
            IReadOnlyList<DoorState> doors = World.Doors;
            for (int i = doors.Count - 1; i >= 0; i--)
            {
                DoorState door = doors[i];
                _thingEntries.Add(new ThingNavigationEntry(door.Id, new PlayerId(door.PlayerId), door.DoorId, new TilePos(door.X, door.Y)));
            }
            if (_doorNavigation.Next(_thingEntries, kind) is TilePos tile) StartGlide(tile);
        }

        private void NavigateTrap(byte kind)
        {
            _thingEntries.Clear();
            IReadOnlyList<TrapState> traps = World.Traps;
            for (int i = traps.Count - 1; i >= 0; i--)
            {
                TrapState trap = traps[i];
                _thingEntries.Add(new ThingNavigationEntry(trap.Id, new PlayerId(trap.PlayerId), trap.TrapId, new TilePos(trap.X, trap.Y)));
            }
            if (_trapNavigation.Next(_thingEntries, kind) is TilePos tile) StartGlide(tile);
        }

        /// <summary>Menu 43's clicks (docs/research/battle-tab.md), as <see cref="BattleTabView.Action"/> maps
        /// them: the blades' zoom is the F key's; the alarm's walks the ringing alarms on the Workshop
        /// trap cycle's last trap.</summary>
        private void BattleTabClicked(BattleTabButton button, int mouse)
        {
            if (_hudControls.InputSuppressed || HudModal) return;
            var viewer = new PlayerId((byte)viewingPlayerId);
            switch (BattleTabView.Action(button, mouse))
            {
                case BattleTabAction.ZoomToBattle:
                    ApplyHudAction(ClassicHudAction.NavigateFight);
                    break;
                case BattleTabAction.CancelCallToArms:
                    _pendingCommands.Add(new RemoveCallToArmsCommand(viewer));
                    break;
                case BattleTabAction.GoToCallToArms:
                    if (BattleTabView.CallToArmsTile(World, viewer) is TilePos flag) StartGlide(flag);
                    break;
                case BattleTabAction.NextAlarm:
                    BattleTabView.TrapEntries(World.Traps, _simulation.Database, World.CurrentTick, _thingEntries);
                    if (_trapNavigation.NextAlarm(_thingEntries) is TilePos alarm) StartGlide(alarm);
                    break;
            }
        }

        /// <summary>The fly-in's start (<c>0x0042C0D0</c>): once for each new entry of one of the viewer's
        /// creatures into a battle with none of the viewer's, refused while one runs. Its points are in
        /// the tablet's frame, DK2 pixels on a 768-high screen (as the win screen draws it).</summary>
        private void StartBattleFlyIn()
        {
            if (_musicObserver == null || _musicObserver.ViewerBattleEntries == _flyInEntries) return;
            _flyInEntries = _musicObserver.ViewerBattleEntries;
            float unit = Screen.height / 768f;
            Vector2 target = _battleTab.BattleCentre(_bottomPanel.Collapsed);
            _battleFlyIn.Start(Time.realtimeSinceStartupAsDouble * 1000,
                BattleFlyIn.StartReferenceX / 2560f * Screen.width / unit, BattleFlyIn.StartReferenceY / 1920f * Screen.height / unit,
                target.x / unit, target.y / unit);
            _soundPlayer?.Play(_battleFlyIn.Sounds);
        }

        /// <summary>One drawn frame of the fly-in (<c>0x0042C390</c>'s tail): only while the HUD shows,
        /// the camera is not letterboxed and the game is not paused (ours pauses while the options
        /// panel is open); spawn, draw, then step, as <c>0x0042F040</c> does; its end sound when done.</summary>
        private void DrawBattleFlyIn()
        {
            if (Event.current.type != EventType.Repaint || !_battleFlyIn.Running) return;
            if (!_tabletArtTried) { _tabletArt ??= LoadTabletArt(); _tabletArtTried = true; }
            double now = Time.realtimeSinceStartupAsDouble * 1000;
            if (!_battleFlyIn.BeginFrame(now, PlayedHud.ShowNormalHud, _scriptedView.LetterboxBar > 0, _hudControls.OptionsVisible,
                    _tabletArt != null ? _tabletArt.Points : System.Array.Empty<System.Numerics.Vector3>(), _flyInRandom)) return;
            if (_tabletArt != null) DrawTablet(_tabletArt, _battleFlyIn.Tablet, BattleFlyIn.FightItem, 0f, 0f);
            _battleFlyIn.EndFrame(now);
            _soundPlayer?.Play(_battleFlyIn.Sounds);
        }

        /// <summary>Action 0x72, a GUI button (ADR-0670).</summary>
        private void PostGuiButton(GuiButtonTarget target) =>
            _pendingCommands.Add(new GuiButtonCommand(new PlayerId((byte)viewingPlayerId), target));

        private void ApplyHudAction(ClassicHudAction action)
        {
            if (action == ClassicHudAction.None || _hudControls.InputSuppressed) return;
            if (HudModal && action is ClassicHudAction.NavigateHeart or ClassicHudAction.NavigatePortal or ClassicHudAction.NavigateFight
                or ClassicHudAction.SelectQuery or ClassicHudAction.SelectSell or ClassicHudAction.ZoomIn or ClassicHudAction.ZoomOut) return;
            _input.CancelPointerGesture(); _bottomPanel?.CancelPointerGesture();
            if (action == ClassicHudAction.NavigatePortal)
            {
                NavigateRoom(RoomTypeId.Portal);
                return;
            }
            if (action == ClassicHudAction.NavigateHeart)
            {
                if (_worldView.MinimapHeartTile is TilePos heart) StartGlide(heart);
                return;
            }
            if (action == ClassicHudAction.NavigateFight)
            {
                if (_musicObserver != null && _battleNavigation.Next(_musicObserver.Battles) is TilePos battle) StartGlide(battle);
                return;
            }
            if (action == ClassicHudAction.ZoomIn || action == ClassicHudAction.ZoomOut)
            {
                PostGuiButton(GuiButtonTarget.MinimapZoom);   // the magnifier's primary or secondary click, 0x00416511/0x00416611
                _minimap.HandleZoomPress(Input.mousePosition, action == ClassicHudAction.ZoomIn, action == ClassicHudAction.ZoomOut);
                return;
            }
            if (action == ClassicHudAction.SelectQuery || action == ClassicHudAction.SelectSell)
            {
                PostGuiButton(action == ClassicHudAction.SelectQuery ? GuiButtonTarget.Query : GuiButtonTarget.Sell);
                _panelChoice = null; _selectedCorner = action;
                _input.CancelTool();
                _queryTool = action == ClassicHudAction.SelectQuery;
                tool = _queryTool ? DungeonToolMode.Hand : DungeonToolMode.SellRoom;
                _bottomView.SelectTool(ActiveTool, selectedRoomId);
                return;
            }
            _hudControls.Apply(action);
        }

        private void UpdateHudControls(bool allowOptionsKey)
        {
            HudVisibility camera = _scriptedView.HudFor(cameraPreset);
            GameBindingInput keys = ClassicKeyBindings.Read(_gameSettings);
            ClassicHudAction shortcut = keys.HudShortcut(
                _hudControls.ShortcutContext(camera, allowOptionsKey, capturingKey: _optionsPages.CapturingAction != null),
                escapePressed: Input.GetKeyDown(KeyCode.Escape));
            ApplyHudAction(shortcut);
            HudVisibility hud = CurrentHud;
            _bottomPanel?.SetVisibility(_hudControls.ModalInput(hud));
            _optionsPanel?.SetVisible(_hudControls.OptionsMenuShown(hud));
            _gameOptionsPanel?.Show(_hudControls.GameOptionsShown(hud));
            _optionsPanel?.SetPage(_hudControls.OptionsPage, _hudControls.Confirmation);
            _fullMap?.SetVisible(camera.ShowNormalHud && _hudControls.FullMapVisible);
            _queryPanel?.Draw(_queryView.Snapshot, hud, HudModal);
            _overviewPanel?.Draw(hud, HudModal ? null : _bottomPanel.PanelArea(BottomPanelCategory.Creatures), Input.mousePosition);
        }

        private bool QueueHudInput()
        {
            if (_hudControls.FullMapVisible)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    TilePos tile = _fullMap.ResolvePointer(Input.mousePosition);
                    _cameraFocus = new Vector3(Mathf.Clamp(tile.X + .5f, 0, _cameraBounds.x - 1f / Fp32.One), _cameraFocus.y,
                        Mathf.Clamp(-tile.Y - .5f, -_cameraBounds.y + 1f / Fp32.One, 0));
                    _glideMilliseconds = 0;
                    ApplyHudAction(ClassicHudAction.ToggleFullMap);
                }
                else if (Input.GetMouseButtonDown(1)) ApplyHudAction(ClassicHudAction.ToggleFullMap);
                return true;
            }
            if (_hudControls.OptionsVisible)
            {
                if (_hudControls.GameOptionsVisible) { _gameOptionsPanel?.PollKeyboard(); return true; }
                if (Input.GetMouseButtonDown(0))
                {
                    ClassicOptionsAction action = _optionsPanel.ActionAt(Input.mousePosition);
                    if (OptionsHud.RefreshesSaves(action)) _saves.Refresh();
                    ApplyHudAction(OptionsHud.HudAction(action));
                    ClassicSessionAction session = _hudControls.TakeSessionAction();
                    if (session == ClassicSessionAction.Restart)
                        _pendingSession = GameSessionRequest.Restart(GameSessionRequest.Campaign(levelName, frontEndReturnPage));
                    else if (session == ClassicSessionAction.ReturnToMenu)
                        _pendingSession = GameSessionRequest.FrontEnd(LaunchedFromFrontEnd ? FrontEndPage.CampaignMap : FrontEndPage.None);
                    else if (session == ClassicSessionAction.Quit) QuitFrontEnd();
                }
                return true;
            }
            bool primary = Input.GetMouseButtonDown(0), secondary = Input.GetMouseButtonDown(1);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (_overviewPanel.TryPress(Input.mousePosition, primary, secondary, shift, control, alt,
                out CreatureOverviewRequest request))
            {
                CreatureOverviewAction selected = _overviewView.Select(World, request.Type, request.Selection, request.Filter);
                if (selected.Kind == CreatureOverviewActionKind.Pickup)
                {
                    var command = new PickUpCreatureCommand(new PlayerId((byte)viewingPlayerId), selected.Target.Value);
                    if (!_worldView.LocalPickups.TryQueue(World, _simulation.Database, command.PlayerId,
                        new CreatureId(command.CreatureId), (long)(Time.unscaledTimeAsDouble * 1000))) return true;
                    _cursorFeedback.Play(HandCursorKind.HoldThing, (long)(Time.unscaledTimeAsDouble * 1000));
                    _pendingCommands.Add(command);
                    _worldView.QueueMinimapPickupPreview(command, World.CurrentTick.Next);
                    _overviewView.QueuePickupPreview(selected.Target, World.CurrentTick.Next);
                }
                else if (selected.Kind == CreatureOverviewActionKind.Focus)
                {
                    _cameraFocus = new Vector3(selected.Position.X.Raw / (float)Fp32.One, _cameraFocus.y,
                        -selected.Position.Y.Raw / (float)Fp32.One);
                    _glideMilliseconds = 0;
                }
                return true;
            }
            ClassicHudAction corner = _minimap.HandleCornerPress(Input.mousePosition, primary, secondary);
            if (corner != ClassicHudAction.None) { ApplyHudAction(corner); return true; }
            if (secondary && _minimap.TryNavigate(Input.mousePosition, Camera.main, _cameraFocus, out TilePos _))
            { ApplyHudAction(ClassicHudAction.ToggleFullMap); return true; }
            KeeperResourceKind resource = _resourceHud.ResourceAt(Input.mousePosition);
            if (resource == KeeperResourceKind.Gold && primary)
            {
                KeeperResourceModifiers modifiers = KeeperResourceModifiers.None;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) modifiers |= KeeperResourceModifiers.Shift;
                if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) modifiers |= KeeperResourceModifiers.Control;
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) modifiers |= KeeperResourceModifiers.Alt;
                _pendingCommands.Add(KeeperResourceInteraction.Withdraw(new PlayerId((byte)viewingPlayerId), modifiers));
                _panelChoice = null; _queryTool = false; tool = DungeonToolMode.Hand;
                _bottomView.SelectTool(PlayerToolMode.Hand);
                return true;
            }
            if (resource == KeeperResourceKind.Heart && secondary)
            { ApplyHudAction(ClassicHudAction.NavigateHeart); return true; }
            return false;
        }

        private bool QueuePanelWorldAction()
        {
            if (_queryTool)
            {
                if (_queryView.Target == CreatureQueryTarget.None && Input.GetMouseButtonDown(0) && !PointerOverHud(Input.mousePosition)
                    && ScreenToTile(Input.mousePosition) is TilePos queryTile)
                {
                    WorldHoverHit hit = ResolveWorldHover(queryTile);
                    if (hit.Kind == WorldHoverKind.Creature)
                    {
                        _queryView.Query(new CreatureQueryTarget(hit.EntityId)); _glideMilliseconds = 0;
                        if (_classicCamera != null && Camera.main != null)
                        {
                            var focus = new System.Numerics.Vector3(_cameraFocus.x, _cameraFocus.y, _cameraFocus.z);
                            var viewport = new CameraViewport(Screen.width, Camera.main.pixelWidth, Camera.main.pixelHeight);
                            Dk2Angle yaw = new Dk2Angle(unchecked((ushort)Mathf.RoundToInt(_classicCamera.Pose(focus, viewport).YawDegrees * 2048 / 360f)));
                            Dk2Angle desired = ClassicCreatureQueryRules.FollowHeading(yaw);
                            _classicCamera.Rotate(new Dk2Angle(unchecked((ushort)(desired.Value - yaw.Value))));
                        }
                    }
                }
                if (Input.GetMouseButtonDown(1))
                {
                    _queryView.Clear(); _queryTool = false;
                    _selectedCorner = ClassicHudAction.None; tool = DungeonToolMode.Hand;
                }
                return true;
            }
            if (Input.GetMouseButtonDown(1) && (tool != DungeonToolMode.Hand || _panelChoice != null))
            {
                if (_input.DragFrom(ActiveTool) != null) _input.CancelPointerGesture();
                else
                {
                    _panelChoice = null; tool = DungeonToolMode.Hand; _selectedCorner = ClassicHudAction.None;
                    _bottomView.SelectTool(PlayerToolMode.Hand);
                }
                return true;
            }
            if (_panelChoice is not BottomPanelChoiceKey choice || choice.Kind == BottomPanelChoiceKind.Room
                || choice.Kind == BottomPanelChoiceKind.Spell && choice.Value == 4) return false;
            if (!Input.GetMouseButtonDown(0) || PointerOverHud(Input.mousePosition)) return true;
            if (ScreenToTile(Input.mousePosition) is not TilePos tile) return true;
            PlayerId viewer = new PlayerId((byte)viewingPlayerId);
            // Without a Workshop DK2's hand places no door or trap (0x0040849F, 0x00408586).
            if (_bottomView.Choice(choice)?.NeedsWorkshop == true) return true;
            if (choice.Kind == BottomPanelChoiceKind.Door) _pendingCommands.Add(new PlaceDoorCommand(viewer, tile, choice.Value));
            else if (choice.Kind == BottomPanelChoiceKind.Trap) _pendingCommands.Add(new PlaceTrapCommand(viewer, tile, choice.Value));
            else
            {
                KeeperSpellDef spell = _simulation.Database.KeeperSpellById(choice.Value);
                if (spell == null) return true;
                if (KeeperSpellRules.TargetsAThing(spell.TargetSelector))
                {
                    WorldHoverHit hit = ResolveWorldHover(tile);
                    if (hit.Kind == WorldHoverKind.Creature)
                        _pendingCommands.Add(new CastSpellOnCreatureCommand(viewer, choice.Value, hit.EntityId));
                }
                else _pendingCommands.Add(new CastSpellAtPlaceCommand(viewer, choice.Value,
                    CreatureState.CentreOf(tile.X), CreatureState.CentreOf(tile.Y)));
            }
            return true;
        }

        private void DrawHudControls()
        {
            ApplyHudScale();
            _resources = _resourceView.Capture(World, displayHealth: CurrentHud.ShowNormalHud);
            _bottomView.Capture(World);
            // FLASH_BUTTON on the panel (tab-alerts.md): the target drawn, and dropped once its item is in use.
            if (_scriptedView.ActiveFlashTarget is HudFlashTarget flash
                && BottomPanelFlash.ClearedByUse(flash, ActiveTool, selectedRoomId, _panelChoice, _bottomPanel.OpenPanel))
                _scriptedView.ClearFlash(flash);
            _bottomPanel.FlashTarget = _scriptedView.ActiveFlashTarget;
            _bottomPanel.GamePaused = _hudControls.TicksPaused;
            _bottomPanel.Draw();
            _fullMap.Capture(World, _simulation.Database, new PlayerId((byte)viewingPlayerId));
            DrawSavePanel(frontEnd: false);
            _gameOptionsPanel?.Draw();
            _messageView.Capture(World, (long)(Time.unscaledTimeAsDouble * 1000), PlayedHud);
            if (_hudWarmFrames > 0)
            {
                // The warm frames draw the panels as shown; nothing about them is heard.
                _hudControls.Sounds.Clear(); _bottomPanel.Sounds.Clear(); _bottomView.Sounds.Clear();
                _messageView.Sounds.Clear(); _optionsPages?.Sounds.Clear();
            }
            else
            {
                _soundPlayer?.Play(_hudControls.Sounds);
                _soundPlayer?.Play(_bottomPanel.Sounds);
                _soundPlayer?.Play(_bottomView.Sounds);
                _soundPlayer?.Play(_messageView.Sounds);
                if (_optionsPages != null) _soundPlayer?.Play(_optionsPages.Sounds);
                _soundPlayer?.QueueSpeech(_messageView.Speech);
            }
            _messageTabs.Draw(_hudControls.ModalInput(CurrentHud), _bottomPanel.Collapsed,
                HudIndicatorSnapshot.Capture(World, _simulation.Database.Level, new PlayerId((byte)viewingPlayerId)),
                _hudControls.OptionsVisible, _hudControls.ObjectiveVisible);
            _battleTab.Draw(_hudControls.ModalInput(CurrentHud), _bottomPanel.Collapsed,
                _musicObserver == null ? default : BattleTabView.Capture(World, _simulation.Database, new PlayerId((byte)viewingPlayerId), _musicObserver.Battles),
                (long)(Time.realtimeSinceStartupAsDouble * 1000));
            StartBattleFlyIn();
            _overviewView.Capture(World);
            _overviewPanel.Draw(CurrentHud, HudModal ? null : _bottomPanel.PanelArea(BottomPanelCategory.Creatures), Input.mousePosition);
            _minimap.UpdateControls(Input.mousePosition, _selectedCorner, _hudControls.OptionsVisible);
            _optionsPanel.Draw(Input.mousePosition); _fullMap.Draw();
            _queryPanel.Draw(_queryView.Capture(World), CurrentHud, HudModal);
        }

        private bool HoldingGold()
            => _worldView.LocalPickups.HoldingGold(World, new PlayerId((byte)viewingPlayerId));

        private void FollowQueriedCreature()
        {
            if (!_queryTool || _queryView.Target == CreatureQueryTarget.None || _scriptedView.PathPlaying || HudModal) return;
            if (_scene.TryGetCreaturePosition(_queryView.Target, out Vector3 position))
            {
                _cameraFocus = new Vector3(position.x, _cameraFocus.y, position.z);
                if (_classicCamera != null && !frameWholeMap) ApplyClassicCamera();
            }
        }

        /// <summary>Which panel's text the status line shows over the HUD: <see cref="HudHoverText"/>, asking the
        /// panels below.</summary>
        private HoverTextSnapshot CaptureHudHover() => _hudHover.Capture(this, _resources);

        int IHudHoverSources.OverviewTextId() => _overviewPanel != null ? _overviewPanel.HoveredTextId(Input.mousePosition) : 0;
        CreatureOverviewType? IHudHoverSources.OverviewColumn() =>
            _overviewPanel != null ? _overviewPanel.HoveredColumnType(Input.mousePosition) : null;
        HoverTextSnapshot IHudHoverSources.CreatureTypeText(CreatureOverviewType column) =>
            _hoverText.CreatureType(_simulation.Database.CreatureById(column.Value));
        bool IHudHoverSources.OverMessageTabs() => _messageTabs != null && _messageTabs.ContainsPointer(Input.mousePosition);
        HoverTextSnapshot IHudHoverSources.MessageTabsText() => _messageTabs.HoverText;
        bool IHudHoverSources.OverBattleTab() => _battleTab != null && _battleTab.ContainsPointer(Input.mousePosition);
        HoverTextSnapshot IHudHoverSources.BattleTabText() => _battleTab.HoverText;
        bool IHudHoverSources.OverBottomPanel() => _bottomPanel.ContainsPointer(Input.mousePosition);
        HoverTextSnapshot IHudHoverSources.BottomPanelText() => _bottomPanel.HoverText;
        ClassicHudAction IHudHoverSources.MinimapControl() =>
            _minimap != null ? _minimap.HoveredControl(Input.mousePosition) : ClassicHudAction.None;
        KeeperResourceKind IHudHoverSources.Resource() => _resourceHud.ResourceAt(Input.mousePosition);
    }
}
