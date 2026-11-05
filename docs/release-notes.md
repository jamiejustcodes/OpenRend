# OpenRend Release Notes — 5 November 2026

**Bundle Archive:** `dist/OpenRend-win-x64.zip`  
**Standalone Executable:** `dist/OpenRend/OpenRend.exe`  
**Target Platform:** Windows x64 (.NET 10 WPF + Native C++20 Direct3D 11 / FFmpeg Engine)  
**SHA256 Checksum:** `4D4BCEB3C0BAD02C0390F234F1AE43E5DA58AA2F4F9BA207FD03050557CD1CDA`  
**Automated Verification:** 100% Native Tests Passed (1/1), 1,099 Managed Tests Passed (0 failed, 0 warnings, 0 errors)

---

## 🌟 Executive Summary

This release delivers the complete **Vegas Pro 1:1 Editing & Compositing Parity Sprint**, reverse engineered directly from Boris FX Vegas Pro 2027 (`vegas2027.exe`, `ScriptPortal.Vegas.dll`, `VegasStyle.dll`) using REA evidence. It introduces non-destructive Video Adjustment Tracks and Events, the complete Vegas Pro Clipboard Paste Suite (Paste Repeat, Paste Insert, and Selective Attribute Transfer), Track Motion 3D Source Alpha with perspective keyframing, Collapsible Contiguous Track Groups with hierarchical mute/solo cascading, 5 Audio Pan Laws, Timeline Event Snap Offsets, Event Healing, Timeline Gap Closure, and Missing Stream Reconstruction.

All edit decisions remain integer project frames with rational rates, preserving source media untouched and sharing identical evaluation across the Direct3D 11 preview and master export pipelines.

---

## 📋 Comprehensive Change Log

### 1. Vegas Pro 1:1 Event Heal, Close Gaps, and Missing Stream Suite (`bcad56e`)
- **Event Heal (`Edit.EventHeal` / ID 44709)**:
  - Consolidates contiguous split slices originating from the same media asset or adjustment layer back into a single unified event.
  - Automatically reconciles internal cut boundaries: removes split-generated fades, preserves start `FadeIn` and end `FadeOut`, continuously stitches envelope automation curves (shifting right points by `leftOrigDuration`), merges Pan/Crop and Bézier mask keyframes, and shifts transcript words.
  - Iteratively heals multi-slice chains (e.g. $A + B + C \to A$) in chronological order.
  - Heals companion grouped audio/video slices across tracks synchronously.
  - Exposed in main menu (`Edit > Event Heal`) and Event Context Menu.
- **Close Gaps (`Edit.CloseGaps` / ID 44713)**:
  - Eliminates empty timeline gaps between adjacent events with full atomic undo/redo.
  - **Multi-Event Selection Mode**: Iterates through selected events on each track in ascending order, shifting downstream events leftward to touch preceding event ends.
  - **Single-Track Mode**: When no events are selected, closes all gaps sequentially on the active/targeted track.
  - Propagates shifts to linked grouped companion clips on complementary tracks to preserve audio/video sync.
  - Exposed in main menu (`Edit > Close Gaps`) and Event Context Menu.
- **Add Missing Stream (`Edit.MissingStream` / ID 44712)**:
  - Inspects source `MediaAsset` streams for selected timeline events.
  - If a video event is missing audio, finds or inserts an audio track below and instantiates a paired audio clip matching exact source in/out and timeline ranges.
  - If an audio event is missing video, finds or inserts a video track above and instantiates a paired video clip matching exact source in/out and timeline ranges.
  - Automatically links both clips into a shared `GroupId`.
  - Exposed in main menu (`Insert > Add Missing Stream for Selected Event`) and Event Context Menu.

### 2. Vegas Pro 1:1 Clipboard Paste Suite & Selective Attribute Transfer (`55fa269`)
- **Paste Repeat (`Edit > Paste Repeat...` / ID 32087 / `Ctrl+B`)**:
  - Recreated Vegas Pro's authentic modal dialog (`PasteRepeatDialog.cs`) with dark theme styling.
  - Features repeat count spinner (1–999) and spacing mode selection:
    - `End to end`: Pastes $N$ copies back-to-back without gap.
    - `Even spacing`: Pastes $N$ copies spaced at custom intervals input via project timecode.
- **Paste Insert (`Edit > Paste Insert` / ID 32086 / `Ctrl+Shift+V`)**:
  - Pastes clipboard events at the edit cursor while automatically rippling all downstream material on the target track(s) later in time by the pasted duration.
