DUNGEON KEEPER 2 REMAKE - PERFORMANCE CAPTURE
=============================================

Thank you for testing. This package records how smoothly one level runs on
your machine and zips the result for the developers. It changes nothing on
your PC: everything it writes goes into this folder.

You need
--------
- Windows 10 or 11, 64-bit, with a graphics card that runs Vulkan or
  Direct3D 12.
- Your own copy of Dungeon Keeper 2 installed (GOG, EA, or the original CDs).
  The game data is read from it; nothing is copied or modified.
- About 3 minutes.

Steps
-----
1. Unzip this package somewhere with a few hundred MB free, for example
   C:\dk2-tester. Keep the files together.
2. Close other games and anything heavy (browsers with video, downloads,
   recording software), so they do not land in the numbers.
3. Open PowerShell as administrator (Start, type PowerShell, right-click,
   "Run as administrator"). Administrator is needed only for the frame trace;
   without it the run still works but records less.
4. Run, with your own install path:

       cd C:\dk2-tester
       powershell -ExecutionPolicy Bypass -File capture.ps1 -Install "C:\GOG Games\Dungeon Keeper 2"

5. The game opens full screen at your desktop's resolution and plays its
   opening camera path on its own. Do not touch the keyboard or mouse for the
   90 seconds; the script closes the game when it is done. If your desktop is
   not 1920x1080, add -Windowed (see Options) so the run is measured at
   1920x1080.
6. The last line names a zip file under captures\. Send that file.

What is in the zip
------------------
- recorder\frames.csv, metadata.txt: one line per frame (frame time, what the
  frame spent on, the camera), written by the game itself, and what hardware
  and settings the game saw.
- perf-report.json: the game's own summary of the run (frame and GPU times).
- presentmon.csv: the frame-to-frame intervals your display was shown, from
  Intel's PresentMon (https://github.com/GameTechDev/PresentMon, MIT license).
- machine.txt: CPU, GPU, driver version, display mode, as Windows reports them.
- godot.log: the game's own log.
- screenshot.png: one screenshot taken after the timed run (the screen, or
  only the game's window with -Windowed), so the developers can see the game
  rendered on your machine.
- manifest.txt: which build this is.
- player-files\: the settings and profile files this run used, kept apart from
  any of your own.

Nothing in the zip identifies you beyond the hardware names, the Windows
version and two folder paths. Your install path is in
player-arguments.txt and perf-report.json, and the folder you unzipped this
package to is in those and in godot.log; delete player-arguments.txt if you
would rather not share it.

Options
-------
    -Seconds 180      a longer run
    -Level Level8     another campaign level (Level1 ... Level20)
    -Windowed         a 1920x1080 window instead of full screen
    -NoPresentMon     skip the frame trace (no administrator needed)
    -FrameCap 120     cap the frame rate (default -1: uncapped)
    -VSync 1          wait for the display (default 0: off)

If the game does not start, godot.log in the capture folder says why; the
usual reason is the -Install path.

The game is free software under the GNU GPL version 3 (LICENSE.txt); the
software it includes is listed in THIRD-PARTY-NOTICES.txt.
