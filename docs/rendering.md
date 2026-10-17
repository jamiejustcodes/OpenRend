# Rendering

Open Render As with Ctrl+Shift+E. Choose a format/template, output folder and file name, then Render. Both current presets use project dimensions and frame rate, H.264 8-bit YUV 4:2:0 video and stereo AAC at 48 kHz.

| Template | Video quality (CRF) | Audio bitrate |
| --- | --- | --- |
| Project settings | 20 | 192 kbps |
| Project settings — High quality | 17 | 256 kbps |

Customize Template opens editable name, quality (CRF 0–51), AAC bitrate (32–512 kbps) and encoder-speed controls. Save updates an existing custom preset, including its name; Save As creates a new preset with an independent identity. Builtins stay unchanged. Delete Template removes a selected custom preset and its favorite reference. Custom changes remain in the dialog draft until Render As is accepted; Cancel discards that draft.

Search matches supported template properties without case sensitivity. Semicolons separate required terms: `high;H.264;1920x1080` requires all three terms to match. Favorite marks the selected template; Show favorites only filters the list. Template, favorites, output folder and the loop-only choice are saved in `render.json` under the application data directory, or `OPENREND_DATA_DIR` when set.

## Loop-region export

Drag the upper timeline ruler to select a region. Drag either boundary to resize it. With the timeline focused, I sets the in point and O sets the out point at the cursor. Render loop region only is available when a nonempty region exists. The dialog shows its start, exclusive end and frame count. With the option off, the complete timeline renders.

The range stays in integer project frames. Video evaluation uses the original absolute timeline frame, and audio begins at the corresponding rational project time. Export does not move or trim project events. Cancellation or encoder failure preserves an existing destination; final output replaces it only after successful encoding. Source media cannot be chosen as the destination.

## Current limits and verification

Loop selection is session state, is bounded to the timeline, and does not enable loop playback. Selection history and project persistence are pending. Custom output dimensions/rates, additional output formats, channel mapping, markers and loudness logs are pending. The dialog exposes supported controls only.

Encoded tests verify range frame counts/rates, audio duration, starting pixels and tone identity at 30, 30000/1001 and 24000/1001 fps. Desktop integration verifies ruler selection, property search, favorites, filename validation, saved choices and a 30-frame loop export. The complete native/managed build gate also covers existing full-timeline exports and cancellation/source protection.

Custom-template tests inspect the encoded H.264 quality/speed settings and AAC bitrate. Desktop integration saves, reopens and renames a custom preset, renders it, then renders a builtin while checking the custom preset and favorite survive.
