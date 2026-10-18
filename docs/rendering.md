# Rendering

Open Render As with Ctrl+Shift+E. Choose a format and template, output folder and file name, then Render. Templates support project dimensions or fixed resolutions (UHD 4K, HD 1080p, 720p), H.264/HEVC/ProRes video, and AAC/WAV/MP3 audio.

## Supported format catalog

| Format | Codecs | Extension | Presets |
| --- | --- | --- | --- |
| MAGIX AVC/AAC MP4 | H.264 (libx264) + AAC | `.mp4` | Project settings, High quality, Internet 4K 2160p, Internet HD 720p |
| MAGIX HEVC/AAC MP4 | HEVC (libx265) + AAC | `.mp4` | Internet UHD 2160p, Internet HD 1080p |
| Apple ProRes | ProRes (prores_ks 10-bit YUV 4:2:2) + 24-bit PCM | `.mov` | Apple ProRes 422, Apple ProRes 422 HQ |
| Sony Wave Audio | PCM Uncompressed Audio (no video) | `.wav` | 48,000 Hz 24-bit PCM, 44,100 Hz 16-bit PCM |
| MP3 Audio | MPEG Layer-3 (libmp3lame, no video) | `.mp3` | 320 kbps 48,000 Hz, 192 kbps 48,000 Hz |

Selecting a template automatically switches the output file name's extension to match the target format (`.mp4`, `.mov`, `.wav`, `.mp3`).

## Template search and filtering

- **Search**: Matches template properties (name, codec, format, resolution, audio rate/bitrate, speed) without case sensitivity. Semicolons separate required terms (`high;H.264;1920x1080` requires all three terms).
- **Formats list**: Filters templates by format category (`All Formats`, `MAGIX AVC/AAC MP4`, `MAGIX HEVC/AAC MP4`, `Apple ProRes`, `Sony Wave Audio`, `MP3 Audio`).
- **Show favorites only**: Narrows list to starred favorite templates.
- **Match project settings**: Narrows list to templates whose resolution matches the current project width and height.

## Custom templates

Customize Template opens editable name, quality (CRF 0–51), AAC bitrate (32–512 kbps) and encoder-speed controls. Save updates an existing custom preset, including its name; Save As creates a new preset with an independent identity. Builtins stay unchanged. Delete Template removes a selected custom preset and its favorite reference.

## Render options

- **Render loop region only**: Available when a timeline loop region is set (via timeline ruler drag or I/O keys). Bounded to integer project frames.
- **Stretch video to fill output frame size (do not letterbox)**: Scales the video directly to the output resolution without pillarbox or letterbox black borders.
- **Channel mapping**:
  - `None (Stereo)`: Default stereo passthrough.
  - `Downmix to Mono`: $(L + R) / 2$ mixed into both left and right channels.
  - `Swap Left and Right`: Inverts stereo soundstage.
  - `Mute Left Channel`: Silences left channel while retaining right channel.
  - `Mute Right Channel`: Silences right channel while retaining left channel.
- **Generate loudness log (.loudness.log)**: Computes ITU-R BS.1770 integrated loudness in LUFS, maximum true peak in dBFS, duration, frame count, sample rate, and channel mapping metadata, saved alongside the output media.
- **Dynamic estimates**: Live calculation of estimated output file size based on bitrate, resolution, and frame duration, plus available free disk space on the target destination drive.

Preferences (chosen template, favorites, output folder, loop region setting, stretch to fill, channel mapping, loudness logging, format filter, and match project setting) persist in `render.json` under the application data directory.
