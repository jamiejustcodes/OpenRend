# Contributing

Use the toolchain and build commands in README.md. Work in small changes with tests for editing, media or persistence behavior. Keep UI changes keyboard accessible and verify the real desktop workflow.

Use generated test media. Never add private media or proprietary assets. Keep timestamps and frame calculations in their declared timebase, close native resources explicitly and avoid decoding on the WPF dispatcher. Comment non-obvious timing and ownership decisions concisely.

Run tools/build.ps1 and review the diff before committing. Desktop tests require an interactive Windows session and OPENREND_RUN_UI_TESTS=1. Include actual validation and remaining limitations in pull requests.
