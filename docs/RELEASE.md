# Release Guide

Step-by-step instructions for cutting a new release of Awesome Library Cleaner and
publishing it via the Jellyfin plugin repository manifest.

## Overview

The project uses the Jellyfin `jellyfin-meta-plugins` reusable workflows:

- `.github/workflows/build.yaml` — builds the plugin zip on every push/PR to `master`
- `.github/workflows/changelog.yaml` — drafts a GitHub Release and bumps the version
- `.github/workflows/publish.yaml` — runs when a GitHub Release is published; builds the
  zip, attaches `.md5`/`.sha256` sidecars to the release, and (only for repos under the
  `jellyfin/` org) deploys to the central repository.

Because this repo is under `storm1er/` (not `jellyfin/`), the deploy step in `publish.yaml`
is skipped by the `if: contains(github.repository, 'jellyfin/')` guards. Publishing therefore
reduces to: build zip → attach to GitHub Release → update `manifest.json`.

## Prerequisites

- Write access to `storm1er/jellyfin-plugin-awesome-library-cleaner`
- `gh` CLI installed and authenticated (`gh auth status`)
- The plugin builds against `Jellyfin.*` packages `10.11.11` (see
  `Jellyfin.Plugin.AwesomeLibraryCleaner.csproj`). Keep them in sync with the
  `targetAbi` you advertise.

## Versioning

Follow the existing convention: semantic `x.y.z` as a 4-part version `x.y.z.0` (e.g.
`1.0.0.0`, `1.1.0.0`). The plugin's assembly version is not pinned in the csproj, so the
version for the release tag and the manifest is yours to choose. Do not reuse a tag.

## Steps

### 1. Merge all intended changes to the default branch

Make sure the working tree on the default branch contains everything you want to ship
(fixes, tests green, `dotnet build` clean, `dotnet test` all passing).

### 2. Verify a clean build and tests

```bash
export PATH="/tmp/opencode/dotnet:$PATH"
dotnet build Jellyfin.Plugin.AwesomeLibraryCleaner.sln -c Release --nologo
dotnet test Jellyfin.Plugin.AwesomeLibraryCleaner.sln -c Release --nologo
```

Expect 0 warnings, 0 errors, all tests passing.

### 3. Update the changelog / create the release draft

The `changelog.yaml` workflow (once the `repository-name` is fixed) can auto-create a
release draft on push to `master`. Alternatively create the draft manually:

```bash
gh release create <VERSION> --title "Awesome Library Cleaner <VERSION>" --notes-file CHANGELOG.md --draft
```

List the notable changes (e.g. the items from the latest fix round). Use `<VERSION>`
matching your chosen version (no leading `v`, e.g. `1.1.0.0`).

### 4. Publish the GitHub Release (builds the artifact)

When you publish the draft release, `publish.yaml` runs automatically:

```bash
gh release view <VERSION> --json state
gh release edit <VERSION> --draft=false
```

Wait for the `🚀 Publish Plugin` workflow to finish. It attaches to the release:

- `awesome-library-cleaner_<VERSION>.zip` (the plugin artifact)
- `awesome-library-cleaner_<VERSION>.zip.md5`
- `awesome-library-cleaner_<VERSION>.zip.sha256`

Verify:

```bash
gh release view <VERSION> --json assets --jq '.assets[].name'
```

### 5. Get the checksum for the manifest

The manifest `checksum` field is the **MD5** of the zip (uppercase, no dashes). Fetch the
zip and compute it:

```bash
gh release download <VERSION> --pattern '*.zip'
md5sum awesome-library-cleaner_<VERSION>.zip
# → 5D246C5F...  awesome-library-cleaner_<VERSION>.zip
```

Alternatively, read the published sidecar:

```bash
gh release download <VERSION> --pattern '*.md5'
cat awesome-library-cleaner_<VERSION>.zip.md5
```

The `.md5` sidecar format is `hash  filename`; take the uppercase MD5 from it.

### 6. Update `manifest.json`

Edit `manifest.json` and add a new entry to the top of the `"versions"` array (keep old
entries). For example:

```json
{
  "changelog": "- Never-played items now fall back to date added under Latest Watch Date\n- Config save no longer wipes disabled-library settings\n- ...",
  "targetAbi": "10.11.0.0",
  "version": "1.1.0.0",
  "sourceUrl": "https://github.com/storm1er/jellyfin-plugin-awesome-library-cleaner/releases/download/1.1.0.0/awesome-library-cleaner_1.1.0.0.zip",
  "checksum": "<MD5 FROM STEP 5>",
  "timestamp": "<today's date in ISO 8601, e.g. 2026-08-07T00:00:00>"
}
```

Notes:

- `targetAbi` is the 4-part assembly version of the Jellyfin server ABI. The prior release
  used `10.11.0.0`; keep it unless you intentionally target a newer API.
- The `sourceUrl` must match exactly the asset name the workflow produced.
- The `checksum` must match the published zip byte-for-byte; Jellyfin rejects installs
  where the downloaded file hash differs.
- Multi-line changelog entries use `\n` escape sequences inside the JSON string.

Commit and push `manifest.json` to the default branch.

### 7. Update the GitHub Release description (optional but recommended)

If you want the release description to match the manifest changelog:

```bash
gh release edit <VERSION> --notes-file CHANGELOG.md
```

### 8. Verify installability

In Jellyfin:

1. **Dashboard → Plugins → Manage Repositories** — confirm the ALC repository is added
   (URL: `https://raw.githubusercontent.com/storm1er/jellyfin-plugin-awesome-library-cleaner/refs/heads/main/manifest.json`).
2. **Dashboard → Plugins → Catalog → Awesome Library Cleaner → Install**.
3. Check that the new version appears and installs without a checksum error.
4. **Dashboard → Restart**, then open **Settings** and confirm the config page loads.

## Troubleshooting

- **Workflow "Publish Plugin" did not run** — releases must be published (not drafts);
  `publish.yaml` triggers on `release` with type `released`.
- **`changelog.yaml` reports the wrong repo** — fix `repository-name` in
  `.github/workflows/changelog.yaml`.
- **No CI on push** — the repo branch is `main` while workflows listen on `master`;
  rename the branch or update the trigger lists.
- **Checksum mismatch on install** — recompute the MD5 of the exact zip attached to the
  release; the manifest `checksum` must match it exactly (uppercase, no `0x` prefix, no
  dashes).
- **Deploy job errors** — the `publish.yaml` deploy step needs the `DEPLOY_HOST` /
  `DEPLOY_USER` / `DEPLOY_KEY` secrets. For a non-`jellyfin/` repo the deploy steps are
  skipped, so the missing secrets only matter if you ever mirror into the jellyfin org.
