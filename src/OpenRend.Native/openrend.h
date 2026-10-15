#pragma once
#include <stdint.h>
#ifdef _WIN32
# ifdef OPENREND_NATIVE_EXPORTS
#  define OR_API __declspec(dllexport)
# else
#  define OR_API __declspec(dllimport)
# endif
#else
# define OR_API
#endif
#ifdef __cplusplus
extern "C" {
#endif
/* All calls use cdecl. Paths are UTF-8. Handles own their resources; calls for a
   handle are serialized internally. Close only after all callers have completed.
   All status calls return 0 on success and -1 on error unless noted otherwise.
   Error strings are thread local and remain valid until the next call on that thread. */
typedef struct or_metadata {
    double duration_seconds;
    int32_t width;
    int32_t height;
    double fps;
    int32_t video_streams;
    int32_t audio_streams;
    int32_t sample_rate;
    int32_t channels;
    int64_t bit_rate;
} or_metadata;
OR_API const char* or_last_error(void);
OR_API const char* or_version(void);
OR_API int or_probe(const char* path, or_metadata* metadata);
/* width/height are output dimensions, or zero to use original dimensions. */
OR_API int or_decoder_open(const char* path, int width, int height, void** decoder);
OR_API int or_decoder_frame(void* decoder, double seconds, uint8_t* bgra, int capacity);
/* Decode 48kHz float32 stereo. Returns frames written (0 at EOF), -1 on error.
   capacity_samples is a count of floats, so must be at least frames * 2. */
OR_API int or_decoder_audio(void* decoder, double seconds, int frames, float* stereo, int capacity_samples);
OR_API int or_decoder_is_hardware(void* decoder);
OR_API void or_decoder_close(void* decoder);
/* HWND is passed as IntPtr. Renderer accepts tightly packed or padded BGRA rows. */
OR_API int or_renderer_create(void* hwnd, int width, int height, void** renderer);
OR_API int or_renderer_present(void* renderer, const uint8_t* bgra, int width, int height, int stride);
OR_API int or_renderer_resize(void* renderer, int width, int height);
OR_API int or_renderer_is_hardware(void* renderer);
OR_API void or_renderer_close(void* renderer);
/* Fixed-size offscreen D3D11 compositor. begin clears opaque black; layers are
   drawn in submission order with straight alpha and opacity, fitted/centered.
   readback writes tightly packed opaque BGRA. Serialize each entire frame's
   begin/layer/readback sequence when sharing a handle across callers. */
OR_API int or_compositor_create(int width, int height, void** compositor);
OR_API int or_compositor_begin(void* compositor);
OR_API int or_compositor_layer(void* compositor, const uint8_t* bgra, int width, int height, int stride, double opacity);
OR_API int or_compositor_readback(void* compositor, uint8_t* bgra, int capacity);
OR_API int or_compositor_is_hardware(void* compositor);
OR_API void or_compositor_close(void* compositor);
#ifdef __cplusplus
}
#endif
