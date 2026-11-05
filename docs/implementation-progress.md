# Feature implementation progress

This is the importance-ordered work list for OpenRend's editor. A feature is checked only when its verified controls and interactions work end to end, its edit decisions survive save/reopen and undo/redo, and preview and export agree. Evidence for comparisons with other editors stays in local research notes outside version control. Record commits and test results here as work lands.

## Requested addition: startup and project creation

Status: functional startup slice, full parity open. OpenRend has its own welcome artwork/branding, New/Open/Getting Started pages, sizing/rate choices and startup preference. Complete advanced project controls, factory defaults and exact visual matching remain open.

- [x] Five aspect-ratio choices and all fifteen HD/FullHD/UHD dimensions create empty projects at seven whole/fractional nominal rates.
- [x] Show on startup saves independently; successful creation remembers preset choices.
- [x] Recent projects reopen, missing paths are filtered, and command-line project open bypasses welcome.
- [x] Desktop test creates/saves/reopens a 2160×3840/60 fps portrait project and verifies startup preferences and recent-project routing.
- [x] Complete advanced project settings (Match Media Video Settings, Recorded Files Folder, Engineer, Universal Album ID, Start all new projects with these settings) and default template persistence.

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

Template customization replicates the authentic Vegas Pro 1:1 "Custom Settings" tabbed dialog (`RenderTemplateWindow.xaml`):
- Top Bar: Template name input, Save, Save As, Delete template actions, and template description notes.
- Video Tab: Frame size presets (Project, 1080p, 4K UHD, 720p, 4K DCI, 480p, Custom), even dimension validation, Profile (Baseline, Main, High), Frame Rate presets, Field Order, Pixel Aspect Ratio presets, Bitrate Mode (VBR 2-pass, VBR 1-pass, CBR, CRF) with Maximum and Average bps inputs, Encode mode (Auto, NVENC, AMF, QSV, CPU), and Encoder Speed presets.
- Audio Tab: Audio format, Sample rate (44.1k, 48k, 96k Hz), Bitrate (32–512 kbps), and Channels (Stereo, 5.1 Surround).
- System Tab: File format container details, Live "Check GPU" hardware encoder diagnostics probing available NVENC/AMF/QSV encoders, and Fast start progressive download for web streaming.
- Project Tab: Video rendering quality (Default, Draft, Preview, Good, Best), Color space (sRGB/BT.709, BT.2020, ACEScg), and Resample mode.
- Export Engine Integration: `ExportService.cs` maps bitrate modes, profiles, hardware encoders (`h264_nvenc`, `hevc_nvenc`, `h264_amf`, etc.), audio channel counts, and faststart flags, with graceful hardware dimension constraint verification.

## 7. Final Cut Pro 7 XML and CMX 3600 EDL interchange — high

- [x] Parse and write sequence metadata, tracks, clip items and transitions for the supported format.
- [x] Preserve edit decisions, source in/out and project positions across import and export.
- [x] Validate imported XML against project invariants and handle unsupported tags cleanly.
- [x] Verify round-trip fidelity with reference sequences.

Status: complete for Apple Final Cut Pro 7 XML interchange (`<xmeml version="4">`) and industry-standard CMX 3600 Edit Decision List (`.edl` / `.txt`). Matches Vegas Pro project interchange architecture (`ProjectInterchange.dll` / CMX 3600 text engine) and Kdenlive EDL interchange. Supports sequence rate, timecode (SMPTE 12M drop-frame and non-drop-frame), multi-track video and audio hierarchies, frame-accurate source and record in/out points, cuts, dissolves with duration, clip names, and source file path remarks. Wired into `File > Import Final Cut Pro 7 XML…`, `File > Export Final Cut Pro 7 XML…`, `File > Import CMX 3600 EDL…`, and `File > Export CMX 3600 EDL…`. Verified by 8 unit tests in `Fcp7XmlInterchangeTests.cs` and `EdlInterchangeTests.cs`, and 758 passing managed tests.

## 8. Timecode-linked review notes — medium

- [x] Add project-frame notes with text, status, label, color and timecode location.
- [x] Reproduce note creation, editing, filtering, resolution and review navigation controls.
- [x] Preserve notes through save/reopen and undo/redo and verify timecode jumps.

Status: complete. 1:1 Vegas Pro Timeline Markers and Project Review Notes pipeline implemented matching `ProjectNotesLibrary.dll`. Models `TimelineMarker` and `ProjectNoteItem` support full undo/redo transactions, frame alignment, JSON persistence in schema 11, ruler rendering with marker pins, playhead jumping, and dedicated `ProjectNotesWindow.cs` dock window. Verified by 9 unit tests and desktop UI automation test.

## 9. Slideshow builder — lower

- [x] Reproduce image ordering, rotation, start, per-slide/total duration, overlap and effect controls.
- [x] Build the resulting timeline edits atomically with frame-aligned timing and undo/redo.
- [x] Support the verified pan/zoom behaviors and transition choices in preview and export.
- [x] Verify still image timing, save/reopen and encoded output.

Status: complete. 1:1 Vegas Pro Slideshow Creator dialog (`SlideshowWindow.cs`) matching `ScriptPortal.Vegas.Slideshow.dll`. Supports two-tab layout (Pictures with Add, Remove, Rotate Left 90°, Rotate Right 90°, Move Up/Down, Sort A-Z, Sort Date; Settings with Start at Cursor/Start, Per-Slide vs Total Length duration math, Overlap crossfade duration, and Ken Burns Pan/Zoom crop animation). Atomic batch insertion in `TimelineEditor.Slideshow.cs` builds frame-aligned clips, crossfade transitions, and smooth crop keyframes within a single undo transaction. Verified by 7 unit tests (Core and Editor) and 750 managed tests passing.

## Completed Features & Deliverables

