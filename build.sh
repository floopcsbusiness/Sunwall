#!/bin/bash
# Builds Sunwall.app from main.swift. Needs Xcode command line tools: xcode-select --install
set -e
cd "$(dirname "$0")"

NAME="Sunwall"
rm -rf "$NAME.app"
mkdir -p "$NAME.app/Contents/MacOS"

swiftc -O main.swift -o "$NAME.app/Contents/MacOS/$NAME"

cat > "$NAME.app/Contents/Info.plist" <<'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key><string>Sunwall</string>
    <key>CFBundleDisplayName</key><string>Sunwall</string>
    <key>CFBundleExecutable</key><string>Sunwall</string>
    <key>CFBundleIdentifier</key><string>local.sunwall</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>CFBundleShortVersionString</key><string>1.0</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>LSUIElement</key><true/>
    <key>NSPrincipalClass</key><string>NSApplication</string>
    <key>NSHighResolutionCapable</key><true/>
    <key>NSLocationWhenInUseUsageDescription</key><string>Sunwall uses your location to follow the real sun where you are.</string>
    <key>NSLocationUsageDescription</key><string>Sunwall uses your location to follow the real sun where you are.</string>
</dict>
</plist>
EOF

codesign --force --deep --sign - "$NAME.app"
echo "Built $NAME.app"
open "$NAME.app"
