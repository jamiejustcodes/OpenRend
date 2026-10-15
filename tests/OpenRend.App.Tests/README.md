The desktop integration suite launches its own built application with a disposable project and isolated application data. It never attaches to an existing editor process. It uses FlaUI UIA3 for clip selection, track toggle controls, keyboard editing, undo/redo and save. It reopens the saved timeline, renders through the desktop output dialog, and verifies the resulting frame count and dimensions with FFprobe.

Build the Release application and generate `tests/native/fixture.mp4` with the native test fixture command first. On an unlocked Windows desktop, run:

```powershell
$env:OPENREND_RUN_UI_TESTS = '1'
dotnet test tests/OpenRend.App.Tests/OpenRend.App.Tests.csproj -c Release
```

Set `OPENREND_APP_PATH` to test another built `OpenRend.exe`. Keep the desktop free of manual keyboard input while the suite runs. Ordinary test runs skip this suite unless explicitly enabled.
