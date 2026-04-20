#!/usr/bin/env bash
# Rebrand Diva AI to a new name across the entire codebase.
# Usage: ./tools/rebrand.sh <NewName> <NewSlug> [NewApiAudience]
# Example: ./tools/rebrand.sh AcmeCorp acme acme-api
set -euo pipefail

NEW_NAME="${1:-}"
NEW_SLUG="${2:-}"
NEW_API_AUDIENCE="${3:-${NEW_SLUG}-api}"

# ── Validation ────────────────────────────────────────────────────────────────
if [[ -z "$NEW_NAME" || -z "$NEW_SLUG" ]]; then
    echo "Usage: $0 <NewName> <NewSlug> [NewApiAudience]" >&2
    exit 1
fi
if [[ "$NEW_NAME" =~ [[:space:]] ]]; then
    echo "NewName must not contain spaces." >&2; exit 1
fi
if [[ ! "$NEW_SLUG" =~ ^[a-z0-9\-]+$ ]]; then
    echo "NewSlug must be lowercase alphanumeric (hyphens allowed)." >&2; exit 1
fi

# ── Check for uncommitted changes ─────────────────────────────────────────────
if [[ -n "$(git status --porcelain)" ]]; then
    echo "Uncommitted changes detected. Commit or stash them before rebranding." >&2
    exit 1
fi

# ── Create branch ─────────────────────────────────────────────────────────────
BRANCH="rebrand/$NEW_SLUG"
git checkout -b "$BRANCH"
echo "Working on branch: $BRANCH"

# ── Helper: in-place sed (cross-platform macOS/Linux) ─────────────────────────
sedi() {
    if sed --version 2>/dev/null | grep -q GNU; then
        sed -i "$@"
    else
        sed -i '' "$@"
    fi
}

# ── File content replacement ──────────────────────────────────────────────────
echo "Replacing strings in files..."
EXTS=(-name "*.cs" -o -name "*.csproj" -o -name "*.slnx" -o -name "*.md" \
      -o -name "*.yml" -o -name "*.yaml" -o -name "*.txt" -o -name "Dockerfile" \
      -o -name "*.json" -o -name "*.ts" -o -name "*.tsx" -o -name "*.html")

FILES=$(find . \( "${EXTS[@]}" \) \
    -not -path "*/bin/*" -not -path "*/obj/*" \
    -not -path "*/node_modules/*" -not -path "*/.git/*")

while IFS= read -r f; do
    sedi \
        -e "s/Diva\./${NEW_NAME}./g" \
        -e "s/\"diva-local\"/\"${NEW_SLUG}-local\"/g" \
        -e "s/\"diva-api\"/\"${NEW_API_AUDIENCE}\"/g" \
        -e "s/\"Diva AI\"/\"${NEW_NAME}\"/g" \
        -e "s/Diva AI/${NEW_NAME}/g" \
        -e "s/diva_/${NEW_SLUG}_/g" \
        -e "s/diva-api/${NEW_API_AUDIENCE}/g" \
        -e "s/diva-data/${NEW_SLUG}-data/g" \
        "$f"
done <<< "$FILES"

# ── Directory renames ─────────────────────────────────────────────────────────
echo "Renaming directories and files..."
for dir in src/Diva.* tests/Diva.*; do
    [[ -d "$dir" ]] || continue
    newdir="${dir/Diva./${NEW_NAME}.}"
    [[ "$dir" != "$newdir" ]] && mv "$dir" "$newdir" && echo "  Renamed: $dir -> $newdir"
done

# Rename solution file
if [[ -f "Diva.slnx" ]]; then
    mv "Diva.slnx" "${NEW_NAME}.slnx"
    echo "  Renamed: Diva.slnx -> ${NEW_NAME}.slnx"
fi

# ── Commit ────────────────────────────────────────────────────────────────────
git add -A
git commit -m "chore: rebrand Diva -> ${NEW_NAME}"

echo ""
echo "Rebrand complete!"
echo "Verify with:"
echo "  dotnet build ${NEW_NAME}.slnx"
echo "  cd admin-portal && npm run build"
echo ""
echo "Note: If AppBranding__Slug changes after go-live, all browser sessions"
echo "      and platform API keys will be invalidated."