- **Paste Event Attributes (`Ctrl+Shift+B` / ID 32328) & Selective Paste (`SelectivePasteDialog.cs`)**:
  - Recreated 1:1 Vegas Pro "Selectively Paste Event Attributes" modal dialog modeled after `ScriptPortal.Vegas.SelPastAttrConfig`:
    - Video Attributes: Video Event FX, Event Pan/Crop (including Mask), Switches (Mute, Lock, Loop, Rate, Aspect, Interlace), Opacity & Fade Curves.
    - Audio Attributes: Audio Event FX, Volume & Pan, Pitch, Invert Phase, Normalize & Channels, Switches, Fade Curves.
    - Options: "Replace existing FX on destination events (uncheck to append)".
  - Added clip-level audio FX chain processing (`AudioEffects`) to `TimelineClip` and integrated with `TimelineRenderService.RenderAudio` for identical DSP evaluation.
- **Keyboard Shortcut Standardization**:
  - `Insert > Video Track`: `Ctrl+Shift+Q` (corrected from legacy collision).
  - `Insert > Audio Track`: `Ctrl+Q`.
  - `Edit > Paste Repeat...`: `Ctrl+B`.
  - `Edit > Paste Insert`: `Ctrl+Shift+V`.
  - `Edit > Paste Event Attributes`: `Ctrl+Shift+B`.

### 3. Video Adjustment Tracks and Adjustment Events Suite (`dae34db`)
- **Core Architecture (`ProjectDocument.cs`)**:
  - Extended `TimelineClip` with `IsAdjustmentEvent`, `FrameWidth`, `FrameHeight`, `PixelAspectRatio`, and `UseAlternateAdjustmentEventBehavior`.
  - Added schema validation ensuring adjustment events only exist on video tracks, have non-negative dimensions, and maintain finite positive pixel aspect ratios.
- **Compositing & DSP Pipeline (`TimelineRenderService.cs`)**:
  - When rendering an adjustment layer, dynamically captures the accumulated lower-track composite frame buffer as source.
  - Resamples bilinearly to adjustment event dimensions, applies clip video effects chain (Invert, Color Grading, Blur, LUTs, etc.), evaluates Bézier masks and Pan/Crop transforms, and alpha-blends down onto the output buffer.
- **UI & Menus**:
  - In `TimelineView.cs`: Rendered adjustment events in authentic Vegas Pro purple palette (`#3E244B`, header `#5D2F6E`, border `#AB47BC`) with `[ADJ]` tag.
  - Added `Insert > Adjustment track` (ID 32178) and `Insert > Adjustment event` (ID 32503).
  - Added `Edit > Resize selected Adjustment Events to Project` (ID 44729) and `Edit > Resize all Adjustment Events to Project` (ID 44730).
  - Added event context menu item `Resize Adjustment Event to Project` (ID 44731).

### 4. Track Motion 3D Source Alpha & Perspective Keyframing (`d74f42a`)
- **3D Camera & Homography Engine**:
  - Extended `TrackCompositeMode` with `SourceAlpha3D = 3`.
  - Extended `TrackMotionKeyframe` with 3D parameters: `Depth`, `RotationOffsetX`, `RotationOffsetY`, `RotationOffsetZ`, and spherical/linear Lerp interpolation.
  - Implemented `CompositeTransformedPerspective` performing camera perspective projection (53.13° FOV camera focal length $f = \max(W, H) \cdot 1.25$, corner scaling $scale = f / (f + Z_{3D})$) with 3x3 projective homography matrix inversion for exact subpixel bilinear sampling.
  - Integrated across preview playback and final video export.

### 5. Track Groups Architecture & Collapsible Layout (`2400998`)
- **Data Models & Vegas Pro Contiguity Rule**:
  - Created `TrackGroup.cs` in `OpenRend.Core`: `Id`, `Name`, `ColorHex`, `IsCollapsed`, `IsMuted`, `IsSolo`, `TrackIds`.
  - Implemented strict contiguity validation matching Vegas Pro: `"Track grouping requires contiguous tracks to be selected."`.
