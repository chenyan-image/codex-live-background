# Steam Continuous Scroll Implementation Plan

**Goal:** Browse Wallpaper Engine wallpapers as one vertically scrolling gallery, without page buttons.

**Architecture:** Keep the native Windows Forms app and existing local/history pagination. A separate dark viewport realizes only visible Steam cards and one extra row; the full filtered catalog determines its scroll range. Selection, preview and source files use the existing pipeline.

**Tech Stack:** C# 5 / .NET Framework, Windows Forms, existing MSI packaging.

1. Add the continuous viewport and a dark mouse/keyboard/drag scrollbar in `source/GalleryControls.cs`. Bound materialized cards to the viewport, preserve selection while scrolling and never decode off-screen thumbnails.
2. Connect it to `source/GalleryManager.cs`: show it only for Steam, hide page controls, reset scroll after search/filter changes, update selection and generated previews. Leave local/history pagination intact.
3. Add a focused isolated `--steam-scroll-test` mode in `source/Manager.cs` and the gallery partial class. Check a 5,000-entry fixture, wheel/drag/key navigation, last-entry selection, search/filter resets, bounded cards, no horizontal overflow and local pagination. Capture a neutral fixture screenshot. Do not repeat window resizing, slider or closing tests.
4. Update release metadata and user documentation to v1.5.1, build MSI/ZIP in a new output folder, verify manifests and MSI metadata, then publish version and change tags. Preserve prior service profiles and upstream attribution. Do not install or restart Codex as part of this change.
