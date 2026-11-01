cmake_minimum_required(VERSION 3.25)
project(maui_webrtc LANGUAGES CXX)

set(REPO_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/..")
set(LIBCXX_ROOT "${REPO_ROOT}/third_party/libcxx")

if(WIN32)
  set(WEBRTC_ROOT "${REPO_ROOT}/third_party/webrtc/windows_x86_64/webrtc")
  set(WEBRTC_LIB "${WEBRTC_ROOT}/lib/webrtc.lib")
elseif(MRTC_ANDROID)
  set(WEBRTC_ROOT "${REPO_ROOT}/third_party/webrtc/android/webrtc")
  set(WEBRTC_LIB "${WEBRTC_ROOT}/lib/arm64-v8a/libwebrtc.a")
else()
  message(FATAL_ERROR "Unsupported platform")
endif()

file(STRINGS "${WEBRTC_ROOT}/VERSIONS" WEBRTC_VERSION_LINE REGEX "^WEBRTC_VERSION=")
string(REPLACE "WEBRTC_VERSION=" "" WEBRTC_VERSION "${WEBRTC_VERSION_LINE}")

add_library(maui_webrtc SHARED src/maui_webrtc.cc)
target_compile_features(maui_webrtc PRIVATE cxx_std_20)
target_compile_definitions(maui_webrtc PRIVATE
  MRTC_WEBRTC_VERSION="${WEBRTC_VERSION}"
  NDEBUG
  # These mirror Chromium's build/config/c++ for a static libc++ and must match the prebuilt library.
  # Without the first one libc++ functions are declared dllimport and calls jump through a bad slot.
  # The hardening mode is visible in the library's symbols: the "s" in the ABI tag "sqn240000".
  _LIBCPP_DISABLE_VISIBILITY_ANNOTATIONS
  _LIBCPP_INSTRUMENTED_WITH_ASAN=0
  _LIBCPP_HARDENING_MODE=_LIBCPP_HARDENING_MODE_EXTENSIVE)
target_include_directories(maui_webrtc PRIVATE
  include
  "${WEBRTC_ROOT}/include"
  "${WEBRTC_ROOT}/include/third_party/abseil-cpp"
  "${WEBRTC_ROOT}/include/third_party/libyuv/include")
target_link_libraries(maui_webrtc PRIVATE "${WEBRTC_LIB}")
# RTTI must stay on: both prebuilt libraries use it, and it changes the vtable layout of libc++'s
# __shared_weak_count (__get_deleter), so disabling it here crashes on the first shared_ptr release.

if(WIN32)
  # webrtc.lib embeds Chromium's libc++ (std::__Cr) and the static CRT, so we must compile against
  # the same headers with clang-cl and /MT. MSVC's STL would not be ABI compatible with its API.
  set_property(TARGET maui_webrtc PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
  target_compile_definitions(maui_webrtc PRIVATE
    WEBRTC_WIN NOMINMAX WIN32_LEAN_AND_MEAN UNICODE _UNICODE)
  target_compile_options(maui_webrtc PRIVATE
    "/clang:-nostdinc++"
    "/clang:-isystem${LIBCXX_ROOT}/config"
    "/clang:-isystem${LIBCXX_ROOT}/include"
    "/EHs-c-"
    "/Z7")
  # Symbols go to a separate .pdb, so the DLL itself stays the same size.
  target_link_options(maui_webrtc PRIVATE "/DEBUG" "/OPT:REF" "/OPT:ICF")
  target_link_libraries(maui_webrtc PRIVATE
    advapi32 bcrypt crypt32 d3d11 dmoguids dxgi iphlpapi msdmo ntdll ole32 oleaut32 secur32 shell32
    strmiids user32 userenv winmm wmcodecdspuuid ws2_32)
elseif(MRTC_ANDROID)
  # Same reasoning as Windows: libwebrtc.a embeds std::__Cr, so the NDK's libc++ must stay out.
  target_compile_definitions(maui_webrtc PRIVATE WEBRTC_POSIX WEBRTC_LINUX WEBRTC_ANDROID)
  target_compile_options(maui_webrtc PRIVATE
    -nostdinc++
    "-isystem${LIBCXX_ROOT}/config"
    "-isystem${LIBCXX_ROOT}/include"
    # Chromium builds Android arm64 this way; without it virtual calls into libwebrtc read a slot of
    # two 32-bit offsets as a pointer and jump to garbage.
    -fexperimental-relative-c++-abi-vtables
    -fno-exceptions -fvisibility=hidden -ffunction-sections -fdata-sections)
  # Nothing references JNI_OnLoad or the methods webrtc.jar calls back into, so they have to be pulled
  # in by hand; MRTC_JNI_SYMBOLS lists the latter, whose obfuscated names change with every version.
  # 16 KB page alignment is required for Google Play on Android 15+.
  target_link_options(maui_webrtc PRIVATE
    -nostdlib++
    -Wl,-u,JNI_OnLoad
    "-Wl,@${MRTC_JNI_SYMBOLS}"
    "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/maui_webrtc.android.map"
    -Wl,--gc-sections
    -Wl,--strip-all
    -Wl,-z,max-page-size=16384)
  target_link_libraries(maui_webrtc PRIVATE android EGL GLESv2 OpenSLES dl log m)
  # The Chromium clang package has no GNU-style linker driver, so linking goes through the NDK's.
  set(CMAKE_CXX_CREATE_SHARED_LIBRARY
    "\"${MRTC_NDK_BIN}/clang++.exe\" --target=${MRTC_ANDROID_TARGET} <CMAKE_SHARED_LIBRARY_CXX_FLAGS> <LINK_FLAGS> -shared -Wl,-soname,<TARGET_SONAME> -o <TARGET> <OBJECTS> <LINK_LIBRARIES>")
endif()
