using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using Xunit;
using OpenRend.Core;
using OpenRend.Project;

namespace OpenRend.App.Tests;

public sealed partial class EditorWorkflowTests
{
    [WindowsDesktopFact]
    [Trait("Category", "Desktop")]
    public async Task CustomRenderPresetCanBeSavedRenamedAndRetainedWhenRenderingABuiltin()
    {
        var root = FindRepository();
        var executable = Environment.GetEnvironmentVariable("OPENREND_APP_PATH")
            ?? Path.Combine(root, "src", "OpenRend.App", "bin", "Release", "net10.0-windows", "win-x64", "OpenRend.exe");
        var temporary = Path.Combine(Path.GetTempPath(), "OpenRendDesktopTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var path = Path.Combine(temporary, "custom.openrend");
        var media = new MediaAsset { Path = Path.Combine(root, "tests", "native", "fixture.mp4"), Width = 320, Height = 180, DurationSeconds = 3, HasVideo = true };
        await ProjectStore.SaveAsync(new() { Name = "UI integration", Width = 320, Height = 180, Media = [media],
            Tracks = [new() { Kind = TrackKind.Video, Clips = [new() { MediaId = media.Id, DurationFrames = 90 }] }] }, path);
        try
        {
            using var running = Launch(executable, path, temporary);
            var window = running.Window;
            RenderAndVerify(window, executable, Path.Combine(temporary, "custom.mp4"), configure: render =>
            {
                var initialCount = Find(render, "RenderTemplates").AsListBox().Items.Length;
                Find(render, "CustomizeRenderTemplate").AsButton().Invoke();
                Window? settings = null;
                WaitFor(() => (settings = render.ModalWindows.SingleOrDefault(dialog => dialog.Title.StartsWith("Custom Settings"))) is not null,
                    "Custom Settings did not open.");
                Find(settings!, "CustomRenderTemplateName").AsTextBox().Text = "Review copy";
                Find(settings!, "CustomRenderCrf").AsTextBox().Text = "99";
                Find(settings!, "SaveRenderTemplateAs").AsButton().Invoke();
                Assert.Contains("51", Find(settings!, "CustomRenderTemplateError").Name);
                Find(settings!, "CustomRenderCrf").AsTextBox().Text = "28";
                Find(settings!, "CustomRenderAudioBitrate").AsTextBox().Text = "96";
                var speed = Find(settings!, "CustomRenderEncoderSpeed").AsComboBox();
                speed.Select(2); speed.Patterns.ExpandCollapse.Pattern.Collapse();
                var artifacts = Path.Combine(root, ".local", "desktop-validation"); Directory.CreateDirectory(artifacts);
                using (var screenshot = Capture.Element(settings!)) screenshot.ToFile(Path.Combine(artifacts, "custom-render.png"));
                Find(settings!, "SaveRenderTemplateAs").AsButton().Invoke();
                WaitFor(() => render.ModalWindows.Length == 0 && Find(render, "RenderTemplates").AsListBox().Items.Length > initialCount,
                    "Saving a custom template did not add it to the render catalog.");
                Find(render, "RenderFavorite").AsCheckBox().Toggle();
                Find(render, "CustomizeRenderTemplate").AsButton().Invoke();
                WaitFor(() => (settings = render.ModalWindows.SingleOrDefault(dialog => dialog.Title.StartsWith("Custom Settings"))) is not null,
                    "The custom template did not reopen.");
                Assert.Equal("28", Find(settings!, "CustomRenderCrf").AsTextBox().Text);
                Assert.Equal("96", Find(settings!, "CustomRenderAudioBitrate").AsTextBox().Text);
                Find(settings!, "CustomRenderTemplateName").AsTextBox().Text = "Final review";
                Find(settings!, "CustomRenderCrf").AsTextBox().Text = "26";
                Find(settings!, "SaveRenderTemplate").AsButton().Invoke();
                WaitFor(() => render.ModalWindows.Length == 0, "Renaming the custom template did not close its editor.");
            });
            string id;
            using (var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "render.json"))))
            {
                var custom = Assert.Single(document.RootElement.GetProperty("CustomTemplates").EnumerateArray());
                id = custom.GetProperty("Id").GetString()!;
                Assert.Equal("Final review", custom.GetProperty("Name").GetString());
                Assert.Equal(26, custom.GetProperty("VideoCrf").GetInt32());
                Assert.Equal(96, custom.GetProperty("AudioBitrateKbps").GetInt32());
                Assert.Equal("Veryfast", custom.GetProperty("EncoderSpeed").GetString());
                Assert.Equal(id, document.RootElement.GetProperty("TemplateId").GetString());
                Assert.Equal(id, Assert.Single(document.RootElement.GetProperty("Favorites").EnumerateArray()).GetString());
            }
            RenderAndVerify(window, executable, Path.Combine(temporary, "builtin.mp4"), configure: render =>
            {
                Find(render, "RenderTemplates").AsListBox().Items.Single(item => item.Text == "Project settings")
                    .Patterns.SelectionItem.Pattern.Select();
            });
            using var retained = JsonDocument.Parse(File.ReadAllText(Path.Combine(temporary, "render.json")));
            Assert.Equal(id, Assert.Single(retained.RootElement.GetProperty("CustomTemplates").EnumerateArray()).GetProperty("Id").GetString());
            Assert.Equal("project-mp4", retained.RootElement.GetProperty("TemplateId").GetString());
            running.CloseGracefully();
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }
}
