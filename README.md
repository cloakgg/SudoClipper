# SUDOCLIPPER

A free, fully local clipping studio. Paste a YouTube link (or drop a video file), and it finds the
moments worth sharing, cuts them, reframes them to vertical 9:16 and burns in captions.

Nothing is uploaded anywhere. Video work happens with `ffmpeg` and `yt-dlp` on your own machine, and
the model that picks your clips is either the built-in free local selector or your own API key.

## Install (one command)

Windows:

```powershell
.\install.cmd
```

macOS / Linux:

```bash
./install.sh
```

The installer builds the interface, packs the tool and installs the `sudoclipper` command globally.
It opens the app in your default browser the moment it is ready — no account, no config file to edit.

After that, `sudoclipper` from anywhere starts the studio again. Useful flags:

```bash
sudoclipper               # start and open the browser
sudoclipper --port 4500   # pick a port
sudoclipper --no-open     # do not open a browser
sudoclipper --doctor      # report the state of ffmpeg / yt-dlp
```

Requires the .NET 10 SDK (or runtime). Node.js is only needed if you want to rebuild the UI yourself.

## First run

SUDOCLIPPER needs two local binaries and downloads them once into its own folder
(`%LOCALAPPDATA%\sudoclipper\bin` on Windows, `~/.local/share/sudoclipper/bin` elsewhere):

| Tool | Why |
| --- | --- |
| `yt-dlp` | downloads the source video and its caption track |
| `ffmpeg` | cuts, reframes to 9:16, burns captions, encodes |

If they are already on your `PATH`, those copies are used instead. Progress is shown in the UI and
in Config → Diagnostics. The download resumes where it stopped if the connection drops or the app is
closed mid-way, and only one setup ever runs at a time — even with two windows open.

## The three tabs

**Clipper** — paste a link or drop a file. Choose how many clips, a length preset, and captions on
or off. A live job panel shows Download → Captions → Pick moments → Render with the worker's log.

**Memory** — every clip rendered on this machine, with thumbnails, source, model, and playback.
Search, open the folder, or delete a clip (which removes the files).

**Config** — provider and API keys, the model list, clipping defaults, output folder, diagnostics.

## Models

Pick a provider, paste a key, and the app lists every model that key can reach:

- **Local (free)** — no key. Scores transcript windows for hook language, questions, numbers and pacing.
- **OpenRouter**, **OpenAI**, **Google Gemini**, **Groq**, **DeepSeek**, **Mistral** — hosted models.
- **Ollama** — models already running on your machine, so nothing at all leaves localhost.
- **Custom endpoint** — any OpenAI-compatible base URL (LM Studio, vLLM, LiteLLM, Together…).

Keys are stored in `%LOCALAPPDATA%\sudoclipper\config.json` (plain text, local only) or read from
`SUDOCLIPPER_<PROVIDER>_KEY` environment variables. They are never sent anywhere except the provider
you selected.

## How a clip is made

1. `yt-dlp` downloads the video (≤1080p) plus its subtitle track, converted to WebVTT.
2. Captions are parsed, de-duplicated and timestamped (auto-caption roll-up handled).
3. The transcript goes to your model, which returns start/end, title, hook, score and a rationale.
   Long videos are chunked and merged by score.
4. `ffmpeg` cuts each moment, builds a 1080×1920 frame (blurred background, centred source), burns a
   styled ASS subtitle track, normalises audio and writes a faststart mp4 plus a thumbnail.
5. The clip is recorded in Memory. The working directory is cleaned up.

Exports land in `~/Videos/SUDOCLIPPER/<video-title>/` by default (change it in Config).

## Notes and limits

- Moment selection needs a transcript, so a video with no English caption track stops with a clear
  message instead of guessing. Captions are fetched in a second, best-effort pass, so a rate-limited
  subtitle request never throws away a finished video download.
- Only the first attached file is used per run; links and files are mutually exclusive in the input.
- Downloaded sources are deleted after rendering; only clips are kept.
- Everything binds to `127.0.0.1`, so the studio is not reachable from other machines.

## Development

```bash
dotnet run --project src/SudoClipper      # API on http://127.0.0.1:4319
cd web && npm install && npm run dev      # UI on http://localhost:5273 with /api proxied
cd web && npm run build                   # writes the bundle into src/SudoClipper/wwwroot
```

```
src/SudoClipper/   .NET minimal API: pipeline, LLM clients, ffmpeg orchestration
web/               React + Tailwind + Motion interface
```

MIT licensed.
"# SudoClipper" 
"# SudoClipper" 
"# SudoClipper" 
