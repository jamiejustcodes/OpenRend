# Contributing to OpenRend

Thank you for your interest in contributing to OpenRend. OpenRend is a native Windows non-linear video editing suite built with **C# / .NET 10**, **WPF**, and a high-performance **C++20** media engine powered by FFmpeg and Direct3D 11.

---

## 🛠️ Development Environment

### Prerequisites
- **Operating System**: 64-bit Windows 10 (version 1903+) or Windows 11.
- **.NET 10 SDK**: Pinned in `global.json` (`10.0.303+`).
- **Visual Studio 2022 / 2026**:
  - Workload: *Desktop development with C++*
  - Individual components: *MSVC v143/v144 - VS 2022 C++ x64/x86 build tools*, *Windows 10/11 SDK*, *C++ CMake tools for Windows*.
- **Git** with LF-to-CRLF configuration appropriate for Windows.

---

## 🏗️ Building and Testing

All native and managed compilation and verification are orchestrated via PowerShell scripts located in `tools/`:

```powershell
# 1. Full check: Native CMake setup, compile C++20 engine, restore .NET, compile WPF, run 1,000+ tests
.\tools\build.ps1

# 2. Skip native compilation (when working purely on managed WPF UI)
.\tools\build.ps1 -SkipNative

# 3. Skip test suite during quick iterative development
.\tools\build.ps1 -SkipTests

# 4. Package complete self-contained release bundle with native dependencies
.\tools\build.ps1 -Package
```

### Running Interactive Desktop UI Tests
End-to-end Windows automation tests require an unlocked desktop session:
```powershell
$env:OPENREND_RUN_UI_TESTS = "1"
dotnet test OpenRend.slnx -c Release --filter "FullyQualifiedName~EditorWorkflowTests"
```

---

## 📐 Architecture & Principles

1. **Deterministic Timeline Evaluation**:
   - Timeline time is strictly evaluated using rational arithmetic (`Rational` rates, integer project frames). Never use floating-point accumulation for edit decisions.
   - Preview and export share the identical composition graph — what renders in the preview viewport must match the master export frame-for-frame.

2. **Native / Managed Boundary**:
   - Native decoding, Direct3D 11 compositing, and video filtering live in `OpenRend.Native` (C++20).
   - High-throughput pixel operations occur off the WPF dispatcher thread.
   - Native resources (Direct3D swapchains, textures, FFmpeg contexts) must be closed explicitly via RAII and `IDisposable`.

3. **Data Integrity & Non-Destructive Editing**:
   - Source media on disk is strictly read-only and never modified.
   - Project files (`.openrend`) use versioned JSON manifests. Backward compatibility with earlier schemas is required.

---

## 📝 Commit & Pull Request Guidelines

- **Small, Tested Commits**: Keep changes atomic, focused, and tested before staging.
- **Explicit Staging**: Stage explicit file paths (`git add path/to/file.cs`) rather than indiscriminate `git add .`.
- **Explain "Why"**: Commit messages and code comments should be concise and explain timing constraints, resource ownership, and non-obvious lifecycle behaviors.
- **No Private Research or Credentials**: Never commit private keys, proprietary SDKs, or real user media. Use synthetic generated media in tests (`tests/native/`).
- **Keep Documentation Synchronized**: When modifying workflows or public interfaces, update `README.md`, `ARCHITECTURE.md`, and `progress`.
