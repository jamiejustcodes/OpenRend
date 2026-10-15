#include "openrend.h"
#include <algorithm>
#include <cmath>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/hwcontext.h>
#include <libavutil/imgutils.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
}

namespace {
thread_local std::string last_error;
std::string av_error(int code) { char message[AV_ERROR_MAX_STRING_SIZE]{}; av_strerror(code, message, sizeof(message)); return message; }
void check(int code, const char* operation) { if (code < 0) throw std::runtime_error(std::string(operation) + ": " + av_error(code)); }
void require(bool valid, const char* message) { if (!valid) throw std::runtime_error(message); }
template<class F> int safe(F&& function) { try { last_error.clear(); return function(); } catch (const std::exception& e) { last_error = e.what(); return -1; } catch (...) { last_error = "Unexpected native media failure"; return -1; } }
struct Input {
    AVFormatContext* format{};
    AVCodecContext* codec{};
    AVFrame* frame{ av_frame_alloc() };
    AVPacket* packet{ av_packet_alloc() };
    int stream{ -1 };
    bool draining{};
    bool hardware_configured{};
    bool hardware_active{};
    ~Input() { av_packet_free(&packet); av_frame_free(&frame); avcodec_free_context(&codec); avformat_close_input(&format); }
    void open(const char* path, AVMediaType type) {
        check(avformat_open_input(&format, path, nullptr, nullptr), "Open media");
        check(avformat_find_stream_info(format, nullptr), "Read stream information");
        stream = av_find_best_stream(format, type, -1, -1, nullptr, 0);
        if (stream < 0) return;
        open_codec(type == AVMEDIA_TYPE_VIDEO);
    }
    void open_codec(bool use_hardware) {
        const AVCodec* implementation = avcodec_find_decoder(format->streams[stream]->codecpar->codec_id);
        require(implementation != nullptr, "Unsupported codec");
        codec = avcodec_alloc_context3(implementation);
        require(codec && frame && packet, "Media allocation failed");
        check(avcodec_parameters_to_context(codec, format->streams[stream]->codecpar), "Read codec parameters");
        codec->thread_count = 4;
        if (use_hardware) {
            for (int i = 0; const auto* config = avcodec_get_hw_config(implementation, i); ++i) {
                if (!(config->methods & AV_CODEC_HW_CONFIG_METHOD_HW_DEVICE_CTX) || config->device_type != AV_HWDEVICE_TYPE_D3D11VA) continue;
                AVBufferRef* device{};
                if (av_hwdevice_ctx_create(&device, AV_HWDEVICE_TYPE_D3D11VA, nullptr, nullptr, 0) >= 0) {
                    codec->hw_device_ctx = device; codec->opaque = this; hardware_configured = true;
                    codec->get_format = [](AVCodecContext* context, const AVPixelFormat* formats) {
                        auto* input = static_cast<Input*>(context->opaque);
                        for (auto p = formats; *p != AV_PIX_FMT_NONE; ++p) if (*p == AV_PIX_FMT_D3D11) { input->hardware_active = true; return *p; }
                        input->hardware_active = false;
                        for (auto p = formats; *p != AV_PIX_FMT_NONE; ++p) if (*p != AV_PIX_FMT_D3D11 && *p != AV_PIX_FMT_DXVA2_VLD) return *p;
                        return AV_PIX_FMT_NONE;
                    };
                }
                break;
            }
        }
        const int opened = avcodec_open2(codec, implementation, nullptr);
        if (opened < 0 && hardware_configured) { software_fallback(); return; }
        check(opened, "Open decoder");
    }
    void software_fallback() { avcodec_free_context(&codec); av_frame_unref(frame); hardware_active = hardware_configured = false; draining = false; open_codec(false); }
    double start() const { const auto* s = format->streams[stream]; return s->start_time == AV_NOPTS_VALUE ? 0.0 : s->start_time * av_q2d(s->time_base); }
    double time() const { const auto* s = format->streams[stream]; return frame->best_effort_timestamp == AV_NOPTS_VALUE ? 0.0 : frame->best_effort_timestamp * av_q2d(s->time_base) - start(); }
    double frame_end() const {
        const auto* s = format->streams[stream];
        double duration = frame->duration > 0 ? frame->duration * av_q2d(s->time_base) : 0;
        if (duration <= 0 && s->avg_frame_rate.num > 0 && s->avg_frame_rate.den > 0) duration = 1.0 / av_q2d(s->avg_frame_rate);
        return time() + std::max(duration, 0.000001);
    }
    void seek(double seconds) {
        const auto* s = format->streams[stream];
        int64_t target = static_cast<int64_t>((seconds + start()) / av_q2d(s->time_base));
        check(av_seek_frame(format, stream, target, AVSEEK_FLAG_BACKWARD), "Seek media");
        avcodec_flush_buffers(codec); av_packet_unref(packet); av_frame_unref(frame); draining = false;
    }
    bool next() {
        while (true) {
            int result = avcodec_receive_frame(codec, frame);
            if (result == 0) return true;
            if (result == AVERROR_EOF) return false;
            check(result == AVERROR(EAGAIN) ? 0 : result, "Decode frame");
            if (draining) return false;
            while (true) { result = av_read_frame(format, packet); if (result < 0 || packet->stream_index == stream) break; av_packet_unref(packet); }
            if (result < 0) { check(avcodec_send_packet(codec, nullptr), "Drain decoder"); draining = true; }
            else { result = avcodec_send_packet(codec, packet); av_packet_unref(packet); check(result, "Submit packet"); }
        }
    }
};
struct Decoder {
    std::mutex mutex;
    Input video, audio;
    SwsContext* scaler{};
    SwrContext* resampler{};
    AVFrame* transferred{ av_frame_alloc() };
    AVFrame* retained_video{ av_frame_alloc() };
    int width{}, height{};
    double previous_video_time{ -100 };
    ~Decoder() { av_frame_free(&retained_video); av_frame_free(&transferred); sws_freeContext(scaler); swr_free(&resampler); }
};
}
void or_set_error(const char* message) { last_error = message; }
extern "C" {
const char* or_last_error() { return last_error.c_str(); }
const char* or_version() { return "OpenRend Native 0.1 / FFmpeg " AV_STRINGIFY(LIBAVFORMAT_VERSION); }
int or_probe(const char* path, or_metadata* metadata) { return safe([&] {
    require(path && metadata, "Missing media path or metadata output");
    Input input; check(avformat_open_input(&input.format, path, nullptr, nullptr), "Open media");
    check(avformat_find_stream_info(input.format, nullptr), "Read stream information");
    *metadata = {};
    metadata->duration_seconds = input.format->duration == AV_NOPTS_VALUE ? 0 : input.format->duration / static_cast<double>(AV_TIME_BASE);
    metadata->bit_rate = input.format->bit_rate;
    for (unsigned i = 0; i < input.format->nb_streams; ++i) {
        const AVStream* stream = input.format->streams[i]; const auto* p = stream->codecpar;
        if (p->codec_type == AVMEDIA_TYPE_VIDEO) {
            if (metadata->video_streams++ == 0) { metadata->width = p->width; metadata->height = p->height; metadata->fps = av_q2d(av_guess_frame_rate(input.format, const_cast<AVStream*>(stream), nullptr)); }
        } else if (p->codec_type == AVMEDIA_TYPE_AUDIO) {
            if (metadata->audio_streams++ == 0) { metadata->sample_rate = p->sample_rate; metadata->channels = p->ch_layout.nb_channels; }
        }
        if (metadata->duration_seconds <= 0 && stream->duration != AV_NOPTS_VALUE) metadata->duration_seconds = std::max(metadata->duration_seconds, stream->duration * av_q2d(stream->time_base));
    }
    return 0;
}); }
int or_decoder_open(const char* path, int width, int height, void** output) { return safe([&] {
    require(path && output && width >= 0 && height >= 0 && width <= 16384 && height <= 16384, "Invalid decoder arguments"); *output = nullptr;
    auto decoder = std::make_unique<Decoder>(); decoder->video.open(path, AVMEDIA_TYPE_VIDEO); decoder->audio.open(path, AVMEDIA_TYPE_AUDIO);
    require(decoder->video.codec || decoder->audio.codec, "Media has no supported audio or video streams");
    if (decoder->video.codec) { decoder->width = width ? width : decoder->video.codec->width; decoder->height = height ? height : decoder->video.codec->height; }
    if (decoder->audio.codec) {
        AVChannelLayout stereo = AV_CHANNEL_LAYOUT_STEREO;
        check(swr_alloc_set_opts2(&decoder->resampler, &stereo, AV_SAMPLE_FMT_FLT, 48000, &decoder->audio.codec->ch_layout, decoder->audio.codec->sample_fmt, decoder->audio.codec->sample_rate, 0, nullptr), "Create audio resampler");
        check(swr_init(decoder->resampler), "Initialize audio resampler");
    }
    *output = decoder.release(); return 0;
}); }
int or_decoder_frame(void* handle, double seconds, uint8_t* bgra, int capacity) { return safe([&] {
    require(handle && bgra && std::isfinite(seconds) && seconds >= 0, "Invalid video frame arguments"); auto& d = *static_cast<Decoder*>(handle); std::lock_guard lock(d.mutex);
    require(d.video.codec, "Media has no video stream"); require(int64_t(d.width) * d.height * 4 <= capacity, "Video frame buffer is too small");
    auto& v = d.video;
    if (seconds < d.previous_video_time - 0.0001 || seconds > d.previous_video_time + 1.0 || d.previous_video_time < -10) { v.seek(seconds); av_frame_unref(d.retained_video); }
    auto decode = [&] { bool got = v.frame->data[0] && v.frame_end() > seconds + 0.0000001;
        while (!got && v.next()) { av_frame_unref(d.retained_video); check(av_frame_ref(d.retained_video, v.frame), "Retain decoded frame"); got = v.frame_end() > seconds + 0.0000001; }
        if (!got && d.retained_video->data[0]) { check(av_frame_ref(v.frame, d.retained_video), "Restore final video frame"); got = true; }
        return got;
    };
    bool got;
    try { got = decode(); } catch (...) { if (!v.hardware_configured) throw; v.software_fallback(); v.seek(seconds); got = decode(); }
    // The final decoded frame remains useful when the caller is at the media end.
    require(got || v.frame->data[0], "No video frame at requested time");
    AVFrame* source = v.frame;
    if (source->format == AV_PIX_FMT_D3D11) { av_frame_unref(d.transferred); check(av_hwframe_transfer_data(d.transferred, source, 0), "Transfer hardware decoded video frame"); source = d.transferred; }
    d.scaler = sws_getCachedContext(d.scaler, source->width, source->height, static_cast<AVPixelFormat>(source->format), d.width, d.height, AV_PIX_FMT_BGRA, SWS_BILINEAR, nullptr, nullptr, nullptr);
    require(d.scaler, "Create video scaler failed"); uint8_t* destinations[] = { bgra, nullptr, nullptr, nullptr }; int strides[] = { d.width * 4, 0, 0, 0 };
    check(sws_scale(d.scaler, source->data, source->linesize, 0, source->height, destinations, strides), "Convert video frame"); d.previous_video_time = seconds; return 0;
}); }
int or_decoder_audio(void* handle, double seconds, int requested, float* output, int capacity) { return safe([&] {
    require(handle && output && requested > 0 && requested <= 48000 * 60 && int64_t(requested) * 2 <= capacity && std::isfinite(seconds) && seconds >= 0, "Invalid audio buffer arguments");
    auto& d = *static_cast<Decoder*>(handle); std::lock_guard lock(d.mutex); require(d.audio.codec && d.resampler, "Media has no audio stream");
    auto& a = d.audio; a.seek(std::max(0.0, seconds - 0.1)); swr_close(d.resampler); check(swr_init(d.resampler), "Reset audio resampler");
    std::fill_n(output, requested * 2, 0.0f); int written = 0;
    while (a.next()) {
        const double frame_time = a.time(); const int frame_capacity = swr_get_out_samples(d.resampler, a.frame->nb_samples); std::vector<float> converted(static_cast<size_t>(frame_capacity) * 2); uint8_t* planes[] = { reinterpret_cast<uint8_t*>(converted.data()) };
        const double delay = swr_get_delay(d.resampler, 48000) / 48000.0;
        int count = swr_convert(d.resampler, planes, frame_capacity, const_cast<const uint8_t**>(a.frame->extended_data), a.frame->nb_samples); check(count, "Resample audio");
        int64_t destination = std::llround((frame_time - delay - seconds) * 48000.0); int source = destination < 0 ? static_cast<int>(std::min<int64_t>(-destination, count)) : 0; destination = std::max<int64_t>(0, destination);
        if (destination >= requested) break;
        int copy = std::min(count - source, requested - static_cast<int>(destination));
        if (copy > 0) { std::copy_n(converted.data() + source * 2, copy * 2, output + destination * 2); written = std::max(written, static_cast<int>(destination) + copy); }
        if (written >= requested) break;
    }
    return written;
}); }
int or_decoder_is_hardware(void* handle) { if (!handle) return 0; auto& d = *static_cast<Decoder*>(handle); std::lock_guard lock(d.mutex); return d.video.hardware_active ? 1 : 0; }
void or_decoder_close(void* decoder) { delete static_cast<Decoder*>(decoder); }
}
