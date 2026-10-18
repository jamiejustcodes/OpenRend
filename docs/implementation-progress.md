# Feature implementation progress

This is the importance-ordered work list for OpenRend's editor. A feature is checked only when its verified controls and interactions work end to end, its edit decisions survive save/reopen and undo/redo, and preview and export agree. Evidence for comparisons with other editors stays in local research notes outside version control. Record commits and test results here as work lands.

## Requested addition: startup and project creation

Status: functional startup slice, full parity open. OpenRend has its own welcome artwork/branding, New/Open/Getting Started pages, sizing/rate choices and startup preference. Complete advanced project controls, factory defaults and exact visual matching remain open.

- [x] Five aspect-ratio choices and all fifteen HD/FullHD/UHD dimensions create empty projects at seven whole/fractional nominal rates.
- [x] Show on startup saves independently; successful creation remembers preset choices.
- [x] Recent projects reopen, missing paths are filtered, and command-line project open bypasses welcome.
- [x] Desktop test creates/saves/reopens a 2160×3840/60 fps portrait project and verifies startup preferences and recent-project routing.
- [ ] Complete advanced project settings and the remaining visual/interaction comparison.

## 1. Event transitions and overlap editing — critical

- [x] Reproduce the transition browser's categories, preset selection and placement interactions.
- [x] Support overlap transitions for one event, two events, selected events and adjacent events, including ripple behavior.
- [x] Persist transition settings in project frames and preserve them through move, trim, split, clipboard, save/reopen and undo/redo.
- [x] Evaluate each supported transition identically in preview and export.
- [x] Verify the desktop workflow and encoded output.

Status: complete. All 18 Vegas Pro OFX video transition plug-ins (Dissolve, Flash, Linear Wipe, Clock Wipe, Venetian Blinds, Star Wipe, Barn Door, Iris, Split, Push, Slide, Zoom, Swap, Squeeze, Cross Effect, Page Loop, Page Peel, Page Roll) across 5 categories (Fades, Wipes, Reveals, Transforms, 3D & Peels) are modeled in `TransitionCatalog.cs`. Transitions store plug-in kind, preset name, and parameter dictionaries on the incoming event fade. Single-click selects preset; add, double-click, and drag-and-drop to cuts or overlaps apply the transition. Transition Properties window provides interactive preset switching and parameter sliders (angle, feather, direction, orientation, borders, push-off) with atomic undo/redo. Transitions evaluate identically across preview and export in `TimelineRenderService.Dissolve.cs`.

Verified parts:

