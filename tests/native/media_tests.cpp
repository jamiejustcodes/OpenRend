#include "openrend.h"
#include <gtest/gtest.h>
#include <Windows.h>
#include <algorithm>
#include <cmath>
#include <numeric>
#include <vector>
#include <random>
#include <filesystem>
#include <string>

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
TEST(Media, DrainsResamplerAtTheFinal44100HzSample) {
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(OR_TEST_AUDIO, 0, 0, &decoder.value)) << or_last_error();
    std::vector<float> audio((96000 + 64) * 2);
    ASSERT_EQ(96000, or_decoder_audio(decoder.value, 0, 96064, audio.data(), static_cast<int>(audio.size()))) << or_last_error();
    float tail_peak = 0;
    for (int sample = 95984 * 2; sample < 96000 * 2; ++sample) tail_peak = std::max(tail_peak, std::abs(audio[sample]));
    EXPECT_GT(tail_peak, 0.03f);
    for (size_t sample = 96000 * 2; sample < audio.size(); ++sample) EXPECT_EQ(0, audio[sample]);
}

class SourceTiming : public testing::Test {
protected:
    static inline std::filesystem::path directory;
    static inline std::filesystem::path temporary_root;
    static std::string file(const char* name) {
        auto value = (directory / name).u8string();
        return std::string(reinterpret_cast<const char*>(value.data()), value.size());
    }
    static void generate(const std::wstring& arguments, const wchar_t* name) {
        auto executable = std::filesystem::path(OR_TEST_MEDIA).parent_path().parent_path().parent_path() / L".deps/ffmpeg/bin/ffmpeg.exe";
        auto command = L"\"" + executable.wstring() + L"\" -v error -y " + arguments + L" \"" + (directory / name).wstring() + L"\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup); PROCESS_INFORMATION process{};
        if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process)) throw std::runtime_error("Could not start fixture encoder");
        const auto waited = WaitForSingleObject(process.hProcess, 60000);
        if (waited != WAIT_OBJECT_0) { TerminateProcess(process.hProcess, 1); WaitForSingleObject(process.hProcess, 5000); }
        DWORD code{}; GetExitCodeProcess(process.hProcess, &code); CloseHandle(process.hProcess); CloseHandle(process.hThread);
        if (waited != WAIT_OBJECT_0 || code != 0) throw std::runtime_error("Fixture encoder failed");
    }
    static void SetUpTestSuite() {
        wchar_t root[MAX_PATH]{}, unique[MAX_PATH]{};
        ASSERT_GT(GetTempPathW(MAX_PATH, root), 0u); temporary_root = std::filesystem::absolute(root).lexically_normal();
        ASSERT_NE(0u, GetTempFileNameW(root, L"ord", 0, unique)); ASSERT_TRUE(DeleteFileW(unique)); ASSERT_TRUE(CreateDirectoryW(unique, nullptr));
        directory = std::filesystem::absolute(unique).lexically_normal();
        ASSERT_NO_THROW(generate(LR"(-f lavfi -i color=red:s=160x90:r=30:d=1 -itsoffset 0.25 -f lavfi -i sine=frequency=997:sample_rate=48000:duration=0.75 -map 0:v -map 1:a -c:v ffv1 -c:a pcm_f32le)", L"delayed.mkv"));
        ASSERT_NO_THROW(generate(L"-i \"" + (directory / L"delayed.mkv").wstring() + L"\" -c copy -output_ts_offset 5", L"offset.mkv"));
        ASSERT_NO_THROW(generate(LR"(-f lavfi -i color=red:s=32x16:r=25:d=0.04 -f lavfi -i color=green:s=32x16:r=25:d=0.04 -f lavfi -i color=blue:s=32x16:r=25:d=0.04 -f lavfi -i color=white:s=32x16:r=25:d=0.04 -filter_complex "[0:v][1:v][2:v][3:v]concat=n=4:v=1:a=0,settb=1/1000,setpts='if(eq(N,0),0,if(eq(N,1),120,if(eq(N,2),520,560)))'[v]" -map [v] -fps_mode vfr -c:v ffv1)", L"vfr.mkv"));
    }
    static void TearDownTestSuite() {
        auto parent = directory.parent_path(); parent += L"/"; parent = parent.lexically_normal();
        if (!directory.empty() && parent == temporary_root && directory.filename().wstring().starts_with(L"ord")) {
            std::error_code error; std::filesystem::remove_all(directory, error);
        }
    }
};
TEST_F(SourceTiming, DelayedAudioRetainsLeadingSilenceAcrossSeeks) {
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(file("delayed.mkv").c_str(), 160, 90, &decoder.value)) << or_last_error();
    std::vector<float> audio(9600 * 2);
    ASSERT_EQ(9600, or_decoder_audio(decoder.value, 0, 9600, audio.data(), static_cast<int>(audio.size()))) << or_last_error();
    EXPECT_TRUE(std::all_of(audio.begin(), audio.end(), [](float sample) { return sample == 0; }));
    ASSERT_EQ(4800, or_decoder_audio(decoder.value, 0.2, 4800, audio.data(), static_cast<int>(audio.size()))) << or_last_error();
    EXPECT_TRUE(std::all_of(audio.begin(), audio.begin() + 2400 * 2, [](float sample) { return sample == 0; }));
    float peak = 0; for (int sample = 2400 * 2; sample < 4800 * 2; ++sample) peak = std::max(peak, std::abs(audio[sample])); EXPECT_GT(peak, 0.04f);
}
TEST_F(SourceTiming, CommonTimestampOffsetDoesNotBecomeSourceDuration) {
    or_metadata metadata{}; ASSERT_EQ(0, or_probe(file("offset.mkv").c_str(), &metadata)); EXPECT_NEAR(1, metadata.duration_seconds, 0.001);
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(file("offset.mkv").c_str(), 32, 16, &decoder.value));
    std::vector<float> audio(9600 * 2); ASSERT_EQ(9600, or_decoder_audio(decoder.value, 0, 9600, audio.data(), static_cast<int>(audio.size())));
    EXPECT_TRUE(std::all_of(audio.begin(), audio.end(), [](float sample) { return sample == 0; }));
    std::vector<uint8_t> pixels(32 * 16 * 4); ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.2, pixels.data(), static_cast<int>(pixels.size())));
    EXPECT_GT(pixels[2], 250); EXPECT_LT(pixels[0], 3);
}
TEST_F(SourceTiming, VariableRateFramesHoldUntilTheNextPts) {
    DecoderHandle decoder; ASSERT_EQ(0, or_decoder_open(file("vfr.mkv").c_str(), 32, 16, &decoder.value)); std::vector<uint8_t> pixels(32 * 16 * 4);
    for (double time : { 0.12, 0.2, 0.5, 0.519, 0.15 }) {
        ASSERT_EQ(0, or_decoder_frame(decoder.value, time, pixels.data(), static_cast<int>(pixels.size()))) << or_last_error();
        EXPECT_LT(pixels[0], 3) << time; EXPECT_NEAR(128, pixels[1], 3) << time; EXPECT_LT(pixels[2], 3) << time;
    }
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.52, pixels.data(), static_cast<int>(pixels.size()))); EXPECT_GT(pixels[0], 250);
    ASSERT_EQ(0, or_decoder_frame(decoder.value, 0.119, pixels.data(), static_cast<int>(pixels.size()))); EXPECT_GT(pixels[2], 250);
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
