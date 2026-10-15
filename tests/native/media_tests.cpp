#include "openrend.h"
#include <gtest/gtest.h>
#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <numeric>
#include <vector>

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
