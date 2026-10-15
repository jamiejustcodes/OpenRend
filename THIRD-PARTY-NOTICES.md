# Third-party components

OpenRend source is licensed under GPL-3.0-or-later. Dependency copyright and license notices remain applicable.

| Component | Use | License / source |
| --- | --- | --- |
| FFmpeg, shared GPL build | Decode, resample, scale, encode and mux | GPL v3 distribution; [FFmpeg source and licensing](https://ffmpeg.org/legal.html) |
| BtbN FFmpeg build scripts | Pinned Windows SDK | [Build repository](https://github.com/BtbN/FFmpeg-Builds); exact binary identity in src/OpenRend.Native/ffmpeg.lock.json |
| .NET / WPF | Desktop runtime | MIT and applicable distribution notices; [dotnet](https://github.com/dotnet) |
| CommunityToolkit.Mvvm | Observable view models | MIT; [source](https://github.com/CommunityToolkit/dotnet) |
| Microsoft.Extensions.DependencyInjection | Service composition | MIT; [source](https://github.com/dotnet/runtime) |
| NAudio | WASAPI output | MIT; [source](https://github.com/naudio/NAudio) |
| xUnit | Managed tests | Apache-2.0; [source](https://github.com/xunit/xunit) |
| GoogleTest | Native tests | BSD-3-Clause; [source](https://github.com/google/googletest) |
| FlaUI | Windows desktop tests | MIT; [source](https://github.com/FlaUI/FlaUI) |

The development SDK is the unmodified dated BtbN GPL shared distribution. The lock file records its URL, version and SHA-256; tools/setup-native.ps1 verifies the archive before use. Its LICENSE.txt is copied into the local development bundle. The FFmpeg configuration and enabled external libraries can be inspected with ffmpeg -buildconf.

Public binary releases require the complete corresponding source and build material for the precise FFmpeg distribution and its enabled libraries, all required dependency notices, and the .NET runtime notices. The development bundle script does not assemble that complete source distribution. No public binary release is claimed by this repository. Codec availability and applicable patent obligations depend on the distribution and jurisdiction.
