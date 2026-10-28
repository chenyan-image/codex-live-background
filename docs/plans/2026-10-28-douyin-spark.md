# Douyin Spark branch implementation plan

**Goal:** Add an optional Douyin desktop streak workflow to CodexHub without changing wallpaper or companion preferences.

**Architecture:** A local-only contact list and durable per-day dispatch journal drive a cancellable sequential runner. A Windows UI Automation adapter reads the official Douyin desktop window. The existing companion temporarily walks to verified control locations and returns to its previous position when the task ends. A new sidebar page contains configuration, start/stop, and per-contact results.

**Tech stack:** Existing .NET Framework C# / WinForms; Windows UI Automation and user32; no server, account tokens, OCR package, or additional downloadable dependency.

## Acceptance conditions

- Disabled by default; only the configured exact contact names are processed.
- Automatic mode waits for the visible foreground Douyin desktop window. Manual start discovers and launches/restores the installed desktop app, then waits for its main window; switching away during the run cancels before another click.
- Message list → search exact name → verify chat header → emoji menu → named 续火花 tile. Ambiguous matches are skipped. A successful empty search is reported as 无此人; unavailable UI is a different error.
- One pass over the list. A local-day journal is flushed before dispatch, preventing duplicate sends after cancellation/restart. Unconfirmed dispatch is not silently retried.
- No actual messages sent during development without an explicitly selected test recipient.
- Guide movement, cancellation and completion preserve the original pet settings, position, activity range, wallpaper, and service configuration.

## Work order

1. Create `codex/douyin-spark` from v1.7.0.6; keep main and stable update channels unchanged.
2. Implement settings, journal and state machine in `source/DouyinSpark.cs`, with fake-adapter tests for success, missing/ambiguous contacts, cancellation, failed persistence and duplicate suppression.
3. Implement the scoped UI Automation adapter in `source/DouyinAccessibility.cs`, using live-observed labels and geometry only within the selected Douyin window; reject stale/wrong-window targets.
4. Add a temporary guide lease to PetWindow in `source/CompanionAutomation.cs`; do not rewrite pet preferences.
5. Add `source/ManagerDouyin.cs`, sidebar navigation, lifecycle disposal, and build references. Keep existing pages and controls.
6. Compile the branch, exercise its UI and dry run, and package a separate preview. Document current platform/UI limitations and push the feature branch only.

## 2026-10-28 follow-up

- Manual start and inspect both launch/restore Douyin automatically. Discovery checks running processes, per-user/per-machine install records and common C/D/E paths; a saved explicit executable handles other custom folders.
- Replace uninterruptible in-process UIA reads with owned short-lived helper processes. A timeout/cancel kills only that helper and releases the request gate; cache runtime IDs to avoid one remote call per node. Helper replies exclude chat-body text.
- Standalone preview creates no Codex quota reader, task watcher, Token reader or surface timers. The original constructor defaults preserve existing Codex mode.
- Preview 3 adds verified keyboard fallback for Chromium search fields without ValuePattern: target/focus validation, Unicode key batches with balanced key releases, legacy/text value echo, and no Enter or clipboard use.

## Validation

Run the isolated runner/adapter fixture with no access to real messaging. Compile the complete manager with the new files and Windows UI Automation references. Check existing pet rendering/window tests once integration is complete. Use Computer Use only for Douyin read-only navigation and CodexHub page checks; keep the final send disabled in inspection mode.