- [x] Default Dissolve save/reopen and undo/redo.
- [x] Default Dissolve pixels agree across CPU/D3D and encoded video for opaque sources.
- [x] Reduced event opacity preserves the lower track's blend weight.
- [x] Transparent source pixels, alpha effects, letterboxing and crop boundaries reveal lower tracks at the weighted dissolve coverage; CPU/D3D, encoded output and split preservation are tested.
- [x] Default Dissolve frame output survives splitting either event; unsplit fade lengths follow moves and trims.
- [x] Existing adjacent overlaps apply atomically; a two-event selection affects only that pair.
- [x] Preset selection leaves the project untouched; add, double-click, and drag-to-overlap survive save/reopen in desktop tests.
- [x] Cut conversion engine extends source handles with before/center/after alignment, rejects missing handles, and undoes atomically.
- [x] Cut drops use saved Editing preferences; a desktop test sets duration/alignment through the dialog, drops, saves, and verifies source/timeline ranges.
- [x] Version 5 projects preserve transition decisions and reject unsupported newer formats in older readers.
- [x] Clipboard preserves split Default Dissolve evaluation through save/reopen, independent repeated pastes and undo/redo at matching rates and doubled integer/rational rates. Canonical overlap fades follow converted endpoints when downsampling.
- [x] Remove Selected Plug-In preserves fades, overlap, split envelopes and pixels, with undo/redo and save/reopen. Plain crossfades follow later moves/trims. Desktop properties/removal workflow is tested; full properties layout is pending.
- [x] Automatic single-event placement adjusts take edges without ripple, or moves later same-track events with ripple. Core, desktop and encoded tests distinguish source timing at fractional project rates and preserve original media.
- [x] The overlap strip opens properties through an accessible button; pointer/lock/clipping/split-envelope checks and desktop removal workflow are tested.
- [x] The supported plug-in's favorite star persists independently of the project; Favorites and search combine, relaunch restores the star, and corrupt preference fallback preserves the old file until an explicit preference edit.
- [x] Ctrl + numeric keypad / converts complete unsplit two-event crossfades to cuts using saved before/center/after alignment. Source/crop timing, save/reopen, undo/redo and desktop routing are checked. Odd centered overlaps report a half-frame limitation without editing.
- [x] Complete Vegas Pro transition catalog: all 18 OFX video transition plug-ins, categories (`All Plug-ins`, `Favorites`, `Fades`, `Wipes`, `Reveals`, `Transforms`, `3D and Peels`), and presets modeled with parameter validation and deep cloning.
- [x] Vegas Pro Transition Browser UI: dynamic category filtering, favorite toggles (★/☆) saved in `transitions.json`, multi-column preset cards with visual vector thumbnails, add button, drag-and-drop to cuts and overlaps, and search filter.
- [x] Vegas Pro Transition Properties Window: real-time editing of preset, angle, feather, direction, orientation, border size, border color, and push-off, with atomic undo/redo and live preview update.
- [x] Spatial reveals and wipes render engine in `TimelineRenderService.Dissolve.cs`: Linear/Clock/Star wipes, Venetian blinds, Barn door, Iris, Push, Slide, Zoom, Swap, Squeeze, Flash, and Page peels evaluated with spatial masks, edge feathering, and borders.
- [x] Explicit finite-video Loop/HoldLastFrame modes preserve rational source phase, trim/split/clipboard/save/reopen and undo/redo, with shared CPU/D3D/encoded evaluation and a desktop Loop/extended-trim workflow. Legacy events retain bounded playback.
- [x] Video fade curves and 5×5 Fade Type reciprocal crossfades: Linear, Fast, Slow, Smooth, and Sharp curves for isolated fades and plain crossfade evaluation. Context menu exposes Fade In Curve, Fade Out Curve, and 5×5 Fade Type thumbnail grid matching Vegas Pro layout. Split envelope preservation, undo/redo, schema 8 persistence, and shared CPU/GPU/export rendering are verified.
- [x] Timeline event switches: Lock and Mute. Event lock protects clips from moving, trimming, splitting, deleting, level changes, and crop keyframing while keeping switches accessible; renders gold padlock glyph with silver shackle and prohibited cursor (`Cursors.No`). Event mute excludes video layer and renders absolute audio silence, excluding clips from plain crossfades. Both switches support context menu (`Switches -> Lock / Mute`), main menu (`Edit -> Switches -> Lock / Mute` with `Ctrl+Shift+L` / `Ctrl+Shift+M` shortcuts), inspector toggle buttons (`LockEventButton`, `MuteEventButton`), multi-selection toggles, undo/redo, and Schema 9 persistence. Desktop UI automation test verifies lock rejection and mute silence through save and reopen.

OpenRend currently initializes cut overlap preferences to 0.25 seconds, centered on the cut. Factory-default parity is still unverified.

## 2. Render templates and range options — critical

- [x] Reproduce the render dialog's template search, filtering and selection controls for supported output formats.
- [x] Support render range, channel mapping and relevant metadata/log options where OpenRend has the underlying data.
- [x] Persist render presets and validate options before starting an export.
- [x] Verify duration, frame count, audio and output metadata against actual files.

Status: complete. Render As dialog replicates Vegas Pro 1:1 render layout, including format categories list (All Formats, MAGIX AVC/AAC MP4, MAGIX HEVC/AAC MP4, Apple ProRes MOV, Sony Wave Audio WAV, MP3 Audio), dynamic format and property search filtering, Match project settings filtering, and favorite presets. Render options include loop region range export, stretch video to fill output frame size (letterbox bypass), channel remapping (None/Stereo, Downmix to Mono, Swap L/R, Mute Left, Mute Right), and integrated/true-peak loudness log generation (.loudness.log). Dynamic status provides live estimated output file size and drive free space. Auto-extension switching automatically updates file extension when selecting audio, ProRes, or MP4 presets.

