# Solar Expanse — Resource Tracker

A BepInEx mod that adds a floating **RESOURCES** panel showing stockpile levels and deposit survey data across all your bodies.

---

## Installation

1. Build and install with `./build.sh` (requires `SOLAR_EXPANSE_ROOT` to point at your game folder, or set in `.mise.toml`).
2. The DLL is copied automatically to `BepInEx/plugins/ResourceTracker.dll`.

---

## Opening the panel

Click the **RESOURCES** button in the top bar (next to LAUNCH WINDOWS). The panel floats and can be:

- **Dragged** by the RESOURCES button itself
- **Resized** by dragging the bottom edge
- **Clicked on a body name** to open that body's info panel

Click **?** (top-right, next to Show Resources) to open this documentation.

---

## Show / Hide Resources

Click **Show Resources** (top-right of the panel) to open a panel where you can toggle individual resource rows on or off. Hiding a resource removes its rows from the table without affecting any active "Bodies with" filters — you can still filter by a resource whose rows are hidden.

---

## STOCKPILES tab

Shows every body's current resource inventory — how much is held, how fast it's flowing in and out, and how long it will last.

### Columns

| Column | Meaning |
|--------|---------|
| **BODY** | Body name and sprite. Click to open that body's info panel. |
| **RESOURCE** | Resource name and icon. |
| **QTY** | Current stockpile quantity. |
| **STATE** | Physical state: Sol (solid), Liq (liquid), Gas, Und (underground). |
| **IN/DAY** | Daily inflow (production + imports). |
| **OUT/DAY** | Daily consumption. |
| **NET/DAY** | Net change per day (positive = growing, negative = depleting). |
| **LASTS** | Time until this stockpile reaches zero at the current net rate. Blank if the stockpile is stable or growing. |

### "Bodies with" filter strip

Each icon in the strip represents a resource. **Toggle one or more icons** to show only bodies that have *all* selected resources stockpiled simultaneously. Useful for finding bodies that can supply multiple resources at once.

> **You can filter by a hidden resource.** The filter applies at the body level — if a body has Water stockpiled but you've hidden the Water row via Show Resources, that body still appears in a "Bodies with: Water" filter. Only the individual row is hidden, not the body.

Click the red **×** button (appears when a filter is active) to clear all filters at once.

### Sort

Use the **Sort** dropdowns to order bodies by name (A→Z) or by a specific resource — by quantity, net rate, or remaining time.

---

## DEPOSITS tab

Shows explored mineral deposits across your empire: how much is there, how efficiently it can be extracted, and how long it will last at current mining rates.

### Columns

| Column | Meaning |
|--------|---------|
| **BODY** | Body name and sprite. Click to open that body's info panel. |
| **RESOURCE** | Resource name and icon. |
| **TOTAL** | Total surveyed deposit mass across all sites of this resource on this body. |
| **EFF** | Effective quantity — see below. |
| **LASTS** | Estimated time until deposits are exhausted at current mining rates. Only shown when a mine is actively extracting. |
| **DEPOSITS** | Individual deposit tiles — one tile per distinct deposit site. |

### What is EFF (Effective Quantity)?

`EFF = Σ (deposit size × quality factor)`

Each deposit has a **quality factor** (0–1) that is its extraction efficiency — how much resource you recover per unit of deposit mass. Higher quality means more yield per tonne mined.

Two bodies with the same TOTAL can have very different EFF:

| Body | TOTAL | Quality | EFF |
|------|-------|---------|-----|
| Alpha | 100 KT | 0.80 | 80 KT |
| Beta  | 100 KT | 0.20 | 20 KT |

Alpha yields four times as much even though both bodies show the same total deposit mass. **Sort by EFF to find the most productive bodies to mine**, not just the largest.

### Deposit tiles

Each tile in the DEPOSITS column represents one deposit site. Top to bottom:

1. Resource icon + physical state (Sol/Liq/Gas/Und)
2. Deposit size
3. Quality factor (green ≥ 0.7, yellow ≥ 0.4, red < 0.4)

### "Bodies with" filter + min qty/qual

Toggle resource icons to filter for bodies that have those deposits. When one or more icons are selected, per-resource threshold inputs appear inline with the icon strip:

- **min qty** — only show bodies where the qualifying deposit mass meets this threshold (in KT)
- **min qual** — only count deposits whose quality factor is at or above this value

Both thresholds work together: a body passes if the total size of its deposits *with quality ≥ min qual* totals *≥ min qty*.

> **You can filter by a hidden resource.** Same behaviour as the Stockpiles tab — filtering operates at the body level regardless of row visibility.

### Sort

Sort bodies by name or by a specific resource — by total deposit mass, effective quantity, best quality factor, or estimated time remaining.
