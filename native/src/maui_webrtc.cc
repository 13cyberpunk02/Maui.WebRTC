#include "maui_webrtc.h"

#include <atomic>
#include <cstring>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "api/create_modular_peer_connection_factory.h"
#include "api/data_channel_interface.h"
#include "api/enable_media_with_defaults.h"
#include "api/jsep.h"
#include "api/make_ref_counted.h"
#include "api/peer_connection_interface.h"
#include "api/rtc_error.h"
#include "api/scoped_refptr.h"
#include "api/set_local_description_observer_interface.h"
#include "api/set_remote_description_observer_interface.h"
#include "api/stats/rtc_stats_collector_callback.h"
#include "api/stats/rtc_stats_report.h"
#include "rtc_base/copy_on_write_buffer.h"
#include "rtc_base/logging.h"
#include "rtc_base/ssl_adapter.h"
#include "rtc_base/thread.h"
#include "rtc_base/time_utils.h"

#include "api/audio_options.h"
#include "api/media_stream_interface.h"
#include "api/rtp_receiver_interface.h"
#include "api/rtp_transceiver_interface.h"
#include "api/video/adapted_video_track_source.h"
#include "api/video/i420_buffer.h"
#include "api/video/video_frame.h"
#include "api/video/video_rotation.h"
#include "api/video/video_sink_interface.h"
#include "api/video/video_source_interface.h"
#include "libyuv/convert.h"
#include "libyuv/convert_argb.h"

#if defined(_WIN32)
#include <d3d11.h>
#include <dxgi1_3.h>
#include <winsock2.h>
#include <wrl/client.h>

#include "modules/video_capture/video_capture.h"
#include "modules/video_capture/video_capture_factory.h"
#endif

#if defined(__ANDROID__)
#include <android/native_window.h>
#include <android/native_window_jni.h>
#include <jni.h>

#include "api/environment/environment_factory.h"
#include "sdk/android/native_api/audio_device_module/audio_device_android.h"
#include "sdk/android/native_api/jni/jvm.h"
#endif

namespace {

#if defined(__ANDROID__)
jobject g_application_context = nullptr;
#endif

// Shared by the factory handle and everything created from it, so the threads outlive all users
// regardless of the order the handles are destroyed in.
struct FactoryState {
  ~FactoryState() {
    // The factory must go first: its teardown still runs tasks on both threads.
    factory = nullptr;
    if (signaling_thread) {
      signaling_thread->Stop();
    }
    if (network_thread) {
      network_thread->Stop();
    }
  }

  std::unique_ptr<webrtc::Thread> network_thread;
  std::unique_ptr<webrtc::Thread> signaling_thread;
  webrtc::scoped_refptr<webrtc::PeerConnectionFactoryInterface> factory;
};

class PcObserver final : public webrtc::PeerConnectionObserver {
 public:
  PcObserver(std::shared_ptr<FactoryState> state, const mrtc_pc_callbacks& callbacks, void* user_data)
      : state_(std::move(state)), callbacks_(callbacks), user_data_(user_data) {}

  void OnSignalingChange(webrtc::PeerConnectionInterface::SignalingState new_state) override {
    callbacks_.on_signaling_state(user_data_, static_cast<int32_t>(new_state));
  }
  void OnConnectionChange(webrtc::PeerConnectionInterface::PeerConnectionState new_state) override {
    callbacks_.on_connection_state(user_data_, static_cast<int32_t>(new_state));
  }
  void OnIceGatheringChange(webrtc::PeerConnectionInterface::IceGatheringState new_state) override {
    callbacks_.on_ice_gathering_state(user_data_, static_cast<int32_t>(new_state));
  }
  void OnIceCandidate(const webrtc::IceCandidate* candidate) override {
    callbacks_.on_ice_candidate(user_data_, candidate->sdp_mid().c_str(), candidate->sdp_mline_index(),
                                candidate->ToString().c_str());
  }
  void OnRenegotiationNeeded() override { callbacks_.on_negotiation_needed(user_data_); }
  void OnDataChannel(webrtc::scoped_refptr<webrtc::DataChannelInterface> data_channel) override;
  void OnTrack(webrtc::scoped_refptr<webrtc::RtpTransceiverInterface> transceiver) override;

