# Wallpaper Gallery Implementation Plan

**Goal:** Build a distinct black wallpaper manager with a persistent library, recent use, bundled original backgrounds, real connection status and Wallpaper Engine discovery.

**Architecture:** Retain the Windows Forms executable and existing verified background engine. A separate gallery partial class owns navigation and asynchronous loading; a JSON library stores local references outside the installation directory. Steam discovery is read-only and treats all workshop metadata as untrusted data.

**Tech Stack:** .NET Framework / C# 5, Windows Forms, System.Drawing, existing PowerShell/Node preview and apply pipeline, Windows Installer MSI.

1. Implement `source/WallpaperLibrary.cs`: atomic JSON persistence, path deduplication, successful-apply history, original-path references, current wallpaper migration, bundled catalog, bounded read-only Steam registry/VDF discovery and project metadata parsing. Never execute wallpaper projects, follow junctions, traverse outside a workshop item, or delete source media. Scene/web/application projects only offer a clearly labeled preview image.
2. Implement `source/BuiltinWallpapers.cs`: six original landscape/abstract compositions generated locally at 2560x1440 during builds. Package the images and catalog; include provenance. No reference-project assets or personal media are redistributed.
3. Implement `source/GalleryControls.cs` and `source/GalleryManager.cs`: charcoal sidebar, independent mint accent, three-column responsive paginated cards, search and type filters, selected-media preview, import/apply separation, source details, history removal without source deletion, manual Steam-directory fallback, service settings and collapsible logs. Place verified Codex status at bottom left and small yz at bottom right.
4. Extend `source/control.ps1` with read-only JSON status and preview-only actions. Never show green connected based merely on an open port or a running service. Preview jobs must cancel/ignore stale results when selection changes and stop their own controller on close; keep existing apply timeouts and no-close behavior.
5. Update build/source packaging, migrate release version to 1.5.0, preserve service compatibility profiles and all prior service/video functionality. Keep reference-project attribution as UI inspiration, retain existing upstream licenses.
6. Verify reference persistence, duplicate imports, missing files, corrupt-index backup, real and synthetic Steam libraries, project traversal/junction rejection, scene preview labeling, real GUI selection/history/filtering/preview, verified status, full package integrity and MSI metadata. Use temporary fixture folders and no real wallpaper changes for tests.
7. Build release artifacts, inspect actual screenshots, publish main/develop/feature branch and change tags. Do not retry the previously rejected final MSI repair installation through an alternate route; deliver the new package for manual installation if that policy still blocks installation.

Design: a narrow charcoal navigation rail, generous black workspace and image-first gallery, with a slim right inspector. It borrows the reference's library organization while using our own layout, typography, icon and black/mint identity. Local imports and Steam files remain original references; unavailable media remain visible in history with a clear missing label. Only successful applications enter usage history.

Validation scope: per user instruction, do not repeat the previously resolved window resizing, slider trails or window closing tests. Validate only the new gallery, library/history, Steam discovery, preview jobs and icon changes, plus build/package integrity.
