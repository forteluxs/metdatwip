# metdatwip

Privacy-first **metadata viewer & scrubber** for **Windows 10/11 and macOS**. Inspect exactly what personal information is hiding inside your files — GPS coordinates, camera serial numbers, author names, edit history, software fingerprints — then strip it before you share. Everything runs locally. No cloud, no telemetry.

> **Status:** 🚀 Active Development — see [PLAN.md](PLAN.md) and the [issue backlog](https://github.com/forteluxs/metdatwip/issues).

---

## Overview

Every photo, PDF, Office document, audio track, and video clip you share carries invisible metadata. A vacation photo can leak your home GPS coordinates. A "final" PDF can reveal the author, the software used, and revision timestamps. A Word doc can carry tracked-change history and comments. An MP4 or MP3 file can expose encoder IDs, hardware tags, and creator info.

**metdatwip** gives you a clear, honest view of that hidden data and a one-click way to remove it:

- **Inspect** — drag in a file or folder and see a categorized, human-readable breakdown of every metadata field, with sensitive fields (GPS, PII, email, phone, IP, device GUIDs) automatically flagged with smart regex classifiers.
- **Scrub** — remove all metadata, or keep a profile (e.g., *Strip All*, *Keep Color Profile*, *Keep Orientation*, or *Keep ICC & Orientation*).
- **Edit & Randomize** — spoof realistic dummy metadata or edit specific properties directly.
- **Batch** — clean an entire folder of files in one pass, with safe in-place or separate output.
- **Verify** — re-scan cleaned files to prove the metadata is actually gone.

## Motivation

Most people have no idea how much metadata they leak. The tools that exist are either buried in command-line utilities (`exiftool`), OS-specific, or upload your files to a website — which defeats the entire point of a *privacy* tool. metdatwip is a friendly desktop app that keeps your files on your machine.

- **Privacy-first:** all processing is local by default. A file never leaves your computer.
- **Cross-platform:** one consistent experience on Windows, macOS, and Linux.
- **Honest:** it shows you what's there before and after, so you can trust it worked.

## Supported formats

| Category | Formats | Features |
|----------|---------|----------|
| **Images** | JPEG, PNG, TIFF, HEIC/HEIF, WebP | EXIF, GPS, XMP, IPTC, ICC Profiles |
| **Documents** | PDF | Document Info dictionary (`/Info`) + XMP Stream (`/Metadata`) |
| **Office (OOXML)** | DOCX, XLSX, PPTX | Core & Extended App properties, Custom metadata |
| **Audio** | MP3, WAV | ID3v2.3/ID3v1 tags, RIFF `LIST INFO` chunks |
| **Video** | MP4, MOV, M4V, MKV, WebM | ISO QuickTime atom metadata (`moov/udta/ilst`), Matroska tags |

## How to use

### Windows 10/11 quickstart

1. Download the latest `metdatwip-win-x64.zip` from [Releases](https://github.com/rwrife/metdatwip/releases) (once published) and unzip, **or** build from source (see below).
2. Run `Metdatwip.exe`.
3. Drag files or a folder onto the window.
4. Review the flagged metadata, pick a scrub profile (e.g., *Strip All* or *Keep color profile*), and click **Scrub**.
5. Cleaned copies are written to an output folder (originals untouched by default).

### macOS quickstart

1. Download `Metdatwip-macos.dmg` from [Releases](https://github.com/rwrife/metdatwip/releases) (once published), open it, and drag **Metdatwip** to Applications, **or** build from source.
2. First launch: right-click → **Open** (unsigned build) to bypass Gatekeeper.
3. Drag files or a folder onto the window and follow the same inspect → scrub flow.

### Build from source (both platforms)

```bash
# Requires .NET 8 SDK
git clone https://github.com/rwrife/metdatwip.git
cd metdatwip
dotnet build
dotnet run --project src/Metdatwip.App
```

### Headless CLI (optional)

For scripting and CI, a small CLI mirrors the core engine:

```bash
# Inspect (prints a metadata report as text/JSON)
metdatwip inspect ./photo.jpg
metdatwip inspect ./photo.jpg --json

# Scrub a single file (writes photo.cleaned.jpg by default)
metdatwip scrub ./photo.jpg

# Batch scrub a folder with a dry-run first
metdatwip scrub ./Exports --recursive --dry-run
metdatwip scrub ./Exports --recursive --out ./Exports-clean

# Keep a whitelist of fields
metdatwip scrub ./photo.jpg --keep orientation,icc-profile
```

## Example workflow

1. You're about to post 40 vacation photos to a public gallery.
2. Drop the folder into metdatwip.
3. It flags that **38 of 40** contain GPS coordinates and camera serial numbers.
4. You pick the **Strip All (keep orientation)** profile and run a dry-run — metdatwip shows exactly which fields will be removed.
5. Click **Scrub**. Cleaned copies land in `Vacation-clean/`.
6. metdatwip re-scans them and confirms: **0 sensitive fields remaining**. Safe to post.

## Local-AI integration (optional)

metdatwip works fully without any AI. When you opt in, it can connect to a **local** small model runtime (Ollama or any llama.cpp / OpenAI-compatible endpoint on `localhost`) to add smart assists:

- **Sensitive-data detection** — scan free-text metadata fields (titles, comments, keywords, author notes) and flag things that look like real names, emails, phone numbers, or addresses that a plain field-name check would miss.
- **Plain-language summaries** — "This PDF reveals it was authored by *J. Smith* using *Acme Suite 12* and last edited on your machine."

Design guarantees:
- **Off by default.** Core inspect/scrub never calls a model.
- **Local only.** metdatwip only talks to a `localhost` endpoint you configure; nothing is sent to the cloud.
- **Metadata only.** Only extracted metadata text is sent to the local model — never the image pixels or document body (unless you explicitly enable a vision model for image content).
- **Graceful fallback.** If no runtime is reachable, metdatwip silently uses rule-based detection.

Suggested tiny models: Llama 3.2 (1B/3B), Qwen2.5 (0.5B–3B), Phi-3-mini, or a MiniCPM-family model for vision.

## Current status / milestones

- [ ] M1 — Core metadata read engine (images: EXIF/GPS/XMP/IPTC)
- [ ] M2 — Scrub engine + safe output + verify re-scan
- [ ] M3 — PDF and Office (OOXML) metadata support
- [ ] M4 — Desktop UI (drag-drop, flagged fields, scrub profiles, batch)
- [ ] M5 — Optional local-AI sensitive-data detection
- [ ] M6 — Packaging & CI (Windows zip/MSIX, macOS .app/.dmg)

See [PLAN.md](PLAN.md) for the full plan.

## License

MIT (planned).
