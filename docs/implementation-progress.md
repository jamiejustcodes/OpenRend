# Feature implementation progress

This is the importance-ordered work list for OpenRend's editor. A feature is checked only when its verified controls and interactions work end to end, its edit decisions survive save/reopen and undo/redo, and preview and export agree. Evidence for comparisons with other editors stays in local research notes outside version control. Record commits and test results here as work lands.

## 1. Event transitions and overlap editing — critical

- [ ] Reproduce the transition browser's categories, preset selection and placement interactions.
- [ ] Support overlap transitions for one event, two events, selected events and adjacent events, including ripple behavior.
- [ ] Persist transition settings in project frames and preserve them through move, trim, split, clipboard, save/reopen and undo/redo.
- [ ] Evaluate each supported transition identically in preview and export.
- [ ] Verify the desktop workflow and encoded output.

Status: default Dissolve slice in progress. OpenRend now stores a transition on the two event fades, applies it to a selected overlap, shows one supported preset in a docked browser, and uses the timeline evaluator for preview and export. The full preset catalog, one-event/adjacent/ripple workflows, drag placement, and edit preservation still need work.

## 2. Render templates and range options — critical

- [ ] Reproduce the render dialog's template search, filtering and selection controls for supported output formats.
- [ ] Support render range, channel mapping and relevant metadata/log options where OpenRend has the underlying data.
- [ ] Persist render presets and validate options before starting an export.
- [ ] Verify duration, frame count, audio and output metadata against actual files.

Status: queued.

## 3. Color grading, curves and LUTs — high

- [ ] Reproduce the grading panel's wheel, curve, white-balance and LUT controls.
- [ ] Persist grading values and LUT references without changing source media.
- [ ] Match processing order and interpolation in preview and export.
- [ ] Verify representative scopes, pixels, save/reopen and undo/redo.

Status: queued.

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
