# Development

Read `progress` at the start of continued work. Update it after meaningful findings and tested changes, and before a context handoff, with the current task, next steps, verification and unresolved issues.

Build a native Windows editor using .NET 10 WPF and C++20. Keep original media unchanged. Store edit decisions in integer project frames with rational rates. Preview and export share timeline evaluation.

Run tools/build.ps1 for the native and managed checks. Windows desktop integration additionally requires OPENREND_RUN_UI_TESTS=1 and an unlocked interactive desktop. Set OPENREND_DATA_DIR for isolated test preferences and recovery files.

Use small, tested commits. Stage explicit paths. Never fabricate test results or history. Keep comments concise and explain timing, resource ownership and non-obvious behavior. Document actual limitations; no placeholder controls or unsupported capability claims.

Never commit credentials, generated binaries, dependency SDKs or private research. Preserve existing work. Public materials describe OpenRend's own implementation and verification.
