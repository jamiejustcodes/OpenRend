# OpenRend native media engine

This C++20 DLL implements local media decoding through FFmpeg and a D3D11 preview renderer. It is called by the .NET media layer using the C ABI in `openrend.h`.

Run `powershell -ExecutionPolicy Bypass -File tools/setup-native.ps1` from the repository root. The script downloads the BtbN shared FFmpeg SDK, generates three-second video and audio-only fixtures, configures CMake, builds the DLL, and runs the native integration suite. CMake discovers the installed Visual Studio toolchain. The resulting runtime is in `build/native/Release`; FFmpeg runtime DLLs are copied beside `OpenRend.Native.dll`. `-SkipTests` disables GoogleTest download and execution.

## Interop contract

Functions use `cdecl`, UTF-8 paths, opaque pointer handles, and fixed-width scalar types. `or_metadata` is a 48-byte sequential structure under the default x64 ABI. Video buffers contain BGRA bytes in rows of `output_width * 4` bytes. Audio buffers contain interleaved stereo float32 samples at 48,000 Hz. Sample capacity counts floats, and the audio return value counts stereo frames. Audio buffers are cleared before decoding so gaps remain silent. Status functions return zero on success and minus one on failure; `or_last_error()` returns a thread-local UTF-8 explanation. Audio returns the number of decoded frames, or minus one on failure.

Decoding calls are synchronous. Call them on the managed worker queue; each decoder serializes its own calls. Audio and video maintain independent demuxers so an audio seek cannot invalidate a video preview. Video reuses the decoder for nearby forward requests and seeks for backward or distant requests. Requests in the final frame's interval retain the final frame. Audio requests seek and resample from the requested media timestamp; playback callers should cache blocks rather than request one sample at a time. Close handles only after all their callers have finished.

The video decoder attempts FFmpeg D3D11VA when the codec and system support it. Hardware frames are transferred into a CPU frame for scaling and the stable BGRA interop boundary. Decode initialization failures fall back to software. `or_decoder_is_hardware()` reports the format selected during actual decoding; before the first frame it returns zero. This path currently performs a GPU-to-CPU transfer and CPU-to-GPU upload, rather than sharing textures across the managed boundary.

The renderer owns a device, immediate context, swapchain, dynamically uploaded BGRA texture, and shader pipeline. It maintains the source aspect ratio with letterboxing. Device creation first uses the hardware adapter and then tries WARP. `or_renderer_is_hardware()` reports which creation succeeded. `or_renderer_resize()` and `or_renderer_present()` are serialized per renderer. The caller owns the HWND and must keep it valid until renderer destruction.

## GPU timeline compositing

The `or_compositor_*` API is an offscreen D3D11 compositor with a fixed output size. `begin` clears the render target to opaque black, `layer` uploads and draws each ordered BGRA source with straight-alpha blending and the supplied opacity, and `readback` copies the completed BGRA texture through a reusable staging resource into the caller's CPU buffer. Layers are fitted to the output while preserving aspect ratio, with integer-centered letterboxing. Per-pixel alpha and clip opacity both affect the blend; output alpha remains 255.

Each compositor owns one device/context, one render target, one staging texture, one replaceable layer texture, and reusable pipeline resources. Layers with different dimensions replace that texture rather than adding an unbounded cache. The context is serialized per API call. Callers sharing a compositor must serialize the full `begin`/`layer`/`readback` frame transaction. Creation attempts a hardware adapter before WARP, and `or_compositor_is_hardware()` reports which succeeded. `OPENREND_FORCE_WARP=1` explicitly selects WARP for testing or troubleshooting.

Compositing executes on D3D11; decoded frame upload and staging readback still cross the CPU/GPU boundary. The current API does not share decoded GPU textures with the compositor or preview swapchain.

## Validation

Native GoogleTest integration exercises real fixture metadata, invalid files/buffer sizes, seeking distinct video frames and returning to an identical frame, final-frame retention, still images at arbitrary timeline positions, 44.1-to-48 kHz audio resampling, audio-only media, stereo sample layout, D3D11 device creation, presentation, and resizing. GPU compositor readback tests verify red/blue blending, opaque black gaps, padded input rows, letterboxing, and eight random translucent layers against the CPU straight-alpha calculation within one byte per channel on both hardware and WARP. The managed media integration suite additionally covers UTF-8 media paths, editing transforms, mix output, and exported video.

The setup SDK comes from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds), the Windows distribution linked by FFmpeg's download page. `ffmpeg.lock.json` pins the dated release, exact archive URL, version and SHA-256. Setup verifies the downloaded archive before extracting or moving it, checks the installed executable version, and preserves that provenance in `.deps/ffmpeg/provenance.json` and `.deps/ffmpeg-provenance.json`. The selected `gpl-shared` distribution carries the licenses included in `.deps/ffmpeg`. It is a downloaded dependency and is excluded from source control.
