#!/bin/bash
set -o pipefail
cd /app

dotnet --info
echo '--- restore ---'
dotnet restore BlazorRepro.csproj || exit $?

echo '--- build (expected to surface CS8034 against deployed 2026.0.12) ---'
LOG=$(mktemp)
dotnet build BlazorRepro.csproj --no-restore -c Debug 2>&1 | tee "$LOG"

# csc wraps the BadImageFormatException load failure in an InvalidOperationException;
# the canonical "incorrect format" message text is what surfaces in the build log.
if grep -q 'warning CS8034' "$LOG" && grep -q 'incorrect format' "$LOG"; then
    echo 'OK: Issue180Repro surfaced the expected CS8034 + BadImageFormat-message'
    exit 0
fi
echo 'FAIL: bug warnings not present — has Metalama.Compiler 2026.0.12 been republished?'
exit 1
