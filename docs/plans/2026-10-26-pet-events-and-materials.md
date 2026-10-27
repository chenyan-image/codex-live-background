# CodexHub pet integration, active acceptance list

Latest user amendments: retain only deepseek, bailong and xiaolemi from the old screenshot; keep the later PC2005-cloud material as deepseek-fat-fish, displayed as 鲸鱼女仆. Other old gallery assets are removed. Task and quota cards morph outward from their originating capsule/ring over 320 ms in a separate window. Calm roaming pauses 12–24 seconds, walks briefly at 24–36 px/s; daily gestures alternate with about eight seconds of idle. Boundary feedback lasts about 0.85 s with compression, lean, recoil and brighter directional stars. Offline speech uses a blue rounded bubble, tail/hearts and 14 px balanced text.

Implemented and checked: four-pet behavior/native-menu/popup/resize-band proof, production VP9-alpha decoding at 24 FPS with three complete clips, and existing companion regression. Release EXE packaging, final hashes and GitHub publication are the remaining delivery steps.

Keep the keleus whale girl and its procedural collision feedback. Replace only deepseek-fat-fish with all 106 production animations from PC2005-cloud/dsh-pet at dfc03961bdb4d26898838cb72c9c2ac09699b1e2. Material download is complete (142 relevant media files, 59,500,643 bytes). Do not bundle preview GIFs as the production player: those are 220×124 / 12.5 FPS, while production VP9-alpha WebM is 640×360 / 24 FPS. Retain MIT and source hashes.

Use the existing pinned LGPL FFmpeg runtime for native transparent video decoding, packaged with the EXE. No Electron/DSH host installation and no runtime dependency on the author's computer paths. Keep decoder processes hidden and inside the existing owned-child job; decode off the UI thread with bounded buffers. Preserve complete clips and alpha, hold the last frame during transition, and prefetch the next clip.

Shared behavior applies to every pet, independent of material type. Only native action catalogs and semantic event mappings differ. Menus must list each pet's own actions/categories. Route scope changes from both the main settings and context menu through PetController.SetScope. Menu outside-click detection must compare physical coordinates across monitors/DPI.

Weighted animation chain defaults: idle 10 / turn 5 / move 5, plus category weights. Complete a clip before choosing the next, with nonrepeating alternatives where available. Add quota events (six used-percent bands, using real Codex remaining quota), work states (thinking, working, result, waiting, success, error), and manual previews without falsifying quota readings.

Speech is explicitly OFFLINE. User said to author built-in phrases and never call Codex for text generation. Per-pet enabled flag and independent 300-second default timer; manual right-click whisper bypasses automatic enable; bubbles expire after about 10 seconds. Local chat can use built-in phrases/current local state and must be labeled offline, with no model/API requests.

Fix whale walking/collision/jump/wait row mappings to retain standing artwork, rather than the seated tea expression. Preserve original expressions for intentional manual selection. Work keyboard frames and seated thinking frames remain separate compositions.

Autonomy must run while Codex is busy: do not let the working animation suppress movement decisions. Restore autonomy clears manual occupation, waits a randomized interval, walks a randomized distance/direction, pauses, repeats, and rebounds at allowed boundaries. Inertia remains independent of roaming and is damped; paused release must not fling.

Detach quota/task popups from the pet geometry. Popup animation may change only the popup's opacity/offset/clip, never the pet size or position. Capsule opens current tasks/operations; ring opens quota/Token/reset. Keep the 270-degree vector dial, separate number/% glyphs, and spinning vector refresh during actual reads. Popup and speech windows are nonactivating tools with no taskbar buttons.

Already implemented in prior preview: inertia/four-edge and corner bounce; left click sound embedded, right click silent; press shrink/recovery; independent capsule size; pet 100% equals former 140% with 70–150 range; font/vector dial and full 100% label; current task reader with read-only SQLite titles; 220 ms popup transition (geometry detachment still pending).

After core functionality is stable: strengthen collision star effects (lower priority). LAST functional fix: larger edge/corner resize hit zones and smoother outer rounded corners. Do not repeat unrelated old slider/resize regressions; test the new hit-zone change itself.

Finish with focused/native/runtime proof, EXE extraction/hash verification, local preview cutover, feature-branch commits/change tags, updated concise README with references at end, and a new prerelease. Stable main code/release stays unchanged. No new release has been published for this batch yet.
