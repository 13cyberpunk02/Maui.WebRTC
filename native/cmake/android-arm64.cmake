# Compiles with the Chromium clang (its libc++ headers need it) against the NDK sysroot.
# Plain cross-compile instead of CMAKE_SYSTEM_NAME=Android, which would force the NDK's own compiler.
set(CMAKE_SYSTEM_NAME Linux)
set(CMAKE_SYSTEM_PROCESSOR aarch64)
set(MRTC_ANDROID ON)
set(MRTC_ANDROID_TARGET aarch64-linux-android24)

set(MRTC_NDK_BIN "${MRTC_NDK_TOOLCHAIN}/bin")
set(CMAKE_TRY_COMPILE_PLATFORM_VARIABLES MRTC_NDK_TOOLCHAIN MRTC_CLANG)
set(CMAKE_TRY_COMPILE_TARGET_TYPE STATIC_LIBRARY)

set(CMAKE_CXX_COMPILER "${MRTC_CLANG}")
set(CMAKE_CXX_COMPILER_ARG1 "--driver-mode=g++")
set(CMAKE_CXX_COMPILER_TARGET ${MRTC_ANDROID_TARGET})
set(CMAKE_SYSROOT "${MRTC_NDK_TOOLCHAIN}/sysroot")
set(CMAKE_AR "${MRTC_NDK_BIN}/llvm-ar.exe" CACHE FILEPATH "")
set(CMAKE_RANLIB "${MRTC_NDK_BIN}/llvm-ranlib.exe" CACHE FILEPATH "")
