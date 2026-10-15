#include "openrend.h"
#include <gtest/gtest.h>
#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <numeric>
#include <vector>
#include <random>

struct DecoderHandle { void* value{}; ~DecoderHandle() { or_decoder_close(value); } };
TEST(Media, ReportsRealStreamMetadata) {
    static_assert(sizeof(or_metadata) == 48);
    or_metadata metadata{}; ASSERT_EQ(0, or_probe(OR_TEST_MEDIA, &metadata)) << or_last_error();
    EXPECT_EQ(320, metadata.width); EXPECT_EQ(180, metadata.height); EXPECT_NEAR(3.0, metadata.duration_seconds, 0.1); EXPECT_NEAR(30, metadata.fps, 0.01); EXPECT_EQ(1, metadata.video_streams); EXPECT_EQ(1, metadata.audio_streams); EXPECT_EQ(48000, metadata.sample_rate);
}
TEST(Media, RejectsMissingMediaAndUndersizedBuffers) {
    or_metadata metadata{}; EXPECT_EQ(-1, or_probe("Z:/does-not-exist.mp4", &metadata)); EXPECT_GT(strlen(or_last_error()), 0u);
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(OR_TEST_MEDIA, 160, 90, &decoder.value)) << or_last_error(); std::vector<uint8_t> pixels(160 * 90 * 4);
    EXPECT_EQ(-1, or_decoder_frame(decoder.value, 0, pixels.data(), 10));
    EXPECT_EQ(-1, or_decoder_frame(decoder.value, -1, pixels.data(), static_cast<int>(pixels.size())));
}
TEST(Media, DecodesAndSeeksDistinctRealVideoFrames) {
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(OR_TEST_MEDIA, 160, 90, &decoder.value)) << or_last_error();
    std::vector<uint8_t> first(160 * 90 * 4), later(first.size()), again(first.size());
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.2, first.data(), static_cast<int>(first.size()))) << or_last_error();
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 1.6, later.data(), static_cast<int>(later.size()))) << or_last_error();
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.2, again.data(), static_cast<int>(again.size()))) << or_last_error();
    EXPECT_NE(first, later); EXPECT_EQ(first, again); EXPECT_EQ(255, first[3]); EXPECT_GT(std::accumulate(first.begin(), first.end(), int64_t{}), 160 * 90 * 255);
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.21, again.data(), static_cast<int>(again.size()))) << or_last_error(); EXPECT_EQ(first, again);
    EXPECT_EQ(0, or_decoder_frame(decoder.value, 2.999, later.data(), static_cast<int>(later.size()))) << or_last_error();
}
TEST(Media, ResamplesAndSeeksAudioOnlyAndAvSources) {
    for (auto path : { OR_TEST_AUDIO, OR_TEST_MEDIA }) {
        DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(path, 0, 0, &decoder.value)) << or_last_error(); std::vector<float> audio(48000 * 2);
        int frames = or_decoder_audio(decoder.value, 0.25, 48000, audio.data(), static_cast<int>(audio.size())); ASSERT_GT(frames, 47000) << or_last_error();
        float peak = 0; for (float sample : audio) { EXPECT_TRUE(std::isfinite(sample)); peak = std::max(peak, std::abs(sample)); } EXPECT_GT(peak, 0.02f); EXPECT_LT(peak, 1.0f);
        EXPECT_NEAR(audio[1000], audio[1001], 0.0001f);
        EXPECT_EQ(-1, or_decoder_audio(decoder.value, 0, 48000, audio.data(), 100));
    }
}
TEST(Media, HoldsStillImageAtArbitraryTimelineTime) {
    std::string image = OR_TEST_MEDIA; image.replace(image.size() - 3, 3, "png");
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(image.c_str(), 160, 90, &decoder.value)) << or_last_error();
    std::vector<uint8_t> pixels(160 * 90 * 4), later(pixels.size());
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0, pixels.data(), static_cast<int>(pixels.size()))) << or_last_error();
    EXPECT_EQ(0, or_decoder_frame(decoder.value, 4, later.data(), static_cast<int>(later.size()))) << or_last_error(); EXPECT_EQ(pixels, later);
}
TEST(Preview, CreatesD3DDevicePresentsAndResizes) {
    HWND hwnd = CreateWindowExW(0, L"STATIC", L"OpenRend native test", WS_POPUP, 0, 0, 320, 180, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    ASSERT_NE(nullptr, hwnd); void* renderer{};
    ASSERT_EQ(0, or_renderer_create(hwnd, 320, 180, &renderer)) << or_last_error();
    std::vector<uint8_t> pixels(160 * 90 * 4, 120);
    EXPECT_EQ(0, or_renderer_present(renderer, pixels.data(), 160, 90, 640)) << or_last_error();
    EXPECT_EQ(0, or_renderer_resize(renderer, 640, 360)) << or_last_error();
    EXPECT_EQ(0, or_renderer_present(renderer, pixels.data(), 160, 90, 640)) << or_last_error();
    EXPECT_TRUE(or_renderer_is_hardware(renderer) == 0 || or_renderer_is_hardware(renderer) == 1);
    or_renderer_close(renderer); DestroyWindow(hwnd);
}
struct CompositorHandle { void* value{}; ~CompositorHandle() { or_compositor_close(value); } };
TEST(Compositor, BlendsOrderedLayersReadsOpaquePixelsAndClearsGaps) {
    CompositorHandle c; ASSERT_EQ(0, or_compositor_create(4, 4, &c.value)) << or_last_error();
    std::vector<uint8_t> red(4 * 4 * 4), blue(red.size()), result(red.size());
    for (size_t i = 0; i < red.size(); i += 4) { red[i + 2] = red[i + 3] = 255; blue[i] = blue[i + 3] = 255; }
    ASSERT_EQ(0, or_compositor_begin(c.value)); ASSERT_EQ(0, or_compositor_layer(c.value, red.data(), 4, 4, 16, 1)) << or_last_error();
    ASSERT_EQ(0, or_compositor_layer(c.value, blue.data(), 4, 4, 16, 0.5)) << or_last_error();
    ASSERT_EQ(0, or_compositor_readback(c.value, result.data(), static_cast<int>(result.size()))) << or_last_error();
    for (size_t i = 0; i < result.size(); i += 4) { EXPECT_NEAR(128, result[i], 1); EXPECT_EQ(0, result[i + 1]); EXPECT_NEAR(128, result[i + 2], 1); EXPECT_EQ(255, result[i + 3]); }
    ASSERT_EQ(0, or_compositor_begin(c.value)); ASSERT_EQ(0, or_compositor_readback(c.value, result.data(), static_cast<int>(result.size())));
    for (size_t i = 0; i < result.size(); ++i) EXPECT_EQ(i % 4 == 3 ? 255 : 0, result[i]);
    EXPECT_EQ(-1, or_compositor_readback(c.value, result.data(), 1)); EXPECT_EQ(-1, or_compositor_layer(c.value, blue.data(), 4, 4, 16, NAN));
}
TEST(Compositor, LetterboxesWideLayersAndAcceptsPaddedRows) {
    CompositorHandle c; ASSERT_EQ(0, or_compositor_create(8, 8, &c.value)) << or_last_error();
    std::vector<uint8_t> wide(40 * 4, 0), output(8 * 8 * 4);
    for (int y = 0; y < 4; ++y) for (int x = 0; x < 8; ++x) { wide[y * 40 + x * 4 + 1] = 255; wide[y * 40 + x * 4 + 3] = 255; }
    ASSERT_EQ(0, or_compositor_layer(c.value, wide.data(), 8, 4, 40, 1)) << or_last_error(); ASSERT_EQ(0, or_compositor_readback(c.value, output.data(), static_cast<int>(output.size())));
    for (int y = 0; y < 8; ++y) for (int x = 0; x < 8; ++x) { const int p = (y * 8 + x) * 4; EXPECT_EQ(y >= 2 && y < 6 ? 255 : 0, output[p + 1]); EXPECT_EQ(255, output[p + 3]); }
}
TEST(Compositor, HardwareAndWarpMatchCpuStraightAlphaWithinOneByte) {
    for (bool force_warp : { false, true }) {
        SetEnvironmentVariableW(L"OPENREND_FORCE_WARP", force_warp ? L"1" : nullptr);
        CompositorHandle c; ASSERT_EQ(0, or_compositor_create(32, 16, &c.value)) << or_last_error();
        if (force_warp) EXPECT_EQ(0, or_compositor_is_hardware(c.value));
        std::mt19937 random(173); std::vector<uint8_t> layer(32 * 16 * 4), expected(layer.size(), 0), actual(layer.size());
        for (size_t p = 3; p < expected.size(); p += 4) expected[p] = 255;
        for (int index = 0; index < 8; ++index) {
            const double opacity = index == 0 ? 1 : (index + 1) / 9.0;
            for (auto& value : layer) value = static_cast<uint8_t>(random() & 255);
            for (size_t p = 0; p < expected.size(); p += 4) { const double alpha = opacity * layer[p + 3] / 255; for (int channel = 0; channel < 3; ++channel) expected[p + channel] = static_cast<uint8_t>(std::nearbyint(layer[p + channel] * alpha + expected[p + channel] * (1 - alpha))); }
            ASSERT_EQ(0, or_compositor_layer(c.value, layer.data(), 32, 16, 32 * 4, opacity)) << or_last_error();
        }
        ASSERT_EQ(0, or_compositor_readback(c.value, actual.data(), static_cast<int>(actual.size()))) << or_last_error();
        int maximum_error = 0; for (size_t p = 0; p < expected.size(); ++p) maximum_error = std::max(maximum_error, std::abs(int(expected[p]) - actual[p])); EXPECT_LE(maximum_error, 1) << "force_warp=" << force_warp;
    }
    SetEnvironmentVariableW(L"OPENREND_FORCE_WARP", nullptr);
}
