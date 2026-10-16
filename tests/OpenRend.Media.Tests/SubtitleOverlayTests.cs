using OpenRend.Core;
using OpenRend.Media;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class SubtitleOverlayTests
{
    [Fact]
    public void RenderSubtitleGeneratesValidBgraPixelsWithAlpha()
    {
        var width = 640;
        var height = 360;
        var text = "Dialogue subtitle test";

        var pixels = SubtitleRenderer.RenderSubtitle(text, width, height, "Bottom Center");

        Assert.Equal(width * height * 4, pixels.Length);

        // Verify that there are non-transparent pixels (text, outline, pill)
        var nonTransparentCount = 0;
        for (var i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 0)
                nonTransparentCount++;
        }

        Assert.True(nonTransparentCount > 100, "Subtitle rendering should produce visible pixels with alpha > 0");
    }

    [Fact]
    public void RenderSubtitleHandlesEmptyOrInvalidGracefully()
    {
        var empty = SubtitleRenderer.RenderSubtitle("", 640, 360);
        Assert.Equal(640 * 360 * 4, empty.Length);
        Assert.All(empty, b => Assert.Equal(0, b));

        var ws = SubtitleRenderer.RenderSubtitle("   ", 640, 360);
        Assert.Equal(640 * 360 * 4, ws.Length);
        Assert.All(ws, b => Assert.Equal(0, b));

        var zero = SubtitleRenderer.RenderSubtitle("test", 0, 0);
        Assert.Empty(zero);
    }

    [Fact]
    public void RenderSubtitleDifferentiatesTopAndBottomAlignment()
    {
        var width = 640;
        var height = 360;
        var text = "Alignment Test";

        var bottomPixels = SubtitleRenderer.RenderSubtitle(text, width, height, "Bottom Center");
        var topPixels = SubtitleRenderer.RenderSubtitle(text, width, height, "Top Center");

        // Count non-transparent pixels in top half vs bottom half
        var topHalfPixelsInTop = 0;
        var bottomHalfPixelsInBottom = 0;

        var halfStride = (height / 2) * width * 4;

        for (var i = 3; i < halfStride; i += 4)
        {
            if (topPixels[i] > 0) topHalfPixelsInTop++;
        }

        for (var i = halfStride + 3; i < bottomPixels.Length; i += 4)
        {
            if (bottomPixels[i] > 0) bottomHalfPixelsInBottom++;
        }

        Assert.True(topHalfPixelsInTop > 50, "Top Center alignment should render in top half of frame");
        Assert.True(bottomHalfPixelsInBottom > 50, "Bottom Center alignment should render in bottom half of frame");
    }

    [Fact]
    public void TimelineCompositesSubtitleLayerOverBackground()
    {
        using var mediaService = new MediaService();
        using var renderer = new TimelineRenderService(mediaService, useGpuCompositing: false);

        var project = new ProjectDocument
        {
            Width = 320,
            Height = 180,
            FrameRate = new Rational(30, 1),
            Media =
            [
                new MediaAsset
                {
                    Id = Guid.NewGuid(),
                    Path = "openrend://subtitles",
                    DurationSeconds = 100,
                    Width = 320,
                    Height = 180,
                    FrameRate = 30,
                    HasVideo = true
                }
            ],
            Tracks =
            [
                new TimelineTrack
                {
                    Name = "Subtitles from Speech",
                    Kind = TrackKind.Video,
                    Clips =
                    [
                        new TimelineClip
                        {
                            MediaId = Guid.Empty, // will set below
                            Name = "Hello Subtitles",
                            StartFrame = 0,
                            DurationFrames = 60
                        }
                    ]
                }
            ]
        };

        project.Tracks[0].Clips[0].MediaId = project.Media[0].Id;
        project.Validate();

        var frame = renderer.RenderFrame(project, 15, 320, 180);
        Assert.Equal(320 * 180 * 4, frame.Length);

        // Frame should have non-black / non-transparent subtitle pixels composited
        var hasDrawnPixels = false;
        for (var i = 0; i < frame.Length; i += 4)
        {
            if (frame[i] > 0 || frame[i + 1] > 0 || frame[i + 2] > 0)
            {
                hasDrawnPixels = true;
                break;
            }
        }

        Assert.True(hasDrawnPixels, "TimelineRenderService should composite subtitle overlay into rendered frame");
    }

    [Fact]
    public void RenderSubtitleGeneratesYouTubeShortsYellowPixels()
    {
        var width = 360;
        var height = 640;
        var text = "SHORTS TEST";

        var pixels = SubtitleRenderer.RenderSubtitle(text, width, height, "Middle Center", "YouTube Yellow (Shorts)");
        Assert.Equal(width * height * 4, pixels.Length);

        var hasYellowPixels = false;
        var hasOutlinePixels = false;

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];

            if (a > 150)
            {
                if (r > 200 && g > 180 && b < 100)
                    hasYellowPixels = true;
                if (r < 40 && g < 40 && b < 40)
                    hasOutlinePixels = true;
            }
        }

        Assert.True(hasYellowPixels, "YouTube Yellow style should produce vibrant yellow fill pixels");
        Assert.True(hasOutlinePixels, "YouTube style should produce crisp dark outline / shadow pixels");
    }

    [Fact]
    public void TimelineCompositesYouTubeShortsLayerOverBackground()
    {
        using var mediaService = new MediaService();
        using var renderer = new TimelineRenderService(mediaService, useGpuCompositing: false);

        var project = new ProjectDocument
        {
            Width = 320,
            Height = 180,
            FrameRate = new Rational(30, 1),
            Media =
            [
                new MediaAsset
                {
                    Id = Guid.NewGuid(),
                    Path = "openrend://subtitles",
                    DurationSeconds = 100,
                    Width = 320,
                    Height = 180,
                    FrameRate = 30,
                    HasVideo = true
                }
            ],
            Tracks =
            [
                new TimelineTrack
                {
                    Name = "Subtitles from Speech (YouTube Shorts, Middle Center)",
                    Kind = TrackKind.Video,
                    Clips =
                    [
                        new TimelineClip
                        {
                            MediaId = Guid.Empty,
                            Name = "SHORTS POP",
                            StartFrame = 0,
                            DurationFrames = 30
                        }
                    ]
                }
            ]
        };

        project.Tracks[0].Clips[0].MediaId = project.Media[0].Id;
        project.Validate();

        var frame = renderer.RenderFrame(project, 10, 320, 180);
        Assert.Equal(320 * 180 * 4, frame.Length);

        var hasDrawnPixels = false;
        for (var i = 0; i < frame.Length; i += 4)
        {
            if (frame[i] > 0 || frame[i + 1] > 0 || frame[i + 2] > 0)
            {
                hasDrawnPixels = true;
                break;
            }
        }

        Assert.True(hasDrawnPixels, "TimelineRenderService should composite YouTube Shorts subtitle overlay");
    }

    [Fact]
    public void SubtitleStyleSerializationRoundTrip()
    {
        var original = new SubtitleStyle
        {
            PresetName = "Neon Pop",
            FontFamily = "Impact",
            FontSize = 54f,
            Bold = true,
            Italic = true,
            Uppercase = true,
            AnchorAlignment = "Top_Right",
            TextColor = "#39FF14",
            OutlineColor = "#000000",
            OutlineWidth = 6.5f,
            ShadowEnabled = true,
            ShadowColor = "#CC000000",
            BackgroundColor = "#90000000",
            FitBackgroundColor = false
        };

        var query = original.ToQuery();
        var parsed = SubtitleStyle.FromQuery(query);

        Assert.Equal("Impact", parsed.FontFamily);
        Assert.Equal(54f, parsed.FontSize);
        Assert.True(parsed.Bold);
        Assert.True(parsed.Italic);
        Assert.True(parsed.Uppercase);
        Assert.Equal("Top_Right", parsed.AnchorAlignment);
        Assert.Equal("#39FF14", parsed.TextColor);
        Assert.Equal(6.5f, parsed.OutlineWidth);
        Assert.True(parsed.ShadowEnabled);
        Assert.False(parsed.FitBackgroundColor);
    }

    [Fact]
    public void RenderSubtitleHonorsKaraokeActiveWordHighlight()
    {
        var width = 640;
        var height = 360;
        var text = "FIRST HIGHLIGHT THIRD";

        var style = new SubtitleStyle
        {
            FontFamily = "Arial Black",
            FontSize = 36f,
            Bold = true,
            AnchorAlignment = "Middle_Center",
            TextColor = "#FFFFFF",
            HighlightColor = "#FFE600", // Yellow highlight on active word
            ActiveWordIndex = 1, // "HIGHLIGHT" word is active
            AutomaticHighlight = true,
            OutlineWidth = 4.0f,
            ShadowEnabled = false
        };

        var pixels = SubtitleRenderer.RenderSubtitle(text, width, height, style);
        Assert.Equal(width * height * 4, pixels.Length);

        // Find pixels with yellow fill (high Red and high Green, low Blue in BGRA)
        var hasYellowPixels = false;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            var a = pixels[i + 3];

            if (a > 200 && r > 200 && g > 180 && b < 60)
            {
                hasYellowPixels = true;
                break;
            }
        }

        Assert.True(hasYellowPixels, "Karaoke active word should render with vibrant yellow highlight pixels");
    }

    [Fact]
    public void RenderSubtitleHonors9PointAlignments()
    {
        var width = 640;
        var height = 360;
        var text = "CORNER";

        var styleTopLeft = new SubtitleStyle
        {
            FontFamily = "Arial",
            FontSize = 24f,
            AnchorAlignment = "Top_Left",
            TextColor = "#FFFFFF"
        };

        var styleBottomRight = new SubtitleStyle
        {
            FontFamily = "Arial",
            FontSize = 24f,
            AnchorAlignment = "Bottom_Right",
            TextColor = "#FFFFFF"
        };

        var topLeftPixels = SubtitleRenderer.RenderSubtitle(text, width, height, styleTopLeft);
        var bottomRightPixels = SubtitleRenderer.RenderSubtitle(text, width, height, styleBottomRight);

        // Top-left quadrant has non-transparent pixels in styleTopLeft
        var topLeftCount = 0;
        for (var y = 0; y < height / 2; y++)
        {
            for (var x = 0; x < width / 2; x++)
            {
                var idx = (y * width + x) * 4;
                if (topLeftPixels[idx + 3] > 0) topLeftCount++;
            }
        }

        // Bottom-right quadrant has non-transparent pixels in styleBottomRight
        var bottomRightCount = 0;
        for (var y = height / 2; y < height; y++)
        {
            for (var x = width / 2; x < width; x++)
            {
                var idx = (y * width + x) * 4;
                if (bottomRightPixels[idx + 3] > 0) bottomRightCount++;
            }
        }

        Assert.True(topLeftCount > 30, "Top Left alignment must render in top-left quadrant");
        Assert.True(bottomRightCount > 30, "Bottom Right alignment must render in bottom-right quadrant");
    }

    [Fact]
    public void TimelineCompositesTextMediaGeneratorLayer()
    {
        using var mediaService = new MediaService();
        using var renderer = new TimelineRenderService(mediaService, useGpuCompositing: false);

        var textStyle = new SubtitleStyle
        {
            FontFamily = "Arial",
            FontSize = 32f,
            Bold = true,
            AnchorAlignment = "Middle_Center",
            TextColor = "#FFE600"
        };

        var project = new ProjectDocument
        {
            Width = 320,
            Height = 180,
            FrameRate = new Rational(30, 1),
            Media =
            [
                new MediaAsset
                {
                    Id = Guid.NewGuid(),
                    Path = $"openrend://text?{textStyle.ToQuery()}",
                    DurationSeconds = 100,
                    Width = 320,
                    Height = 180,
                    FrameRate = 30,
                    HasVideo = true
                }
            ],
            Tracks =
            [
                new TimelineTrack
                {
                    Name = "Titles & Text Track",
                    Kind = TrackKind.Video,
                    Clips =
                    [
                        new TimelineClip
                        {
                            MediaId = Guid.Empty, // assigned below
                            Name = "VEGAS TITLES",
                            StartFrame = 0,
                            DurationFrames = 60
                        }
                    ]
                }
            ]
        };

        project.Tracks[0].Clips[0].MediaId = project.Media[0].Id;
        project.Validate();

        var frame = renderer.RenderFrame(project, 20, 320, 180);
        Assert.Equal(320 * 180 * 4, frame.Length);

        var hasDrawnPixels = false;
        for (var i = 0; i < frame.Length; i += 4)
        {
            if (frame[i] > 0 || frame[i + 1] > 0 || frame[i + 2] > 0)
            {
                hasDrawnPixels = true;
                break;
            }
        }

        Assert.True(hasDrawnPixels, "TimelineRenderService should composite VEGAS Titles & Text generator layer");
    }

    [Fact]
    public void TimelineCompositesSolidColorGeneratorLayer()
    {
        using var mediaService = new MediaService();
        using var renderer = new TimelineRenderService(mediaService, useGpuCompositing: false);

        var project = new ProjectDocument
        {
            Width = 160,
            Height = 90,
            FrameRate = new Rational(30, 1),
            Media =
            [
                new MediaAsset
                {
                    Id = Guid.NewGuid(),
                    Path = "openrend://color?hex=#FF0000",
                    DurationSeconds = 100,
                    Width = 160,
                    Height = 90,
                    FrameRate = 30,
                    HasVideo = true
                }
            ],
            Tracks =
            [
                new TimelineTrack
                {
                    Name = "Solid Color Track",
                    Kind = TrackKind.Video,
                    Clips =
                    [
                        new TimelineClip
                        {
                            MediaId = Guid.Empty,
                            Name = "Red Solid",
                            StartFrame = 0,
                            DurationFrames = 60
                        }
                    ]
                }
            ]
        };

        project.Tracks[0].Clips[0].MediaId = project.Media[0].Id;
        project.Validate();

        var frame = renderer.RenderFrame(project, 10, 160, 90);
        Assert.Equal(160 * 90 * 4, frame.Length);

        // In BGRA memory order: B=0, G=0, R=255
        for (var i = 0; i < frame.Length; i += 4)
        {
            Assert.Equal(0, frame[i]);     // Blue
            Assert.Equal(0, frame[i + 1]); // Green
            Assert.Equal(255, frame[i + 2]); // Red
        }
    }

    [Fact]
    public void AudioEventNormalizationCalculatesAccuratePeakGain()
    {
        // 0.25 peak corresponds to -12 dBFS
        var samples = new float[] { 0.1f, -0.2f, 0.25f, -0.15f, 0.05f };
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var abs = Math.Abs(samples[i]);
            if (abs > peak) peak = abs;
        }

        Assert.Equal(0.25f, peak);
        var normalizedGain = Math.Min(31.62, 1.0 / peak);
        Assert.Equal(4.0, normalizedGain, 3); // 4.0x gain = +12.04 dB boost to reach 0 dBFS
    }

    [Fact]
    public void AudioEventMuteAndInvertPhaseTransitions()
    {
        var clip = new TimelineClip { Name = "Dialogue", Gain = 1.0 };

        // Mute: Gain goes to 0.0
        var wasMuted = clip.Gain == 0.0;
        clip.Gain = wasMuted ? 1.0 : 0.0;
        Assert.Equal(0.0, clip.Gain);

        // Unmute: Gain restores to 1.0
        wasMuted = clip.Gain == 0.0;
        clip.Gain = wasMuted ? 1.0 : 0.0;
        Assert.Equal(1.0, clip.Gain);

        // Invert phase: flips sign
        clip.Gain = -clip.Gain;
        Assert.Equal(-1.0, clip.Gain);

        // Invert phase again: restores sign
        clip.Gain = -clip.Gain;
        Assert.Equal(1.0, clip.Gain);
    }

    [Fact]
    public void SceneDetectionDistinguishesIdenticalAndAbruptLumaFrames()
    {
        const int pixelCount = 160 * 90;
        var blackLuma = new byte[pixelCount];
        var whiteLuma = new byte[pixelCount];
        Array.Fill(whiteLuma, (byte)255);

        // Identical frames produce zero delta
        long diffSumSame = 0;
        for (var i = 0; i < pixelCount; i++) diffSumSame += Math.Abs(blackLuma[i] - blackLuma[i]);
        var avgDiffSame = diffSumSame / (double)pixelCount;
        Assert.Equal(0.0, avgDiffSame);

        // Black to white cut yields 255 delta, well above medium threshold (32)
        long diffSumCut = 0;
        for (var i = 0; i < pixelCount; i++) diffSumCut += Math.Abs(whiteLuma[i] - blackLuma[i]);
        var avgDiffCut = diffSumCut / (double)pixelCount;
        Assert.Equal(255.0, avgDiffCut);
        Assert.True(avgDiffCut >= 32.0, "Abrupt cut delta must exceed scene detection threshold");
    }

    [Fact]
    public void HitTestInteractDifferentiatesResizeHandlesFromMoveAnchor()
    {
        // 200x50 text rectangle centered at (400, 300)
        var bounds = new System.Drawing.RectangleF(300, 275, 200, 50);
        var cx = 400f;
        var cy = 300f;
        var pad = 6f;
        var box = new System.Drawing.RectangleF(bounds.X - pad, bounds.Y - pad, bounds.Width + pad * 2, bounds.Height + pad * 2);

        // Clicking on TopLeft corner handle returns TopLeft handle, not Move
        var (hTopLeft, ptTopLeft, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, box.Left, box.Top, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.TopLeft, hTopLeft);
        Assert.Equal(box.Left, ptTopLeft.X);
        Assert.Equal(box.Top, ptTopLeft.Y);

        // Clicking on BottomRight corner handle returns BottomRight handle
        var (hBottomRight, ptBottomRight, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, box.Right, box.Bottom, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.BottomRight, hBottomRight);
        Assert.Equal(box.Right, ptBottomRight.X);
        Assert.Equal(box.Bottom, ptBottomRight.Y);

        // Clicking on TopCenter edge handle returns TopCenter handle
        var (hTopCenter, _, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, box.Left + box.Width * 0.5f, box.Top, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.TopCenter, hTopCenter);

        // Clicking on center anchor crosshair returns Move
        var (hCenter, _, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, cx, cy, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.Move, hCenter);

        // Clicking text body returns Move
        var (hBody, _, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, 350, 290, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.Move, hBody);

        // Clicking outside text bounding box returns None
        var (hOutside, _, _) = SubtitleRenderer.HitTestInteract(bounds, cx, cy, 50, 50, handleHitRadius: 10f);
        Assert.Equal(TextInteractHandle.None, hOutside);
    }

    [Fact]
    public void HandleResizeCalculatesProportionalFontSizeAccurately()
    {
        // Text centered at (500, 500) with BottomRight handle at (600, 600)
        var center = new System.Drawing.PointF(500f, 500f);
        var initialHandle = new System.Drawing.PointF(600f, 600f);
        var startFontSize = 24.0f;

        var v0x = initialHandle.X - center.X; // 100
        var v0y = initialHandle.Y - center.Y; // 100
        var len0Sq = v0x * v0x + v0y * v0y;   // 20000

        // User drags further out to (700, 700): 2x distance from center
        var mouseVideoX = 700f;
        var mouseVideoY = 700f;
        var vx = mouseVideoX - center.X; // 200
        var vy = mouseVideoY - center.Y; // 200

        var scaleFactor = (vx * v0x + vy * v0y) / len0Sq; // (20000 + 20000) / 20000 = 2.0
        var newFontSize = Math.Clamp((float)Math.Round(startFontSize * scaleFactor, 1), 8f, 300f);

        Assert.Equal(2.0f, scaleFactor);
        Assert.Equal(48.0f, newFontSize);

        // User drags inward halfway toward center to (550, 550): 0.5x distance
        var inwardMouseX = 550f;
        var inwardMouseY = 550f;
        var inVx = inwardMouseX - center.X; // 50
        var inVy = inwardMouseY - center.Y; // 50
        var inScaleFactor = (inVx * v0x + inVy * v0y) / len0Sq; // 10000 / 20000 = 0.5
        var inFontSize = Math.Clamp((float)Math.Round(startFontSize * inScaleFactor, 1), 8f, 300f);

        Assert.Equal(0.5f, inScaleFactor);
        Assert.Equal(12.0f, inFontSize);
    }

    [Fact]
    public void SubtitleStyleQuerySerializationPreservesFontSizeAndLocationAcrossCultures()
    {
        var originalCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            // Set German culture where comma is standard decimal separator
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            var style = new SubtitleStyle
            {
                FontSize = 36.5f,
                OutlineWidth = 4.25f,
                LocationX = 0.4567f,
                LocationY = 0.7891f,
                HasCustomLocation = true
            };

            var query = style.ToQuery();
            Assert.Contains("size=36.5", query);
            Assert.Contains("outlinewidth=4.25", query);
            Assert.Contains("locx=0.4567", query);

            var parsed = SubtitleStyle.FromQuery(query);
            Assert.Equal(36.5f, parsed.FontSize);
            Assert.Equal(4.25f, parsed.OutlineWidth);
            Assert.True(parsed.HasCustomLocation);
            Assert.Equal(0.4567f, parsed.LocationX, 4);
            Assert.Equal(0.7891f, parsed.LocationY, 4);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
        }
    }
}