- **UI Bracket Visualization (`TimelineView.cs`)**:
  - Collapsed groups fold member tracks to height 0 and render a 26px compact group header bar with expand chevron (`▶`), group name, track count, Mute (`M`), and Solo (`S`) toggle buttons.
  - Expanded groups render an authentic colored vertical grouping bracket down the left edge of track headers with top (`┌`) and bottom (`└`) ticks, alongside collapse chevron (`▼`), group Mute, and Solo buttons.
  - Added track header context menu actions: `Group Selected Tracks` (`Ctrl+Shift+G`), `Ungroup Tracks`, `Remove from Track Group`, `Toggle Group Collapse`, `Group Mute`, `Group Solo`, and `Rename Group...`.
- **Cascaded DSP Evaluation**:
  - Group mute and solo states cascade hierarchically across audio and video member tracks for identical preview and master export evaluation.
  - `Edit Details` window integration: Added `Track Groups` view mode with TSV clipboard export.

### 6. Audio Pan Laws & Timeline Event Snap Offset Suite (`994743b`)
- **5 Vegas Pro Audio Pan Laws**:
  - `Linear (-6 dB)`: standard linear attenuation $L = 1 - p$, $R = p$.
  - `ConstantPower0dB`: equal loudness curve maintaining unity gain at center $L = \cos(\theta)$, $R = \sin(\theta)$ with 0 dB center.
  - `ConstantPower3dB`: 3 dB attenuation at center.
  - `ConstantPowerMinus3dB`: -3 dB attenuation at center ($L = \cos(\frac{\pi(p + 1)}{4})$, $R = \sin(\frac{\pi(p + 1)}{4})$).
  - `SquareRootMinus3dB`: square-root power curve.
  - Integrated in `AudioDspService.cs` and track header context menus.
- **Timeline Event Snap Offset (`TimelineClip.SnapOffsetFrames`)**:
  - Interactive triangular snap pin glyph at the bottom edge of timeline events.
  - Draggable snap handle snapping event position to cursor, cut boundaries, and markers.
  - Context menu items: `Set Snap Offset to Cursor` and `Reset Snap Offset`.

### 7. Timeline Regions, Audio Channels Remapping, & Pitch Shifting (`5dd4a07`)
- **Timeline Regions**:
  - Dedicated `TimelineRegion` model with start frame, duration, numbered green flags (`01`, `02`), and name labels.
  - Interactive resizing handles on ruler flags and context menu editing.
  - Shortcut `R` / `Insert > Region`.
- **Audio Channel Remapping**:
  - Event channel routing modes: `Both (Stereo)`, `Left Only`, `Right Only`, and `Swap (L<->R)`.
  - Evaluated in realtime during audio preview and export channel downmixing.
- **Event Pitch Shifting**:
  - Precise semitone modulation (-12 to +12 semitones, octaves) applied per event.
  - Keyboard shortcuts: `+` (+1 semitone), `-` (-1 semitone), `0` (reset pitch).

### 8. UI & Responsive Interaction Polishing (`a420001` / `efd099f` / `5870a63`)
- Track header slider collisions resolved; layout adapts responsively to header width.
- Timeline file drag-and-drop hover indicators with ghost clip placement preview.
- Viewport and preview player sizing constraints optimized for authentic Vegas Pro ratios.
- Vector icons and theme styling unified across dark UI palette.

---

## 🧪 Verification & Quality Gate

```
================================================================================
Test Suite Results:
--------------------------------------------------------------------------------
1. Native Direct3D 11 / C++20 Integration:  1/1 Passed (100%) - 2.23s
2. OpenRend.Core.Tests:                     413/413 Passed (100%)
3. OpenRend.Editor.Tests:                   376/376 Passed (100%)
4. OpenRend.Media.Tests:                    308/308 Passed (100%)
5. OpenRend.Playback.Tests:                 2/2 Passed (100%)
--------------------------------------------------------------------------------
TOTAL: 1,099 Managed Tests Passed | 0 Failed | 0 Errors | 0 Warnings
================================================================================
```

---

## 📦 Distribution Artifacts

- **Standalone Directory:** `dist/OpenRend/`
  - `OpenRend.exe`: Primary WPF / .NET 10 application launcher
  - `OpenRend.Native.dll`: C++20 / Direct3D 11 timeline rendering and compositing engine
  - `ffmpeg.exe` & `ffprobe.exe`: Hardware-accelerated media decoders/encoders
  - Complete third-party licenses and attribution notices
- **Zip Archive:** `dist/OpenRend-win-x64.zip`
- **Checksum Manifest:** `dist/SHA256SUMS.txt`
  - `4D4BCEB3C0BAD02C0390F234F1AE43E5DA58AA2F4F9BA207FD03050557CD1CDA *OpenRend-win-x64.zip`