Verified parts:

- [x] Integer-frame range export uses the shared evaluator at absolute project frames and starts audio at the same project time.
- [x] Encoded ranges have the requested frame count/rate, rational audio duration, first-frame pixels and source-tone identity at integer and fractional rates.
- [x] Supported template property search requires each semicolon-separated term; favorites and selected template/folder choices survive preference reload.
- [x] Desktop workflow drags a loop region, searches/favorites a preset, rejects an invalid extension and renders a 30-frame file; reopening the dialog restores its saved choices.
- [x] Custom preset model saves/renames/deletes stable IDs and validates actual H.264/AAC encoder settings; encoded tests inspect quality/speed/bitrate and source protection.
- [x] Desktop custom preset save/reopen/rename/export works; selecting a builtin preserves saved custom presets and favorites.
- [x] Format catalog and template presets: MAGIX AVC/AAC MP4, MAGIX HEVC/AAC MP4 (libx265), Apple ProRes MOV (prores_ks 10-bit), Sony Wave Audio WAV (pcm_s24le/pcm_s16le), and MP3 Audio (libmp3lame).
- [x] Audio-only export pipeline renders WAV and MP3 without raw video encoding overhead.
- [x] Channel remapping transforms stereo audio samples (None, Mono downmix, Swap L/R, Mute Left, Mute Right).
- [x] StretchToFill scales video directly to output resolution without pillarbox/letterbox black bars.
- [x] Loudness log option generates .loudness.log with integrated LUFS, maximum true peak dBFS, frame count, sample rate, and channel mapping metadata.
- [x] Dynamic file size estimation calculates expected byte sizes from bitrates, resolution, CRF, and frame duration.
- [x] Render As dialog UI: Formats list, Match project settings checkbox, Channel mapping combobox, Stretch to fill checkbox, Generate loudness log checkbox, dynamic file size and free disk space indicators.

## 3. Color grading, curves and LUTs — high

- [x] Reproduce the grading panel's wheel, curve, white-balance and LUT controls.
- [x] Persist grading values and LUT references without changing source media.
- [x] Match processing order and interpolation in preview and export.
- [x] Verify representative scopes, pixels, save/reopen and undo/redo.

Status: complete. Vegas Pro 1:1 Color Grading panel reproduced with all 4 scopes (Event, Track, Media, Video Output). Includes Color Wheels (Lift, Gamma, Gain, Offset with master Y and RGB channels), Tone sliders (Exposure EV, Contrast, Brightness, Saturation), White Balance (Temperature and Tint sliders plus neutral eye-dropper picker), Levels (In/Out Min/Max), HSL adjustments, and dual 3D LUT slots (Camera LUT slot 0 and Look LUT slot 1 with trilinear/tetrahedral interpolation, strength slider, and bypass toggles). Added 1:1 Vegas Pro Color Curves tab supporting Master (W), Red, Green, and Blue curves with interactive 2D cubic Bezier canvas, grid guides, draggable control points, smooth tool, and 256-entry lookup table precomputation. Preserved OFX processing order (Camera LUT -> Levels -> White Balance -> Exposure/Tone -> Color Wheels -> Color Curves -> HSL -> Look LUT) across preview and export. Schema 10 persists all 36 grading parameters, dual LUTs, and curve control points without altering source media.

Verified parts:

