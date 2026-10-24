# Bundled wallpapers and illustrated format guide

Ship the three user-selected Workshop items as independent installed files: two original 3840x2160 / 60 FPS H.264 videos and one static scene preview. Keep the six generated backgrounds. Defaults are available in the local library without Steam or the original drive, and installing does not replace the user's chosen wallpaper.

The repository tracks a source catalog, provenance and pinned SHA-256 values instead of committing large video blobs. `build.ps1 -BundledMediaSource` verifies each file and includes it under `wallpapers/bundled`; developers may reuse this directory from an installed release. The application resolves it relative to its own installation and displays per-item descriptions instead of labeling every built-in image original.

Verify nine installed entries, correct video/image classification and thumbnails, relocation after upgrade, source independence and package hashes. Capture the real Steam library with the manager's own rendering in an isolated screenshot process; the Computer Use helper is missing. Update README with format and limit tables, source references and a complete screenshot. Do not rerun manager window-resizing or slider tests.
