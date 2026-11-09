#!/usr/bin/env bash
# Builds the JetApi desktop package and the jetapi command-line tool for one platform.
#
#   build/package.sh <rid> [version]
#
#   win-x64, win-arm64      -> dist/JetApi-<version>-<rid>.zip
#   osx-x64, osx-arm64      -> dist/JetApi-<version>-<rid>.dmg      (macOS only: needs hdiutil)
#   linux-x64, linux-arm64  -> dist/JetApi-<version>-<rid>.tar.gz and .AppImage
#   every platform          -> dist/jetapi-cli-<version>-<rid>.zip (Windows) or .tar.gz (docs/cli.md)
#
# Both are published self-contained as a single file with ReadyToRun code (see the .csproj files).
# Packages are not signed yet; macOS bundles get an ad-hoc signature so they start on Apple silicon.
set -euo pipefail

rid="${1:?usage: build/package.sh <rid> [version]}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${2:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")}"
publish="$root/artifacts/publish/$rid"
dist="$root/dist"
name="JetApi-$version-$rid"
assets="$root/src/JetApi.Desktop/Assets"

# Pinned tool for Linux AppImages (continuous builds are not reproducible).
appimagetool_version="1.9.0"

rm -rf "$publish"
mkdir -p "$dist"

dotnet publish "$root/src/JetApi.Desktop/JetApi.Desktop.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --output "$publish" \
  -p:Version="$version"

# Debug symbols and XML docs are not needed by users; the single-file app carries everything else.
find "$publish" -maxdepth 1 \( -name '*.pdb' -o -name '*.xml' \) -delete

# The command-line tool: one executable in an archive of its own.
cli_publish="$root/artifacts/publish-cli/$rid"
cli_name="jetapi-cli-$version-$rid"
rm -rf "$cli_publish"
dotnet publish "$root/src/JetApi.Cli/JetApi.Cli.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --output "$cli_publish" \
  -p:Version="$version"
find "$cli_publish" -maxdepth 1 \( -name '*.pdb' -o -name '*.xml' \) -delete

case "$rid" in
  win-*)
    rm -f "$dist/$name.zip"
    if command -v pwsh >/dev/null 2>&1; then
      # Git Bash paths (/c/...) mean nothing to PowerShell; convert them on Windows.
      to_native() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi; }
      pwsh -NoProfile -Command "Compress-Archive -Path '$(to_native "$publish")\*' -DestinationPath '$(to_native "$dist/$name.zip")'"
      rm -f "$dist/$cli_name.zip"
      pwsh -NoProfile -Command "Compress-Archive -Path '$(to_native "$cli_publish")\*' -DestinationPath '$(to_native "$dist/$cli_name.zip")'"
    else
      (cd "$publish" && zip -qr "$dist/$name.zip" .)
      rm -f "$dist/$cli_name.zip"
      (cd "$cli_publish" && zip -qr "$dist/$cli_name.zip" .)
    fi
    ;;

  osx-*)
    app="$root/artifacts/$rid/JetApi.app"
    rm -rf "$app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    cp -R "$publish/." "$app/Contents/MacOS/"
    cp "$assets/jetapi.icns" "$app/Contents/Resources/jetapi.icns"
    cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>JetApi</string>
  <key>CFBundleDisplayName</key><string>JetApi</string>
  <key>CFBundleIdentifier</key><string>io.github.mehmedozdemir.jetapi</string>
  <key>CFBundleExecutable</key><string>JetApi</string>
  <key>CFBundleIconFile</key><string>jetapi.icns</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>${version%%-*}</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
    codesign --force --deep --sign - "$app"
    rm -f "$dist/$name.dmg"
    hdiutil create -volname "JetApi" -srcfolder "$app" -ov -format UDZO "$dist/$name.dmg"
    ;;

  linux-*)
    tar -C "$publish" -czf "$dist/$name.tar.gz" .

    appdir="$root/artifacts/$rid/JetApi.AppDir"
    rm -rf "$appdir"
    mkdir -p "$appdir/usr/bin"
    cp -R "$publish/." "$appdir/usr/bin/"
    cp "$assets/jetapi.png" "$appdir/jetapi.png"
    cat > "$appdir/jetapi.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=JetApi
Comment=REST and gRPC API client
Exec=JetApi
Icon=jetapi
Categories=Development;
Terminal=false
DESKTOP
    cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
here="$(dirname "$(readlink -f "$0")")"
exec "$here/usr/bin/JetApi" "$@"
APPRUN
    chmod +x "$appdir/AppRun" "$appdir/usr/bin/JetApi"

    case "$rid" in
      linux-x64) arch="x86_64" ;;
      linux-arm64) arch="aarch64" ;;
    esac
    # appimagetool runs on the build machine (x86_64) and packs either architecture.
    tool="$root/artifacts/appimagetool-$appimagetool_version"
    if [ ! -x "$tool" ]; then
      curl -fsSL -o "$tool" "https://github.com/AppImage/appimagetool/releases/download/$appimagetool_version/appimagetool-x86_64.AppImage"
      chmod +x "$tool"
    fi
    ARCH="$arch" "$tool" --appimage-extract-and-run "$appdir" "$dist/$name.AppImage"
    ;;

  *)
    echo "Unsupported runtime identifier: $rid" >&2
    exit 1
    ;;
esac

case "$rid" in
  win-*) ;;
  *)
    chmod +x "$cli_publish/jetapi"
    tar -C "$cli_publish" -czf "$dist/$cli_name.tar.gz" .
    ;;
esac

echo "Packaged $rid -> $dist"