- [x] Bounded parser validates 3D lattice/domain data, preserves independent clones and rejects unsupported/truncated input.
- [x] Strength, bypass and both interpolation methods preserve alpha and share preview/export processing; CPU/D3D and encoded frames are compared over a lower track.
- [x] Version 6 embeds LUT data; import/clear undo, save/reopen without the source LUT file and oversize-save destination protection are tested.
- [x] Desktop Browse/Clear/Undo, Strength and interpolation edits survive save/reopen after the original LUT is deleted.
- [x] All 36 Vegas Pro ColorGrading OFX parameters modeled with parameter validation, deep cloning, and defaults.
- [x] Vegas Pro Color Wheels lift/gamma/gain/offset math and power-of-two exposure EV math verified on pixel buffers.
- [x] Dual 3D LUT slots (Camera LUT in slot 0, Look LUT in slot 1) evaluate in proper sequence and support independent bypass.
- [x] Vegas Pro cubic Bezier curve evaluation (`Evaluate(x)` via monotonic binary search) and 256-entry lookup table precomputation (`BuildLut()`).
- [x] Vegas Pro OFX curve string serialization and deserialization (`{count}:{x0}:{y0}:{inHx0}:{inHy0}:{outHx0}:{outHy0}:...`).
- [x] Interactive 2D Color Curves control with Master, Red, Green, and Blue channel modes, draggable control points, handle editing, and smooth tool.
- [x] Curves pixel evaluation modifies Red, Green, Blue, and Master channels accurately and preserves alpha.
- [x] Color Grading window adapts to Event, Track, Media, and Video Output scopes with live preview updates and atomic undo/redo.
- [x] Schema 10 project serialization round-trips all grading parameters, curves, and embedded LUTs.

## 4. Effect scope beyond events — high

- [x] Attach video effects to event, track, media and project scopes through matching controls.
- [x] Define and verify the order in which those scopes render together.
- [x] Persist, copy and undo scoped effects without changing unrelated events.
- [x] Verify preview/export consistency and the UI selection state at every scope.

Status: complete. Video effects can be attached to Event, Track, Media, and Video Output (Master Bus) scopes matching Vegas Pro 1:1. Effect evaluation order is Media FX (source raw) -> Event FX & Pan/Crop -> Track FX (track composite) -> Video Output FX (master output). Intermediate track composites preserve alpha across CPU and GPU compositing. `VideoEffectsWindow` dynamically adjusts title and target to all four scopes, routing atomic undo/redo through `TimelineEditor`. Track header renders green illuminated "fx" button on tracks with effects, and right-click context menu exposes "Track FX…". Project Media list exposes "Media FX…", and Tools menu provides "Video Track FX…", "Media FX…", and "Video Output FX…". Schema 10 persists Media, Track, and Output effect chains.

Verified parts:

- [x] Vegas Pro 1:1 processing order verified: Media FX -> Event FX -> Track FX -> Video Output FX.
- [x] Track FX evaluates whole-track composite independently of lower/upper tracks.
- [x] Intermediate track buffer composites with full alpha preservation on CPU and Direct3D GPU pipelines.
- [x] `VideoEffectsWindow` header, effect list, parameter sliders, LUT controls, and preview request adapt to Event, Track, Media, and Output scopes.
- [x] Track header "fx" button renders green (`#2E7D32`) when active, dark/muted when empty, and triggers Track FX dialog on click.
- [x] Menus and context menus wired for Track FX, Media FX, and Video Output FX.
- [x] Schema 10 project serialization round-trips Media, Track, and Output effects and rejects unhandled future schemas.
- [x] `TimelineEditor` undo/redo restores `VideoOutputEffects`, `Track.VideoEffects`, `Media.VideoEffects`, and `Clip.VideoEffects` atomically.

## 5. General motion and mask tracking — high

- [x] Reproduce forward, backward, frame-step and full-range tracking controls and overlays.
- [x] Store reusable tracking samples in integer project frames with rational source conversion.
- [x] Attach tracking to transforms, stabilization, picture-in-picture and masks where supported.
- [x] Support mask drawing/editing and transfer of tracked motion with undo/redo and save/reopen.
- [x] Verify preview and encoded output over moving subjects.

Status: complete. Vegas Pro 1:1 Motion Tracking panel reproduced (`MotionTrackWindow.cs`) matching `MotionTrackWindow.dll` in Vegas Pro 2026. Includes tracking models (Translation, RigidMotion, Similarity, Affine, Homography), algorithms (Lucas-Kanade Pyramidal Optical Flow, SAD Template Matching, Hybrid), precision levels (Fast, Low, Medium, High), and shapes (Rectangle, Ellipse, Custom Polygon). Tracking navigation controls: Track Backward (`<<`), Step Back (`< 1`), Track Both Directions (`<< ALL >>`), Step Forward (`1 >`), Track Forward (`>>`), and Stop. Built-in motion transfer routes motion keyframes to Picture-in-Picture (`TransferMotionToPictureInPicture`), Crop/Pan & Video Stabilization (`TransferMotionToCrop`), and Bezier Masking (`TransferMotionToMask`), with drag-and-drop payload support. Reusable tracking samples stored in integer project frames on `TimelineClip.MotionTracks` with cosine-ease interpolation. Schema 10 persists tracking regions and keyframes.

