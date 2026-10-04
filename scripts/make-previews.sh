#!/usr/bin/env bash
# Write src/Ssp.Web/wwwroot/previews/<name>.svg for each circuit in circuits/library.
# The gallery shows these files. GalleryPreviewFileTests fails when one differs from a fresh render.
set -euo pipefail

cd "$(dirname "$0")/.."
UPDATE_GOLDEN=1 dotnet test tests/Ssp.Web.Tests -c Release --filter "FullyQualifiedName~GalleryPreviewFileTests"
