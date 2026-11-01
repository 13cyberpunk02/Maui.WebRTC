# Builds native/ into artifacts/native/win-x64/maui_webrtc.dll.
# Compiler and linker come from third_party/clang (scripts/fetch-clang.ps1); CRT and Windows SDK from Visual Studio.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath
$clang = "$root/third_party/clang/bin/clang-cl.exe" -replace '\\', '/'
$linker = "$root/third_party/clang/bin/lld-link.exe" -replace '\\', '/'
$ninja = "$vs\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe" -replace '\\', '/'
$out = "$root/artifacts/native/win-x64" -replace '\\', '/'
$build = "$root/build/win-x64" -replace '\\', '/'

$configure = "cmake -S `"$root/native`" -B `"$build`" -G Ninja -DCMAKE_BUILD_TYPE=Release " +
    "-DCMAKE_MAKE_PROGRAM=`"$ninja`" -DCMAKE_CXX_COMPILER=`"$clang`" " +
    "-DCMAKE_LINKER=`"$linker`" -DCMAKE_RUNTIME_OUTPUT_DIRECTORY=`"$out`""
cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && $configure && cmake --build `"$build`""
if ($LASTEXITCODE -ne 0) { throw "Native build failed ($LASTEXITCODE)" }
