# Integrated completion flow probe

Run only after the integrated Release assemblies and Unity adapter are ready:

```powershell
./tools/classic-unity-probe/run-completion-flow.ps1 -EditorGui
```

The launcher uses the shared runtime-copy helper, including the ninth DLL,
NLayer. It creates a short, marked isolated Unity project, private profile,
settings, high-score table and save directory beneath this checkout's `build/`.
The original install is read-only. The owner's Unity project is never opened.
After a failed attempt, `-ScratchProject <marked-project-path>` reuses only that
project's import cache; state and evidence directories remain fresh each time.
`-EditorGui` omits batch mode if audio or GameView repaint behavior requires an
editor UI; the separate editor process is still launched hidden.

Evidence goes to a unique add-only directory beneath
`D:/DK2Evidence/runs/unity-input-2026-10-29-completion/`. Every report, decoded
uploaded movie-frame image and metadata file uses `CreateNew`. Frame/tick
journal writers also use `CreateNew`; each real runner creates its own session.
No audio files or decoder caches are written. Failed projects and evidence stay
in place. The final manifest fingerprints all nine DLLs, source/shader inputs
and produced evidence. No cleanup is performed.
The launcher generates a small runtime GUI witness in the scratch project's
ordinary Assets folder; the editor probe assigns its callback after Play starts.
An Editor-assembly MonoBehaviour is not used as a runtime repaint witness.
Unexpected runtime error/exception logs fail the probe even if controller gates
complete. Only the known indexing ArgumentOutOfRange exception whose stack is
explicitly in UnityEditor.Search.SearchInit.IndexationOnStartup is counted separately. Render exceptions are
not evidence of a successful visible page.

The probe checks a real10-second title hold, startup movies, menu PCM/volume,
movie gallery/return and strict alternating attract timing, controller-driven
new campaign and Level1 launch, opening speech/subtitle flag, live source-volume
changes, frozen save/load hashes, comparative live GameSpeed ticks, synthetic
loss and win return paths, actual reward audio/tablet waits, post-win title and
movie, statistics/name entry, Level2 unlock/launch, restart, abandon to map37,
quit cancellation and gallery unlock/reset consumers. Actual `Time.frameCount`
gates progress; movies must upload at least two decoded frames with an audio clip
and an observed GameView repaint before a labelled private-Finish skip.

Reports distinguish runtime observations from fixtures. Initial storage is blank
to avoid importing the owner's registry progress/high scores. Model actions,
monotonic front-end clock jumps, the explicit reward-view start and forced
VerdictRules outcomes are labelled; profile progress is written only by real
campaign consumers. Battle music is a labelled MusicDirector request, not a
played fight. The frame journal records actual host input; the companion event
reports identify controller API calls that are not physical keyboard/mouse input.

Clip PCM and source volumes establish data delivery, not human hearing. Device
RMS is recorded as an observation or pending if silent. Owner hearing, physical
input, matched-native gameplay and fullscreen acceptance remain pending even
when every scheduled automated check passes. HUD comparisons at both sizes are
the separate E5 capture set. This flow currently targets1024×768.
