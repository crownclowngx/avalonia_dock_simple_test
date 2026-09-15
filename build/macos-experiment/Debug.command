#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
# Preserve normal data location. Capture stdout/stderr from the actual executable.
./MyAvaloniaManagement.app/Contents/MacOS/MyAvaloniaManagement 2>&1 | tee ./macos-launch.log