 private:
  std::shared_ptr<FactoryState> state_;
  mrtc_pc_callbacks callbacks_;
  void* user_data_;
};

class DcObserver final : public webrtc::DataChannelObserver {
 public:
  DcObserver(webrtc::DataChannelInterface* channel, const mrtc_dc_callbacks& callbacks, void* user_data)
      : channel_(channel), callbacks_(callbacks), user_data_(user_data) {}

  void OnStateChange() override { callbacks_.on_state(user_data_, static_cast<int32_t>(channel_->state())); }
  void OnMessage(const webrtc::DataBuffer& buffer) override {
    callbacks_.on_message(user_data_, buffer.data.cdata<uint8_t>(), buffer.data.size(), buffer.binary ? 1 : 0);
  }

 private:
  webrtc::DataChannelInterface* channel_;
  mrtc_dc_callbacks callbacks_;
  void* user_data_;
};

class CreateSdpObserver : public webrtc::CreateSessionDescriptionObserver {
 public:
  CreateSdpObserver(mrtc_sdp_callback callback, void* user_data) : callback_(callback), user_data_(user_data) {}

  void OnSuccess(webrtc::SessionDescriptionInterface* desc) override {
    std::unique_ptr<webrtc::SessionDescriptionInterface> owned(desc);
    callback_(user_data_, owned->type().c_str(), owned->ToString().c_str(), nullptr);
  }
  void OnFailure(webrtc::RTCError error) override { callback_(user_data_, nullptr, nullptr, error.message()); }

 private:
  mrtc_sdp_callback callback_;
  void* user_data_;
};

void InvokeResult(mrtc_result_callback callback, void* user_data, const webrtc::RTCError& error) {
  callback(user_data, error.ok() ? nullptr : error.message());
}

class SetLocalObserver : public webrtc::SetLocalDescriptionObserverInterface {
 public:
  SetLocalObserver(mrtc_result_callback callback, void* user_data) : callback_(callback), user_data_(user_data) {}
  void OnSetLocalDescriptionComplete(webrtc::RTCError error) override { InvokeResult(callback_, user_data_, error); }

 private:
  mrtc_result_callback callback_;
  void* user_data_;
};

class SetRemoteObserver : public webrtc::SetRemoteDescriptionObserverInterface {
 public:
  SetRemoteObserver(mrtc_result_callback callback, void* user_data) : callback_(callback), user_data_(user_data) {}
  void OnSetRemoteDescriptionComplete(webrtc::RTCError error) override { InvokeResult(callback_, user_data_, error); }

 private:
  mrtc_result_callback callback_;
  void* user_data_;
};

class StatsObserver : public webrtc::RTCStatsCollectorCallback {
 public:
  StatsObserver(mrtc_stats_callback callback, void* user_data) : callback_(callback), user_data_(user_data) {}
  void OnStatsDelivered(const webrtc::scoped_refptr<const webrtc::RTCStatsReport>& report) override {
    callback_(user_data_, report->ToJson().c_str());
  }

 private:
  mrtc_stats_callback callback_;
  void* user_data_;
};

class PushVideoSource : public webrtc::AdaptedVideoTrackSource {
 public:
  void Push(webrtc::scoped_refptr<webrtc::I420Buffer> buffer, webrtc::VideoRotation rotation) {
    const int width = buffer->width();
    const int height = buffer->height();
    const int64_t now_us = webrtc::TimeMicros();

    int adapted_width, adapted_height, crop_width, crop_height, crop_x, crop_y;
    if (!AdaptFrame(width, height, now_us, &adapted_width, &adapted_height, &crop_width, &crop_height, &crop_x,
                    &crop_y)) {
      return;
    }

    if (adapted_width != width || adapted_height != height) {
      auto scaled = webrtc::I420Buffer::Create(adapted_width, adapted_height);
      scaled->CropAndScaleFrom(*buffer, crop_x, crop_y, crop_width, crop_height);
      buffer = scaled;
    }
    if (apply_rotation() && rotation != webrtc::kVideoRotation_0) {
      buffer = webrtc::I420Buffer::Rotate(*buffer, rotation);
      rotation = webrtc::kVideoRotation_0;
    }

    OnFrame(webrtc::VideoFrame::Builder()
                .set_video_frame_buffer(buffer)
                .set_rotation(rotation)
                .set_timestamp_us(now_us)
                .build());
  }

