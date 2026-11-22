# Changelog

All notable changes to Peekaboo are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-11-23

### Added

- On-demand live view that starts an NVR stream only while someone is watching,
  shares one upstream pull per camera and stream, and stops ffmpeg when the last
  viewer leaves.
- RTSP + ISAPI transport and an optional Hikvision SDK transport for live view,
  recording search, and playback.
- Grid layouts from 1×1 to 3×3 with per-device layout and camera assignments.
- Main/sub stream switching per cell while live.
- NVR and camera management with SQLite persistence, NVR clock detection, and an
  optional per-NVR time zone override.
- Recording search and playback with a zoomable, draggable timeline, shared playback
  pulls, and credential-free RTSP links for external players.
- Motion alarm logs rendered as interactive day timelines with playback links.
- Cookie-based accounts, PBKDF2 password hashing, rate-limited login, and
  admin-managed users.
- Single Docker image with SQLite persistence and an optional private image that
  adds the Hikvision SDK.
- `/api/health` reports the running version, and the web UI shows it in the footer.
