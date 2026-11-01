/* Stable C ABI over libwebrtc. Only this header is visible to C#; no C++ types may cross it.
 *
 * Callbacks arrive on an internal libwebrtc thread: they must return quickly and must not call
 * mrtc_*_destroy. String and buffer arguments are only valid for the duration of the callback.
 * After mrtc_pc_destroy / mrtc_dc_destroy returns, that object's callbacks no longer fire. */
#ifndef MAUI_WEBRTC_H_
#define MAUI_WEBRTC_H_

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define MRTC_API __declspec(dllexport)
#else
#define MRTC_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct mrtc_factory mrtc_factory;
typedef struct mrtc_peer_connection mrtc_peer_connection;
typedef struct mrtc_data_channel mrtc_data_channel;
typedef struct mrtc_track mrtc_track;
typedef struct mrtc_video_source mrtc_video_source;
typedef struct mrtc_renderer mrtc_renderer;
typedef struct mrtc_camera mrtc_camera;

/* libwebrtc version this library was built against. The string has static lifetime. */
MRTC_API const char* mrtc_version(void);

/* Sets libwebrtc's own logging, written to stderr (logcat on Android).
 * 0 = verbose, 1 = info, 2 = warning, 3 = error, 4 = none. */
MRTC_API void mrtc_set_log_level(int32_t level);

#if defined(__ANDROID__)
/* Android only: must be called before mrtc_factory_create. `application_context` is an
 * android.content.Context JNI reference; the library keeps its own global reference. */
MRTC_API void mrtc_android_set_context(void* application_context);
#endif

/* Returns NULL on failure. Release with mrtc_factory_destroy. Peer connections and data channels
 * keep the underlying threads alive, so they may outlive this handle. */
MRTC_API mrtc_factory* mrtc_factory_create(void);
MRTC_API void mrtc_factory_destroy(mrtc_factory* factory);

typedef struct mrtc_ice_server {
  const char* url;
  const char* username; /* may be NULL */
  const char* password; /* may be NULL */
} mrtc_ice_server;

/* Enum values match libwebrtc's SignalingState, PeerConnectionState and IceGatheringState. */
typedef struct mrtc_pc_callbacks {
  void (*on_signaling_state)(void* user_data, int32_t state);
  void (*on_connection_state)(void* user_data, int32_t state);
  void (*on_ice_gathering_state)(void* user_data, int32_t state);
  void (*on_ice_candidate)(void* user_data, const char* sdp_mid, int32_t sdp_mline_index, const char* candidate);
  void (*on_negotiation_needed)(void* user_data);
  /* Ownership of `channel` passes to the callee. */
  void (*on_data_channel)(void* user_data, mrtc_data_channel* channel);
  /* Ownership of `track` passes to the callee. */
  void (*on_track)(void* user_data, mrtc_track* track);
} mrtc_pc_callbacks;

/* `error` is NULL on success. */
typedef void (*mrtc_result_callback)(void* user_data, const char* error);
/* On success `error` is NULL; on failure `type` and `sdp` are NULL. */
typedef void (*mrtc_sdp_callback)(void* user_data, const char* type, const char* sdp, const char* error);

/* Returns NULL on failure. All callback pointers are required. */
MRTC_API mrtc_peer_connection* mrtc_pc_create(mrtc_factory* factory,
                                              const mrtc_ice_server* ice_servers,
                                              int32_t ice_server_count,
                                              const mrtc_pc_callbacks* callbacks,
                                              void* user_data);
/* Closes the connection and blocks until it is torn down. */
MRTC_API void mrtc_pc_destroy(mrtc_peer_connection* pc);

MRTC_API void mrtc_pc_create_offer(mrtc_peer_connection* pc, mrtc_sdp_callback callback, void* user_data);
MRTC_API void mrtc_pc_create_answer(mrtc_peer_connection* pc, mrtc_sdp_callback callback, void* user_data);
/* `type` is "offer", "pranswer", "answer" or "rollback". */
MRTC_API void mrtc_pc_set_local_description(mrtc_peer_connection* pc, const char* type, const char* sdp,
                                            mrtc_result_callback callback, void* user_data);
MRTC_API void mrtc_pc_set_remote_description(mrtc_peer_connection* pc, const char* type, const char* sdp,
                                             mrtc_result_callback callback, void* user_data);
MRTC_API void mrtc_pc_add_ice_candidate(mrtc_peer_connection* pc, const char* sdp_mid, int32_t sdp_mline_index,
                                        const char* candidate, mrtc_result_callback callback, void* user_data);

/* Delivers libwebrtc's standard stats report as a JSON array of stats objects. */
typedef void (*mrtc_stats_callback)(void* user_data, const char* json);
MRTC_API void mrtc_pc_get_stats(mrtc_peer_connection* pc, mrtc_stats_callback callback, void* user_data);