  SourceState state() const override { return kLive; }
  bool remote() const override { return false; }
  bool is_screencast() const override { return false; }
  std::optional<bool> needs_denoising() const override { return std::nullopt; }
};

// Scales to fit the target while keeping the aspect ratio; the caller provides a black 32-bit canvas.
// Holds on to its scratch buffer so that a steady stream of frames doesn't reallocate.
class FrameFitter {
 public:
  struct Placement {
    int x, y, width, height;
  };

  const webrtc::I420BufferInterface* Fit(const webrtc::I420BufferInterface& source, int target_width,
                                         int target_height, Placement* placement) {
    const int64_t width_by_height = int64_t{target_width} * source.height();
    const int64_t height_by_width = int64_t{target_height} * source.width();
    int width = target_width;
    int height = target_height;
    if (width_by_height > height_by_width) {
      width = static_cast<int>(height_by_width / source.height());
    } else {
      height = static_cast<int>(width_by_height / source.width());
    }
    // I420 needs even dimensions.
    width = width > 1 ? width & ~1 : 2;
    height = height > 1 ? height & ~1 : 2;
    placement->width = width;
    placement->height = height;
    placement->x = (target_width - width) / 2;
    placement->y = (target_height - height) / 2;

    if (width == source.width() && height == source.height()) {
      return &source;
    }
    if (!scaled_ || scaled_->width() != width || scaled_->height() != height) {
      scaled_ = webrtc::I420Buffer::Create(width, height);
    }
    scaled_->ScaleFrom(source);
    return scaled_.get();
  }

 private:
  webrtc::scoped_refptr<webrtc::I420Buffer> scaled_;
};

class Renderer final : public webrtc::VideoSinkInterface<webrtc::VideoFrame> {
 public:
  ~Renderer() override {
    SetTrack(nullptr);
#if defined(__ANDROID__)
    SetWindow(nullptr);
#endif
  }

  void SetTrack(webrtc::scoped_refptr<webrtc::VideoTrackInterface> track) {
    if (track_) {
      track_->RemoveSink(this);
    }
    track_ = std::move(track);
    if (track_) {
      track_->AddOrUpdateSink(this, webrtc::VideoSinkWants());
    }
  }

  void OnFrame(const webrtc::VideoFrame& frame) override {
    frames_.fetch_add(1, std::memory_order_relaxed);

    std::lock_guard<std::mutex> lock(mutex_);
    if (!HasTarget()) {
      return;
    }
    webrtc::scoped_refptr<webrtc::I420BufferInterface> buffer = frame.video_frame_buffer()->ToI420();
    if (!buffer) {
      return;
    }
    if (frame.rotation() != webrtc::kVideoRotation_0) {
      buffer = webrtc::I420Buffer::Rotate(*buffer, frame.rotation());
    }
    Present(*buffer);
  }

  int64_t frames() const { return frames_.load(std::memory_order_relaxed); }

#if defined(__ANDROID__)
  void SetWindow(ANativeWindow* window) {
    std::lock_guard<std::mutex> lock(mutex_);
    if (window_ != nullptr) {
      ANativeWindow_release(window_);
    }
    window_ = window;
    if (window_ != nullptr) {
      ANativeWindow_setBuffersGeometry(window_, 0, 0, WINDOW_FORMAT_RGBA_8888);
    }
  }
#endif

#if defined(_WIN32)
  IDXGISwapChain1* GetSwapChain() {
    std::lock_guard<std::mutex> lock(mutex_);
    if (!swap_chain_) {
      CreateSwapChain();
    }
    return swap_chain_.Get();
  }

  void SetSize(int width, int height, float scale) {
    std::lock_guard<std::mutex> lock(mutex_);
    pending_width_ = width > 0 ? width : 1;
    pending_height_ = height > 0 ? height : 1;
    scale_ = scale > 0 ? scale : 1.0f;
  }
#endif

 private:
#if defined(__ANDROID__)
  bool HasTarget() const { return window_ != nullptr; }

