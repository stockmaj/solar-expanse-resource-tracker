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

---

## STOCKPILES tab

Shows every body's current resource inventory — how much is held, how fast it's flowing in and out, and how long it will last.

### Columns

| Column | Meaning |
|--------|---------|
| **BODY** | Body name. Click to open that body's info panel. |
| **RESOURCE** | Resource name and icon. |
| **QTY** | Current stockpile quantity. |
| **STATE** | Physical state: Sol (solid), Liq (liquid), Gas, Und (underground). |
| **IN/DAY** | Daily inflow (production + imports). |
| **OUT/DAY** | Daily consumption. |
| **NET/DAY** | Net change per day (positive = growing, negative = depleting). |
| **LASTS** | Time until this stockpile reaches zero at the current net rate. Blank if the stockpile is stable or growing. |

### "Bodies with" filter strip

Each icon in the strip represents a resource. **Toggle one or more icons** to show only bodies that have *all* selected resources stockpiled simultaneously. This is useful for finding bodies that can supply multiple resources at once.

> **Note:** You can filter by a resource that is hidden in the "Show Resources" panel. The filter applies at the body level — if a body has Water stockpiled but you've hidden the Water row, the body still appears in the "Bodies with: Water" filter. Only the individual resource row is hidden, not the body.

Click the red **×** button (appears when a filter is active) to clear all filters at once.

### Sort

Use the **Sort** dropdowns to order bodies by name (A→Z) or by a specific resource — sorting by the quantity, net rate, or remaining time of any chosen resource.

---

## DEPOSITS tab

Shows explored mineral deposits across your empire: how much is there, how efficiently it can be extracted, and how long it will last at current mining rates.

### Columns

| Column | Meaning |
|--------|---------|
| **BODY** | Body name. Click to open that body's info panel. |
| **RESOURCE** | Resource name and icon. |
| **TOTAL** | Total surveyed deposit mass (all deposits of this resource on this body combined). |
| **EFF** | Effective quantity (see below). |
| **LASTS** | Estimated time until deposits are exhausted at current mining rates. Only shown when a mine is actively extracting. |
| **DEPOSITS** | Individual deposit tiles — one per distinct deposit site. |

### What is EFF (Effective Quantity)?

`EFF = Σ (deposit size × quality factor)`

Each deposit has a **quality factor** (0–1) that represents extraction efficiency: how much resource you recover per unit of deposit mass. A quality of 1.0 means perfect recovery; 0.25 means you get one quarter of the nominal deposit size.

Two bodies with the same TOTAL can have very different EFF:

| Body | TOTAL | Quality | EFF |
|------|-------|---------|-----|
| Alpha | 100 KT | 0.80 | 80 KT |
| Beta | 100 KT | 0.20 | 20 KT |

Alpha is four times more valuable to mine even though both bodies have the same total deposit mass. **Sort by EFF to find the best bodies to mine**, not just the largest deposits.

### Deposit tiles

Each tile in the DEPOSITS column represents one deposit site:

- **Top number** — deposit size
- **Colored number** — quality factor (green = high, yellow = medium, red = low)
- **Small icon + state** — resource type and physical state

### "Bodies with" filter + min qty/qual

Toggle resource icons to filter for bodies that have those deposits. When one or more icons are selected, per-resource threshold inputs appear:

- **min qty** — only show bodies where the qualifying deposit mass meets this threshold (in KT)
- **min qual** — only count deposits whose quality factor is at or above this value when checking the min qty threshold

Both thresholds work together: a body passes if the total size of its deposits *with quality ≥ min qual* is *≥ min qty*.

> **Filtering by hidden resources:** As with the Stockpiles tab, the "Bodies with" filter works on deposit data even if the resource row is hidden via "Show Resources". Filtering operates at the body level; row visibility is a separate display preference.

### Sort

Sort bodies by name or by a specific resource — by total deposit mass, effective quantity, best quality factor, or estimated time remaining.

---

## Show / Hide Resources

Click **Show Resources** (top-right of the panel) to open a panel where you can toggle individual resource rows on or off. Hiding a resource removes its rows from the table without affecting any active filters.

---

## Help

Full documentation: <https://github.com/stockmaj/solar-expanse-resource-tracker>
