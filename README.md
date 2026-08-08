# metawipe

Privacy-first **metadata viewer & scrubber** for **Windows 10/11 and macOS**. Inspect exactly what personal information is hiding inside your files — GPS coordinates, camera serial numbers, author names, edit history, software fingerprints — then strip it before you share. Everything runs locally. No cloud, no telemetry.

> **Status:** 🚧 Bootstrapping — see [PLAN.md](PLAN.md) and the [issue backlog](https://github.com/rwrife/metawipe/issues).

---

## Overview

Every photo, PDF, and Office document you share carries invisible metadata. A vacation photo can leak your home GPS coordinates. A "final" PDF can reveal the author, the software used, and revision timestamps. A Word doc can carry tracked-change history and comments long after you thought they were gone.

**metawipe** gives you a clear, honest view of that hidden data and a one-click way to remove it:

- **Inspect** — drag in a file and see a categorized, human-readable breakdown of every metadata field, with sensitive fields (GPS, personal names, serial numbers) flagged.
- **Scrub** — remove all metadata, or keep a whitelist (e.g., strip GPS but keep orientation and color profile).
- **Batch** — clean an entire folder of files in one pass, with a dry-run preview and safe output (never overwrites originals unless you ask).
- **Verify** — re-scan cleaned files to prove the metadata is actually gone.

## Motivation

Most people have no idea how much metadata they leak. The tools that exist are either buried in command-line utilities (`exiftool`), OS-specific, or upload your files to a website — which defeats the entire point of a *privacy* tool. metawipe is a friendly desktop app that keeps your files on your machine.

- **Privacy-first:** all processing is local by default. A file never leaves your computer.
- **Cross-platform:** one consistent experience on Windows and macOS.
- **Honest:** it shows you what's there before and after, so you can trust it worked.

## Use cases

- **Before posting photos online** — strip GPS location and camera serials from JPEGs/PNGs/HEIC.
- **Before sending a PDF** — remove author, producer, creation software, and title metadata.
- **Before sharing an Office doc** — clear author, company, tracked-change residue, and comments from `.docx`/`.xlsx`/`.pptx`.
- **Whistleblowers / journalists** — verify a leaked document carries no identifying fingerprints.
- **Bulk cleanup** — scrub a whole `Screenshots/` or `Exports/` folder before archiving or publishing.
- **Sanity check** — audit your own files to learn what your camera/apps embed.

## Supported formats (target)

| Category | Formats |
|----------|---------|
| Images | JPEG, PNG, TIFF, HEIC/HEIF, WebP (EXIF, GPS, XMP, IPTC, ICC) |
| Documents | PDF (Info dict + XMP) |
| Office (OOXML) | DOCX, XLSX, PPTX (core/app properties, custom props) |

## How to use

### Windows 10/11 quickstart

1. Download the latest `metawipe-win-x64.zip` from [Releases](https://github.com/rwrife/metawipe/releases) (once published) and unzip, **or** build from source (see below).
2. Run `Metawipe.exe`.
3. Drag files or a folder onto the window.
4. Review the flagged metadata, pick a scrub profile (e.g., *Strip All* or *Keep color profile*), and click **Scrub**.
5. Cleaned copies are written to an output folder (originals untouched by default).

### macOS quickstart

1. Download `Metawipe-macos.dmg` from [Releases](https://github.com/rwrife/metawipe/releases) (once published), open it, and drag **Metawipe** to Applications, **or** build from source.
2. First launch: right-click → **Open** (unsigned build) to bypass Gatekeeper.
3. Drag files or a folder onto the window and follow the same inspect → scrub flow.

### Build from source (both platforms)

```bash
# Requires .NET 8 SDK
git clone https://github.com/rwrife/metawipe.git
cd metawipe
dotnet build
dotnet run --project src/Metawipe.App
```

### Headless CLI (optional)

For scripting and CI, a small CLI mirrors the core engine:

```bash
# Inspect (prints a metadata report as text/JSON)
metawipe inspect ./photo.jpg
metawipe inspect ./photo.jpg --json

# Scrub a single file (writes photo.cleaned.jpg by default)
metawipe scrub ./photo.jpg

# Batch scrub a folder with a dry-run first
metawipe scrub ./Exports --recursive --dry-run
metawipe scrub ./Exports --recursive --out ./Exports-clean

# Keep a whitelist of fields
metawipe scrub ./photo.jpg --keep orientation,icc-profile
```

## Example workflow

1. You're about to post 40 vacation photos to a public gallery.
2. Drop the folder into metawipe.
3. It flags that **38 of 40** contain GPS coordinates and camera serial numbers.
4. You pick the **Strip All (keep orientation)** profile and run a dry-run — metawipe shows exactly which fields will be removed.
5. Click **Scrub**. Cleaned copies land in `Vacation-clean/`.
6. metawipe re-scans them and confirms: **0 sensitive fields remaining**. Safe to post.

## Local-AI integration (optional)

metawipe works fully without any AI. When you opt in, it can connect to a **local** small model runtime (Ollama or any llama.cpp / OpenAI-compatible endpoint on `localhost`) to add smart assists:

- **Sensitive-data detection** — scan free-text metadata fields (titles, comments, keywords, author notes) and flag things that look like real names, emails, phone numbers, or addresses that a plain field-name check would miss.
- **Plain-language summaries** — "This PDF reveals it was authored by *J. Smith* using *Acme Suite 12* and last edited on your machine."

Design guarantees:
- **Off by default.** Core inspect/scrub never calls a model.
- **Local only.** metawipe only talks to a `localhost` endpoint you configure; nothing is sent to the cloud.
- **Metadata only.** Only extracted metadata text is sent to the local model — never the image pixels or document body (unless you explicitly enable a vision model for image content).
- **Graceful fallback.** If no runtime is reachable, metawipe silently uses rule-based detection.

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
