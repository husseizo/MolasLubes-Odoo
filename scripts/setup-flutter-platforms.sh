#!/usr/bin/env bash
# Run this once after cloning to generate iOS and any missing platform files.
# Requires Flutter SDK in PATH.
set -euo pipefail
cd "$(dirname "$0")/../molas_supervisor_mobile"
echo "Generating Flutter platform stubs..."
flutter create --org com.molaslubes --project-name molas_supervisor_mobile \
    --platforms ios .
echo "Done. Run 'flutter pub get' next."
