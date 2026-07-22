# Changelog

All notable project changes should be recorded here. This project follows a Keep a Changelog-style structure; version numbers should match the application metadata and release tag.

## [Unreleased]

### Added

- Windows GitHub Actions validation for restore, Release build, and tests.
- Regression tests for Minecraft command escaping/building, configuration normalization, Twitch token handling, command parsing, and IRC parsing.
- Canonical repository documentation, contribution guidance, issue forms, and a pull-request checklist.
- Structured JSON-lines diagnostics with session/application metadata, bounded rolling retention, and automatic secret/path redaction.

### Changed

- Clarified that TwitchCraft is a standalone Windows Twitch-to-Minecraft integration application rather than a Forge/Fabric client mod.
- Updated the source solution path and added a repository-level solution containing the application and test projects.

### Fixed

- None recorded yet.

### Security

- Added safe-log-sharing and secret-handling guidance.
- Redacted registered Twitch/RCON secrets, OAuth and authorization values, RCON property values, and Windows user profile names before diagnostic output is written.

### Removed

- None.

### Known Issues

- Automated UI and live Minecraft/RCON integration tests are not yet included.

## [1.7.1.1]

The existing repository release predates this changelog. See Git tags and GitHub Releases for the historical release artifacts and notes.
