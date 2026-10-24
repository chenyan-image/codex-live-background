# Video decoding and full-window images

Goal: apply videos while the manager covers Codex, and fill the entire Codex window with every image aspect ratio.

Evidence: the saved decoder diagnostic reports `DREAM_VIDEO_WINDOW_HIDDEN`; the current verified renderer has `taskMode=full` but `data-dream-art-wide=false` for a square image. Its body has no image while the main conversation owns the artwork. An auxiliary detached page has no Codex shell and must remain excluded.

1. Keep actual frame validation. Add a decoded bitmap path for hidden/occluded renderers instead of immediately failing on document visibility. Preserve codec errors, muted playback, bounded timeouts and cleanup; prefer a visible verified renderer when available.
2. Make explicit full-window mode independent of image ratio. Paint one cover-sized image on the body and clear only bound shell surfaces and their duplicate artwork. Preserve composer/panel opacity, menus and native positioning. Do not stretch or tile pictures.
3. Distinguish connection readiness from actual background state with a read-only renderer health check. Report video playing, paused/hidden, loading and failure separately.
4. Verify relevant probe regressions, real decoding in the existing hidden Codex renderer, synthetic portrait/square/landscape shell coverage and video pause/resume. Do not repeat old manager resize/slider tests, restart Codex or replace installed files.
5. Package and publish v1.5.2 with versioned changes, prior upgrade profiles and upstream attribution. Deliver an MSI for manual upgrade.
