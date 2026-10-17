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

- [ ] Reproduce the transition browser's categories, preset selection and placement interactions.
- [ ] Support overlap transitions for one event, two events, selected events and adjacent events, including ripple behavior.
- [ ] Persist transition settings in project frames and preserve them through move, trim, split, clipboard, save/reopen and undo/redo.
- [ ] Evaluate each supported transition identically in preview and export.
- [ ] Verify the desktop workflow and encoded output.

Status: default Dissolve is partial. The preset belongs to the incoming event fade. One selected event can target existing adjacent overlaps; two selected events target their pair. Single-click selects a preset; add, double-click, overlap drag, and cut drag are functional. The Editing preferences panel saves cut overlap duration and alignment. Source alpha is handled by a shared premultiplied mixture, tested locally; exact target pixel math remains unverified. The full preset catalog, automatic one-event placement/ripple, factory defaults, and complete UI matching remain open.

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

OpenRend currently initializes cut overlap preferences to 0.25 seconds, centered on the cut. Factory-default parity is still unverified.

## 2. Render templates and range options — critical

- [ ] Reproduce the render dialog's template search, filtering and selection controls for supported output formats.
- [ ] Support render range, channel mapping and relevant metadata/log options where OpenRend has the underlying data.
- [ ] Persist render presets and validate options before starting an export.
- [ ] Verify duration, frame count, audio and output metadata against actual files.

Status: partial. Render As now exposes supported H.264/AAC presets, custom quality/audio bitrate/encoder-speed settings, property search, favorites, output folder/name and loop-region rendering. The timeline provides ruler/edge drags and I/O in/out points. Full format/resolution/rate catalog, channel mapping, metadata/log options and complete UI matching remain open. Loop selection is session state and is currently bounded to the timeline; project persistence and loop playback remain open.

Verified parts:

- [x] Integer-frame range export uses the shared evaluator at absolute project frames and starts audio at the same project time.
- [x] Encoded ranges have the requested frame count/rate, rational audio duration, first-frame pixels and source-tone identity at integer and fractional rates.
- [x] Supported template property search requires each semicolon-separated term; favorites and selected template/folder choices survive preference reload.
- [x] Desktop workflow drags a loop region, searches/favorites a preset, rejects an invalid extension and renders a 30-frame file; reopening the dialog restores its saved choices.
- [x] Custom preset model saves/renames/deletes stable IDs and validates actual H.264/AAC encoder settings; encoded tests inspect quality/speed/bitrate and source protection.
- [x] Desktop custom preset save/reopen/rename/export works; selecting a builtin preserves saved custom presets and favorites.

## 3. Color grading, curves and LUTs — high

- [ ] Reproduce the grading panel's wheel, curve, white-balance and LUT controls.
- [ ] Persist grading values and LUT references without changing source media.
- [ ] Match processing order and interpolation in preview and export.
- [ ] Verify representative scopes, pixels, save/reopen and undo/redo.

Status: partial event LUT slice. A 3D .cube LUT can be imported, embedded, cleared and adjusted with Strength and Trilinear/Tetrahedral interpolation in Video event FX. Full grading wheels/curves, Camera/Look slot layout, native color-space math and complete UI matching remain open.

Verified parts:

- [x] Bounded parser validates 3D lattice/domain data, preserves independent clones and rejects unsupported/truncated input.
- [x] Strength, bypass and both interpolation methods preserve alpha and share preview/export processing; CPU/D3D and encoded frames are compared over a lower track.
- [x] Version 6 embeds LUT data; import/clear undo, save/reopen without the source LUT file and oversize-save destination protection are tested.
- [x] Desktop Browse/Clear/Undo, Strength and interpolation edits survive save/reopen after the original LUT is deleted.

## 4. Effect scope beyond events — high

- [ ] Attach video effects to event, track, media and project scopes through matching controls.
- [ ] Define and verify the order in which those scopes render together.
- [ ] Persist, copy and undo scoped effects without changing unrelated events.
- [ ] Verify preview/export consistency and the UI selection state at every scope.

Status: queued.

## 5. General motion and mask tracking — high

- [ ] Reproduce forward, backward, frame-step and full-range tracking controls and overlays.
- [ ] Store reusable tracking samples in integer project frames with rational source conversion.
- [ ] Attach tracking to transforms, stabilization, picture-in-picture and masks where supported.
- [ ] Support mask drawing/editing and transfer of tracked motion with undo/redo and save/reopen.
- [ ] Verify preview and encoded output over moving subjects.

Status: queued.

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

- 2026-10-17: Ranked the nine feature gaps and defined completion criteria. The initial source and shipped-artifact audit is complete; no feature is checked yet. Baseline native integration and Release build passed. The aggregate managed test run ran out of memory; the serial run passed 426 tests with two desktop tests skipped.
- 2026-10-17: Implemented and pushed the first default Dissolve slice in `b960feb`, `903e8ac`, `4d2f8e6`, `8576c45`, and `0e7d27f`. Focused Core, Media, and desktop transition tests passed, including project save/reopen, undo/redo, CPU/D3D frame agreement, and encoded video pixels. The native integration test, Release build, and Core/Editor/Playback/Media suites passed under `tools/build.ps1`. Its desktop effects workflow failed at keyboard channel selection; a separate rerun of that test also failed, while the two other desktop tests passed. Full transition parity remains open.
- 2026-10-17 continuation: `2db8362`, `8a06a96`, `5e98cb4`, `3b41ce6`, and `957d0c0` added reduced-opacity blending, split preservation, overlap edit alignment, and adjacent placement. `5113b76` corrected preset ownership and selection routing. Seven focused Core checks and four focused Media checks passed; the app Release build passed with zero warnings/errors. Next: match preset selection/add/drag gestures and implement automatic overlap/ripple with verified timing.
- 2026-10-17 continuation: `d208fcd` added the preset selection/add/double-click/drag paths. The add and double-click desktop tests passed; drag passed after moving drag detection to the list that owns mouse capture. The target hit-test check passed for valid overlaps, locked tracks, and three-event ambiguity. `66a04d7` added cut conversion timing; four focused Core cases passed, covering all alignments, unchanged content timing, undo, and insufficient source handles. Next: expose cut conversion through editing preferences and drop placement, then finish one-event automatic placement/ripple and alpha processing.
- 2026-10-17 continuation: `ea60d44` fixed the desktop test's open channel list before its keyboard checks; the effects workflow passed. `fd236ad` and `5d9df36` added saved cut preferences, the Editing panel, and cut drop placement. UI screenshots were reviewed and label contrast, spinner styling, tab position, and dark caption corrected. Native integration passed; `tools/build.ps1 -SkipNative` then passed all 450 managed tests, including six desktop workflows, with zero build warnings/errors. `965d308` moved saves to project version 5; all 167 Core tests and the cut-drop desktop workflow passed again afterward. Next: verify factory defaults, automatic one-event placement/ripple and native take-edge behavior; finish source alpha handling and broaden the preset catalog/UI.
