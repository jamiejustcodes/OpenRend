using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenRend.Core;
using OpenRend.Media;
using OpenRend.Timeline;
using Xunit;

namespace OpenRend.Media.Tests;

public sealed class PerformanceBufferPoolingTests : IClassFixture<MediaFixture>
{
    private readonly MediaFixture fixture;

    public PerformanceBufferPoolingTests(MediaFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task RenderFrame_DestinationBufferOverload_MatchesAllocatingMethodBitExactly()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [asset],
            Tracks =
            [
                new TimelineTrack
                {
                    Kind = TrackKind.Video,
                    Clips = [new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 30 }]
                }
            ]
        };

        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var allocatingResult = renderer.RenderFrame(project, 5, 64, 48);

        var destination = new byte[64 * 48 * 4];
        renderer.RenderFrame(project, 5, 64, 48, destination);

        Assert.Equal(allocatingResult, destination);
    }

    [Fact]
    public async Task RenderFrame_WithRentedBufferLargerThanDimensions_OperatesWithoutError()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [asset],
            Tracks =
            [
                new TimelineTrack
                {
                    Kind = TrackKind.Video,
                    Clips = [new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 30 }]
                }
            ]
        };

        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        // Rent a buffer that may have capacity > 64 * 48 * 4
        var rented = ArrayPool<byte>.Shared.Rent(64 * 48 * 4 + 1024);
        try
        {
            renderer.RenderFrame(project, 5, 64, 48, rented);
            var allocatingResult = renderer.RenderFrame(project, 5, 64, 48);

            Assert.True(allocatingResult.AsSpan().SequenceEqual(rented.AsSpan(0, 64 * 48 * 4)));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    [Fact]
    public async Task RenderAudio_DestinationBufferOverload_MatchesAllocatingMethodBitExactly()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [asset],
            Tracks =
            [
                new TimelineTrack
                {
                    Kind = TrackKind.Audio,
                    Gain = 0.8,
                    Pan = -0.25,
                    Clips = [new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 60, Gain = 0.9 }]
                }
            ]
        };

        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var allocatingSamples = renderer.RenderAudio(project, 0.5, 1024);

        var destination = new float[1024 * MediaService.Channels];
        renderer.RenderAudio(project, 0.5, 1024, destination);

        Assert.Equal(allocatingSamples, destination);
    }

    [Fact]
    public async Task RenderAudio_WithSubBusAndMasterEffects_MatchesAllocatingMethod()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);
        var busId = Guid.NewGuid();
        var project = new ProjectDocument
        {
            Width = 64,
            Height = 48,
            Media = [asset],
            AudioBuses =
            [
                new AudioBusTrack
                {
                    Id = busId,
                    Name = "SubBus A",
                    Gain = 0.9,
                    Pan = 0.1
                }
            ],
            Tracks =
            [
                new TimelineTrack
                {
                    Kind = TrackKind.Audio,
                    BusTrackId = busId,
                    Gain = 0.85,
                    Clips = [new TimelineClip { MediaId = asset.Id, StartFrame = 0, DurationFrames = 60, Gain = 0.95 }]
                }
            ]
        };

        using var renderer = new TimelineRenderService(media, useGpuCompositing: false);

        var expected = renderer.RenderAudio(project, 0.25, 2048);
        var actual = new float[2048 * MediaService.Channels];
        renderer.RenderAudio(project, 0.25, 2048, actual);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task MediaService_GetFrameAndGetAudio_DestinationOverloads_MatchAllocatingMethods()
    {
        using var media = new MediaService();
        var asset = await media.ProbeAsync(fixture.Source);

        var allocatingFrame = media.GetFrame(asset.Path, 0.5, 64, 48);
        var pooledFrame = new byte[64 * 48 * 4];
        media.GetFrame(asset.Path, 0.5, 64, 48, pooledFrame);
        Assert.Equal(allocatingFrame, pooledFrame);

        var allocatingAudio = media.GetAudio(asset.Path, 0.2, 512);
        var pooledAudio = new float[512 * MediaService.Channels];
        media.GetAudio(asset.Path, 0.2, 512, pooledAudio);
        Assert.Equal(allocatingAudio, pooledAudio);
    }

    [Fact]
    public void ExactBufferPool_RentsExactSizesAndReusesBuffers()
    {
        const int byteSize = 1920 * 1080 * 4;
        var b1 = ExactBufferPool.RentBytes(byteSize);
        Assert.Equal(byteSize, b1.Length);
        ExactBufferPool.ReturnBytes(b1);

        var b2 = ExactBufferPool.RentBytes(byteSize);
        Assert.Same(b1, b2); // Reused identical buffer
        ExactBufferPool.ReturnBytes(b2);

        const int floatSize = 4096;
        var f1 = ExactBufferPool.RentFloats(floatSize);
        Assert.Equal(floatSize, f1.Length);
        ExactBufferPool.ReturnFloats(f1);

        var f2 = ExactBufferPool.RentFloats(floatSize);
        Assert.Same(f1, f2); // Reused identical buffer
        ExactBufferPool.ReturnFloats(f2);
    }
}

