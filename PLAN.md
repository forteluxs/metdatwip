# metawipe — Project Plan

## Scope

A cross-platform (Windows 10/11 + macOS) desktop utility to **view and remove file metadata** for privacy, before sharing.

**In scope:**
- Read and categorize metadata from images (JPEG/PNG/TIFF/HEIC/WebP), PDFs, and OOXML Office docs (DOCX/XLSX/PPTX).
- Flag sensitive fields (GPS, personal names, serial numbers, software fingerprints, edit history).
- Scrub metadata (strip-all or keep-whitelist), writing safe cleaned copies.
- Verify by re-scanning cleaned output.
- Batch/folder processing with dry-run preview.
- Desktop UI (drag-drop) + a headless CLI sharing one core engine.
- Optional, local-only AI assist for sensitive-text detection (off by default).

**Out of scope (see Non-goals).**

## Architecture / tech approach

- **Platform:** .NET 8. Shared, UI-free `Metawipe.Core` library holds all metadata logic so both the GUI and CLI (and tests) reuse it.
- **UI:** Avalonia UI (MVVM) for a single cross-platform codebase running on Windows and macOS. (WPF was rejected — Windows-only; Avalonia gives one shell for both targets, consistent with keeping parity.)
- **Core interfaces:**
  - `IMetadataReader` → format-specific readers produce a normalized `MetadataDocument` (list of `MetadataField { Group, Name, Value, IsSensitive, Removable }`).
  - `IMetadataScrubber` → applies a `ScrubProfile` (StripAll / KeepWhitelist) and writes a cleaned copy; returns a `ScrubResult`.
  - `ISensitivityClassifier` → rule-based by default; optional `IMetadataAiService` layer.
  - `IMetadataAiService` → talks to a local Ollama / llama.cpp OpenAI-compatible endpoint; reachability probe + graceful fallback.
- **Format backends (behind interfaces, swappable):**
  - Images: MetadataExtractor (read) + a targeted writer that rebuilds files without EXIF/XMP/IPTC segments (ImageSharp / SkiaSharp for re-encode where needed; segment-strip for JPEG/PNG to avoid re-compression).
  - PDF: PdfPig / PDFsharp to read & clear the Info dictionary and XMP stream.
  - OOXML: `System.IO.Packaging` / DocumentFormat.OpenXml to clear `core.xml`, `app.xml`, and custom properties.
- **Safety model:** never modify originals by default; write to `*.cleaned.*` or an `--out` folder. Two-phase batch (plan → apply) with dry-run. Always offer a verify re-scan.
- **Persistence:** JSON settings + saved scrub profiles under `%APPDATA%\metawipe` (Windows) / `~/Library/Application Support/metawipe` (macOS).
- **Testing:** xUnit against `Metawipe.Core` with fixture files (known EXIF/GPS, sample PDF, sample DOCX). Round-trip tests assert sensitive fields are gone and image pixels/PDF pages remain intact.

## Milestones

1. **M1 — Core read engine (images):** `MetadataDocument` model, image reader (EXIF/GPS/XMP/IPTC), sensitivity rules, CLI `inspect`. Tests on fixtures.
2. **M2 — Scrub engine:** `ScrubProfile`, image scrubber (segment strip / safe re-encode), safe output, verify re-scan, CLI `scrub` + `--dry-run`. Tests assert removal + pixel integrity.
3. **M3 — Documents:** PDF Info+XMP reader/scrubber; OOXML core/app/custom-property reader/scrubber. Extend CLI + tests.
4. **M4 — Desktop UI:** Avalonia shell, drag-drop, categorized field view with sensitive flags, scrub-profile picker, batch/folder mode with dry-run and progress.
5. **M5 — Local-AI assist:** `IMetadataAiService`, sensitive-text classification over free-text fields, reachability probe, rule-based fallback, off-by-default setting.
6. **M6 — Packaging & CI:** Windows self-contained zip + MSIX; macOS `.app` + `.dmg`; GitHub Actions matrix (windows-latest, macos-latest) building, testing, and publishing artifacts.

## Non-goals

- No cloud processing, accounts, or telemetry — ever.
- Not a full photo/PDF/Office **editor** (no content editing, only metadata).
- Not a forensic recovery tool; metawipe removes metadata, it does not attempt to recover deleted data.
- No steganography detection/removal in v1.
- No mobile (iOS/Android) targets in v1.
- Not a general file manager or duplicate finder (see sibling tools file-lantern, dupe-sweeper).

## Packaging / distribution target

- **Windows 10/11:** self-contained `win-x64` portable zip + MSIX installer.
- **macOS:** `.app` bundle (x64 + arm64) packaged in a `.dmg`; document unsigned-build Gatekeeper steps until signing is configured.
- **CI:** GitHub Actions matrix on `windows-latest` and `macos-latest` — build, run xUnit tests, and attach artifacts to releases.
