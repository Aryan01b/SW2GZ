# Mode Pills Redesign — Design Spec

**Date:** 2026-06-07
**Branch:** v2.1.0
**Supersedes (visually):** the chevron-based mode picker shipped in commit `ea24882` ("L3b — replace 3 mode pills with Mode-flyout dropdown")

## Goal

Replace the single chevron-style Mode flyout on the **Common** tab box with a creative two-part shape:

1. A **large "Create [Mode]" button** on the left — face only, no chevron, label tracks the active mode.
2. **Three small "pill" toggles** on the right — one per mode (Robot / World / Asset), stacked vertically.

Mode switching moves from "click the chevron → pick from a menu" to "click a pill." The active pill is rendered as **disabled** (grayed) to signal current mode, since SolidWorks toolbar buttons have no native toggle/pressed state.

Also: delete the throwaway "Demo Split" button that was added while iterating on the split-button pattern.

## Why

The earlier ea24882 design hid mode selection behind a chevron dropdown — discoverable only after the user clicks. The new shape surfaces all three modes as visible toggle controls while keeping the primary "Create" action prominent. The pattern mirrors the Smart Dimension / Insert Components family that users already recognize from native SolidWorks.

## Visual

```
Common box (left edge):

┌──────────────┬─────────────┐
│              │  ◉ Robot    │   ← grayed = active mode
│   [glyph]    ├─────────────┤
│  Create      │  ○ World    │   ← enabled = click to switch
│   Robot      ├─────────────┤
│      ·       │  ○ Asset    │
└──────────────┴─────────────┘
   big face            3 pills
   TextBelow           TextHorizontal (3-per-column)
   NoFlyout            (regular AddCommandItem2 each)
```

Followed in the Common box by the existing Coord / Preview / Export TextBelow buttons.

## Behavior

| User action | Result |
|---|---|
| Click **big face** ("Create Robot/World/Asset") | `OpenCreatePmp` — opens create wizard for active mode (existing) |
| Click **pill: Robot/World/Asset** (when enabled) | `SetMode(mode)` — switches mode, rebuilds tab, big-button label retracks (existing) |
| Pill in **active-mode** position | Disabled (grayed). Tooltip: "Active mode." |
| Pills when **doc is locked** (any content exists) | All 3 disabled. Tooltip: "Mode locked — delete content to switch." |

The big "Create" face stays clickable in all states (assembly-active gate only).

## SolidWorks SDK mechanics

### Big "Create" button — keep the flyout group, drop the chevron

Stays an `IFlyoutGroup` created by `ICommandManager.CreateFlyoutGroup2` because that's the only way to mutate the face label after registration — recreating the flyout with the same userId is treated as an update by SW (the trick already used in `BuildModeFlyout`). The chevron disappears because the tab-box style flag changes from `swCommandTabButton_ActionFlyout` to `swCommandTabButton_NoFlyout`.

The three flyout sub-items (ModeRobot/World/Asset chevron menu) are deleted — pills replace them. `CreateFlyoutGroup2` with zero sub-items is valid; SW just renders the face.

### Three pills — regular command items, TextHorizontal style

Each pill registers via `ICommandGroup.AddCommandItem2`. The tab-box layout flag `swCommandTabButton_TextHorizontal` makes SW render them as small icon-left/text-right buttons that auto-stack 3-per-column to match a TextBelow button's height. Three pills = one full column → exact fit next to the big button.

### Active-pill visual

SolidWorks has no `swCommandTabButtonStyle_e` flag for "pressed" or "selected." The cleanest workaround is **enable-callback inversion**: each pill's update callback returns 0 (disabled) when that pill represents the currently-active mode, else 1. The user reads "the grayed pill is where I am" the same way they read disabled radio buttons. Doc-locked state simply forces all three pills to return 0.

A future iteration could swap the pill icon to a filled variant when active, but that requires regenerating the icon strip (`scripts/GenerateIcons.ps1`) with 3 extra glyphs — out of scope for this design.

## Components

### `RibbonCommandIds.cs`

