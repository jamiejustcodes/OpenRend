# Video event effects

OpenRend has 63 built-in video effects. They are original implementations with the controls listed by the application; matching an effect name in another editor does not establish identical parameters or output.

| Category | Effects |
| --- | --- |
| Color | Black and White, Black Restore, Brightness and Contrast, Broadcast Colors, Channel Blend, Color Balance, Fill Light, Gradient Map, HSL Adjust, Invert, LAB Adjust, Levels, LUT Filter, Saturation Adjust, Sepia, Soft Contrast, Threshold, White Balance |
| Blur | Chroma Blur, Defocus, Gaussian Blur, Linear Blur, Median, Quick Blur, Radial Blur |
| Detail | Convolution Kernel, Min and Max, Sharpen, Unsharp Mask |
| Distort | Lens Correction, Mirror, Offset and Wrap, Picture In Picture, Pinch/Punch, Spherize, Swirl, Tiny Planet, Wave |
| Keying | Chroma Keyer, Cookie Cutter, Crop, Mask Generator |
| Stylize | Add Noise, Border, Film Grain, Glow, Newsprint, Pixelate, Radial Pixelate, TV Simulator, Vignette |
| Glow | Light Rays, Starburst |
| Render | Timecode |
| AI | AI Auto Reframe, AI Colorization, AI Dehaze, AI Sharpen, AI Smoothen, AI Style Transfer, AI Upscale, AI Z-Depth, AI Smart Mask 2.0 |

Each effect exposes its supported parameters, validated ranges and defaults through one catalog shared by the editor and saved project model. Levels has an All, Red, Green, Blue or Alpha channel selector. All adjusts RGB while preserving coverage; Alpha adjusts coverage while preserving the source RGB values.

LUT Filter imports and embeds a 3D `.cube` lattice with Strength, bypass and Trilinear/Tetrahedral interpolation. Browse/Clear and undo are available in event FX. See [LUT processing](lut-effects.md) for supported files, persistence and color-space limits.

Open a video's FX button, then choose **Plug-in chooser**. Categories and search filter the available effects. Add appends an effect to the draft chain, Replace changes the selected chain entry, and Remove deletes it. OK applies the whole draft as one undoable edit; Cancel preserves the project. Parameter edits, bypass and chain ordering remain undoable in the FX editor.

Effects process source-resolution frames before Pan/Crop and composition. Preview and export use the same chain. Color operations work on encoded RGB. Black and White blends toward the equal RGB average; Invert blends toward the byte complement. These two effects and Levels truncate the resulting channel value to a byte. Gaussian Blur uses three separable box-filter passes as an approximation; Quick Blur uses one. Filters account for coverage so hidden colors in transparent pixels cannot tint visible edges. Grain and noise use the project frame and a saved seed so rendering the same frame repeats the same result.

Projects save as version 6 and read versions 1–6. Effect parameters are stored independently in split clips, clipboard copies and undo snapshots. Version 4 introduced parameterized effects; version 5 protects transition decisions and version 6 embeds LUT data, and version 7 preserves explicit video source modes. Older builds reject newer project versions rather than silently dropping their settings.

These effects currently run on the CPU, followed by CPU or D3D11 composition. Large source frames and long chains allocate intermediate image buffers and have not been qualified for real-time throughput. Effect keyframes, external plug-in hosting, AI models, tracking, stabilization, temporal effects, full color grading and the broader effects catalog remain in development. The chooser lists supported effects only.
