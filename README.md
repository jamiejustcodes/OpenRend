# OpenRend

OpenRend is a free, open source Windows video editor built with C#/.NET 10, WPF and a C++20 media engine. It is in active development.

The application imports video and audio, places media on multiple tracks, trims and splits clips, previews the timeline, saves projects and exports H.264/AAC MP4. Preview and export use the same edit decisions. Source media stays unchanged.

## Build and run

Requirements: Windows 10/11 x64, .NET 10 SDK, Visual Studio with Desktop development with C++, Windows SDK, CMake and Git. The native setup script downloads the pinned shared FFmpeg SDK and GoogleTest. Managed packages restore from NuGet.

```powershell
.\tools\build.ps1
.\src\OpenRend.App\bin\Release\net10.0-windows\win-x64\OpenRend.exe
```

Create a self-contained local development bundle:

```powershell
.\tools\build.ps1 -Package
.\dist\OpenRend\OpenRend.exe
```

Keep the entire bundle directory together. This is a development build; it has no installer or code signature. See [third-party notices](THIRD-PARTY-NOTICES.md) before redistributing dependency binaries.

## Editing

Import media with Ctrl+I or drop files onto the window. Double-click a source to place it at the cursor, or drag it to a compatible timeline track. Video with sound creates separate picture and audio clips. Ctrl+click selects several clips; use Ctrl+A to select all.

Drag a clip to move it, or point near either edge to show a resize cursor and highlighted trim grip. The edge target extends slightly outside the clip. Hold and drag to trim; Escape cancels the drag. S splits selected clips at the cursor; Delete removes them. Imported picture/sound pairs stay linked for editing. G links a selection, U unlinks it, and Edit → Ignore clip groups temporarily isolates members. Splits produce independent left/right groups. Snap aligns moves with clip boundaries and the playhead. Ripple closes removed time on the affected tracks. Ctrl+Z/Ctrl+Y undo and redo. Track headers provide mute, solo, lock and video visibility controls. Clip Properties edits source ranges, gain, opacity, pan and fades.

Ctrl+C/Ctrl+X copy or cut selected clips, including linked members. Ctrl+V pastes at the cursor as one undoable edit. Groups receive independent identities; pasting between projects converts positions and source ranges to the destination frame rate. The Project Media tree filters video, audio and missing sources.

Video events have Pan/Crop, FX and event-options buttons at their lower-right corner. Narrow events show an overflow button; zoom in or use the context menu for the full controls. Event options toggle headers, names and lengths for the current session. Locked tracks disable event actions.

Video event FX provides an ordered built-in chain of Brightness and Contrast, Saturation and Invert. Add, remove, reorder or bypass effects, drag a parameter or enter its value, and undo changes. The FX window previews the edited frame; the timeline preview and export apply the same chain before Pan/Crop and composition. These effects process encoded RGB values; effect keyframes and external plug-ins are not supported yet.

Track headers include video opacity and audio gain/pan sliders. Drag to set a level as one undoable edit, or double-click to reset it. Arrow keys adjust a focused level. Track levels combine with clip levels in preview and export; locked tracks reject changes. The controls also expose Windows UI Automation range values.

Select a video event and open Tools → Video event Pan/Crop, or use the timeline context menu. Edit source-pixel width, height, center, rotation and pivot; aspect lock links width and height. Drag the source overlay to pan or any corner handle to resize; the pointer shows the handle's resize direction. Crop keyframes use whole project frames with Linear, Hold, Slow, Fast, Smooth or Sharp temporal interpolation. Right-click a diamond to choose its curve, or focus the keyframe lane at a key and press Shift+F10. Drag a diamond or enter its exact frame to retime it. Landing on an occupied frame keeps both keys by shifting the existing keys toward the next free frame, or backward when the end is full. Undo restores their previous times. Frame navigation updates the timeline preview. Crop edits support undo, persistence, trimming, splitting and clipboard frame-rate conversion, and use the same GPU or CPU composition during preview and export.

Space plays or pauses, Enter pauses, Escape stops, Alt+Left/Right steps a frame and Ctrl+Home/End seeks to the bounds. Completed video frames remain visible as the audio clock advances; slow rendering can drop frames while catching up. Pausing and seeking request the exact cursor frame. Ctrl+wheel zooms the timeline; Shift+wheel scrolls time; the wheel scrolls tracks. Splitters resize the workspace. Preview quality selects draft, preview or project resolution. Master Bus adjusts monitor gain.

Preview zoom and safe area guides affect the monitor only. Snapshot buttons copy or save the current project-resolution frame. Monitor meters use a decibel scale; monitor mute and gain do not change the rendered export. Video backend details are available in the preview information tooltip.

With the timeline focused, Up/Down selects a track and Left/Right selects a clip. M toggles track mute, Shift+M solo, L lock and V video visibility. Timeline clips expose selection and track controls expose their actions to Windows UI Automation.

Save with Ctrl+S. Projects store versioned JSON and relative source paths where possible. This build reads versions 1–3 and saves version 3; older builds reject new saves to protect crop animation and effects from being discarded. File → Relink missing media repairs moved source references. Recovery snapshots are saved every 30 seconds while editing. File → Open recovery project opens the latest snapshot. OPENREND_DATA_DIR overrides the preferences/recovery directory for isolated sessions.

Ctrl+Shift+E renders the edited timeline. The renderer composites video, keeps black gaps, mixes audio, applies clip levels/fades and finalizes MP4 before replacing the chosen destination. Cancellation and failure preserve an existing destination. Export cannot overwrite a source file.

## Verification and scope

Tests exercise rational timing, edit transactions, fade preservation, persistence, legacy project import, native decoding, seeking, audio resampling, video composition and real encoded output. See [feature status](docs/feature-status.md) for evidence and remaining work.

The current application is an early functional editor. Arbitrary docking, broader effect automation, spatial spline smoothing, masks, titles, proxies, plugin hosting, scripting and large-project performance qualification remain in development. Clips with transforms or active video effects decode at source resolution and use CPU upload/readback around GPU composition; the built-in effect operations run on the CPU.

## License

OpenRend source is GPL-3.0-or-later. Third-party components retain their own licenses. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