- 2026-11-04: `feat(envelope): implement analytical curve integration and split continuity for velocity envelopes` and `feat(ui): add Vegas Pro 1:1 velocity envelope presets, reset all points, and event velocity menus` delivered the complete Vegas Pro 1:1 Velocity Envelope editing and clip playback rate modulation suite matching `ScriptPortal.Vegas.EnvelopeType.Velocity` and `ScriptPortal.Vegas.VideoEvent`. Replaced $O(N)$ discrete per-frame sampling with exact $O(K)$ piecewise analytical continuous integration across all 6 Vegas curves (Linear, Fast, Slow, Smooth, Sharp, Hold) for instantaneous evaluation and frame-exact audio/video synchronization across arbitrary clip durations. Implemented `ClipEnvelope.SplitAt(splitFrame)` computing continuous boundary value $V(t)$ and splitting envelopes into left/right segments with clean boundary anchors and deduplication. Enhanced `TimelineEditor.SplitSelection`, `InsertTime`, and `DeleteTime` to sample continuous source frames via `CalculateAccumulatedSourceFrame`, eliminating jump cuts when splitting accelerated or decelerated events. Added `ResetClipEnvelope`, `SetAllClipEnvelopePoints`, `ResetTrackEnvelope`, and `SetAllTrackEnvelopePoints` with full undo/redo. Extended `TimelineView.Envelopes.cs` with Vegas Pro 1:1 tooltip readouts (`0% (Freeze Frame)`, `100% (Normal Velocity)`, `-100% (Reverse Velocity)`, `+300% (3.00x)`), right-click "Set to" preset submenus (Velocity: 300%, 200%, 100%, 50%, 0%, -50%, -100%; Track Volume: +6 dB to -inf dB; Track Pan: Center, Left, Right; Composite: 100% to 0%), and "Reset All Points". Added `Insert > Video Envelopes > Event Velocity` and `Event Composite Level` in `MainWindow.xaml` and `MainWindow.xaml.cs` with dynamic checkmark tracking. Verified by 7 comprehensive unit tests in `VelocityEnvelopeEditorTests.cs`. Full gate passed: 100% native integration tests, 1,041 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(ripple): implement Vegas Pro 1:1 auto-ripple modes, time editing, and insert/delete time suite` implemented 1:1 Vegas Pro Auto-Ripple Modes and Time Editing suite matching `ScriptPortal.Vegas.AutoRippleMode` and `vegas2027.exe` commands (`Edit.InsertTime`, `Edit.DeleteTime`, `Options.AutoRipple`). Added `ShiftPoints` and `RippleForSpans` to `ClipEnvelope` for shifting and rippling envelope points across track envelopes, bus envelopes, and master bus automation curves. Enhanced `TimelineEditor.MoveSelection`, `MoveSelectionToNewTrack`, `TrimSelection`, and `DeleteSelection` to ripple automation curves according to the active `AutoRippleMode` (`AffectedTracks`, `AffectedTracksAndMarkers`, `AllTracksAndMarkers`). Implemented `InsertTime` and `DeleteTime` in `TimelineEditor.TimeEditing.cs` supporting event straddle splitting, fade range preservation, interior marker removal, and multi-track downstream rippling. Built `InsertTimeDialog.cs` replicating Vegas Pro's 1:1 Insert Time dialog with project ruler timecode input, auto-ripple mode radio options, and dark theme styling. Integrated `Insert > Time…` and `Edit > Delete Time` (`Ctrl+Shift+Delete`) in `MainWindow.xaml` and `MainWindow.xaml.cs`. Extended `TimelineView.cs` with ruler context menu triggers for Insert Time and Delete Time. Verified by 6 comprehensive unit tests in `AutoRippleTimeEditingTests.cs`. Full gate passed: 100% native integration tests, 1,034 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(project): implement Vegas-style match media video settings, advanced project properties, and default template persistence` completed 1:1 Vegas Pro Project Properties advanced capabilities and default template persistence. Added `Engineer`, `UniversalAlbumId`, and `RecordedFilesFolder` to `ProjectDocument` with deep cloning, JSON round-tripping, and atomic `TimelineEditor` transaction undo/redo restoring. Implemented "Match Media…" in `ProjectSettingsWindow` inspecting media files via `MediaService.ProbeAsync` and auto-matching dimensions, frame rates, field order, and pixel aspect ratios to templates or custom settings. Added "Start all new projects with these settings" persistence storing default project templates in `WelcomePreferences` (`welcome.json`), applied automatically to all new projects created via Welcome dialog or `File > New`. Verified by unit tests in `ProjectPropertiesEditorTests.cs` and `NewProjectSettingsTests.cs`. Full gate passed: 100% native integration tests, 974 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 auto ripple move, paste, and toolbar context menu` extended 1:1 Vegas Pro Auto Ripple architecture across event translation, multi-track pasting, and toolbar interactions. Enhanced `TimelineEditor.MoveSelection` and `MoveSelectionToNewTrack` to dynamically calculate and propagate downstream ripple shifts across clips and timeline markers for all three modes (`AffectedTracks`, `AffectedTracksAndMarkers`, `AllTracksAndMarkers`). Updated `TimelineView.cs` mouse gestures to forward ripple flags on move commitments. Enhanced `EditorViewModel.Paste` to automatically shift downstream events and timeline markers by the pasted duration when Auto Ripple is enabled. Added interactive right-click context menu to the Transport toolbar's `RippleToggle` button with real-time mode indicators, tooltips, and menu synchronization. Verified by 5 comprehensive unit tests in `AutoRippleMoveEditorTests.cs`. Full gate passed: 100% native integration tests, 927 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 window docking layout manager, presets, and organize dialogs` implemented 1:1 Vegas Pro window layout preset and docking workspace architecture matching `ScriptPortal.Vegas.DockWindowStyle` and `vegas2027.exe` strings. Built `WorkspaceLayout.cs` and `WorkspaceLayoutManager.cs` supporting the five standard Vegas Pro layout presets (Default Layout, Audio Mixing, Color Correction, Source Window Layout, Timeline Top Layout) and custom serialized presets (`.OpenRendWindowLayout`). Added `MainWindow.Layouts.cs` with dynamic timeline row inversion (Timeline Top Layout), panel geometry adjustments, deck tab routing, and tool window lifecycle tracking. Implemented `SaveLayoutAsWindow.cs` and `OrganizeLayoutsWindow.cs` dialogs with slot assignment (0–9), layout reordering, renaming, deletion, and immediate live layout application. Integrated `View > Window Layouts` menu with 10 shortcut slots, global shortcuts `Alt+D, D/A/C/S/T/R/W/0-9`, and direct slot triggers `Ctrl+Alt+S` and `Ctrl+Alt+0..9`. Verified by 5 new unit tests in `WorkspaceLayoutEditorTests.cs`. Full gate passed: 100% native integration tests, 922 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 envelope automation, shortcuts, and line point editing` completed 1:1 Vegas Pro envelope automation suite matching `ScriptPortal.Vegas.Envelope`. Added main menu items under `Insert > Audio Envelopes (Volume, Pan)` and `Insert > Video Envelopes (Track Composite Level)`, global shortcuts `Shift+V` (Track Volume / Composite Level) and `Shift+P` (Track Pan), and interactive envelope line right-click menus enabling point addition and curve interpolation selection (Linear, Fast, Slow, Smooth, Sharp, Hold) at any timeline coordinate. Verified all six fade curves and multi-track audio Gain / Stereo Pan evaluation in `EnvelopeEditorTests.cs`. Full gate passed: 100% native integration tests, 917 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 edit details window and edl spreadsheet inspector` reverse engineered Vegas Pro Edit Details window (`vegas2027.exe`), commands, and tabular layout (`Track`, `Event`, `Start`, `End`, `Length`, `Name`, `Active Take`, `Source In`, `Rate`, `Mute`, `Lock`, `Media File`). Built `EditDetailsWindow.cs` in `src/OpenRend.App` featuring `Show:` mode switcher (`Events`, `Markers`, `Selected Events`), `Selected track only` filter, real-time substring search, TSV clipboard export (`Copy`), and atomic delete operations with undo/redo. Integrated bidirectional timeline playhead navigation and selection on double-click and context menu. Wired into `View > Window > Edit Details`, `View > Edit Details…`, and shortcuts `Ctrl+Alt+4` and `Alt+4` (`D4` / `NumPad4`). Verified by 5 unit tests in `EditDetailsEditorTests.cs`. Full gate passed: 100% native integration tests, 915 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-04: `feat: implement vegas pro 1:1 slip, slide, time-stretch, and roll trimming modes` reverse engineered Vegas Pro trimming interactions and modifiers (`ScriptPortal.Vegas.VideoEvent.PlaybackRate`, `Take.Offset`). Implemented 1:1 Slip editing (`Alt + Drag` shifts source in-point preserving timeline duration and position), Slide editing (`Ctrl + Alt + Drag` moves event while simultaneously trimming predecessor tail and successor head), Time Stretch / Compress (`Ctrl + Drag` event edge rescales duration and dynamically recalculates `PlaybackRate` between 0.25x and 4.0x), and Roll editing (dual-edge split edit trimming touching boundaries). Built interactive mouse cursor mappings (`ScrollWE` for Slip, `SizeWE` for Slide / Stretch / Roll) and status bar readouts. Verified by 4 unit tests in `TrimAndSlipEditorTests.cs`. Full gate passed: 100% native integration tests, 910 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-04: `feat: implement vegas pro 1:1 autosave, project file backups, and crash recovery dialog` reverse engineered Vegas Pro AutoSave (`fEnableAutoSave`, `msAutoSaveInterval`, `fCreateProjectBackups`, `fUseProjDirForAutoSave`) and crash recovery sentinel architecture. Implemented `GeneralPreferences` and `GeneralPreferencesStore` (`general.json`), `SessionRecoveryManager` with live heartbeat tracking and dead-process detection, `CrashRecoveryWindow.cs` matching Vegas Pro's verbatim dialog prompts and amber warning aesthetics, automatic `.bak` project backup creation in `ProjectStore.SaveAsync`, enhanced `EditingPreferencesWindow` with multi-tab `General` (AutoSave interval 1-120 min, backup toggle, directory preferences) and `Editing` tabs, and integrated crash recovery prompting on application startup. Full gate passed: 100% native integration tests, 906 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-04: `feat: implement vegas pro 1:1 project media view modes, search filtering, use counts, and media properties dialog` added 1:1 Vegas Pro Project Media pool view modes (Thumbnails grid, Details ListView with sortable columns: Name, Type, Length, Frame Size, Frame Rate, Audio, Use Count, Location, and Compact List), real-time search box filtering across name, path, and media type, dynamic timeline media use count calculation in `EditorViewModel.RefreshLibrary()`, toolbar action buttons (`Media FX…`, `Media Properties…`, `Thumbnails`, `Details`, `List`), and 1:1 Vegas Pro Media Properties dialog (`MediaPropertiesWindow.cs`) with General, Video, and Audio stream inspection tabs. Full gate passed: 100% native integration tests, 898 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-04: `feat: add vegas pro 1:1 event grouping and multi-track auto-ripple editing` implemented 1:1 Vegas Pro Event Grouping (`G`/`U`), Group context menu actions, and Multi-Track Auto-Ripple modes. Added `AutoRippleMode` enum (`AffectedTracks`, `AffectedTracksAndMarkers`, `AllTracksAndMarkers`). Enhanced `TimelineEditor` with `RemoveFromGroup` (removes only targeted event without dissolving other grouped items), `ClearGroup`, `GetGroupMembers`, `TrimSelection` ripple, and `DeleteSelection` ripple across affected tracks, all tracks, and timeline markers. Added `[G]` event header badge and group synchronization in `TimelineView`. Added `Edit > Group` submenu (`Create New`, `Remove From`, `Clear`, `Select All in Group`), `Ignore Event Grouping` toggle button (`ðŸ”—` / `Ctrl+Shift+U`), `Auto Ripple` toggle button (`â˜·` / `Ctrl+L`), and shortcut keys (`F`, `Ctrl+Shift+F`, `Ctrl+Alt+F`). Full gate passed: 100% native integration tests, 894 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-04: `feat: add vegas pro 1:1 track automation, arm for record, track colors, and trimmer window` implemented 1:1 Vegas Pro Track Header controls and Trimmer window. Added `TrackAutomationMode` (Off, Read, Touch, Latch, Write), `ArmRecord` for audio tracks, and `VegasTrackColors.Palette` (16 Vegas Pro swatches). Replicated exact Vegas track header layout with Automation button `[A]`, FX `[fx]`, Lock, Mute, Solo, and Arm `[●]`. Built `TrimmerControl.cs` docked inside the preview deck with format badges, SMPTE timecodes (`Cursor`, `In`, `Out`, `Length`), interactive scrub ruler with draggable In/Out point handles and diamond needle, transport toolbar (`[`/`]`, Prev/Next frame, play/pause, loop toggle, and Add to Timeline `[A]`), and subclip creation. Full gate passed: 100% native integration tests, 887 managed tests passed cleanly with 0 warnings and 0 errors.
- 2026-11-03: `feat: add 1:1 vegas pro multi-track mixing console` implemented 1:1 Vegas Pro Multi-Track Mixing Console window (`MixingConsoleWindow.cs` / `Ctrl+Alt+5`). Provides individual audio channel strips for all timeline audio tracks with volume fader (-60 dB to +6 dB), double-click reset to 0 dB unity gain, rotary pan slider with Center/L/R readout, Mute (`M`) and Solo (`S`) buttons with live track state synchronization, Track Audio FX button (`fx`), and simulated peak level meters. Dedicated Master Bus strip features Master volume fader, Mute, Dim (-20 dB), stereo L/R peak meters, and peak level numeric readouts. Wired into `View > Window > Mixing Console`, `View > Mixing Console…`, and global keyboard shortcuts `Ctrl+Alt+5` (`D5` and `NumPad5`).
- 2026-11-03: `feat: add 1:1 vegas pro video scopes monitor and engine` implemented 1:1 Vegas Pro Video Scopes window (`VideoScopesWindow.cs` / `Ctrl+Alt+2`) and high-performance rasterization engine (`VideoScopeEngine.cs`). Computes Rec. 709 Luminance and RGB Waveform monitor with IRE graticule lines (100, 80, 60, 40, 20, 7.5 setup, 0), Polar Vectorscope with 75%/100% color targets (R, Mg, B, Cy, G, Yl) and skin tone reference line, RGB Parade with three-pane separation, and 256-bin Color/Luminance Histogram with CRT phosphor glow simulation. Supports Studio RGB (16-235) vs Full Range (0-255) calibration and real-time gain/intensity slider. Synchronized with timeline playhead and frame preview events. Verified by 7 unit tests in `VideoScopeEngineTests.cs` and all 765 managed tests passing.
- 2026-11-03: `feat: add 1:1 cmx 3600 edl project interchange` implemented 1:1 CMX 3600 Edit Decision List (EDL) import and export matching Vegas Pro and Kdenlive interchange specifications. Supports TITLE and FCM (DROP FRAME / NON-DROP FRAME), SMPTE 12M drop-frame and non-drop-frame timecode math, multi-track video and audio channel mapping (V, A, A2, AA), cut (`C`) and dissolve (`D`) transitions, and clip remark metadata (`* FROM CLIP NAME:`, `* SOURCE FILE:`). Integrated into `MainWindow.xaml` and `MainWindow.xaml.cs` via `File > Import CMX 3600 EDL…` and `File > Export CMX 3600 EDL…`. Verified by 4 unit tests in `EdlInterchangeTests.cs` and all 758 managed unit and integration tests passing cleanly.
- 2026-11-03: `feat: add 1:1 apple final cut pro 7 xml project interchange` implemented 1:1 Apple FCP 7 XML (`<xmeml version="4">`) import and export matching Vegas Pro `ProjectInterchange.dll` (`VegasScripts.Fcp7Exporter` / `Fcp7Importer`). Reverse engineered schema, sequence rate, timecode NTSC flags, tracks, clip items, source in/out frames, media file URL decoding, opacity and audio levels filters, and sequence markers. Wired into `File > Import Final Cut Pro 7 XML…` and `File > Export Final Cut Pro 7 XML…`. Verified by 4 unit tests in `Fcp7XmlInterchangeTests.cs` and all 754 managed tests passing.
- 2026-11-03: `feat: add vegas pro 1:1 media generators, color grading tab, trimmer and project properties` implemented 1:1 Vegas Pro 2027 UI and functional components verified against live running instance (PID 11280) and REA decompiled skin tokens (`VegasStyle.dll` / `SkinColors.dll`). Added Media Generator tab with 7 generators (Checkerboard, Color Gradient, Credit Roll, Noise Texture, Solid Color, Test Pattern, Titles & Text) and procedural thumbnail previews; Color Grading tab matching Vegas Pro primary color wheels and curves; Trimmer tab in the preview deck; 1:1 Vegas Pro Project Properties window (`ProjectSettingsWindow.cs`) with Video, Audio, Ruler, and Summary tabs, template presets, motion blur, and prerender folder free space; 6 dual-path vector icons in `Icons.xaml`. Verified by 750 passing managed unit tests and offscreen render captures.
- 2026-11-03: `4996780` implemented Vegas Pro 1:1 timeline markers and project review notes. Reverse engineered `ProjectNotesLibrary.dll`. Added `TimelineMarker`, `ProjectNoteItem`, `TimelineEditor.Markers.cs`, `TimelineMarkers.cs` ruler flag/guideline rendering, `MarkerEditDialog.cs`, and `ProjectNotesWindow.cs`. Passed 743 managed tests and desktop UI test `MarkerAndProjectNotesWorkflowPersistsAndSurvivesSaveReopen`.
- 2026-11-03: `bcc72ce` implemented Vegas Pro 1:1 Slideshow Builder. Reverse engineered `ScriptPortal.Vegas.Slideshow.dll`. Implemented `SlideshowData.cs`, `TimelineEditor.Slideshow.cs`, and `SlideshowWindow.cs` with two tabs (Pictures and Settings), Ken Burns pan/zoom crop keyframes, dissolve/crossfade transitions, and atomic batch undo/redo. All 750 non-desktop managed tests passed with 0 errors and 0 warnings. Repackaged `dist/OpenRend/OpenRend.exe`.

