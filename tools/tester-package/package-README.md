DUNGEON KEEPER 2 REMAKE - PERFORMANCE CAPTURE
=============================================

Thank you for testing. This package records how smoothly one level runs on
your machine and zips the result for the developers. It changes nothing on
your PC: everything it writes goes into this folder.

You need
--------
- Windows 10 or 11, 64-bit.
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

5. The game opens full screen at 1920x1080 and plays its opening camera path
   on its own. Do not touch the keyboard or mouse for the 90 seconds; the
   script closes the game when it is done.
6. The last line names a zip file under captures\. Send that file.

What is in the zip
------------------
- recorder\frames.csv, metadata.txt: one line per frame (frame time, what the
  frame spent on, the camera), written by the game itself, and what hardware
  and settings Unity saw.
- presentmon.csv: the frame-to-frame intervals your display was shown, from
  Intel's PresentMon (https://github.com/GameTechDev/PresentMon, MIT license).
- machine.txt: CPU, GPU, driver version, display mode, as Windows reports them.
- Player.log: the game's own log, including any slow-frame lines.
- screenshot.png: one screenshot taken after the timed run, so the developers
  can see the game rendered on your machine.
- manifest.txt: which build this is.

Nothing in the zip identifies you beyond the hardware names and the Windows
version. Your install path is in player-arguments.txt; delete that file if
you would rather not share it.

Options
-------
    -Seconds 180      a longer run
    -Level level8     another campaign level (level1 ... level20)
    -Windowed         a 1920x1080 window instead of full screen
    -NoPresentMon     skip the frame trace (no administrator needed)

If the game does not start, Player.log in the capture folder says why; the
usual reason is the -Install path.
