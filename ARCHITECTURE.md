# Architecture

OpenRend.App hosts a WPF desktop workspace. CommunityToolkit observable view models hold orchestration state, dependency injection owns shared services, and the UI library paints the timeline. Edit commands live in OpenRend.Timeline, domain records in OpenRend.Core and atomic versioned JSON persistence in OpenRend.Project.

Time is represented by integer project frames and a rational frame rate. Clip intervals are half-open. Source-in uses the same project grid, converted to seconds only at the decode boundary. Splits retain the original fade envelope anchor. Transactions validate state and roll back failures; undo/redo restore independent snapshots.

The .NET media layer owns a bounded native decoder cache. FFmpeg demuxers and codec contexts are held through SafeHandle. Audio and video use separate demuxers. Native video decoding attempts D3D11VA where supported, transferring decoded surfaces to BGRA CPU buffers for the managed interop boundary. Audio is resampled to stereo 48 kHz float. Workers perform decode and render work off the WPF dispatcher.

The preview HWND owns a Direct3D 11 device and swapchain, with a WARP fallback. Hardware capability reflects the actual device. Shared timeline evaluation chooses video layers and audio contributions with mute/solo/visibility, clip opacity, gain, pan and fades. A bounded cache of offscreen Direct3D compositors blends the layers and reads the completed frame back through staging textures; device creation failure uses the deterministic CPU path. Decoded upload and readback still cross the CPU/GPU boundary. Playback feeds WASAPI and derives its frame position from audio output progress, excluding inserted underrun silence. Preview requests coalesce and obsolete edit revisions are discarded.

Export evaluates every timeline frame, mixes exact sample windows and feeds raw picture plus audio to the FFmpeg encoder. It writes a temporary sibling MP4, waits for successful finalization and then replaces the destination. Cancellation terminates the owned encoder and removes only that job's temporary files. Exporting over source media is rejected.

Projects contain edit decisions, metadata and media references. Saves use a temporary sibling and atomic replacement. Recovery files and workspace preferences live in the local application data directory, with an environment override for isolated tests. Original sources are opened for reading.

Current limits include GPU/CPU transfer overhead, sequential export, sampled waveform caches and limited timeline virtualization. See the roadmap for work beyond the verified implementation.