/* Enum values match libwebrtc's DataChannelInterface::DataState. */
typedef struct mrtc_dc_callbacks {
  void (*on_state)(void* user_data, int32_t state);
  void (*on_message)(void* user_data, const uint8_t* data, size_t size, int32_t is_binary);
} mrtc_dc_callbacks;

/* Returns NULL on failure. */
MRTC_API mrtc_data_channel* mrtc_pc_create_data_channel(mrtc_peer_connection* pc, const char* label);
/* Messages received before this call are buffered by libwebrtc and delivered once it is made. */
MRTC_API void mrtc_dc_set_callbacks(mrtc_data_channel* channel, const mrtc_dc_callbacks* callbacks, void* user_data);
/* Returns a heap copy of the label; release with mrtc_string_free. */
MRTC_API char* mrtc_dc_label(mrtc_data_channel* channel);
MRTC_API int32_t mrtc_dc_state(mrtc_data_channel* channel);
/* Returns non-zero if the message was queued for sending. */
MRTC_API int32_t mrtc_dc_send(mrtc_data_channel* channel, const uint8_t* data, size_t size, int32_t is_binary);
MRTC_API void mrtc_dc_close(mrtc_data_channel* channel);
MRTC_API void mrtc_dc_destroy(mrtc_data_channel* channel);

MRTC_API void mrtc_string_free(char* value);

/* A source frames are pushed into, from any thread. Tracks keep it alive, so it may be destroyed first. */
MRTC_API mrtc_video_source* mrtc_video_source_create(void);
MRTC_API void mrtc_video_source_destroy(mrtc_video_source* source);
/* `rotation` is 0, 90, 180 or 270: how far the frame must be turned clockwise to display upright.
 * `uv_pixel_stride` is 1 for planar data and 2 for interleaved chroma, such as Android's YUV_420_888. */
MRTC_API void mrtc_video_source_push(mrtc_video_source* source, int32_t width, int32_t height,
                                     const uint8_t* y, int32_t y_stride,
                                     const uint8_t* u, int32_t u_stride,
                                     const uint8_t* v, int32_t v_stride,
                                     int32_t uv_pixel_stride, int32_t rotation);

/* Tracks. Kind: 0 = audio, 1 = video. All return NULL on failure. */
MRTC_API mrtc_track* mrtc_factory_create_audio_track(mrtc_factory* factory, const char* label);
MRTC_API mrtc_track* mrtc_factory_create_video_track(mrtc_factory* factory, mrtc_video_source* source, const char* label);
MRTC_API int32_t mrtc_track_kind(mrtc_track* track);
MRTC_API void mrtc_track_set_enabled(mrtc_track* track, int32_t enabled);
MRTC_API void mrtc_track_destroy(mrtc_track* track);
/* Returns non-zero on success. */
MRTC_API int32_t mrtc_pc_add_track(mrtc_peer_connection* pc, mrtc_track* track, const char* stream_id);

/* Draws a video track. Frames are counted even when there is no surface to draw on. */
MRTC_API mrtc_renderer* mrtc_renderer_create(void);
MRTC_API void mrtc_renderer_destroy(mrtc_renderer* renderer);
/* `track` may be NULL to detach. The renderer keeps its own reference to the track. */
MRTC_API void mrtc_renderer_set_track(mrtc_renderer* renderer, mrtc_track* track);
MRTC_API int64_t mrtc_renderer_frames(mrtc_renderer* renderer);

#if defined(__ANDROID__)
/* `surface` is an android.view.Surface JNI reference, or NULL when it has been destroyed. */
MRTC_API void mrtc_renderer_set_surface(mrtc_renderer* renderer, void* surface);
#endif

#if defined(_WIN32)
/* Returns an IDXGISwapChain1* owned by the renderer, for ISwapChainPanelNative::SetSwapChain. NULL on failure. */
MRTC_API void* mrtc_renderer_get_swap_chain(mrtc_renderer* renderer);
/* `width` and `height` are physical pixels; `scale` is the panel's composition scale. */
MRTC_API void mrtc_renderer_set_size(mrtc_renderer* renderer, int32_t width, int32_t height, float scale);

/* Captures from a camera into `source`. Returns NULL if the device cannot be opened. */
MRTC_API int32_t mrtc_camera_count(void);
MRTC_API mrtc_camera* mrtc_camera_start(int32_t device_index, int32_t width, int32_t height, int32_t fps,
                                        mrtc_video_source* source);
MRTC_API void mrtc_camera_stop(mrtc_camera* camera);
#endif

#ifdef __cplusplus
}
#endif

#endif /* MAUI_WEBRTC_H_ */
