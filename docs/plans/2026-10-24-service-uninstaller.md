# Complete Service Uninstaller Implementation Plan

**Goal:** Ship v1.3.0 with complete service removal, reliable startup recovery, automatic legacy migration, animated previews and a single EXE installer.

**Architecture:** Reuse native Windows Forms and verified service/process identities. Pause live styling, unregister only this application's startup entries and package hook, wait for its guard/monitors, stop the verified injector, then remove the owned startup directory and session record. Preserve wallpaper, profiles, video tools and the manager.

**Tech Stack:** Windows PowerShell 5.1, .NET Framework WinForms, PackageDebugSettings COM API.

---

1. Update source/skin-removal.ps1 so skin removal only pauses and removes current styling; test that startup entries, files and injector state remain.
2. Add source/service-removal.ps1 with ownership checks, cooperative service shutdown, stale-hook repair, exact process verification, bounded paths and junction rejection before recursive cleanup. Failed cleanup must report partial completion and be retryable.
3. Add tests/service-removal.ps1 using isolated directories and mocked Windows integration. Cover normal/repeated removals, foreign hooks, missing files, blocked cleanup, running monitor and reparse-point boundaries.
4. Add a fourth manager action with an explicit description and confirmation. Replace the native TrackBar with a buffered control to eliminate redraw trails; verify pointer, keyboard and pixel restoration. Buffer all layout containers and repaint resized surfaces. Bound status and worker waits, allow immediate window dismissal during work, and test close/timeout paths. Correct the opacity wording.
5. Generate short local GIF previews using the pinned FFmpeg runtime, animate them in the manager, cache and restore them. Apply fixed dark appearance to live wallpaper and Codex preferences.
6. Add a login Startup-folder fallback and ensure the guard runs on Codex package activation. Keep service helpers hidden and independent of the manager window.
7. Detect canonical and legacy installs, close only old managers, stop only verified background components, checkpoint and archive old program files, preserve user data, and support rollback.
8. Build an offline single EXE installer using .NET and an embedded, SHA-256-verified package. The customer never needs to unzip it. Validate extraction boundaries and all file hashes before migration.
9. Update documentation and build v1.3.0, verify package/installer and source correspondence, upgrade the local manager and publish main/develop, feature branch, change tags and release.

The existing service stays running on this computer during development. Only isolated test installations are uninstalled automatically.