- **Add:** `ModeRobotPill = 4`, `ModeWorldPill = 5`, `ModeAssetPill = 6`. (Slot 3 is the flyout group; slots 0–2 are flyout sub-item IDs in a separate namespace and stay reserved as documentation, even though the sub-items are removed.)
- **Drop:** `DemoFlyoutGroup` constant.
- Update `AllUserIds` to include the 3 pill IDs. The old flyout sub-item IDs (`ModeRobot`/`ModeWorld`/`ModeAsset` 0/1/2) were already in `AllUserIds` and stay — they're cheap documentation and the sub-items themselves are dropped at the flyout-build site, not the constants.

### `Sw2gzRibbonRegistrar.cs`

- `BuildModeFlyout(mode)`: drop the 3 `_modeFlyout.AddCommandItem(...)` calls. Face label logic unchanged.
- `Register()`: after the existing AssetSurface line, add three `AddItem` calls registering the pills, click callbacks `ModeRobotClick / ModeWorldClick / ModeAssetClick` (reused, already exist), update callbacks `ModeRobotPillUpdate / ModeWorldPillUpdate / ModeAssetPillUpdate`, image columns 1 / 2 / 3 of the strip (the existing Robot / World / Asset glyphs).
- `BuildCommonTabBox`: change the flyout textType from `textBelow | ActionFlyout` to `textBelow | NoFlyout`. After the existing Coord/Preview/Export loop, append the 3 pill cmdIds with `TextHorizontal` style.
- **Delete:** `_demoFlyout` field, `BuildDemoFlyout` method, the `BuildDemoFlyout()` call in `Register`, the demo flyout block in `BuildCommonTabBox`.

### `SwAddin.cs`

- **Add:** `ModeRobotPillUpdate()`, `ModeWorldPillUpdate()`, `ModeAssetPillUpdate()`. Each returns 0 if its mode equals the active mode, OR if the doc is locked, OR if no assembly is active. Else 1.
- **Delete:** the `Demo*` region (`DemoFaceClick`, `DemoOneClick`, `DemoTwoClick`, `DemoThreeClick`, `DemoFlyoutUpdate`, `DemoSubItemUpdate`).
- `ModeRobotClick / WorldClick / AssetClick`: unchanged (now wired by pills instead of chevron sub-items).
- `ModeFlyoutUpdate`: unchanged (still the face update callback).
- `ModeSubItemUpdate`: **delete** — the sub-items it gated no longer exist.

### `agent-progress/progress.md`

Add a short line under v2.1.0 UI shell noting the Mode pills redesign.

## Edge cases & risks

| Risk | Mitigation |
|---|---|
| `CreateFlyoutGroup2` with zero sub-items rejected by SW | Verify on first run. Fallback: register a single hidden sub-item with no callback. |
| `NoFlyout` flag (0x08) interacts strangely with `_modeFlyout.CmdID` | Mode flyout cmdId from `IFlyoutGroup.CmdID` should still render the face. If SW refuses NoFlyout on a flyout cmdId, fall back to a regular `AddCommandItem2` for the big button and accept the static label (face shows "Create" with mode hint in tooltip). |
| Pill cmdIds collide with existing SW commands | Use IDs 4/5/6 within the SW2GZ group's namespace (per-group, not global). `CmdGroupId = 92` isolates them. |
| Tab cache from prior install pins old layout | Existing `ignorePrevious=true` in `Register()` already handles this. |
| `ModePillUpdate` callbacks fire on every SW poll → perf | Same shape as `ModeSubItemUpdate` already shipped — known acceptable. |

## Testing

- 542-test suite stays green (this is UI plumbing; no logic changes).
- Manual: open assembly → SW2GZ tab → Common box has [Big Create + 3 stacked pills + Coord + Preview + Export]. Click each pill → mode switches, big button label retracks, active pill grays out. Add content → all pills gray out, big button stays live. Click big button → wizard opens.

## Out of scope (future iterations)

- Filled-icon variants for the active pill (requires icon-strip regen).
- Mode-specific chevron sub-actions on the big "Create" button (e.g., "From template", "From URDF") — would re-add `ActionFlyout` style.
- Persisting active mode across sessions per-document (covered by the existing `Sw2gzDocStore` work).
