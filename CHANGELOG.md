# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [0.5.0] - 2026-06-21
### Added
- Sort selection is saved and restored on reload, separately for each tab.
- Screenshots added to README.

### Fixed
- Columns on both tabs were misaligned — data appeared far to the right of its header. Now fixed.
- Deposit filter inputs (min qty / min qual) were appearing below the icon strip instead of to the right of it.
- Sort filter was not saving correctly when switching between body-name sort and resource sort.
- Deposit filter state from a previous session could incorrectly carry over after reload.
