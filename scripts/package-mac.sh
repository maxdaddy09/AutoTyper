#!/usr/bin/env bash
# Builds the macOS download: release/AutoTyper-<version>-macos-<arch>.zip, holding AutoTyper.app
# (self-contained, so nothing to install, and signed ad hoc), README.txt and LICENSE.txt. The
# version comes from Directory.Build.props.
#
# Run it on a Mac. An Apple Silicon Mac can build both kinds.
#
#   bash scripts/package-mac.sh arm64    # Apple Silicon
#   bash scripts/package-mac.sh x64      # Intel
set -euo pipefail

usage="Usage: scripts/package-mac.sh <arm64|x64>"

fail() {
    echo "package-mac.sh: $*" >&2
    exit 1
}

if [ "$#" -ne 1 ]; then
    echo "$usage" >&2
    exit 2
fi

arch=$1
case "$arch" in
    arm64) expected_arch=arm64 ;;
    x64) expected_arch=x86_64 ;;
    *)
        echo "$usage" >&2
        fail "unknown architecture '$arch'"
        ;;
esac

[ "$(uname -s)" = "Darwin" ] || fail "this builds a Mac app and has to run on a Mac (it needs codesign and ditto)"
for tool in dotnet codesign ditto plutil xattr; do
    command -v "$tool" > /dev/null 2>&1 || fail "'$tool' was not found on the PATH"
done

# The repository root, wherever the script is started from.
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)

props="$root/Directory.Build.props"
project="$root/AutoTyper.Mac/AutoTyper.Mac.csproj"
template="$root/packaging/macos/Info.plist.template"
readme="$root/packaging/macos/README.txt"
license="$root/LICENSE"
for file in "$props" "$project" "$template" "$readme" "$license"; do
    [ -f "$file" ] || fail "$file was not found"
done

# The first <Version>...</Version> in Directory.Build.props. The second sed reads everything, so
# nothing upstream of it can be cut off by a closed pipe.
version=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$props" | sed -n '1p')
[ -n "$version" ] || fail "No <Version> found in $props"
# Digits, letters, dots, plus and minus only: the version goes into a file name and a sed command.
version_pattern='^[0-9][0-9A-Za-z.+-]*$'
[[ $version =~ $version_pattern ]] || fail "'$version' in $props is not a usable version number"

name="AutoTyper-$version-macos-$arch"
release="$root/release"
staging="$release/staging"
stage="$staging/$name"          # the folder that is zipped: AutoTyper.app, README.txt, LICENSE.txt
app="$stage/AutoTyper.app"
macos="$app/Contents/MacOS"     # Avalonia's native libraries have to sit beside the executable
plist="$app/Contents/Info.plist"
zip="$release/$name.zip"

# Only this build's own folder and zip are replaced, so the zip for the other architecture, or the
# Windows one, in release/ is left alone. The staging folder never outlives the script.
cleanup() {
    rm -rf "$stage"
    rmdir "$staging" 2> /dev/null || true
}
trap cleanup EXIT

cleanup
rm -f "$zip"
mkdir -p "$macos"

echo "Publishing AutoTyper $version for osx-$arch..."
# Self-contained puts the .NET runtime inside the app, so it runs on a Mac without .NET installed.
# Multi-file (not a single file) keeps the Avalonia dylibs as real files next to the executable.
dotnet publish "$project" -c Release -r "osx-$arch" \
    -p:SelfContained=true -p:PublishSingleFile=false -p:DebugType=None -p:SatelliteResourceLanguages=en \
    -o "$macos" \
    || fail "dotnet publish failed"

[ -f "$macos/AutoTyper" ] || fail "the publish did not produce $macos/AutoTyper"
chmod +x "$macos/AutoTyper"
for lib in libAvaloniaNative.dylib libSkiaSharp.dylib libHarfBuzzSharp.dylib; do
    [ -f "$macos/$lib" ] || fail "$lib is missing from the publish output; the app could not start without it"
done

# Catches an executable built for the wrong chip (lipo prints arm64 or x86_64). Skipped, not failed,
# if lipo is missing or has nothing to say.
if command -v lipo > /dev/null 2>&1; then
    built_arch=$(lipo -archs "$macos/AutoTyper" 2> /dev/null || true)
    case " $built_arch " in
        *" $expected_arch "*) ;;
        *) [ -z "$built_arch" ] || fail "AutoTyper was built for '$built_arch', not $expected_arch" ;;
    esac
fi

# Info.plist: the template with the version filled in (__VERSION__ appears twice).
sed "s|__VERSION__|$version|g" "$template" > "$plist"
if grep -q '__VERSION__' "$plist"; then
    fail "$plist still contains __VERSION__"
fi
plutil -lint "$plist" || fail "$plist is not a valid property list"

# Signed ad hoc ("-"): there is no Developer ID, so Gatekeeper still asks the user to allow the app
# once, but the code must be signed at all or Apple Silicon will not run it. xattr -cr first clears
# resource forks and Finder information, which codesign refuses to sign over.
xattr -cr "$app"
codesign --force --deep --sign - "$app" || fail "codesign failed"
codesign --verify --deep --strict --verbose=2 "$app" || fail "the signature on $app does not verify"

cp "$readme" "$stage/README.txt"
cp "$license" "$stage/LICENSE.txt"

# ditto, not zip: it keeps the executable bits and the layout of the app bundle. --keepParent puts the
# folder AutoTyper-<version>-macos-<arch> at the top of the zip, with the app and the two text files in it.
ditto -c -k --keepParent "$stage" "$zip" || {
    rm -f "$zip"
    fail "ditto could not create $zip"
}
[ -s "$zip" ] || fail "$zip was not created"

cleanup

bytes=$(wc -c < "$zip" | tr -d ' ')
megabytes=$(awk -v bytes="$bytes" 'BEGIN { printf "%.1f", bytes / 1048576 }')
echo "Created $zip ($megabytes MB)"
