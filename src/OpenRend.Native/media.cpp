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
#include <libavutil/display.h>
#include <libavutil/imgutils.h>
#include <libavutil/parseutils.h>
#include <libswscale/swscale.h>
#include <libswresample/swresample.h>
}

namespace {
thread_local std::string last_error;
std::string av_error(int code) { char message[AV_ERROR_MAX_STRING_SIZE]{}; av_strerror(code, message, sizeof(message)); return message; }
void check(int code, const char* operation) { if (code < 0) throw std::runtime_error(std::string(operation) + ": " + av_error(code)); }
void require(bool valid, const char* message) { if (!valid) throw std::runtime_error(message); }
template<class F> int safe(F&& function) { try { last_error.clear(); return function(); } catch (const std::exception& e) { last_error = e.what(); return -1; } catch (...) { last_error = "Unexpected native media failure"; return -1; } }
double media_origin(const AVFormatContext* format) {
    if (format->start_time != AV_NOPTS_VALUE) return format->start_time / static_cast<double>(AV_TIME_BASE);
    double earliest = std::numeric_limits<double>::infinity();
    for (unsigned i = 0; i < format->nb_streams; ++i) {
        const auto* stream = format->streams[i];
        if (stream->start_time != AV_NOPTS_VALUE) earliest = std::min(earliest, stream->start_time * av_q2d(stream->time_base));
    }
    return std::isfinite(earliest) ? earliest : 0;
}
double media_duration(const AVFormatContext* format) {
    const double origin = media_origin(format);
    double end = 0;
    bool found = false, unknown = false;
    const bool matroska = format->iformat && std::strstr(format->iformat->name, "matroska");
    for (unsigned i = 0; i < format->nb_streams; ++i) {
        const auto* stream = format->streams[i];
        if (stream->codecpar->codec_type != AVMEDIA_TYPE_VIDEO && stream->codecpar->codec_type != AVMEDIA_TYPE_AUDIO) continue;
        if (stream->duration != AV_NOPTS_VALUE && stream->duration > 0) {
            const double start = stream->start_time == AV_NOPTS_VALUE ? origin : stream->start_time * av_q2d(stream->time_base);
            end = std::max(end, start + stream->duration * av_q2d(stream->time_base) - origin); found = true;
        } else {
            // Matroska's DURATION tag is the stream's final timestamp, including any offset.
            const auto* tag = matroska ? av_dict_get(stream->metadata, "DURATION", nullptr, 0) : nullptr;
            int64_t timestamp{};
            if (tag && av_parse_time(&timestamp, tag->value, 1) == 0) { end = std::max(end, timestamp / static_cast<double>(AV_TIME_BASE) - origin); found = true; }
            else unknown = true;
        }
    }
    const double fallback = format->duration == AV_NOPTS_VALUE ? 0 : format->duration / static_cast<double>(AV_TIME_BASE);
    return found && !unknown ? std::max(0.0, end) : std::max(end, fallback);
}
int quarter_turns(const AVCodecParameters* parameters) {
    // Container display matrices are stream-wide coded side data, not rotate tags.
    const auto* side = av_packet_side_data_get(parameters->coded_side_data, parameters->nb_coded_side_data, AV_PKT_DATA_DISPLAYMATRIX);
    if (!side || side->size < sizeof(int32_t) * 9) return 0;
    const auto* matrix = reinterpret_cast<const int32_t*>(side->data);
    const double angle = av_display_rotation_get(matrix);
    if (!std::isfinite(angle)) return 0;
    const int turns = static_cast<int>(std::lround(angle / 90.0));
    const double determinant = double(matrix[0]) * matrix[4] - double(matrix[1]) * matrix[3];
    if (std::abs(angle - turns * 90.0) > 0.01 || determinant <= 0) return 0;
    return (turns % 4 + 4) % 4;
}
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
    double time() const { const auto* s = format->streams[stream]; return frame->best_effort_timestamp == AV_NOPTS_VALUE ? 0.0 : frame->best_effort_timestamp * av_q2d(s->time_base) - media_origin(format); }
    void seek(double seconds) {
        const auto* s = format->streams[stream];
        int64_t target = static_cast<int64_t>(std::floor((seconds + media_origin(format)) / av_q2d(s->time_base) + 0.0000001));
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
    int rotation{};
    std::vector<uint8_t> scaled_pixels;
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
    metadata->duration_seconds = media_duration(input.format);
    metadata->bit_rate = input.format->bit_rate;
    for (unsigned i = 0; i < input.format->nb_streams; ++i) {
        const AVStream* stream = input.format->streams[i]; const auto* p = stream->codecpar;
        if (p->codec_type == AVMEDIA_TYPE_VIDEO) {
            if (metadata->video_streams++ == 0) {
                metadata->width = p->width; metadata->height = p->height;
                if (quarter_turns(p) % 2) std::swap(metadata->width, metadata->height);
                metadata->fps = av_q2d(av_guess_frame_rate(input.format, const_cast<AVStream*>(stream), nullptr));
            }
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
    if (decoder->video.codec) {
        decoder->rotation = quarter_turns(decoder->video.format->streams[decoder->video.stream]->codecpar);
        const bool portrait = decoder->rotation % 2;
        decoder->width = width ? width : portrait ? decoder->video.codec->height : decoder->video.codec->width;
        decoder->height = height ? height : portrait ? decoder->video.codec->width : decoder->video.codec->height;
    }
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
    auto decode = [&] {
        // A frame is held until the next PTS, which can be much later in VFR media.
        while (v.frame->data[0] || v.next()) {
            if (v.time() > seconds + 0.0000001) break;
            av_frame_unref(d.retained_video); check(av_frame_ref(d.retained_video, v.frame), "Retain decoded frame");
            av_frame_unref(v.frame);
        }
    };
    try { decode(); } catch (...) { if (!v.hardware_configured) throw; v.software_fallback(); v.seek(seconds); av_frame_unref(d.retained_video); decode(); }
    d.previous_video_time = seconds;
    if (!d.retained_video->data[0]) {
        std::fill_n(bgra, d.width * d.height * 4, uint8_t{});
        for (int64_t i = 3; i < int64_t(d.width) * d.height * 4; i += 4) bgra[i] = 255;
        return 0;
    }
    AVFrame* source = d.retained_video;
    if (source->format == AV_PIX_FMT_D3D11) { av_frame_unref(d.transferred); check(av_hwframe_transfer_data(d.transferred, source, 0), "Transfer hardware decoded video frame"); source = d.transferred; }
    const int scaled_width = d.rotation % 2 ? d.height : d.width;
    const int scaled_height = d.rotation % 2 ? d.width : d.height;
    d.scaler = sws_getCachedContext(d.scaler, source->width, source->height, static_cast<AVPixelFormat>(source->format), scaled_width, scaled_height, AV_PIX_FMT_BGRA, SWS_BILINEAR, nullptr, nullptr, nullptr);
    if (d.rotation) d.scaled_pixels.resize(static_cast<size_t>(scaled_width) * scaled_height * 4);
    require(d.scaler, "Create video scaler failed"); uint8_t* destinations[] = { d.rotation ? d.scaled_pixels.data() : bgra, nullptr, nullptr, nullptr }; int strides[] = { scaled_width * 4, 0, 0, 0 };
    check(sws_scale(d.scaler, source->data, source->linesize, 0, source->height, destinations, strides), "Convert video frame");
    for (int y = 0; d.rotation && y < d.height; ++y) for (int x = 0; x < d.width; ++x) {
        int sx{}, sy{};
        if (d.rotation == 1) { sx = d.height - 1 - y; sy = x; }
        else if (d.rotation == 2) { sx = d.width - 1 - x; sy = d.height - 1 - y; }
        else { sx = y; sy = d.width - 1 - x; }
        std::memcpy(bgra + (static_cast<size_t>(y) * d.width + x) * 4, d.scaled_pixels.data() + (static_cast<size_t>(sy) * scaled_width + sx) * 4, 4);
    }
    return 0;
}); }
int or_decoder_audio(void* handle, double seconds, int requested, float* output, int capacity) { return safe([&] {
    require(handle && output && requested > 0 && requested <= 48000 * 60 && int64_t(requested) * 2 <= capacity && std::isfinite(seconds) && seconds >= 0, "Invalid audio buffer arguments");
    auto& d = *static_cast<Decoder*>(handle); std::lock_guard lock(d.mutex); require(d.audio.codec && d.resampler, "Media has no audio stream");
    auto& a = d.audio; a.seek(std::max(0.0, seconds - 0.1)); swr_close(d.resampler); check(swr_init(d.resampler), "Reset audio resampler");
    std::fill_n(output, requested * 2, 0.0f); int written = 0;
    auto copy_samples = [&](const std::vector<float>& converted, int count, double time) {
        int64_t destination = std::llround((time - seconds) * 48000.0);
        int source = destination < 0 ? static_cast<int>(std::min<int64_t>(-destination, count)) : 0;
        destination = std::max<int64_t>(0, destination);
        if (destination >= requested) { written = requested; return; }
        const int copy = std::min(count - source, requested - static_cast<int>(destination));
        if (copy > 0) { std::copy_n(converted.data() + source * 2, copy * 2, output + destination * 2); written = std::max(written, static_cast<int>(destination) + copy); }
    };
    double next_output_time = 0;
    bool converted_any = false;
    while (a.next()) {
        const double frame_time = a.time(); const int frame_capacity = swr_get_out_samples(d.resampler, a.frame->nb_samples); std::vector<float> converted(static_cast<size_t>(frame_capacity) * 2); uint8_t* planes[] = { reinterpret_cast<uint8_t*>(converted.data()) };
        const double delay = swr_get_delay(d.resampler, 48000) / 48000.0;
        int count = swr_convert(d.resampler, planes, frame_capacity, const_cast<const uint8_t**>(a.frame->extended_data), a.frame->nb_samples); check(count, "Resample audio");
        copy_samples(converted, count, frame_time - delay);
        next_output_time = frame_time - delay + count / 48000.0;
        converted_any = true;
        if (written >= requested) return written;
    }
    while (converted_any) {
        const int count_capacity = std::max(32, swr_get_out_samples(d.resampler, 0));
        std::vector<float> converted(static_cast<size_t>(count_capacity) * 2);
        uint8_t* planes[] = { reinterpret_cast<uint8_t*>(converted.data()) };
        const int count = swr_convert(d.resampler, planes, count_capacity, nullptr, 0);
        check(count, "Drain audio resampler");
        if (count == 0) break;
        copy_samples(converted, count, next_output_time);
        next_output_time += count / 48000.0;
        if (written >= requested) break;
    }
    return written;
}); }
int or_decoder_is_hardware(void* handle) { if (!handle) return 0; auto& d = *static_cast<Decoder*>(handle); std::lock_guard lock(d.mutex); return d.video.hardware_active ? 1 : 0; }
void or_decoder_close(void* decoder) { delete static_cast<Decoder*>(decoder); }
}