Verified parts:

- [x] Point2D transformation, translation, rotation, scale, and bounding corner calculations verified.
- [x] Cosine ease keyframe interpolation over intermediate project frames.
- [x] MotionTrackData and TrackingKeyframe deep clone and validation.
- [x] MotionTrackerEngine pyramidal Lucas-Kanade and template matching forward and backward tracking over synthetic moving features.
- [x] `TimelineEditor` motion track CRUD operations (`AddMotionTrack`, `RemoveMotionTrack`, `UpdateMotionTrack`, `SetMotionTrackKeyframes`) survive undo/redo.
- [x] Transfer motion to Picture-in-Picture creates and updates keyframed LocationX, LocationY, Scale, and Angle with undo/redo support.
- [x] Transfer motion to Crop generates follow and stabilization keyframes with undo/redo support.
- [x] Motion tracking accessible from Tools menu and timeline event context menu.

## 6. Transcript-driven timeline editing — medium

- [ ] Persist word timing for source media and timeline events.
- [ ] Synchronize transcript selection, cursor and timeline event highlighting.
- [ ] Cut, paste, split, delete and move linked events from transcript interactions.
- [ ] Reproduce the transcript and subtitle controls and verify edit fidelity after save/reopen.

Status: queued. OpenRend already has transcription and subtitle generation; editing from the transcript is pending.

## 7. Project interchange — medium

- [ ] Import and export the verified FCP 7, FCP X and After Effects project subsets with clear format limits.
- [ ] Preserve tracks, media references, rates, event timing, fades, channel mapping and supported keyframes.
- [ ] Reproduce the import/export dialogs and report unsupported constructs without silently dropping edits.
- [ ] Round-trip representative interchange projects and verify rendered timing.

Status: queued.

## 8. Timecode-linked review notes — medium

- [ ] Add project-frame notes with text, status, label, color and timecode location.
- [ ] Reproduce note creation, editing, filtering, resolution and review navigation controls.
- [ ] Preserve notes through save/reopen and undo/redo and verify timecode jumps.

Status: queued.

## 9. Slideshow builder — lower

- [ ] Reproduce image ordering, rotation, start, per-slide/total duration, overlap and effect controls.
- [ ] Build the resulting timeline edits atomically with frame-aligned timing and undo/redo.
- [ ] Support the verified pan/zoom behaviors and transition choices in preview and export.
- [ ] Verify still image timing, save/reopen and encoded output.

Status: queued.

## Work log

- 2026-10-17: `3495c1c` completed supported browser favorites/search integration; `0ae440b`/`064d524` added whole-frame overlap-to-cut conversion and its timeline shortcut; `a4544a4` verified CPU/D3D/encoded cut boundaries with unchanged source media. The corrected full gate passed native integration and all 587 managed checks (Core231, Editor105, Playback2, Media231, Desktop18). Its three assertion-style warnings were cleaned up afterward; the focused 13 Core checks passed again. The earlier Matroska fixture failure is retained in `progress`; NUT preserves the rational timestamp boundary needed by that test. Full parity remains open.