  void Present(const webrtc::I420BufferInterface& buffer) {
    ANativeWindow_Buffer output;
    if (ANativeWindow_lock(window_, &output, nullptr) != 0) {
      return;
    }
    auto* pixels = static_cast<uint8_t*>(output.bits);
    const int stride = output.stride * 4;
    std::memset(pixels, 0, static_cast<size_t>(stride) * output.height);

    FrameFitter::Placement at;
    const auto* fitted = fitter_.Fit(buffer, output.width, output.height, &at);
    // libyuv names formats by word order, so its "ABGR" is R, G, B, A in memory.
    libyuv::I420ToABGR(fitted->DataY(), fitted->StrideY(), fitted->DataU(), fitted->StrideU(), fitted->DataV(),
                       fitted->StrideV(), pixels + at.y * stride + at.x * 4, stride, at.width, at.height);
    ANativeWindow_unlockAndPost(window_);
  }

  ANativeWindow* window_ = nullptr;
#endif

#if defined(_WIN32)
  bool HasTarget() const { return swap_chain_ != nullptr; }

  void CreateSwapChain() {
    using Microsoft::WRL::ComPtr;
    const D3D_DRIVER_TYPE drivers[] = {D3D_DRIVER_TYPE_HARDWARE, D3D_DRIVER_TYPE_WARP};
    HRESULT hr = E_FAIL;
    for (D3D_DRIVER_TYPE driver : drivers) {
      hr = D3D11CreateDevice(nullptr, driver, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0,
                             D3D11_SDK_VERSION, &device_, nullptr, &context_);
      if (SUCCEEDED(hr)) {
        break;
      }
    }
    if (FAILED(hr)) {
      return;
    }

    ComPtr<IDXGIDevice> dxgi_device;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device_.As(&dxgi_device)) || FAILED(dxgi_device->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) {
      return;
    }

    DXGI_SWAP_CHAIN_DESC1 desc = {};
    desc.Width = width_;
    desc.Height = height_;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.Scaling = DXGI_SCALING_STRETCH;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
    factory->CreateSwapChainForComposition(device_.Get(), &desc, nullptr, &swap_chain_);
  }

  void ApplyPendingSize() {
    if (pending_width_ != width_ || pending_height_ != height_) {
      context_->ClearState();
      context_->Flush();
      if (FAILED(swap_chain_->ResizeBuffers(0, pending_width_, pending_height_, DXGI_FORMAT_UNKNOWN, 0))) {
        return;
      }
      width_ = pending_width_;
      height_ = pending_height_;
    }
    if (scale_ != applied_scale_) {
      // The panel shows swap chain pixels 1:1 in DIPs, so physical-size buffers must be scaled back down.
      Microsoft::WRL::ComPtr<IDXGISwapChain2> swap_chain2;
      if (SUCCEEDED(swap_chain_.As(&swap_chain2))) {
        DXGI_MATRIX_3X2_F matrix = {};
        matrix._11 = 1.0f / scale_;
        matrix._22 = 1.0f / scale_;
        swap_chain2->SetMatrixTransform(&matrix);
      }
      applied_scale_ = scale_;
    }
  }

  void Present(const webrtc::I420BufferInterface& buffer) {
    ApplyPendingSize();

    const int stride = width_ * 4;
    canvas_.assign(static_cast<size_t>(stride) * height_, 0);

    FrameFitter::Placement at;
    const auto* fitted = fitter_.Fit(buffer, width_, height_, &at);
    // libyuv names formats by word order, so its "ARGB" is B, G, R, A in memory.
    libyuv::I420ToARGB(fitted->DataY(), fitted->StrideY(), fitted->DataU(), fitted->StrideU(), fitted->DataV(),
                       fitted->StrideV(), canvas_.data() + at.y * stride + at.x * 4, stride, at.width, at.height);

    Microsoft::WRL::ComPtr<ID3D11Texture2D> back_buffer;
    if (FAILED(swap_chain_->GetBuffer(0, IID_PPV_ARGS(&back_buffer)))) {
      return;
    }
    context_->UpdateSubresource(back_buffer.Get(), 0, nullptr, canvas_.data(), stride, 0);
    swap_chain_->Present(0, 0);
  }

  Microsoft::WRL::ComPtr<ID3D11Device> device_;
  Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
  Microsoft::WRL::ComPtr<IDXGISwapChain1> swap_chain_;
  std::vector<uint8_t> canvas_;
  int width_ = 16, height_ = 16;
  int pending_width_ = 16, pending_height_ = 16;
  float scale_ = 1.0f, applied_scale_ = 1.0f;
#endif

