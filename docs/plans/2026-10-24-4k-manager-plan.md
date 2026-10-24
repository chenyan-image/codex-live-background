# 4K Manager Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Support native 4K with verified quality fallback and ship a refined Windows manager with skin removal.

**Architecture:** Keep bounded source inspection separate from renderer-ready media. Reuse the pinned local FFmpeg runtime and actual Codex decode probe. Retain native Windows Forms and existing attach-only service integration.

**Tech Stack:** Node.js 22, Windows PowerShell 5.1, .NET Framework WinForms/GDI+.

---

### Task 1: Video import and fallback

Modify engine/scripts/prepare-video.mjs and engine/assets/ffmpeg-runtime.json. Add FFprobe to the verified runtime file list. Inspect large/nonstandard sources without loading entire files. Preserve direct playback, then attempt distinct 4096/2560/1920 profiles. Cache by source hash and profile, validate every candidate before applying, stop on non-codec errors. Add tests/video-preparation.mjs with real generated patterns and injected decoder failures. Run existing playback/WebM regressions and a real Codex probe.

### Task 2: Skin removal

Modify source/control.ps1 and extract source/skin-removal.ps1 for isolated verification. Validate service ownership, pause/remove live styling, remove only owned startup settings, stop the recorded injector, and preserve Codex/user media. Test fresh, repeated and disconnected removals using mocked external effects.

### Task 3: Interface and icon

Modify source/Manager.cs. Add source/IconAssets.cs and source/assets with upstream SVG/license. Render a dark, spacious native interface, media card, opacity control, progress/state and three keyboard-accessible buttons. Add explicit removal confirmation, friendly feedback and responsive layouts. Compile and capture screenshots at normal and larger DPI/layout sizes.

### Task 4: Distribution

Update source/build.ps1, source/service-compatibility.ps1, usage, README, CHANGELOG and third-party notices. Build v1.2.0 in a fresh output directory with signed Node runtime, preserve UTF-8 BOM for Chinese PowerShell, verify all manifest hashes. Publish marked commits on feature/main/develop with version and change tags and release ZIP/checksum. Upgrade the local tool without restarting Codex.