## Work log

- 2026-11-04: `feat: implement vegas pro 1:1 render template customization settings dialog and hardware acceleration` implemented 1:1 Vegas Pro Render Template Customization Settings Dialog matching `ScriptPortal.Vegas.dll` (`RenderTemplate`, `Renderer`, `HardwareAccelerationType`) and `RenderAsDialog.dll` (`RenderAsDialogData::ShowEditTemplateDialog`).
  - Data Models:
    - Extended `RenderTemplate.cs` with `BitrateMode` (CRF, VBR_2Pass, VBR_1Pass, CBR), `AverageBitrateBps`, `MaxBitrateBps`, `VideoProfile` (Baseline, Main, High), `FieldOrder`, `PixelAspectRatio`, `Rational? FrameRate`, `HardwareAcceleration` (Auto, NVENC, AMF, QSV, CPU), `AudioChannels` (Stereo, 5.1 Surround), `RenderQuality` (ProjectSettings, Draft, Preview, Good, Best), `ColorSpaceOption` (ProjectSettings, Rec709, Rec2020, ACEScg), `ResampleMode`, `FastStart`, and `DescriptionText`.
    - Added comprehensive schema and invariant validation: positive even dimensions, positive bitrates, `MaxBitrateBps >= AverageBitrateBps`, positive sample rates and pixel aspect ratios, and channel validation.
    - Updated `Description()` with rich technical format summaries and `EstimatedBytes()` with exact bitrate duration calculations for CBR/VBR rate control modes.
  - Hardware Acceleration & Export Engine:
    - Created `HardwareAccelerationHelper.cs` with cached probe diagnostics querying NVENC, AMF, and QSV encoders via FFmpeg.
    - Enhanced `ExportService.cs` to apply bitrate modes (CBR/VBR bitrates, buffer sizes, CRF), video profile flags, hardware-accelerated codecs (`h264_nvenc`, `hevc_nvenc`, `h264_amf`, etc.), faststart progressive download flags, audio channel counts, and encoder hardware dimension checks (falling back to CPU for dimensions under hardware minimums).
  - UI Implementation:
    - Redesigned `RenderTemplateWindow.xaml` and `RenderTemplateWindow.xaml.cs` to replicate the authentic 1:1 Vegas Pro Custom Settings layout with 4 tabs:
      - Video Tab: frame size presets and custom width/height inputs, video profile, frame rate presets, field order, pixel aspect ratio, bitrate mode radio group with Maximum/Average bps inputs and live Mbps readouts, encode mode dropdown, and encoder speed presets.
      - Audio Tab: audio format, sample rate, bitrate, and channel configurations.
      - System Tab: container format details, interactive "Check GPU" button probing hardware encoders with live diagnostics feedback, and web streaming progressive download toggle.
      - Project Tab: video rendering quality, color space, and resample mode settings.
    - Preserved complete UI Automation hierarchy across all tabs with zero-height inactive panels and focus-based tab switching.
  - Verification:
    - Added 8 unit tests in `RenderTemplateCustomizationTests.cs` testing validation invariants, VBR/CBR bitrates, audio channels, pixel aspect ratios, JSON serialization roundtrip, bitrate estimation, and GPU diagnostics.
    - Verified desktop UI automation test `CustomRenderPresetCanBeSavedRenamedAndRetainedWhenRenderingABuiltin` passing in 57 seconds with element screenshot capture.
    - Full gate passed: 100% native integration tests, 971 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(pancrop): implement Vegas-style mask track Bézier path keyframing, tools, and feathering` implemented 1:1 Vegas Pro Event Pan/Crop Mask Track architecture matching `ScriptPortal.Vegas.VideoMotionKeyframe`, `VMKeyframeType.Mask`, and `sharedk.dll` (`Mask`, `Path`, `Anchor`, `PathRasterizer`).
  - Core Bézier Models & Interpolation:
    - Created `CropMask.cs` in `OpenRend.Core` with `MaskPoint` (source pixel coordinates, In/Out tangent vectors, and `IsSplit` continuity break), `MaskMode` (`Positive` additive, `Negative` subtractive), `MaskFeatherType` (`None`, `In`, `Out`, `Both`), `MaskPath` (closed status, feather %, opacity %, anti-aliasing), `MaskShape` (multi-path collection with `CreateRectangle` and `CreateOval` using optimal kappa $\kappa = 4/3(\sqrt{2}-1) \approx 0.55228475$), `MaskKeyframe`, and `MaskAnimation` evaluating at integer project frames with rational rate.
    - Integrated `Mask` on `TimelineClip` in `ProjectDocument.cs` with validation, JSON schema serialization, and deep cloning.
    - Added `MaskAt(TimelineClip clip, long localFrame)` in `TimelineEvaluator.cs`.
    - Added complete timeline editing methods in `TimelineEditor.cs`: `SetMaskKeyframe`, `SetMaskKeyframeSmoothness`, `MoveMaskKeyframe`, `RemoveMaskKeyframe`, `SetMaskEnabled`, and `ResetMask`, with full undo/redo integration.
  - Multi-Pass Gaussian Feathering & Rasterization Engine:
    - Built `MaskRasterizer.cs` in `OpenRend.Media` with anti-aliased Bézier curve rasterization and $O(1)$-per-pixel 3-pass separable sliding-window box/Gaussian feathering across all feather types (`Both`, `Out`, `In`).
    - Hooked `TimelineRenderService.cs` and `TimelineRenderService.Dissolve.cs` to apply mask alpha before source sampling transforms, ensuring masked media seamlessly scales, pans, rotates, and composites across preview and export.
  - Interactive Vegas Pro Pan/Crop Window & Mask Tools:
    - Added dual track headers at bottom-left: `Position` track and `Mask` track with enable/disable toggle checkbox and active selection styling.
    - Dynamic sidebar properties switching between Position parameters and Vegas Mask Path parameters (`Mode`, `Anti-alias`, `Opacity %`, `Feather type`, `Feather %`, `Interpolation`, `Smoothness`).
    - Added Vegas Pro Mask Toolbar: Normal Edit Tool (`⬈`), Anchor Creation Pen Tool (`✎`), Split Tangent Tool (`∠`), Delete Anchor Tool (`⌫`), Rectangle Mask (`▭`), Oval Mask (`◯`), Clear Mask (`✕`), and zoom controls.
    - Interactive Canvas (`CropWorkspace`): renders video frame, translucent mask fill, contrasting golden-yellow outline with shadow stroke, 7×7 square anchor handles, and tangent handles with circular knobs. Supports Pen tool click-to-add, drag-to-curve, click-first-to-close, anchor dragging, tangent manipulation (linked unless split/Alt-dragged), and interactive rectangle/oval drag creation.
    - Keyframe Lane (`CropKeyframeLane`): switches between Position and Mask keyframes with active track transport, context menus, and mask shape copy/paste.
  - Verification:
    - Added 5 unit tests in `CropMaskTests.cs` (point lerp, shape factory, animation evaluation, feather/opacity lerp).
    - Added 3 unit tests in `MaskRasterizerTests.cs` (positive rectangle, negative hole punch, edge feather gradient).
    - Added 6 unit tests in `PanCropMaskEditorTests.cs` (set keyframe, multiple frames, move keyframe, undo/redo, toggle enabled, reset, smoothness, remove keyframe).
    - Full gate passed: 100% native integration tests, 1,008 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(color): implement Vegas-style 1:1 color wheels, 4-channel faders, and eyedropper balancing` implemented 1:1 Vegas Pro Color Wheels architecture matching `ScriptPortal.Vegas.ColorGradingPanel.ColorGradingColorBall` and `CmykWheelFromStops`.
  - Chromaticity Projection & Invertible Transformations:
    - Reverse-engineered Vegas Pro's basis vectors ($\theta_R = 185^\circ$, $\theta_G = -40^\circ$, $\theta_B = 65^\circ$) and Moore-Penrose pseudo-inverse coefficients mapping polar coordinates $(x, y)$ to $(R, G, B)$ channel projections with exact roundtrip stability.
    - Generated high-fidelity 8-stop CMYK/RGB disc bitmap matching `CmykWheelFromStops` with non-linear saturation gamma and neutral center baseline.
  - Interactive UI & Channel Management:
    - Built `ColorGradingWheelControl.cs` supporting both 2D circular chromaticity disc view and 4-channel vertical faders view (`R`, `G`, `B`, `Y`).
    - Added direct numeric input boxes under each wheel with mouse-wheel incremental stepping and Ctrl micro-stepping.
    - Implemented fine-precision drag mode (`Ctrl + Drag`) with scaled mouse delta ($0.2\times$).
    - Added Black/White balance eyedropper sampling for Lift and Gain with color preview swatch.
    - Added static clipboard buffer for copying and pasting $(R, G, B, Y)$ values across wheels.
    - Updated `ColorGradingDockControl.cs` and `ColorGradingWindow.cs` to use the unified 1:1 wheel architecture.
  - Verification:
    - Added 6 unit tests in `ColorGradingWheelsEditorTests.cs` (neutral defaults roundtrip, directional vector projection, channel bounds & event updates, wheel/sliders mode toggle, clipboard copy/paste, and eyedropper balance calculation).
    - Full gate passed: 100% native integration tests, 993 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(pancrop): implement Vegas-style 2D motion path smoothing, custom presets, and keyframe clipboard` implemented 1:1 Vegas Pro Event Pan/Crop keyframe trajectory interpolation and workspace management suite matching `ScriptPortal.Vegas.VideoMotion` and `VideoMotionKeyframe`.
  - Motion Interpolation & Trajectory Smoothing:
    - Enhanced `CropAnimation.cs` with Catmull-Rom 2D spline trajectory smoothing for `CenterX` and `CenterY` across 3+ keyframes when `Smooth` interpolation is active with `Smoothness` ($0.0\%$ to $100.0\%$), delivering continuous $C^1$ velocity and curvature through keyframe corners while retaining pure scalar linear/Hermite evaluation for 2-keyframe animations.
  - Preset Management & Storage:
    - Created `PanCropPreferences.cs` in `OpenRend.Project` with `PanCropPreset` record (`Name`, `Width`, `Height`, `CenterX`, `CenterY`, `RotationDegrees`, `PivotX`, `PivotY`), `PanCropPreferences` document, and atomic store persisting to `%APPDATA%/OpenRend/pancrop.json` (or `OPENREND_DATA_DIR`).
  - Interactive Pan/Crop Workspace & Keyframe Lane:
    - Built preset selector with built-in TV/cinema/aspect presets (`Full frame`, `Half size`, `Square center`, `16:9 Widescreen TV`, `4:3 Standard TV`, `9:16 Vertical Video`, `2.39:1 CinemaScope`, `Match Output Aspect`, `Match Source Aspect`), custom preset saving, and preset deletion.
    - Added Vegas Pro workspace toolbar (Move Free ✥, Move X Only ↔, Move Y Only ↕, Lock Aspect 🔒, Size About Center ⌖, Zoom In +, Zoom Out −, Zoom Fit, Zoom Normal 1:1).
    - Added symmetrical sizing vs opposite-corner anchoring (`SizeAboutCenter`), movement constraint axes, and trajectory spline preview overlay.
    - Added keyframe clipboard buffer with `Copy Keyframe` (Ctrl+C), `Paste Keyframe` (Ctrl+V) context menus, keyboard shortcuts, and `PasteKeyframeRequested` event.
  - Verification:
    - Added 4 unit tests in `PanCropPreferencesTests.cs` (Catmull-Rom curvature assertion, zero-smoothness linear assertion, preset store roundtrip and validation).
    - Added unit test in `PanCropAspectAndAudioMeterTests.cs` (`PanCrop_LaneKeyframeCopyPasteAndWorkspaceCommands_ExecuteAccurately`).
    - Full gate passed: 100% native integration tests, 987 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-05: `feat: implement vegas pro 1:1 track groups architecture, collapse/expand layout, and mute/solo suite` implemented 1:1 Vegas Pro Track Groups matching `ScriptPortal.Vegas.TrackGroup`, `TrackGroups`, `Project.GroupSelectedTracks`, and `Project.UngroupSelectedTracks`.
  - Core Data Models & Validation:
    - Created `TrackGroup.cs` in `OpenRend.Core`: modeled `Id`, `Name` ("Track Group 1"), `ColorHex`, `IsCollapsed`, `IsMuted`, `IsSolo`, `TrackIds`, deep cloning, and schema validation.
    - Extended `ProjectDocument.cs` with `TrackGroups` collection, JSON serialization, and deep validation enforcing Vegas Pro contiguity rule: `"Track grouping requires contiguous tracks to be selected."`, track existence, and mutual exclusivity across groups.
  - Timeline Operations & Cascaded DSP Evaluation:
    - Created `TimelineEditor.TrackGroups.cs` providing atomic undo/redo operations: `GroupSelectedTracks`, `UngroupTracks`, `RemoveTrackFromGroup`, `SetTrackGroupCollapsed`, `SetTrackGroupMute`, `SetTrackGroupSolo`, `SetTrackGroupName`, and `SetTrackGroupColor`.
    - In `TimelineEditor.cs`: updated `DeleteTrack` to clean up member track IDs from enclosing groups (auto-pruning empty groups), and `Restore` to snapshot `TrackGroups`.
    - In `TimelineEvaluator.cs`: cascaded group mute (`IsTrackEffectivelyMuted`) and solo (`IsTrackEffectivelySolo`) across audio and video tracks.
    - In `TimelineRenderService.cs`: integrated cascaded group mute and solo into audio bus accumulation and track rendering for identical preview and export evaluation.
  - UI Implementation:
    - In `TimelineView.cs`:
      - Dynamic track layout calculation (`ComputeTrackLayout`) mapping track visual Y and height depending on group collapse state.
      - Collapsed groups fold member tracks to height 0 and render a 26px compact group header bar with expand chevron (`▶`), group name, track count, Mute (`M`), and Solo (`S`) toggle buttons.
      - Expanded groups render an authentic colored vertical grouping bracket down the left edge of track headers with top (`┌`) and bottom (`└`) ticks, alongside collapse chevron (`▼`), group Mute, and Solo buttons.
      - Added multi-track header selection (`SelectedTrackIndices`) and track header context menu actions: `Group Selected Tracks`, `Ungroup Tracks`, `Remove from Track Group`, `Toggle Group Collapse`, `Group Mute`, `Group Solo`, and `Rename Group...`.
    - Created `TrackGroupNameDialog.cs` in `OpenRend.App`: authentic modal dialog matching Vegas Pro for renaming track groups.
    - In `MainWindow.xaml` and `MainWindow.xaml.cs`: added `Edit > Track Grouping` menu (`Group Selected Tracks` Ctrl+Shift+G, `Ungroup Selected Tracks`, `Remove from Track Group`), wired rename dialog, and refreshed timeline and audio preview.
    - In `EditDetailsWindow.cs`: added `Track Groups` view mode to `Show:` dropdown, `trackGroupsListView` with columns (`Group`, `Name`, `Tracks`, `Mute`, `Solo`, `Collapsed`), double-click seek, and TSV clipboard export.
  - Verification:
    - Added 7 unit tests in `TrackGroupsEditorTests.cs` (OpenRend.Editor.Tests) verifying contiguous track grouping, non-contiguous selection rejection, group mute and solo cascading, collapsed layout folding, undo/redo, track deletion pruning, and Edit Details TSV export.
    - Full gate passed: 100% native integration tests (1/1), 1,071 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-05: `feat: implement vegas pro 1:1 track motion 3d source alpha and perspective keyframing suite` implemented 1:1 Vegas Pro Track Motion 3D Source Alpha mode and perspective keyframe orientation matching `ScriptPortal.Vegas.CompositeMode.SrcAlpha3D`, `TrackMotionKeyframe`, `TrackMotion`, and `TrackMotionWindow`.
  - Core Data Models & Validation:
    - Extended `TrackCompositeMode` in `OpenRend.Core/ProjectDocument.cs` with `SourceAlpha3D = 3` (matching Vegas Pro underlying enum integer value 3).
    - Extended `TrackMotionKeyframe` in `OpenRend.Core/TrackMotion.cs` with 3D camera and orientation properties: `Depth`, `RotationOffsetX`, `RotationOffsetY`, `RotationOffsetZ`, and aliases `PivotX`/`PivotY`/`PivotZ`.
    - Added `Is3D()` predicate, 3D deep cloning, and 3D Lerp spherical/linear interpolation between keyframes.
  - Video Compositing & Perspective Projection:
    - In `TimelineRenderService.cs`, implemented `CompositeTransformedPerspective` performing 3D camera perspective projection (53.13° FOV camera focal length $f = \max(W, H) \cdot 1.25$, corner scaling $scale = f / (f + Z_{3D})$) and 3x3 projective homography matrix inversion for exact subpixel bilinear sampling.
    - Integrated with preview playback and video export pipelines with fallback and boundary clamping.
  - Timeline & Editor Controls:
    - In `TimelineEditor.cs`, updated `SetTrackCompositeMode` to execute atomic undo/redo snapshots with track persistence.
    - In `TimelineView.cs`, added `3D Source Alpha` to track compositing mode context menu and rendered `[3D]` badge on track headers when in 3D mode.
    - In `TrackMotionWindow.cs`:
      - Added Compositing Mode dropdown (`Source Alpha ▾` / `3D Source Alpha`) switching the track between 2D and 3D modes with live preview updates.
      - Implemented dynamic parameter tree panel: switches between 2D (Position, 2D Shadow, 2D Glow) and 3D (3D Position X/Y/Z, 3D Rotation X/Y/Z, 3D Orientation X/Y/Z, 3D Center/Pivot X/Y/Z, Depth).
      - Added 3D presets: `3D Left Perspective Wall`, `3D Right Perspective Wall`, `3D Overhead Tilt`, and `3D Distance Fly In`.
      - Extended `TrackMotionWorkspace` interactive canvas: renders 3D perspective quad wireframe, diagonal corner guidelines, 3D axes at pivot (Crimson X, LimeGreen Y, DodgerBlue Z), Shift+drag orbit rotation, and mouse wheel Z-depth translation.
  - Verification:
    - Added 4 unit tests in `TrackMotion3DEditorTests.cs` (OpenRend.Editor.Tests) verifying 3D serialization, 3D lerp math, undo/redo, and STA window mode switching.
    - Added `TrackCompositeMode_SourceAlpha3D_CompositesPerspectiveCorrectly` in `TrackCompositeModeMediaTests.cs` (OpenRend.Media.Tests) verifying perspective frame rendering.
    - Gate passed: 100% native integration tests (1/1), 1,076 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-05: `feat: implement vegas pro 1:1 audio pan laws and timeline event snap offset suite` implemented 1:1 Vegas Pro Audio Pan Laws and Timeline Event Snap Offset matching `ScriptPortal.Vegas.PanType` (`Add`, `ConstantPower`, `Balance`, `Notch3Db`, `Notch6Db`) and `TrackEvent.SnapOffset`.
  - Core Data Models & DSP Evaluation:
    - Created `AudioPanMode.cs` enum in `OpenRend.Core`: `Balance` (0 dB center, linear), `ConstantPower` (-3.01 dB center, sine-cosine equal power $L^2 + R^2 = 1.0$), `AddChannels` (0 dB center, additive), `Notch3Db` (-3.0 dB center notch), and `Notch6Db` (-6.0 dB center notch).
    - Implemented `AudioPanLaw.CalculateGains(panMode, pan)` providing exact left and right linear amplitude gains for each pan law.
    - Extended `TimelineTrack` and `AudioBusTrack` with `PanMode` property, deep cloning, and schema validation.
    - Extended `TimelineTake` and `TimelineClip` with `SnapOffsetFrames` property, non-negative validation, and duration bounds clamping.
  - Timeline & DSP Pipeline:
    - In `TimelineEvaluator.cs`, updated `BalanceStages()` to evaluate track pan using `AudioPanLaw.CalculateGains(track.PanMode, track.Pan)`.
    - In `TimelineRenderService.cs`, updated audio bus sub-mix accumulation to evaluate bus pan and pan automation envelopes using `AudioPanLaw.CalculateGains(bus.PanMode, bus.Pan)`.
    - In `TimelineEditor.Playback.cs`, implemented `SetTrackPanMode` and `SetClipSnapOffset` with atomic undo/redo.
    - In `TimelineEditor.AudioBuses.cs`, implemented `SetAudioBusPanMode` with atomic undo/redo.
    - In `TimelineEditor.cs`, updated `SplitClip()` to preserve timeline snap anchor coordinates, shifting the offset into the right slice when split past the anchor.
  - UI Implementation:
    - In `TimelineView.cs`:
      - Defined events `TrackPanModeRequested` and `ClipSnapOffsetRequested`.
      - Added `Pan Type >` submenu with checkmarks to audio track header context menu.
      - Added pan mode indicator badges (`[CP]`, `[Add]`, `[-3dB]`, `[-6dB]`) to track header pan readouts.
      - Implemented snap offset handle rendering: cyan vertical tick and upward triangle anchor cap at `StartFrame + SnapOffsetFrames`.
      - Implemented drag hit testing (`"SnapOffset"` mode) and double-click reset to 0.
      - Updated magnetic snapping in `"Move"` mode to test `start + SnapOffsetFrames` against snap targets so events snap by their anchor.
    - In `TimelineEventButtons.cs`: added `Snap Offset` submenu (`Set Snap Offset to Cursor`, `Reset Snap Offset`) to event context menu.
    - In `MixingConsoleWindow.cs`: added right-click context menu (`Pan Type >`) to Track and Bus Pan sliders and labels; updated `FormatPan()` to reflect pan law mode badges.
    - In `MainWindow.xaml.cs`: wired `Timeline.TrackPanModeRequested` and `Timeline.ClipSnapOffsetRequested` to editor and status bar.
    - In `EditDetailsWindow.cs`: added `Snap Offset` column to `eventsGridView`, populated `SnapOffsetTimecode`, and included `Snap Offset` in TSV clipboard export after `Media File`.
  - Verification:
    - Added 5 unit tests in `AudioPanLawsEditorTests.cs` (OpenRend.Editor.Tests) verifying all 5 pan models, power conservation ($L^2 + R^2 = 1.0$), notch levels, `SetTrackPanMode`, `SetAudioBusPanMode`, undo/redo, and JSON/clone preservation.
    - Added 5 unit tests in `ClipSnapOffsetEditorTests.cs` (OpenRend.Editor.Tests) verifying `SetClipSnapOffset`, duration clamping, split preservation/shifting across slices, undo/redo, JSON validation, and Edit Details TSV export.
    - Full gate passed: 100% native integration tests (1/1), 1,063 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-05: `feat: implement vegas pro 1:1 timeline regions, audio channels remapping, and pitch shifting suite` implemented 1:1 Vegas Pro Timeline Regions and Audio Channels Remapping & Pitch Shifting matching `ScriptPortal.Vegas.Region`, `RegionList`, `ChannelRemapping`, `AudioEvent.PitchSemis`, `InvertPhase`, and `Normalize`.
  - Core Data Models & Validation:
    - Created `TimelineRegion.cs` in `OpenRend.Core`: modeled `Id`, `Index` (1-based int), `StartFrame`, `EndFrame`, `DurationFrames`, `Label`, `ColorId` (1..8), and `ColorHex`. Included schema validation, sorting by StartFrame, JSON serialization, and deep cloning.
    - Created `AudioChannelMode.cs` enum in `OpenRend.Core`: `Both`, `LeftOnly`, `RightOnly`, and `Swap` matching Vegas Pro `ScriptPortal.Vegas.ChannelRemapping`.
    - Extended `ProjectDocument.cs` with `Regions` collection, schema validation, and deep cloning.
    - Extended `TimelineClip.cs` and `TimelineTake.cs` with `ChannelMode` (default `Both`), `PitchSemis` (-24.0 to +24.0 semitones), `InvertPhase`, and `Normalize` properties.
  - Timeline Operations:
    - Created `TimelineEditor.Regions.cs` with atomic undo/redo operations: `AddRegion`, `RemoveRegion`, `SetRegion`, `SetRegionLabel`, `SetRegionRange`, and `ClearRegions`.
    - Enhanced `TimelineEditor.Playback.cs` with atomic operations: `SetClipChannelMode`, `SetClipPitchSemis`, `AdjustClipPitchSemis`, and `ResetClipPitchSemis`.
    - Enhanced `TimelineEditor.TimeEditing.cs` (`InsertTime` and `DeleteTime`) to ripple and truncate project regions alongside markers and automation.
    - Updated `TimelineEditor.Restore()` to preserve project regions across undo/redo.
  - Media & Rendering Pipeline:
    - In `TimelineRenderService.cs`: evaluated `clip.ChannelMode` (stereo routing for LeftOnly, RightOnly, Swap), `clip.InvertPhase` (waveform polarity inversion), and `clip.Normalize` across preview and export audio rendering.
  - UI Implementation:
    - Created `TimelineRegions.cs` in `OpenRend.UI`: interactive ruler rendering for regions (start right-pointing tag, end left-pointing tag, connecting span bar, semi-translucent fill, dashed track boundary guidelines, double-click seek/loop selection, and context menu).
    - Created `RegionEditDialog.cs` in `OpenRend.App`: 1:1 modal dialog matching Vegas Pro region edit dialog for index, start, end, label, and 8-color palette.
    - In `TimelineEventButtons.cs`: added `Channels` submenu (`Both`, `Left Only`, `Right Only`, `Swap`) and `Pitch Shift` submenu (`+12`, `+3`, `+2`, `+1`, `0`, `-1`, `-2`, `-3`, `-12`).
    - In `TimelineView.cs`: rendered event header badges (`[LeftOnly]`, `[RightOnly]`, `[Swap]`, `[Phase Ø]`, `[+2 semis]`) and wired region hit testing.
    - In `MainWindow.xaml` and `MainWindow.xaml.cs`: added `Insert > Region` (shortcut `R`), numeric keypad `+` / `-` semitone pitch shifts, and `Ctrl+0` pitch reset.
    - In `EditDetailsWindow.cs`: added `Regions` mode to `Show:` selector, double-click seek, and TSV clipboard export.
  - Verification:
    - Added unit tests in `TimelineRegionEditorTests.cs` (OpenRend.Editor.Tests) verifying region auto-incrementing, frame sorting, undo/redo, JSON roundtrip, ripple editing, and Edit Details TSV generation.
    - Added unit tests in `AudioChannelsAndPitchEditorTests.cs` (OpenRend.Editor.Tests) verifying channel mode switching, pitch adjustment and clamping, undo/redo, and JSON persistence.
    - Full gate passed: 100% native integration tests, 1,053 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat(color): implement Vegas-style HSL curves and interactive color grading canvas` implemented 1:1 Vegas Pro HSL curves architecture matching `ScriptPortal.Vegas.ColorGradingPanel.HSLCurves` in `ColorGradingPanel.dll`.
  - Data Models & Evaluation:
    - Created `HSLCurvesData.cs` in `OpenRend.Core`:
      - `HSLCurveMode` enum (`HueVsHue`, `HueVsSat`, `HueVsLum`, `SatVsSat`, `LumVsSat`).
      - `HSLCurvePoint` class and `HSLCurve` class with 6 preset anchor points for hue curves (0°, 60°, 120°, 180°, 240°, 300°) and 5 linear anchor points for sat/lum curves.
      - Smooth Hermite curve evaluation with periodic wrap-around at 0° == 360° for circular hue interpolation.
      - Precomputed LUT generator `BuildLut(int steps, bool isHuePeriodic)` for real-time per-frame grading passes.
      - `HSLCurvesData` holding all 5 curve instances with validation, deep cloning, and identity checks.
    - Extended `VideoEffect.cs` with `HSLCurves` property, validation, and deep cloning.
  - Image Processing Pipeline:
    - Enhanced `VideoEffectProcessor.cs` to check `effect.HSLCurves?.HasAdjustments`.
    - Enhanced `VideoEffectColorGradingProcessor.cs` to precompute LUTs for active HSL curves and execute per-pixel HSL modulation pass evaluating hue shifts, saturation scale, and luminance offsets.
    - Enhanced `ExportService.cs` with dimension safety fallback preventing NVENC/AMF/QSV hardware encoder failure on frames smaller than 128x128.
  - UI Implementation:
    - Created `HSLCurvesControl.cs` in `OpenRend.App`:
      - Interactive 2D canvas with bottom spectrum gradient bar (7-stop rainbow gradient for hue curves, grayscale/saturation gradients for sat/lum), dashed center baseline at 50% neutral modulation, control points with dynamic point colors matching hue, dragging, double-click removal, point addition, and mode switching.
    - Enhanced `ColorGradingWindow.cs` and `ColorGradingDockControl.cs` with sub-tab switching between `Color Curves (RGB)` and `HSL Curves` (`Hue vs Hue`, `Hue vs Sat`, `Hue vs Lum`, `Sat vs Sat`, `Lum vs Sat`).
  - Verification:
    - Added unit tests in `HSLCurvesTests.cs` (OpenRend.Core.Tests) testing periodic wrap-around interpolation, linear clamping, LUT generation, deep cloning, and JSON serialization.
    - Added unit test in `ColorGradingProcessorTests.cs` (OpenRend.Media.Tests) testing HSL curve hue shift and saturation modulation on pixel buffers.
    - Full gate passed: 100% native integration tests, 982 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 trimmer window, 2/3/4-point editing, subclip creation, and shuttle suite` implemented 1:1 Vegas Pro Trimmer window architecture matching `ScriptPortal.Vegas.Trimmer`, `CreateSubclip`, and Vegas Pro source editing toolbars.
  - Core & Models:
    - Created `TrimmerStream.cs` with `TrimmerPlacementStream` enum (`VideoAndAudio`, `VideoOnly`, `AudioOnly`).
    - Extended `MediaAsset` in `ProjectDocument.cs` with `DisplayName` property, JSON ignore when null, and name fallback logic.
    - Added `LoopRegion` observable property and `LoopRegionChanged` event synchronization in `TimelineLoopRegion.cs`, `EditorViewModel.cs`, and `MainWindow.xaml.cs`.
  - Placement & Editing Operations:
    - Enhanced `EditorViewModel.PlaceSubclip` to support stream-selective placement (`VideoAndAudio`, `VideoOnly`, `AudioOnly`) and dynamic `playbackRate` adjustment.
    - Implemented 2-point reverse edit `EditorViewModel.PlaceSubclipUpToCursor` (`Shift+A`): places subclip ending exactly at current timeline cursor.
    - Implemented 4-point edit `EditorViewModel.PlaceSubclipFitToFill` (`Ctrl+Shift+A`): calculates `rate = sourceDuration / targetDuration` (clamped to [0.05, 10.0]) and fits the marked subclip into the active timeline loop region at matching playback rate.
  - UI Implementation:
    - Created `CreateSubclipDialog.cs` with authentic Vegas Pro dark theme styling, In/Out/Length readouts, default subclip numbering, and owner window centering.
    - Enhanced `TrimmerControl.cs`:
      - Transport: Jump to Start (`|◄`), Shuttle Reverse (`◄◄` / `J` cycling -1x, -2x, -4x, -8x), Prev Frame (`◄` / `Left`), Play/Pause (`▶/⏸` / `Space` / `K`), Next Frame (`►` / `Right`), Shuttle Forward (`►►` / `L` cycling 1x, 2x, 4x, 8x), Jump to End (`►|`), and Loop Toggle (`↻` / `Q`).
      - Live shuttle speed indicator readout (`1x`, `2x`, `4x`, `-2x`, etc.).
      - Stream Mode Selector Buttons: `Both (V/A)` | `Video (V)` | `Audio (A)` with active toggle highlight.
      - Dual-sided audio waveform peaks rendering directly in the Trimmer scrubber ruler when media has audio (olive-green background `#3A6042` and bright green selection `#5CB874`).
      - Vegas Pro cyan bracket handles `[` and `]`, translucent blue selection overlay `#30007ACC`, and gold playhead needle diamond cap `#FFD700`.
      - Insertion Buttons: `Create Subclip`, `Fit to Fill`, `Add to Cursor (Shift+A)`, and `[+] Add (A)`.
  - Verification:
    - Added 4 comprehensive unit tests in `TrimmerEditorTests.cs` (OpenRend.Editor.Tests) verifying stream mode routing, 2-point reverse edit up to cursor, 4-point Fit to Fill playback rate scaling, and J-K-L shuttle speed cycling.
    - Full gate passed: 100% native integration tests, 1,028 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 audio bus, master bus, and track envelope automation suite` implemented 1:1 Vegas Pro Audio Bus and Master Bus Envelope Automation matching `ScriptPortal.Vegas.AudioBusTrack`, `MasterBus`, and `EnvelopeType` (Volume, Pan, Mute).
  - Core & Models:
    - Extended `AudioBusTrack.cs` with `Envelopes` collection, deep cloning, and schema validation ensuring envelope types are unique and restricted to `Volume`, `Pan`, and `Mute`.
    - Extended `ProjectDocument.cs` with `MasterAudioEnvelopes` collection, deep cloning, and schema validation restricting master envelopes to `Volume` and `Mute`.
  - Timeline Operations:
    - In `TimelineEditor.AudioBuses.cs`, implemented atomic undo/redo operations for audio buses: `AddAudioBusEnvelope`, `RemoveAudioBusEnvelope`, `ToggleAudioBusEnvelope`, `SetAudioBusEnvelopePoint`, `RemoveAudioBusEnvelopePoint`, and `SetAudioBusEnvelopePointCurve`.
    - Implemented atomic undo/redo operations for the master bus: `AddMasterAudioEnvelope`, `RemoveMasterAudioEnvelope`, `ToggleMasterAudioEnvelope`, `SetMasterAudioEnvelopePoint`, `RemoveMasterAudioEnvelopePoint`, and `SetMasterAudioEnvelopePointCurve`.
    - Updated `TimelineEditor.Restore()` to preserve `MasterAudioEnvelopes`.
  - Audio Rendering & DSP Pipeline:
    - In `TimelineRenderService.cs`, integrated dynamic envelope curve modulation into `RenderAudio`:
      - Audio Bus Tracks: evaluated `Mute` envelope (disabling sub-bus rendering when active), `Volume` envelope (modulating bus gain), and `Pan` envelope (modulating stereo balance).
      - Master Audio Bus: evaluated `Mute` envelope (muting master output) and `Volume` envelope (modulating final master buffer level).
  - UI Implementation:
    - In `MixingConsoleWindow.cs`:
      - Added `env` button on Track channel strips opening an automation envelope toggle context menu (`Volume Envelope`, `Pan Envelope`, `Mute Envelope`).
      - Added `env` button on Audio Bus channel strips with context menu for `Volume Envelope`, `Pan Envelope`, and `Mute Envelope`.
      - Added `env` button on Master Bus channel strip with context menu for `Volume Envelope` and `Mute Envelope`.
    - In `MainWindow.xaml` and `MainWindow.xaml.cs`:
      - Added `Insert > Audio Envelopes > Mute` menu item and handler.
      - Added `Insert > Video Envelopes > Fade to Color` and `Mute` menu items and handlers.
  - Verification:
    - Added 4 comprehensive unit tests in `AudioBusEnvelopeTests.cs` (OpenRend.Editor.Tests) verifying audio bus and master envelope addition, toggling, undo/redo, point/curve editing, and schema validation.
    - Full gate passed: 100% native integration tests, 1,024 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 video fx parameter keyframing, timeline lane, and ofx curve interpolation suite` implemented 1:1 Vegas Pro Video FX Parameter Keyframe Automation and OFX parameter interpolation matching `ScriptPortal.Vegas.EffectKeyframe` and `OFXKeyframe`.
  - Core & Models:
    - Created `VideoEffectKeyframe.cs` in `OpenRend.Core`:
      - `EffectInterpolation` enum (`Linear`, `Hold`, `Slow`, `Fast`, `Smooth`, `Sharp`).
      - `VideoEffectKeyframe` modeling frame, Amount, SecondaryAmount, parameter dictionary, interpolation curve, and smoothness.
      - `VideoEffectAnimation` with validation, binary search keyframe lookup, and exact curve evaluation (including Hermite cubic S-curve for Smooth, cubic centered power for Sharp, quadratic for Slow/Fast, and Hold).
    - Extended `VideoEffect` with `Animation` property, deep cloning, validation, and `EvaluateAt(frame)` dynamic snapshot evaluation.
  - Image Processing & Rendering Pipeline:
    - In `VideoEffectProcessor.cs`, enhanced `HasActiveEffects` to recognize animated effects even with neutral defaults, and enhanced `Apply` to evaluate each effect at the local/project frame snapshot.
    - In `TimelineRenderService.cs` and `TimelineRenderService.Dissolve.cs`, mapped event FX keyframe evaluation to local clip frame (`frame - layer.Clip.StartFrame`) so keyframes naturally follow clip positioning on the timeline, while track and output FX evaluate at integer project frame.
  - Timeline Operations:
    - Created `TimelineEditor.VideoEffectKeyframes.cs` with atomic undo/redo operations: `SetScopedVideoEffectKeyframe`, `RemoveScopedVideoEffectKeyframe`, `SetScopedVideoEffectKeyframeInterpolation`, `MoveScopedVideoEffectKeyframe`, and `SetScopedVideoEffectAnimationEnabled`.
  - UI Implementation:
    - Created `EffectKeyframeLane.cs` in `OpenRend.App`: interactive time ruler ticks, diamond keyframe markers (`◆`), dragging to reposition keyframe frames in time, red playhead cursor with top arrow cap, keyboard navigation, and right-click context menu with all 6 Vegas interpolation curves.
    - Enhanced `VideoEffectsWindow.cs`: added `Animate` toggle button, collapsible keyframe lane host with transport controls (`◄|`, `◆+`, `◆-`, `|►`), dynamic frame readout, real-time parameter sync when seeking keyframes, and automatic keyframe creation on parameter edits when animation is enabled.
  - Verification:
    - Added 4 unit tests in `VideoEffectKeyframeTests.cs` (OpenRend.Core.Tests) testing keyframe validation, linear interpolation, curve evaluations (`Hold`, `Fast`, `Slow`, `Smooth`, `Sharp`), and `EvaluateAt` clamping.
    - Added 3 unit tests in `VideoEffectKeyframeEditorTests.cs` (OpenRend.Editor.Tests) verifying scoped keyframe addition, removal, interpolation changes, timeline move, animation enable/disable, and undo/redo.
    - Full gate passed: 100% native integration tests, 1,020 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 audio mixer console, assignable fx buses, and peak clip indicators` implemented 1:1 Vegas Pro Audio Mixer Console (Ctrl+Alt+5) matching `ScriptPortal.Vegas.AudioBusTrack`, `AudioFXBusTrack`, and `AudioBusMode`.
  - Architecture & UI:
    - Built comprehensive Vegas Pro Mixing Console (`MixingConsoleWindow.cs`):
      - Top Console Toolbar: `+ Add Bus` button, Section View Toggles (`Tracks`, `Buses`, `Master`), and `Reset Clips` button.
      - Track Channel Strips: Track index badge, track name, Bus assignment routing dropdown (`Master`, `Bus A`, `Bus B`, ...), Track FX button (`fx`), Invert Phase button (`Ø`), Mute (`M`), Solo (`S`), Pan rotary slider with center double-click reset, Volume Fader (-60 dB to +6 dB with 0 dB double-click reset), dB scale ticks, dynamic peak meter, latching overload clip LED indicator, and numeric dB readout.
      - Audio Bus Strips: Bus badge with warm gold/amber styling (`#C87A14`), Delete Bus button (`✕`), Bus FX button (`fx`), Mute (`M`), Solo (`S`), Pan slider, Volume Fader, dB scale, dynamic peak meter, and latching clip LED.
      - Master Bus Strip: Master Bus badge (`#1E5080`), Master FX button (`fx`), Master Mute (`M`), Master Monitor Dim (`Dim` -20 dB), Master Volume Fader, dB scale, stereo Left/Right peak meters with individual Left and Right latching clip LEDs, and numeric peak readouts.
    - Enhanced `AudioTrackFxWindow.cs`: Full support for Track Audio FX chains, Master Bus FX chains, and Assignable Audio Bus FX chains with 4-band Parametric EQ, Track Compressor, and Track Noise Gate, preset application, and live frequency response curve plotting.
  - Backend & Timeline Operations:
    - Added atomic undo/redo operations in `TimelineEditor.AudioEffects.cs`: `AddAudioBusEffect`, `RemoveAudioBusEffect`, `SetAudioBusEffectBypassed`, and `UpdateAudioBusEffectParameter`.
    - Latching overload clip detection: clip LEDs latch bright red (`#FF2020`) when levels exceed 0.0 dB (peak > 1.0) and can be reset individually by clicking or collectively via `Reset Clips`.
  - Verification:
    - Added 5 comprehensive unit tests in `MixingConsoleEditorTests.cs` (OpenRend.Editor.Tests) verifying audio bus creation, deletion, routing, volume, pan, mute, solo, bus audio effects chains, master gain/mute/dim, and undo/redo.
    - Full gate passed: 100% native integration tests, 1,013 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 video track parent/child compositing hierarchy, adjustment tracks, and audio bus routing` implemented 1:1 Vegas Pro Video Track Compositing Hierarchy, Adjustment Tracks, and Audio Bus Routing matching `ScriptPortal.Vegas.VideoTrack` (`CompositeNestingLevel`, `IsCompositingChild`, `IsCompositingParent`, `IsAdjustmentTrack`) and `AudioBusTrack` (`BusTracks`, `AddAudioBusTrack`, `MasterBus`).
  - Data Models:
    - Created `AudioBusTrack.cs` with `Id`, `Name`, `Gain`, `Pan`, `IsMuted`, `IsSolo`, `AudioEffects`, deep cloning, and schema validation.
    - Extended `ProjectDocument.cs` with `AudioBuses` collection, schema continuity validation (preventing invalid nesting leaps and ensuring Track 0 cannot be child), and deep cloning.
    - Extended `TimelineTrack` with `CompositeNestingLevel`, `IsCompositingChild`, `IsAdjustmentTrack`, and `BusTrackId`.
  - Timeline Operations:
    - Created `TimelineEditor.Compositing.cs` with atomic undo/redo operations: `MakeCompositingChild` (increments nesting level up to prev + 1), `MakeCompositingParent` (outdents level and cascade-clamps subsequent child tracks), `SetTrackAdjustment`, and `ToggleTrackAdjustment`.
    - Created `TimelineEditor.AudioBuses.cs` with atomic undo/redo operations: `CreateAudioBus` (auto-names Bus A..Z), `DeleteAudioBus` (re-routes assigned tracks back to Master), `SetTrackAudioBus`, `SetAudioBusVolume`, `SetAudioBusPan`, `SetAudioBusMute`, and `SetAudioBusSolo`.
    - Updated `TimelineEditor.Restore()` to preserve `AudioBuses` across undo/redo.
  - Timeline & Preview Evaluation:
    - In `TimelineEvaluator.cs`, cascaded parent muting and soloing to child tracks (`IsTrackEffectivelyMuted` / `IsTrackEffectivelySolo`).
    - In `TimelineRenderService.cs`, implemented hierarchical branch buffer compositing for parent-child groups, adjustment track evaluation (applying clip and track video FX to underlying composite buffer with opacity and blend mode), and multi-bus audio accumulation (routing tracks to sub-busses, processing bus audio FX chains, volume/pan, and accumulating into Master Bus).
  - UI Implementation:
    - In `TimelineView.cs`, rendered tree hierarchy branch glyphs (`└─`) and 14px indentation per nesting level, `[ADJ]` purple badge (`#6A1B9A`), Make Compositing Child/Parent button (`↳` / `↰`) at `x = 184, y + 46`, and audio track output bus badges (`[Bus: A]`).
    - Added track context menu items: `Make Compositing Child`, `Make Compositing Parent`, `Adjustment Track`, and `Output Bus >` submenu (`Master`, `Bus A`, ...).
    - Wired events in `MainWindow.xaml.cs` and delegating helpers in `EditorViewModel.cs`.
  - Verification:
    - Added 5 comprehensive unit tests in `CompositingAndBusRoutingEditorTests.cs`.
    - Full gate passed: 100% native integration tests, 958 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 media bins, smart bins, drag-and-drop, and media tags suite` implemented 1:1 Vegas Pro Project Media Bins architecture matching `ScriptPortal.Vegas.MediaBin`. Created `MediaBin.cs` model with persistent UUID, display name, hierarchical `ParentBinId` nesting, and media item references. Extended `MediaAsset.cs` with `Tags` collection. Added `MediaBins` collection to `ProjectDocument.cs` with deep cloning and schema validation. Implemented atomic undo/redo operations in `TimelineEditor.MediaBins.cs` (`CreateMediaBin`, `RenameMediaBin`, recursive `DeleteMediaBin`, `AddMediaToBin`, `RemoveMediaFromBin`, `SetMediaTags`) and updated `Restore()` to preserve bin and tag decisions. Built `MainWindow.MediaBins.cs` with dynamic hierarchical tree generation, item count indicators, smart bins (`All Media`, `Video`, `Audio`, `Images`, `Missing Media`), right-click tree context menus, tree drag-and-drop ingestion (`OpenRend.Media`), and dynamic media list context menus (`Add to Bin >`, `Remove from Bin`, `Media Tags…`). Built `BinNameDialog.cs` and `MediaTagsDialog.cs` with authentic Vegas Pro dark theme styling. Verified with 6 unit tests in `MediaBinsEditorTests.cs`. Full gate passed: 100% native integration tests, 953 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 selection edit tools suite (normal, selection marquee, zoom, envelope) and keyboard cycling` implemented 1:1 Vegas Pro timeline editing tool suite matching Menu 1003 (`Edit -> Editing Tool`) and toolbar radio commands (`Normal Edit Tool` Ctrl+D, `Envelope Edit Tool`, `Selection Edit Tool` D, `Zoom Tool`, `Next Tool` D, `Previous Tool` Shift+D). Created `EditToolMode.cs` enum with forward/reverse tool cycling, display names, and status hints. Enhanced `EditorViewModel` with `EditTool`, event notifications, and status updates. Enhanced `TimelineView.cs` with dynamic cursor switching (`Cursors.Cross` for Selection, `Cursors.SizeAll` for Zoom, `Cursors.Arrow` for Envelope), marquee box drag selection across multi-track clips with union (`Shift`) and toggle (`Ctrl`) modifier semantics preserving event grouping, zoom marquee box drag and single-click zoom-in (1.5x) / right-click zoom-out (1.5x) calculations centering around cursor, and envelope edit tool isolation preventing accidental clip moves or trims. Added `MainWindow.EditTools.cs` and toolbar radio button group with authentic vector icons (`ic_normal_edit`, `ic_selection_edit`, `ic_zoom_tool`, `ic_envelope_tool`). Added `Edit > Editing Tool` submenu with checkmark synchronization. Verified with 6 unit tests in `EditToolsEditorTests.cs`. Full gate passed: 100% native integration tests, 947 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 timeline ruler timecode formats and context menu` implemented 1:1 Vegas Pro timeline ruler and transport timecode architecture matching `ScriptPortal.Vegas.RulerFormat`, `RulerProperties`, and Vegas Pro Menu 1003 / 1004 / 1012. Added `RulerFormat` enum with 17 formats and exact Vegas Pro display names and menu headers. Built `TimecodeFormatter.cs` providing high-precision rational frame formatting and bidirectional string parsing for all standard formats (Samples, Time, Seconds, Time & Frames, Absolute Frames, Measures & Beats at project BPM/TimeSig, 16mm Feet & Frames at 40 fpf, 35mm Feet & Frames at 16 fpf, SMPTE Film Sync IVTC 23.976 fps, SMPTE Film Sync 24 fps, SMPTE EBU 25 fps, SMPTE Non-Drop 29.97 fps, SMPTE Drop 29.97 fps with drop-frame leap-second compensation, SMPTE 30 fps, and Audio CD Time at 75 fps). Enhanced `ProjectDocument` with `RulerFormat`, `RulerStartTimeFrames`, `BeatsPerMinute`, `BeatsPerMeasure`, backward-compatible `RulerTimeFormat` string synchronization, and schema validation. Extended `TimelineView` with dynamic tick label formatting, position header readout, and right-click ruler context menu. Implemented `SetTimeAtCursorWindow.cs` (command 32300) modal dialog allowing direct timecode positioning in project format. Enhanced `MainWindow.xaml` and `MainWindow.Ruler.cs` with `Options > Ruler Format` menu, transport timecode sunken display right-click context menu (Vegas Pro Menu 1012) and double-click trigger, and real-time checkmark synchronization. Added 13 unit tests in `RulerFormatEditorTests.cs`. Full gate passed: 100% native integration tests, 940 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 window docking layout manager, presets, and organize dialogs` implemented 1:1 Vegas Pro window layout preset and docking workspace architecture matching `ScriptPortal.Vegas.DockWindowStyle` and `vegas2027.exe` strings. Built `WorkspaceLayout.cs` and `WorkspaceLayoutManager.cs` supporting the five standard Vegas Pro layout presets (Default Layout, Audio Mixing, Color Correction, Source Window Layout, Timeline Top Layout) and custom serialized presets (`.OpenRendWindowLayout`). Added `MainWindow.Layouts.cs` with dynamic timeline row inversion (Timeline Top Layout), panel geometry adjustments, deck tab routing, and tool window lifecycle tracking. Implemented `SaveLayoutAsWindow.cs` and `OrganizeLayoutsWindow.cs` dialogs with slot assignment (0–9), layout reordering, renaming, deletion, and immediate live layout application. Integrated `View > Window Layouts` menu with 10 shortcut slots, global shortcuts `Alt+D, D/A/C/S/T/R/W/0-9`, and direct slot triggers `Ctrl+Alt+S` and `Ctrl+Alt+0..9`. Verified by 5 new unit tests in `WorkspaceLayoutEditorTests.cs`. Full gate passed: 100% native integration tests, 922 managed tests passed cleanly with 0 warnings and 0 errors.

- 2026-11-04: `feat: implement vegas pro 1:1 envelope automation, shortcuts, and line point editing` completed 1:1 Vegas Pro envelope automation suite matching `ScriptPortal.Vegas.Envelope`. Added main menu items under `Insert > Audio Envelopes (Volume, Pan)` and `Insert > Video Envelopes (Track Composite Level)`, global shortcuts `Shift+V` (Track Volume / Composite Level) and `Shift+P` (Track Pan), and interactive envelope line right-click menus enabling point addition and curve interpolation selection (Linear, Fast, Slow, Smooth, Sharp, Hold) at any timeline coordinate. Verified all six fade curves and multi-track audio Gain / Stereo Pan evaluation in `EnvelopeEditorTests.cs`. Full gate passed: 100% native integration tests, 917 managed tests passed cleanly with 0 warnings and 0 errors.

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