  std::mutex mutex_;
  std::atomic<int64_t> frames_{0};
  FrameFitter fitter_;
  webrtc::scoped_refptr<webrtc::VideoTrackInterface> track_;
};

std::unique_ptr<webrtc::SessionDescriptionInterface> ParseDescription(const char* type, const char* sdp) {
  std::optional<webrtc::SdpType> sdp_type = webrtc::SdpTypeFromString(type);
  if (!sdp_type) {
    return nullptr;
  }
  return webrtc::CreateSessionDescription(*sdp_type, sdp);
}

}  // namespace

struct mrtc_factory {
  std::shared_ptr<FactoryState> state;
};

// Member order matters: the connection has to go before the observer it calls into.
struct mrtc_peer_connection {
  std::shared_ptr<FactoryState> state;
  std::unique_ptr<PcObserver> observer;
  webrtc::scoped_refptr<webrtc::PeerConnectionInterface> pc;
};

struct mrtc_data_channel {
  std::shared_ptr<FactoryState> state;
  std::unique_ptr<DcObserver> observer;
  webrtc::scoped_refptr<webrtc::DataChannelInterface> channel;
};

struct mrtc_track {
  std::shared_ptr<FactoryState> state;
  webrtc::scoped_refptr<webrtc::MediaStreamTrackInterface> track;
};

struct mrtc_video_source {
  webrtc::scoped_refptr<PushVideoSource> source;
};

struct mrtc_renderer {
  // Declared first so the threads outlive the renderer detaching from its track.
  std::shared_ptr<FactoryState> state;
  Renderer renderer;
};

void PcObserver::OnDataChannel(webrtc::scoped_refptr<webrtc::DataChannelInterface> data_channel) {
  callbacks_.on_data_channel(user_data_, new mrtc_data_channel{state_, nullptr, std::move(data_channel)});
}

void PcObserver::OnTrack(webrtc::scoped_refptr<webrtc::RtpTransceiverInterface> transceiver) {
  callbacks_.on_track(user_data_, new mrtc_track{state_, transceiver->receiver()->track()});
}

const char* mrtc_version(void) {
  return MRTC_WEBRTC_VERSION;
}

void mrtc_set_log_level(int32_t level) {
  webrtc::LogMessage::LogToDebug(static_cast<webrtc::LoggingSeverity>(level));
}

#if defined(__ANDROID__)
void mrtc_android_set_context(void* application_context) {
  JNIEnv* env = webrtc::AttachCurrentThreadIfNeeded();
  if (g_application_context != nullptr) {
    env->DeleteGlobalRef(g_application_context);
  }
  g_application_context = env->NewGlobalRef(static_cast<jobject>(application_context));
}
#endif

mrtc_factory* mrtc_factory_create(void) {
#if defined(__ANDROID__)
  // SSL is already initialized by libwebrtc's JNI_OnLoad on Android.
  if (g_application_context == nullptr) {
    return nullptr;
  }
#else
  static std::once_flag ssl_once;
  std::call_once(ssl_once, [] {
#if defined(_WIN32)
    // libwebrtc leaves Winsock setup to the application; without it no UDP socket can be created.
    WSADATA wsa_data;
    WSAStartup(MAKEWORD(2, 2), &wsa_data);
#endif
    webrtc::InitializeSSL();
  });
#endif

  auto state = std::make_shared<FactoryState>();
  state->network_thread = webrtc::Thread::CreateWithSocketServer();
  state->signaling_thread = webrtc::Thread::Create();
  if (!state->network_thread->Start() || !state->signaling_thread->Start()) {
    return nullptr;
  }

  webrtc::PeerConnectionFactoryDependencies deps;
  deps.network_thread = state->network_thread.get();
  deps.signaling_thread = state->signaling_thread.get();
#if defined(__ANDROID__)
  // There is no default audio device on Android; it has to go through the Java AudioRecord/AudioTrack.
  webrtc::Environment env = webrtc::CreateEnvironment();
  deps.adm = webrtc::CreateJavaAudioDeviceModule(webrtc::AttachCurrentThreadIfNeeded(), env,
                                                 g_application_context);
  deps.env = env;
#endif
  webrtc::EnableMediaWithDefaults(deps);

  state->factory = webrtc::CreateModularPeerConnectionFactory(std::move(deps));
  if (!state->factory) {
    return nullptr;
  }
  return new mrtc_factory{std::move(state)};
}

