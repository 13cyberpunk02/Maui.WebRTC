# Fetches the libc++ headers matching the prebuilt libwebrtc. The prebuilt archives only ship *.h files,
# so extensionless headers (<vector>, <string>) and __config_site are missing; commits come from VERSIONS.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versions = @{}
Get-Content "$root/third_party/webrtc/windows_x86_64/webrtc/VERSIONS" | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $versions[$k] = $v
}

$out = "$root/third_party/libcxx"
New-Item -ItemType Directory -Force "$out/include", "$out/config" | Out-Null

$libcxx = "$($versions.WEBRTC_SRC_THIRD_PARTY_LIBCXX_SRC_URL)/+archive/$($versions.WEBRTC_SRC_THIRD_PARTY_LIBCXX_SRC_COMMIT)/include.tar.gz"
curl.exe -fsSL --retry 3 -o "$out/include.tar.gz" $libcxx
tar -xzf "$out/include.tar.gz" -C "$out/include"
Remove-Item "$out/include.tar.gz"

$buildtools = "$($versions.WEBRTC_SRC_BUILDTOOLS_URL)/+archive/$($versions.WEBRTC_SRC_BUILDTOOLS_COMMIT)/third_party/libc++.tar.gz"
curl.exe -fsSL --retry 3 -o "$out/config.tar.gz" $buildtools
tar -xzf "$out/config.tar.gz" -C "$out/config"
Remove-Item "$out/config.tar.gz"
