# Builds native/ into artifacts/native/android-arm64/libmaui_webrtc.so.
$ErrorActionPreference = 'Stop'
$root = (Split-Path $PSScriptRoot -Parent) -replace '\\', '/'

$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { 'C:\Program Files (x86)\Android\android-sdk' }
$ndk = Get-ChildItem "$sdk\ndk" -Directory | Sort-Object Name -Descending | Select-Object -First 1
$toolchain = "$($ndk.FullName)\toolchains\llvm\prebuilt\windows-x86_64" -replace '\\', '/'
$ninja = "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe" -replace '\\', '/'
$out = "$root/artifacts/native/android-arm64"
$build = "$root/build/android-arm64"

New-Item -ItemType Directory -Force $build | Out-Null
$symbols = & "$toolchain/bin/llvm-nm.exe" --defined-only -j "$root/third_party/webrtc/android/webrtc/lib/arm64-v8a/libwebrtc.a" 2>$null |
    Where-Object { $_ -clike 'Java_*' } | Sort-Object -Unique
if (-not $symbols) { throw 'No JNI symbols found in libwebrtc.a' }
Set-Content "$build/jni_symbols.rsp" ($symbols | ForEach-Object { "-u $_" })

cmake -S "$root/native" -B $build -G Ninja -DCMAKE_BUILD_TYPE=Release `
    "-DCMAKE_MAKE_PROGRAM=$ninja" `
    "-DCMAKE_TOOLCHAIN_FILE=$root/native/cmake/android-arm64.cmake" `
    "-DMRTC_NDK_TOOLCHAIN=$toolchain" `
    "-DMRTC_JNI_SYMBOLS=$build/jni_symbols.rsp" `
    "-DMRTC_CLANG=$root/third_party/clang/bin/clang-cl.exe" `
    "-DCMAKE_LIBRARY_OUTPUT_DIRECTORY=$out"
if ($LASTEXITCODE -ne 0) { throw "CMake configure failed ($LASTEXITCODE)" }
cmake --build $build
if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE)" }
