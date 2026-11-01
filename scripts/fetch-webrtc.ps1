# Downloads and unpacks the prebuilt libwebrtc. Archives already in third_party/webrtc-downloads are reused,
# so slow connections can fetch them by hand first.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$release = 'https://github.com/shiguredo-webrtc-build/webrtc-build/releases/download/m157.8103.4.0'
$downloads = "$root/third_party/webrtc-downloads"
$target = "$root/third_party/webrtc"

$archives = @{
    'webrtc.windows_x86_64.zip' = @{ Size = 807074817; Into = 'windows_x86_64' }
    'webrtc.android.tar.gz'     = @{ Size = 151700588; Into = 'android' }
}

New-Item -ItemType Directory -Force $downloads | Out-Null
foreach ($name in $archives.Keys) {
    $file = "$downloads/$name"
    $expected = $archives[$name].Size
    if (-not (Test-Path $file) -or (Get-Item $file).Length -ne $expected) {
        curl.exe -fL --retry 3 -o $file "$release/$name"
    }
    if ((Get-Item $file).Length -ne $expected) { throw "$name does not have the expected size" }

    $into = "$target/$($archives[$name].Into)"
    if (-not (Test-Path "$into/webrtc/VERSIONS")) {
        New-Item -ItemType Directory -Force $into | Out-Null
        tar -xf $file -C $into
    }
}
