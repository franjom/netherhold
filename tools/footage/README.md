# Gameplay footage tools

Footage of human play feeds strategy notes for the campaign bot (stages 3 and 8 of the
[agentic campaign playtesting design](../../docs/plans/2026-11-07-agentic-campaign-playtesting-design.md)).
The purpose is a more capable, more challenging bot, not training an AI model on anyone's
videos. It shows how people play. It is not evidence of how DK2 behaves and never changes a simulation
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

- **HARD RULE: never name a footage author.** No creator, streamer or channel name or handle
  appears in the repository, a commit message, a KB record, a note, a report or a conversation
  summary. Refer to a source only by its local number (`S1`, `S2`, ...). Only the owner keeps the
  mapping from number to channel and video, outside the clone; agents never write it down.
- Metadata and transcripts are fine. Downloading a video or cutting frames from it needs the
  creator's permission, recorded here with its date and scope before the first download.
- Footage, frames and transcripts stay outside the clone and outside `F:\DK2Evidence`, under
  `F:\DK2Footage`. Delete video and frames once a stream's notes are written; keep the notes.
- Every note cites its source number and timestamp, never a video id or URL. A strategy from footage is kept only if
  self-play shows it helps.
- Read the transcript first to find the moments worth frames; a 3-hour stream at one frame
  every 5 s is about 2,000 images, too many to read.

## Sources

| Source | Videos (2026-11-14) | Permission |
|---|---|---|
| S1 | 925 | Not asked as of 2026-11-14 |

## Turning a recording into a question list

The owner records DK2 playing, an agent analyses the footage, and the resulting
note is read back into the project. The first run reversed behaviour this project
had shipped for three weeks (Q56); every test agreed with the wrong rule, so the
running game is the only oracle for that class of defect.

### What makes the output useful

1. **CONFIRMED observations** — what was seen, with timings, separated from any
   interpretation. *"Waited ~30 seconds at exactly 4 Imps; the replacement
   appeared ~1.3–1.5 seconds after the sacrifice; subsequent ones ~5 seconds
   apart."*
2. **An explicit CONTRADICTION against our code**, naming the method and quoting
   the line. *"`ImpSupplySystem.TopUp` sets `FreeImpCountdownTicks = 0` … that
   cannot reproduce the observed first replacement."* The highest value per word,
   and the part a model that only reads the repo cannot produce.
3. **A confidence ladder over the inferences**, on this project's own rungs.
   Keeping the unknowns *labelled* rather than smoothed over is what made them
   safe to act on.
4. **Numbered questions addressed to the decompiler**, ordered, with a closing
   sentence naming which one mattered most.

### The rule for reading it back

**Its observations are oracle. Its inferences are reasoning about our repo.** Act
on a CONFIRMED observation the way you would act on the owner speaking; check an
inference before building on it. In the 2026-09-05 document one inference was
wrong in a harmless direction — `TimeBetweenReattemptingSleepSeconds` was offered
as a possible retry interval, and it is read by **zero instructions in the whole
executable**.

A remembered observation that contradicts a recording is also a finding rather
than noise: Q150 was settled without a second recording because the owner
corrected the first one from memory (four creature types *always* bed down
first), and the decompiler did the rest.

### The prompt

````text
You are analysing recordings of Dungeon Keeper 2 (1999, v1.70) for a project
that reimplements it in C# as a behaviourally faithful clean-room
reimplementation. The original game is the oracle: the goal is not working code,
it is code that behaves like DK2.

You will be given one or more screen recordings, and read access to the repo.

Produce a document with these four parts, in this order:

1. CONFIRMED OBSERVATIONS. Only what is visible. Give timings as measured, with
   the uncertainty ("approximately 1.3-1.5 seconds", not "1.4 seconds"). Say
   what was on screen, not what it means. Include negative observations - what
   did NOT happen is evidence.

2. CONTRADICTIONS WITH THE IMPLEMENTATION. For each observation, find the code
   that claims to implement it and say whether it can produce what was seen.
   Name the file and method and quote the line. If it cannot, say so plainly.
   THIS IS THE MOST VALUABLE SECTION - a contradiction here is worth more than
   ten observations, because it is the only thing that catches a defect the
   test suite agrees with.

3. WHAT IS ESTABLISHED, on this project's own confidence ladder:
   CONFIRMED (seen), STRONGLY INFERRED (the only reading that fits),
   HYPOTHESIS (fits, others also fit), UNKNOWN (say so; do not smooth it over).
   An UNKNOWN left labelled is useful. An UNKNOWN dressed as an inference is a
   future defect.

4. QUESTIONS FOR THE DECOMPILER, numbered, ordered by what they unblock, and
   phrased so someone with Ghidra open can answer them one at a time. End with
   one sentence naming which question matters most and why.

Write the document as a `docs/research/` note that follows AGENTS.md (one fact,
one home; every claim on the confidence ladder; the evidence named), and update
the open-question rows it answers. DO NOT commit. DO NOT open a pull request.
Do not invent DK2 behaviour: if the footage does not show it, it is UNKNOWN.
````
