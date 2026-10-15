# Feature status

Status applies to OpenRend's own implementation and stated test cases. It does not establish exhaustive behavior or performance across all media.

| Area | Status | Evidence / limits |
| --- | --- | --- |
| Rational project frame grid | Tested | Long frame roundtrip and integer boundaries in core tests |
| Placement, move, trim, split | Tested | Domain tests; splits preserve fade envelopes |
| Undo/redo and locks | Tested | Validation failures roll back, redo branches reset |
| Mute, solo, visibility | Tested | Shared evaluator and real media export tests |
| JSON projects, autosave, relink | Tested | Relative paths, atomic failure, unknown-version rejection |
| Legacy project import | Tested | Read-only conversion with ordering/audio/fade/group preservation |
| Linked groups | Tested | Atomic move/trim/split/delete, independent split groups, locked-member rollback |
| Cut / copy / paste | Tested | Group identity remapping, cross-project media and frame-rate conversion, source-end rounding, undo and locked-cut failure |
| FFmpeg decode and seek | Tested | Native tests, real H.264/FFV1/audio, UTF-8 paths and still frame retention |
| Stream timing / orientation | Tested | Delayed audio/video, nonzero origin, VFR holds, resampler EOF drain and quarter-turn matrices |
| Thumbnails / waveform peaks | Implemented | Source decoding; waveform is bounded sampled windows |
| Multitrack preview / export | Tested | Real trim/offset/split/opacity/mute/mix encoded output; ffprobe frame count |
| D3D11 preview / D3D11VA decode | Tested locally | Actual hardware device/decoder verified on development machine; CPU transfer remains |
| GPU timeline compositing | Tested locally | Hardware and WARP match CPU within one byte per channel; upload/readback remains |
| WASAPI synchronized playback | Tested locally | Real output clock seek/pause/drain, underrun accounting and loopback tone verification |
| WPF desktop editing | Tested locally | Live import/placement/decoded preview; FlaUI selection/track states/split/undo/redo/save/reopen/export and ffprobe validation |
| Timeline accessibility | Tested | SelectionItem and Invoke/Toggle providers, track/clip keyboard navigation |
| Track opacity / gain / pan | Tested | Atomic level edits, lock checks, persistence/default compatibility, accessible range sliders and actual encoded pixels/stereo samples |
| Preview display controls | Tested | Monitor zoom, safe-area overlay and drop-frame timecode; snapshots use project-resolution render |
| Docking | Partial | Splitters and persistent sizing; arbitrary redocking pending |
| Effects | Partial | Opacity, gain, pan and fades; transforms/crop/keyframes/titles pending |
| Proxies, advanced cache, scripting, plugin hosting | Planned | No placeholder UI controls |
| Broad media/performance qualification | Pending | No professional-scale throughput or full format coverage claim |
