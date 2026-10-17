# Feature status

Status applies to OpenRend's own implementation and stated test cases. It does not establish exhaustive behavior or performance across all media.

| Area | Status | Evidence / limits |
| --- | --- | --- |
| Rational project frame grid | Tested | Long frame roundtrip and integer boundaries in core tests |
| Placement, move, trim, split | Tested | Domain tests; splits preserve fade envelopes |
| Undo/redo and locks | Tested | Validation failures roll back, redo branches reset |
| Mute, solo, visibility | Tested | Shared evaluator and real media export tests |
| JSON projects, autosave, relink | Tested | Relative paths, concurrent reader snapshots, ordered save requests, blocked-save preservation, source overwrite rejection and unknown-version rejection |
| Legacy project import | Tested | Read-only conversion with ordering/audio/fade/group preservation |
| Linked groups | Tested | Atomic move/trim/split/delete, independent split groups, locked-member rollback |
| Cut / copy / paste | Tested | Group identity remapping, cross-project media and frame-rate conversion, source-end rounding, undo and locked-cut failure |
| FFmpeg decode and seek | Tested | Native tests, real H.264/FFV1/audio, UTF-8 paths and still frame retention |
| Stream timing / orientation | Tested | Delayed audio/video, nonzero origin, VFR holds, resampler EOF drain and quarter-turn matrices |
| Thumbnails / waveform peaks | Implemented | Source decoding; waveform is bounded sampled windows |
| Multitrack preview / export | Tested | Real trim/offset/split/opacity/mute/mix encoded output; ffprobe frame count |
| Render templates / ranges | Partial, tested locally | Two project-size/rate H.264/AAC presets, property search, saved favorites/choices, loop selection by ruler or I/O, encoded frame/sample checks at integer and rational rates, and desktop loop export. Full format/custom template catalog, mapping, metadata/log options and loop persistence/playback remain open. See [rendering](rendering.md). |
| D3D11 preview / D3D11VA decode | Tested locally | Actual hardware device/decoder verified on development machine; CPU transfer remains |
| GPU timeline compositing | Tested locally | Hardware and WARP match CPU within one byte per channel; upload/readback remains |
| WASAPI synchronized playback | Tested locally | Real output clock seek/pause/drain, underrun accounting and loopback tone verification |
| WPF desktop editing | Tested locally | Native preview pixels advance during playback and restore on Stop; edge hover/held resize cursor, outside-edge trim, Escape cancellation, selection/track states/split/undo/redo/save/reopen/export and ffprobe validation |
| Timeline accessibility | Tested | SelectionItem and Invoke/Toggle providers, track/clip keyboard navigation |
| Event buttons and options | Tested locally | Pan/Crop, video FX and overflow actions target the clicked event; locked/narrow/clipped cases, accessible actions after zoom/scroll/resize, and session-only header/name/length options |
| Track opacity / gain / pan | Tested | Atomic level edits, lock checks, persistence/default compatibility, accessible range sliders and actual encoded pixels/stereo samples |
| Preview display controls | Tested | Monitor zoom, safe-area overlay and drop-frame timecode; snapshots use project-resolution render |
| Event Pan/Crop | Tested locally | Source-pixel rectangle, rotation/pivot, aspect lock, interactive workspace and six temporal curves with right-click/keyboard selection; integer-frame dragging/retiming and collision preservation, persistence, trim/split/clipboard continuity, GPU/CPU comparison and actual encoded output |
| Docking | Partial | Splitters and persistent sizing; arbitrary redocking pending |
| Event video effects | Tested | 62 parameterized built-ins (including 9 AI effects); bypass, locks, undo/redo, independent split state, version-4 persistence, deterministic noise/grain, alpha-safe filtering, CPU/D3D comparison and encoded export. See the [supported catalog](video-effects.md). |
| Video FX chooser | Tested locally | Category/search filters and staged Add/Replace/Remove; Cancel isolation and accepted chain as one undo step |
| AI Tools (TTS, STT, Auto Reframe) | Tested locally | AI Text to Speech (Ctrl+Shift+P) with Kokoro/StyleTTS/SAPI voice models and timeline/media pool placement; AI Speech to Text with subtitle transcription, find/replace, SubRip (.srt) export and Subtitles from Speech track; AI Auto Reframe with 9:16/1:1/4:5/4:3/16:9/2.4:1 presets, subject tracking and smooth Pan/Crop keyframes |
| Effects | Partial | Opacity, gain, pan, fades, animated crop and built-in video FX; effect keyframes, spatial spline smoothing, masks and titles pending |
| Proxies, advanced cache, scripting, plugin hosting | Planned | No placeholder UI controls |
| Broad media/performance qualification | Pending | No professional-scale throughput or full format coverage claim |