void mrtc_factory_destroy(mrtc_factory* factory) {
  delete factory;
}

mrtc_peer_connection* mrtc_pc_create(mrtc_factory* factory,
                                     const mrtc_ice_server* ice_servers,
                                     int32_t ice_server_count,
                                     const mrtc_pc_callbacks* callbacks,
                                     void* user_data) {
  webrtc::PeerConnectionInterface::RTCConfiguration config;
  for (int32_t i = 0; i < ice_server_count; ++i) {
    webrtc::PeerConnectionInterface::IceServer server;
    server.urls.push_back(ice_servers[i].url);
    if (ice_servers[i].username != nullptr) {
      server.username = ice_servers[i].username;
    }
    if (ice_servers[i].password != nullptr) {
      server.password = ice_servers[i].password;
    }
    config.servers.push_back(std::move(server));
  }

  auto result = std::make_unique<mrtc_peer_connection>();
  result->state = factory->state;
  result->observer = std::make_unique<PcObserver>(factory->state, *callbacks, user_data);

  webrtc::PeerConnectionDependencies deps(result->observer.get());
  auto pc = factory->state->factory->CreatePeerConnectionOrError(config, std::move(deps));
  if (!pc.ok()) {
    return nullptr;
  }
  result->pc = pc.MoveValue();
  return result.release();
}

void mrtc_pc_destroy(mrtc_peer_connection* pc) {
  if (pc == nullptr) {
    return;
  }
  pc->pc->Close();
  pc->pc = nullptr;
  delete pc;
}

void mrtc_pc_create_offer(mrtc_peer_connection* pc, mrtc_sdp_callback callback, void* user_data) {
  auto observer = webrtc::make_ref_counted<CreateSdpObserver>(callback, user_data);
  pc->pc->CreateOffer(observer.get(), webrtc::PeerConnectionInterface::RTCOfferAnswerOptions());
}

void mrtc_pc_create_answer(mrtc_peer_connection* pc, mrtc_sdp_callback callback, void* user_data) {
  auto observer = webrtc::make_ref_counted<CreateSdpObserver>(callback, user_data);
  pc->pc->CreateAnswer(observer.get(), webrtc::PeerConnectionInterface::RTCOfferAnswerOptions());
}

void mrtc_pc_set_local_description(mrtc_peer_connection* pc, const char* type, const char* sdp,
                                   mrtc_result_callback callback, void* user_data) {
  auto desc = ParseDescription(type, sdp);
  if (!desc) {
    callback(user_data, "Invalid session description");
    return;
  }
  pc->pc->SetLocalDescription(std::move(desc), webrtc::make_ref_counted<SetLocalObserver>(callback, user_data));
}

void mrtc_pc_set_remote_description(mrtc_peer_connection* pc, const char* type, const char* sdp,
                                    mrtc_result_callback callback, void* user_data) {
  auto desc = ParseDescription(type, sdp);
  if (!desc) {
    callback(user_data, "Invalid session description");
    return;
  }
  pc->pc->SetRemoteDescription(std::move(desc), webrtc::make_ref_counted<SetRemoteObserver>(callback, user_data));
}

void mrtc_pc_add_ice_candidate(mrtc_peer_connection* pc, const char* sdp_mid, int32_t sdp_mline_index,
                               const char* candidate, mrtc_result_callback callback, void* user_data) {
  std::unique_ptr<webrtc::IceCandidate> parsed(
      webrtc::CreateIceCandidate(sdp_mid, sdp_mline_index, candidate, nullptr));
  if (!parsed) {
    callback(user_data, "Invalid ICE candidate");
    return;
  }
  std::function<void(webrtc::RTCError)> done = [callback, user_data](webrtc::RTCError error) {
    InvokeResult(callback, user_data, error);
  };
  pc->pc->AddIceCandidate(std::move(parsed), std::move(done));
}

void mrtc_pc_get_stats(mrtc_peer_connection* pc, mrtc_stats_callback callback, void* user_data) {
  pc->pc->GetStats(webrtc::make_ref_counted<StatsObserver>(callback, user_data).get());
}

