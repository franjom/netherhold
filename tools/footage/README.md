# Gameplay footage tools

Footage of human play feeds strategy notes for the campaign bot (stages 3 and 8 of the
[agentic campaign playtesting design](../../docs/plans/2026-11-07-agentic-campaign-playtesting-design.md)).
It shows how people play. It is not evidence of how DK2 behaves and never changes a simulation
rule; that still comes from the binary and native captures.

## Set up on the owner's machine (2026-11-14)

| Tool | Version | Where | Use |
|---|---|---|---|
| YouTube Data API v3 key | - | User environment variable `YOUTUBE_API_KEY` | Channel and video listings, titles, dates, durations, descriptions |
| `yt-dlp` | 2026.10.26 | `uv tool`, `~/.local/bin/yt-dlp` | Subtitles and transcripts; video only with permission (below) |
| `ffmpeg` / `ffprobe` | 9.0.2 full build | winget package `Gyan.FFmpeg` | Frames from a permitted video, scene-change selection |

Checked 2026-11-14: the key answers a `channels` lookup and both tools run.

- **The key is a secret.** Read it from the environment; never print, log, commit or paste it
  into a conversation. Check it is present by its length, not its value.
- A session started before the variable or the winget install sees neither. Restart the
  editor, or read the key with
  `[Environment]::GetEnvironmentVariable("YOUTUBE_API_KEY","User")` inside the command that
  uses it.
- Restrict the key to YouTube Data API v3 in Google Cloud.

## Rules

- Metadata and transcripts are fine. Downloading a video or cutting frames from it needs the
  creator's permission, recorded here with its date and scope before the first download.
- Footage, frames and transcripts stay outside the clone and outside `F:\DK2Evidence`, under
  `F:\DK2Footage`. Delete video and frames once a stream's notes are written; keep the notes.
- Every note cites the video id and timestamp. A strategy from footage is kept only if
  self-play shows it helps.
- Read the transcript first to find the moments worth frames; a 3-hour stream at one frame
  every 5 s is about 2,000 images, too many to read.

## Sources

| Source | Videos (2026-11-14) | Permission |
|---|---|---|
| S1 | 925 | Not asked as of 2026-11-14 |
