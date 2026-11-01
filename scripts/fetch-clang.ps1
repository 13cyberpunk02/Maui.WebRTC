# Fetches the Chromium clang that built the prebuilt libwebrtc. Its libc++ headers need a compiler this new;
# the NDK's clang is too old. The revision is resolved from the tools commit recorded in VERSIONS.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$versions = @{}
Get-Content "$root/third_party/webrtc/windows_x86_64/webrtc/VERSIONS" | ForEach-Object {
    $k, $v = $_ -split '=', 2
    $versions[$k] = $v
}

$url = "$($versions.WEBRTC_SRC_TOOLS_URL)/+/$($versions.WEBRTC_SRC_TOOLS_COMMIT)/clang/scripts/update.py?format=TEXT"
$py = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String((Invoke-WebRequest $url -UseBasicParsing).Content))
$rev = ([regex]"CLANG_REVISION = '([^']+)'").Match($py).Groups[1].Value
$sub = ([regex]'CLANG_SUB_REVISION = (\d+)').Match($py).Groups[1].Value
if (-not $rev -or -not $sub) { throw 'Could not resolve the clang revision' }

$out = "$root/third_party/clang"
New-Item -ItemType Directory -Force $out | Out-Null
$archive = "$out/clang.tar.xz"
curl.exe -fsSL --retry 3 -o $archive "https://commondatastorage.googleapis.com/chromium-browser-clang/Win/clang-$rev-$sub.tar.xz"
tar -xf $archive -C $out
Remove-Item $archive
