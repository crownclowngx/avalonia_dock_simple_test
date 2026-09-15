#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
root="$PWD"
app="$root/MyAvaloniaManagement.app"
executable="$app/Contents/MacOS/MyAvaloniaManagement"

if [[ "$(uname -s)" != Darwin ]]; then
    echo "Run this preparation script on macOS."
    exit 1
fi
rid="$(cat "$root/runtime-identifier.txt")"
if [[ "$rid" == osx-arm64 && "$(uname -m)" != arm64 ]]; then
    echo "This is the Apple silicon package. Use osx-x64 on an Intel Mac."
    exit 1
fi
if [[ "$(sw_vers -productVersion | cut -d. -f1)" -lt 14 ]]; then
    echo "This .NET 10 experiment requires macOS 14 or later."
    exit 1
fi

mkdir -p "$root/Controls"
chmod u+x "$executable" "$root/Debug.command" "$root/Prepare.command"
if [[ -f "$app/Contents/MacOS/createdump" ]]; then
    chmod u+x "$app/Contents/MacOS/createdump"
fi

# Local test only: remove download quarantine from this specific experimental app.
# No system-wide Gatekeeper settings are changed.
xattr -dr com.apple.quarantine "$app"
# Windows cannot run Apple's codesign. Re-sign Mach-O files locally, inside out.
while IFS= read -r -d '' item; do
    if file -b "$item" | grep -q 'Mach-O'; then
        if [[ "$item" != "$executable" ]]; then
            codesign --force --sign - "$item"
        fi
    fi
done < <(find "$app/Contents/MacOS" -type f -print0)
codesign --force --sign - --entitlements "$root/Experiment.entitlements" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
echo "Prepared. Copy plugin folders into: $root/Controls"
echo "Opening the Host. If it exits, run: bash \"$root/Debug.command\""
open "$app"