mrtc_data_channel* mrtc_pc_create_data_channel(mrtc_peer_connection* pc, const char* label) {
  webrtc::DataChannelInit init;
  auto channel = pc->pc->CreateDataChannelOrError(label, &init);
  if (!channel.ok()) {
    return nullptr;
  }
  return new mrtc_data_channel{pc->state, nullptr, channel.MoveValue()};
}

void mrtc_dc_set_callbacks(mrtc_data_channel* channel, const mrtc_dc_callbacks* callbacks, void* user_data) {
  if (channel->observer) {
    channel->channel->UnregisterObserver();
  }
  channel->observer = std::make_unique<DcObserver>(channel->channel.get(), *callbacks, user_data);
  channel->channel->RegisterObserver(channel->observer.get());
}

char* mrtc_dc_label(mrtc_data_channel* channel) {
  std::string label = channel->channel->label();
  char* copy = new char[label.size() + 1];
  label.copy(copy, label.size());
  copy[label.size()] = '\0';
  return copy;
}

int32_t mrtc_dc_state(mrtc_data_channel* channel) {
  return static_cast<int32_t>(channel->channel->state());
}

int32_t mrtc_dc_send(mrtc_data_channel* channel, const uint8_t* data, size_t size, int32_t is_binary) {
  webrtc::DataBuffer buffer(webrtc::CopyOnWriteBuffer(data, size), is_binary != 0);
  return channel->channel->Send(buffer) ? 1 : 0;
}

void mrtc_dc_close(mrtc_data_channel* channel) {
  channel->channel->Close();
}

void mrtc_dc_destroy(mrtc_data_channel* channel) {
  if (channel == nullptr) {
    return;
  }
  if (channel->observer) {
    channel->channel->UnregisterObserver();
  }
  delete channel;
}

void mrtc_string_free(char* value) {
  delete[] value;
}

mrtc_video_source* mrtc_video_source_create(void) {
  return new mrtc_video_source{webrtc::make_ref_counted<PushVideoSource>()};
}

void mrtc_video_source_destroy(mrtc_video_source* source) {
  delete source;
}

void mrtc_video_source_push(mrtc_video_source* source, int32_t width, int32_t height,
                            const uint8_t* y, int32_t y_stride,
                            const uint8_t* u, int32_t u_stride,
                            const uint8_t* v, int32_t v_stride,
                            int32_t uv_pixel_stride, int32_t rotation) {
  auto buffer = webrtc::I420Buffer::Create(width, height);
  libyuv::Android420ToI420(y, y_stride, u, u_stride, v, v_stride, uv_pixel_stride, buffer->MutableDataY(),
                           buffer->StrideY(), buffer->MutableDataU(), buffer->StrideU(), buffer->MutableDataV(),
                           buffer->StrideV(), width, height);
  source->source->Push(std::move(buffer), static_cast<webrtc::VideoRotation>(rotation));
}

mrtc_track* mrtc_factory_create_audio_track(mrtc_factory* factory, const char* label) {
  auto source = factory->state->factory->CreateAudioSource(webrtc::AudioOptions());
  if (!source) {
    return nullptr;
  }
  auto track = factory->state->factory->CreateAudioTrack(label, source.get());
  return track ? new mrtc_track{factory->state, std::move(track)} : nullptr;
}

mrtc_track* mrtc_factory_create_video_track(mrtc_factory* factory, mrtc_video_source* source, const char* label) {
  auto track = factory->state->factory->CreateVideoTrack(source->source, label);
  return track ? new mrtc_track{factory->state, std::move(track)} : nullptr;
}

int32_t mrtc_track_kind(mrtc_track* track) {
  return track->track->kind() == webrtc::MediaStreamTrackInterface::kVideoKind ? 1 : 0;
}

void mrtc_track_set_enabled(mrtc_track* track, int32_t enabled) {
  track->track->set_enabled(enabled != 0);
}

void mrtc_track_destroy(mrtc_track* track) {
  delete track;
}

int32_t mrtc_pc_add_track(mrtc_peer_connection* pc, mrtc_track* track, const char* stream_id) {
  return pc->pc->AddTrack(track->track, {stream_id}).ok() ? 1 : 0;
}

