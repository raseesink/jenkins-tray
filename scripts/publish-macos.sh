#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
rid=${1:-osx-arm64}
case "$rid" in osx-arm64) architecture=arm64 ;; osx-x64) architecture=x86_64 ;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 2 ;; esac
cd "$root"
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet restore src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj --locked-mode
# Discard native bundle/linker caches before switching architectures in a shared checkout.
dotnet clean src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj -c Release -r "$rid" -p:RuntimeIdentifiers= -v:q
dotnet publish src/JenkinsTray.MacOS/JenkinsTray.MacOS.csproj -c Release -r "$rid" -p:RuntimeIdentifiers= --self-contained true --no-restore --disable-build-servers -p:LinkMode=None
bundle="src/JenkinsTray.MacOS/bin/Release/net10.0-macos/$rid/publish/JenkinsTray.app"
# The macOS workload may place its publish bundle directly in the RID output.
if [ ! -d "$bundle" ]; then bundle="src/JenkinsTray.MacOS/bin/Release/net10.0-macos/$rid/JenkinsTray.app"; fi
[ -x "$bundle/Contents/MacOS/JenkinsTray" ]
lipo -verify_arch "$architecture" "$bundle/Contents/MacOS/JenkinsTray"
find "$bundle" -type f -name '*.dylib' | while IFS= read -r native_library; do
    lipo -verify_arch "$architecture" "$native_library"
done
mkdir -p "artifacts/$rid"
# Copy without deleting unrelated artifacts; the named target is this script's output.
if [ -d "artifacts/$rid/JenkinsTray.app" ]; then rm -r "artifacts/$rid/JenkinsTray.app"; fi
ditto "$bundle" "artifacts/$rid/JenkinsTray.app"
ditto -c -k --sequesterRsrc --keepParent "artifacts/$rid/JenkinsTray.app" "artifacts/JenkinsTray-$rid.zip"
echo "Created artifacts/JenkinsTray-$rid.zip"
