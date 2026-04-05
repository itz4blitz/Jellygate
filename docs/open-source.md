# Open Source Notes

## Repo Scope

This repository contains both runtime pieces needed for the Jellyfin to Aurral auth bridge:

- the gateway container
- the Jellyfin bridge plugin

## Release Outputs

Version tags publish:

- a multi-arch Docker image to GHCR
- a Jellyfin plugin zip as a GitHub release asset
- the Unraid template XML as a GitHub release asset

## Manual Plugin Upload

The Jellyfin plugin zip can be uploaded manually from a local build or downloaded from the GitHub release page.

## Suggested Repository Settings

- public repository
- Actions enabled
- Packages enabled for GHCR
- workflow permissions left at the default `read` plus explicit package/content permissions inside the release workflow