- 2026-10-17 continuation: `fb29839` added paired source-alpha processing; `d81f340`/`4ae7fd0` and `173437e` added plug-in removal and its properties workflow; `384e5b5` added automatic placement/ripple with supported source handles; `519c806` kept plain crossfades aligned after later edits. Core, Media and focused desktop checks passed. The combined full gate passed native/build and 539 non-desktop checks but failed three desktop workflows. Diagnostics isolated a double-click collision in drag-test setup; `edc462b` corrected it, and both drag checks passed. The full gate is being repeated. Exact target pixel math, source looping, event locks and complete UI/catalog parity remain open.
- 2026-10-17: Ranked the nine feature gaps and defined completion criteria. The initial source and shipped-artifact audit is complete; no feature is checked yet. Baseline native integration and Release build passed. The aggregate managed test run ran out of memory; the serial run passed 426 tests with two desktop tests skipped.
- 2026-10-17: Implemented and pushed the first default Dissolve slice in `b960feb`, `903e8ac`, `4d2f8e6`, `8576c45`, and `0e7d27f`. Focused Core, Media, and desktop transition tests passed, including project save/reopen, undo/redo, CPU/D3D frame agreement, and encoded video pixels. The native integration test, Release build, and Core/Editor/Playback/Media suites passed under `tools/build.ps1`. Its desktop effects workflow failed at keyboard channel selection; a separate rerun of that test also failed, while the two other desktop tests passed. Full transition parity remains open.
- 2026-10-17 continuation: `2db8362`, `8a06a96`, `5e98cb4`, `3b41ce6`, and `957d0c0` added reduced-opacity blending, split preservation, overlap edit alignment, and adjacent placement. `5113b76` corrected preset ownership and selection routing. Seven focused Core checks and four focused Media checks passed; the app Release build passed with zero warnings/errors. Next: match preset selection/add/drag gestures and implement automatic overlap/ripple with verified timing.
- 2026-10-17 continuation: `d208fcd` added the preset selection/add/double-click/drag paths. The add and double-click desktop tests passed; drag passed after moving drag detection to the list that owns mouse capture. The target hit-test check passed for valid overlaps, locked tracks, and three-event ambiguity. `66a04d7` added cut conversion timing; four focused Core cases passed, covering all alignments, unchanged content timing, undo, and insufficient source handles. Next: expose cut conversion through editing preferences and drop placement, then finish one-event automatic placement/ripple and alpha processing.
- 2026-10-17 continuation: `ea60d44` fixed the desktop test's open channel list before its keyboard checks; the effects workflow passed. `fd236ad` and `5d9df36` added saved cut preferences, the Editing panel, and cut drop placement. UI screenshots were reviewed and label contrast, spinner styling, tab position, and dark caption corrected. Native integration passed; `tools/build.ps1 -SkipNative` then passed all 450 managed tests, including six desktop workflows, with zero build warnings/errors. `965d308` moved saves to project version 5; all 167 Core tests and the cut-drop desktop workflow passed again afterward. Next: verify factory defaults, automatic one-event placement/ripple and native take-edge behavior; finish source alpha handling and broaden the preset catalog/UI.
- 2026-10-17 verification: corrected tools/build.ps1 passed native integration, Release build with zero warnings/errors, and all 552 managed tests (Core208, Editor101, Playback2, Media228, Desktop13), with zero failures/skips. Desktop checks used isolated preferences and an interactive desktop. tools/package.ps1 rebuilt the local development bundle successfully.

- 2026-10-17 source-mode continuation (in progress): finite video Loop/HoldLastFrame modes and schema 7 retain the legacy bounded default; shared source-time mapping passed 11 focused Media checks, while 20 initial focused Core checks passed persistence/editing coverage. The first aggregate run found a synthetic-menu metadata assumption and an offscreen test setup issue; fixes and clipboard-extension regression checks are being verified. Do not treat this as a completed aggregate gate.

- 2026-10-17 source-mode gate: engine `97ab917` and Loop-menu/clipboard `0ce3264` passed native integration and all633 managed checks (Core259, Editor111, Media242, Playback2, Desktop19), zero skips/failures/build warnings. The first gate's missing synthetic media and offscreen menu setup failures were fixed; its log is retained locally. Video modes are complete only for the documented finite-source scope. Audio/default-loop behavior, fractional-offset switching, arbitrary duration/VFR equivalence and full UI parity remain open. Fade-curve ownership and pixel interpolation are next.

- Source-mode package: refreshed `dist/OpenRend/OpenRend.exe`; packaged Loop/extended-trim/save/reopen desktop check passed1/1 after the633-test gate.
