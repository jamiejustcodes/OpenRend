# Starting a project

OpenRend opens Welcome when launched without a project path. New Project (Ctrl+N) reopens it. Choose an aspect ratio, resolution and frame rate, then Create Project. The new project starts without media or events. Existing source files are never changed.

| Aspect | HD | FullHD | UHD |
| --- | --- | --- | --- |
| Widescreen (16:9) | 1280×720 | 1920×1080 | 3840×2160 |
| Fullscreen (4:3) | 1280×960 | 1920×1440 | 4096×3072 |
| Portrait (9:16) | 720×1280 | 1080×1920 | 2160×3840 |
| Square (1:1) | 720×720 | 1080×1080 | 2160×2160 |
| Scope (2.4:1) | 1280×536 | 2048×858 | 4096×1716 |

Nominal rates are 24, 25, 30, 48, 50, 60 and 120 fps. Full Frame uses the whole rate; Drop Frame uses nominal×1000/1001. The project stores a rational rate, and the selector displays three decimal places. This selector changes frame rate; it does not itself configure timecode numbering.

Show Welcome Screen on Startup saves immediately. Aspect/resolution/rate choices are remembered after successful creation. Open Project lists existing recent projects and provides Browse. Getting Started explains the working import/edit/render flow. Launching an existing project path opens it directly.

Advanced Settings opens OpenRend's project settings dialog from its defaults; it does not apply pending tile choices. It supports project name, dimensions and rational frame rate. Full advanced video/audio/color settings remain pending. OpenRend's welcome uses its own branding and vector artwork.

Initial welcome choices are widescreen FullHD with a nominal 30 fps fractional rate. Preferences and recent paths are stored in `welcome.json` under application data, or `OPENREND_DATA_DIR` when set.

Desktop integration verifies portrait UHD/60 creation and save/reopen, startup suppression, remembered choices, recent-project open and command-line bypass. Model tests cover all fifteen dimension presets and all fourteen whole/fractional rate combinations.