mrtc_renderer* mrtc_renderer_create(void) {
  return new mrtc_renderer();
}

void mrtc_renderer_destroy(mrtc_renderer* renderer) {
  delete renderer;
}

void mrtc_renderer_set_track(mrtc_renderer* renderer, mrtc_track* track) {
  if (track == nullptr) {
    renderer->renderer.SetTrack(nullptr);
    renderer->state = nullptr;
    return;
  }
  if (track->track->kind() != webrtc::MediaStreamTrackInterface::kVideoKind) {
    return;
  }
  auto state = track->state;
  renderer->renderer.SetTrack(
      webrtc::scoped_refptr<webrtc::VideoTrackInterface>(static_cast<webrtc::VideoTrackInterface*>(track->track.get())));
  renderer->state = std::move(state);
}

int64_t mrtc_renderer_frames(mrtc_renderer* renderer) {
  return renderer->renderer.frames();
}

#if defined(__ANDROID__)
void mrtc_renderer_set_surface(mrtc_renderer* renderer, void* surface) {
  renderer->renderer.SetWindow(
      surface != nullptr
          ? ANativeWindow_fromSurface(webrtc::AttachCurrentThreadIfNeeded(), static_cast<jobject>(surface))
          : nullptr);
}
#endif

#if defined(_WIN32)
void* mrtc_renderer_get_swap_chain(mrtc_renderer* renderer) {
  return renderer->renderer.GetSwapChain();
}

void mrtc_renderer_set_size(mrtc_renderer* renderer, int32_t width, int32_t height, float scale) {
  renderer->renderer.SetSize(width, height, scale);
}

namespace {
class CameraSink final : public webrtc::VideoSinkInterface<webrtc::VideoFrame> {
 public:
  explicit CameraSink(webrtc::scoped_refptr<PushVideoSource> source) : source_(std::move(source)) {}

  void OnFrame(const webrtc::VideoFrame& frame) override {
    auto i420 = frame.video_frame_buffer()->ToI420();
    if (i420) {
      source_->Push(webrtc::I420Buffer::Copy(*i420), frame.rotation());
    }
  }

 private:
  webrtc::scoped_refptr<PushVideoSource> source_;
};
}  // namespace

struct mrtc_camera {
  std::unique_ptr<CameraSink> sink;
  webrtc::scoped_refptr<webrtc::VideoCaptureModule> module;
};

int32_t mrtc_camera_count(void) {
  std::unique_ptr<webrtc::VideoCaptureModule::DeviceInfo> info(webrtc::VideoCaptureFactory::CreateDeviceInfo());
  return info ? static_cast<int32_t>(info->NumberOfDevices()) : 0;
}

mrtc_camera* mrtc_camera_start(int32_t device_index, int32_t width, int32_t height, int32_t fps,
                               mrtc_video_source* source) {
  std::unique_ptr<webrtc::VideoCaptureModule::DeviceInfo> info(webrtc::VideoCaptureFactory::CreateDeviceInfo());
  if (!info) {
    return nullptr;
  }
  char name[256] = {};
  char id[256] = {};
  if (info->GetDeviceName(static_cast<uint32_t>(device_index), name, sizeof(name), id, sizeof(id)) != 0) {
    return nullptr;
  }

  webrtc::VideoCaptureCapability requested;
  requested.width = width;
  requested.height = height;
  requested.maxFPS = fps;
  webrtc::VideoCaptureCapability capability;
  if (info->GetBestMatchedCapability(id, requested, capability) < 0) {
    capability = requested;
  }

  auto module = webrtc::VideoCaptureFactory::Create(id);
  if (!module) {
    return nullptr;
  }
  auto camera = std::make_unique<mrtc_camera>();
  camera->sink = std::make_unique<CameraSink>(source->source);
  camera->module = std::move(module);
  camera->module->RegisterCaptureDataCallback(camera->sink.get());
  if (camera->module->StartCapture(capability) != 0) {
    camera->module->DeRegisterCaptureDataCallback();
    return nullptr;
  }
  return camera.release();
}

void mrtc_camera_stop(mrtc_camera* camera) {
  if (camera == nullptr) {
    return;
  }
  camera->module->StopCapture();
  camera->module->DeRegisterCaptureDataCallback();
  delete camera;
}
#endif
