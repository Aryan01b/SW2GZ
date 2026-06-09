# Preview panel — design brainstorm

Companion to the four HTML mockups in this folder. Captures the rationale
for each direction and the full menu of features under consideration so we
can pick a scope deliberately, not by accretion.

Open `index.html` in a browser to browse the four options side-by-side.

## Why redesign now

The current sidebar is a single scrolling column with every section always
expanded. Works for `full_arm` (4 links, 3 joints). Breaks for any robot
above ~20 joints: you scroll past the joint you want, every detail panel
fights for the same column, and the search problem ("where is `wheel_fl`?")
has no answer. The current dialog also clips text behind the button row
and uses a marketing-card aesthetic that doesn't match SOLIDWORKS' visual
language.

## Design axes

| Axis | Options |
|---|---|
| **Sidebar width** | narrow (260-300 px) ↔ wide (440-500 px) |
| **Information layout** | single column ↔ multi-pane (split or tabs) ↔ overlay (HUD) |
| **Hierarchy presentation** | flat list ↔ collapsible accordion ↔ tree |
| **Selected-item detail** | inline-expand ↔ persistent right pane ↔ HUD pop |
| **Discoverability** | scroll ↔ search ↔ filter ↔ icon-rail |
| **Density** | airy (current) ↔ tight (Blender/RViz) |

Each mockup picks a coherent point on these axes.

## The four directions

### Option A — Outliner
Single 280 px column, every section collapses with a chevron, parent→child
link tree replaces flat list. Filter input at top. Inspired by Blender's
outliner. Tight monospace rows for technical fields.

- **Strength:** mirrors the URDF graph structure visually — you see joint-link-joint chains the way a roboticist thinks about them.
- **Weakness:** still one column, so deep selection details fight scroll position.
- **Best for:** users with multi-arm robots where parent-child structure is the main thing to verify.

### Option B — Tabbed
320 px column with top tabs: **Scene** / **Properties** / **Validation** / **Source**. Clicking a link in Scene auto-switches to Properties for that item. Filter input pinned above the tab strip.

- **Strength:** each tab shows ONE thing at appropriate density. No scrolling past unrelated sections.
- **Weakness:** modal — you can't see scene and properties at the same time without context-switching.
- **Best for:** quick inspection workflows where the user reads Properties, validates, then closes.

### Option C — Split
460 px sidebar, two columns side-by-side. Left: hierarchy tree, always visible. Right: full properties of selected item, persistent. Bottom strip: compact icon row of display toggles.

- **Strength:** closest to Blender's outliner + properties editor pairing. Tree context never lost while reading details.
- **Weakness:** eats viewport width. On a 1366×768 laptop that's a third of the screen.
- **Best for:** heavy inspection — comparing joints, walking the tree while watching properties update.

### Option D — Minimal
60 px icon rail on the left. Click an icon → drawer slides out with that panel. Click the active icon to dismiss. HUD overlays surface key facts. Maximum viewport real estate.

- **Strength:** 3D view dominates. Drawers are opt-in for the details you actually need.
- **Weakness:** discoverability — new users may not realise data is one click away. Validation count surfaces via a red dot on the rail.
- **Best for:** demo / presentation contexts, or experienced users who want minimum chrome.

## Feature catalogue (what the preview could offer)

Mockups vary in WHICH features they expose; this is the full set. Pick what
ships in the chosen direction.

**Always-on** *(already in current preview)*
- Visual mesh toggle (DAE)
- Collision hull toggle (STL)
- Link frame toggles (TF triads)
- Joint axis arrows
- Inertial COM markers
- Floating link-name labels
- World axes + gizmo
- Ground grid
- Live SW joint poll

**Hierarchy + navigation**
- Parent→child tree (replaces flat list)
- Filter / search input
- Click link/joint in sidebar → flash highlight in 3D
- Camera presets (front / top / iso / side)
- Fit-to-link button (zoom to selected)

**Joint inspection**
- Per-joint slider (already present)
- Joint axis arrow color-coded by type
- Joint limit arc/cone in 3D (visual range of motion)
- Mate name + SW feature link (the SW mate that produced the joint)
- Mate-→-joint dependency panel
- Mimic relationship visualizer (when Gear/Screw mates land)

**Link inspection**
- Inertia ellipsoid (visualize tensor as ellipsoid)
- Per-link bounding box
- Per-link mass + COM in numeric panel
- Mesh fidelity badge (triangle count, file size)
- Toggle individual link visibility

**Validation**
- Warning/error list with click-to-locate
- Severity badges on links + joints in sidebar
- Mate coverage report ("3 of 5 mates mapped")
- Unit consistency check (rad vs deg flags)
- Limit-sanity check (e.g. revolute >360° is suspicious)

**Statistics + diagnostics**
- Total mass, total volume, total bbox
- DOF count (sum of revolute + prismatic + 6×floating)
- Tree depth, max branching
- Mesh totals (DAE + STL counts, byte sizes)
- URDF text size
- Output folder path (live)

**Source views**
- Raw URDF/xacro in a code pane
- Launch file
- TF tree as ASCII text (`world → base_link → link_2 → ...`)
- ros2_control config

**Export coordination**
- Copy preview URL (port) to clipboard
- "Looks good — Export" button (current)
- Re-render after pipeline re-run
- Open temp workspace folder

**Coord conventions**
- Display SW up / forward axis
- Display URDF root rotation (world_to_base_link)
- REP-103 vs other conventions badge

**Power-user**
- View-config URL (deep link to current toggle state)
- Screenshot button (PNG of current 3D view)
- ASCII TF tree export
- Toggle SW-mate-vs-URDF-joint cross-references
- Keyboard shortcuts (TBD)

## Joint-type coverage

See `joint-mate-reference.html` in this folder for the full SW Mate → URDF
→ SDF/Gz → ros2_control table. Briefly:

- **v2.1.0 covered:** Concentric (+ angle limit / + distance limit), LimitAngle, LimitDistance, Lock, Coincident-as-fixed.
- **v2.2 planned:** Slot (prismatic with slot-length limits), Gear / Screw / Rack-pinion (URDF `<mimic>` + SDF native), Universal (decomposed in URDF).
- **Unsupported:** Path, CamFollower (no clean URDF/SDF equivalent).

## What I'd ship

Personal vote — **Option C (Split)** with progressive disclosure for big
robots:
- Tree always visible (left 240 px) keeps users oriented.
- Properties column (right 220 px) is the heart of the inspection workflow.
- Bottom toggle strip frees the tree column from display chrome.
- For models with > 30 links, auto-collapse the tree to depth 2 with an
  "expand all" affordance.

Option B (Tabbed) is the close second if the wider sidebar is a
deal-breaker.

Open the mockups and tell me which direction (or hybrid) to take into the
live `preview/index.html`.
