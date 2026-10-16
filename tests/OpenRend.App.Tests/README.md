The desktop integration suite launches its own built application with a disposable project and isolated application data. It never attaches to an existing editor process. It uses FlaUI UIA3 for clip selection, track toggle/range controls, media filters, keyboard editing, undo/redo and save. It verifies maximized work-area bounds and track-slider keyboard focus/Escape behavior. It reopens the saved timeline, renders through the desktop output dialog, and verifies the resulting frame count and dimensions with FFprobe.

The event FX workflow clicks the per-event Pan/Crop, FX and overflow controls, checks slider drag/keyboard/cancel behavior, edits and bypasses an ordered effect chain, verifies changed native preview pixels, saves/reopens it and renders output. It also writes visual checks to `.local/desktop-validation/event-fx.png` and `event-toolbar.png`.

Build the Release application and generate `tests/native/fixture.mp4` with the native test fixture command first. On an unlocked Windows desktop, run:

```powershell
$env:OPENREND_RUN_UI_TESTS = '1'
dotnet test tests/OpenRend.App.Tests/OpenRend.App.Tests.csproj -c Release
```

Set `OPENREND_APP_PATH` to test another built `OpenRend.exe`. Keep the desktop free of manual keyboard input while the suite runs. Ordinary test runs skip this suite unless explicitly enabled.
