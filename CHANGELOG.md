# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [0.5.0] - 2026-06-21
### Added
- **Screenshots** in README for Stockpiles tab, Deposits tab, and Show/Hide Resources panel.
- **Sort persistence** — selected sort resource and qualifier are saved to config and restored on reload, per tab.
- **Signature now includes net rate and days-remaining** so the table refreshes when flow rates or depletion times change without a quantity change.
- **Body sprite cache pruning** — sprites for bodies no longer in the result set are removed on each refresh.
- **Show All / Hide All** toggle in the Show Resources panel.
- **Deposits filter strip** documentation clarifies that manufactured goods (Alloys, Electronics, Glass, Polymers, Steel, Supplies, Consumer Goods, Antimatter) do not appear because they have no deposit sites.

### Fixed
- **Column alignment** — BodyCol and ResCol LayoutElements now explicitly set `flexibleWidth = 0`, preventing their inner HLGs' flex children from bleeding into the outer row HLG and pushing every subsequent column far to the right. Both tabs affected.
- **Resource icon split into its own child** in both stockpile and deposit rows — icon and name are now separate fixed-width elements inside a sub-HLG, so the icon never shifts the name column.
- **Table header padding** — `tableHeaderVLG` now uses `RectOffset(4, 4, 0, 0)` matching the scroll-content VLG's 4 px left/right padding, so header labels land over the correct data columns.
- **Filter inputs restored to strip** — deposit min qty/qual inputs are back inline to the right of the icon buttons (inside `stripContainer`) after a regression moved them to a separate row below.
- **Icon strip height** — `childForceExpandHeight = false` on the icon group's HLG prevents the icons from stretching when the filter input area grows.
- **Header area height** — `ApplyHeaderHeight` now grows the strip row to absorb the filter input area rather than adding a separate VLG sibling row, keeping the sort row immediately below.
- **Body click fixed on Deposits tab** — Button `targetGraphic` is now an invisible overlay child of the text GO instead of an Image on the bodyCell container; this stops the Image from reporting a preferred width and inflating the body column.
- **Deposit filter inputs** — qty field now resets to `0.00` on invalid input; column headers appear only on the first filter row; each cell gets equal width via `preferredWidth = 1f`.
- **ApplyTabVisuals extracted from SwitchTab** — Init now calls visual setup directly without triggering a config save, scroll reset, or data refresh.
- **Stale active-resource config** — non-deposit resources are cleaned from `_activeResources` before saving when switching to the Deposits tab, preventing stale filter state on reload.
