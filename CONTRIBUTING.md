# SlugcatInMyMonitor development workflow

This repository uses a lightweight Git Flow.

## Branches

- `main` contains releasable code. A direct feature or fix merge is not used.
- `develop` is the integration branch for the next release.
- `feature/<name>` and `fix/<name>` branch from `develop` and merge back into `develop`.
- `release/<version>` branches from `develop` only when final release stabilization is needed.
- `hotfix/<name>` branches from `main`, merges into `main`, and is then merged back into `develop`.

## Pull requests

Open normal pull requests against `develop`. Combine the completed work into one
`develop` to `main` pull request when it is ready to release. CI runs for both
integration and release pull requests.

Use a Conventional Commit prefix in the pull request title when practical:
`feat:`, `fix:`, `docs:`, `build:`, `ci:`, `refactor:`, `test:`, or `chore:`.
Release Drafter uses these prefixes and labels to group changes and suggest the
next semantic version.

## Releases

Merging `develop` into `main` updates the next draft GitHub Release. Review and
publish that draft in GitHub when the release is ready. Publishing it creates the
version tag and starts the Windows build; the ZIP and SHA-256 file are then
attached to the published release automatically.
