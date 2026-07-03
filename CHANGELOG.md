# Changelog

## v3.0.1

- Fixed a bug where the ballon from my MonsterCosmetics mod in the mini-semibot ingame would not apply the correct color.

## v3.0.0

> ⚠ **Breaking — config keys renamed.** Several options were renamed/split, so your existing
> `.cfg` values reset to defaults on first launch. Review the config after updating:
>
> - `UnlockAll` → `AutoUnlockBridgeCosmetics`
> - `EnableCosmeticOverrideUI` → `EnableCosmeticCustomizer`
> - `FixBridgedCosmetics` → `RemoveBridgePhysics` + `LoopBridgeAnimation` + `BridgeEquipAnimationMode`
> - `DefaultRarity` → `BridgeDefaultRarity`
> - `ResetUnlocks` → `ResetBridgeUnlocks`
> - `HighlightModdedCosmetics` → `HighlightBridgeCosmetics` (clearer — it only ever affected bridge cosmetics)
> - Modded non-bridge options (`AutoUnlockModdedCosmetics`, `AllowModdedCosmeticCustomizer`, `ResetModdedUnlocks`) live in a new **`[OtherModdedCosmetics]`** config section, plus a new `HighlightModdedCosmetics` (purple border for other mods' cosmetics, default off)

### Mini-Semibot

- New **Mini-Semibot** world cosmetic (in the WORLD tab, toggle with `EnableMiniSemibot`): a cute small copy of your avatar that follows you around, dressed in your outfit, mirroring your expressions, visible to everyone in the room. All its behaviour knobs live in its **Shift + click** popup
- **Shift + click** to customize (requires MenuLib): Position (Behind/Front), Size, Outfit (Same As You / Random Preset), Look At (menu and ingame options) + Idle Glance, Holding visual (Clean Arm / Orb / Orb + Light) with the grabber beam and beam color, Leg Speed, Avoid Walls, State Effects (mirrors your hurt/heal/upgrade flash), Flashlight, Show in Expression Preview, Recapture Icon, and **When You Die** behavior (Death Head / Crouch & Wait / Hide)
- The mini's grabber beam mirrors your **overcharge** — the same growing glow, sparks and hum as your real beam, scaled to the mini's size
- **Mouth** (talk) modes: Never / Random chatter / When I Talk (tracks your live voice) / **Mimic Clips** — plays the voice lines the **Mimic** mod recorded (requires that mod), with adjustable Chatter, Voice Volume and Voice Range
- Outfit, colors, animated colors and chosen behaviors are **synced to other players**; supports Same-As-You and Random-Preset outfits, local and remote
- **Hide on Kart** option for the King of the Losers arena, and a **Follow Smoothing** (Off / Soft / Bouncy) easing as it trails you
- When you explode in an **arena**, the Mini-Semibot **explodes too** — same blast, in its own outfit colors (purely visual, hurts no one)

### World cosmetics

- New per-cosmetic **Follow Smoothing** for WORLD cosmetics (`Off` / `Soft` / `Bouncy`): they can trail you with a soft lag or a bouncy overshoot instead of being rigidly glued. Set per cosmetic in its **Shift + click** popup; gated by the new `EnableWorldFollowSpring` option (default `false`). Local-only, not synced
- New per-cosmetic **Show To Self (In Game)** option (**Shift + click**): world cosmetics are hidden from your own first-person view by default (as in MoreHead) — turn it on to see your own. Other players always see them
- New per-cosmetic **Avoid Walls** and **Hide on Kart** options for WORLD cosmetics (**Shift + click**): keep the cosmetic clear of level geometry, and hide it while on the kart / in the King of the Losers arena. Owner-authoritative and synced — your choice shows on everyone's screen

### Bridge tinting

- Bridge cosmetics with a supported color channel can now be tinted across **Standard / URP / Unlit / Hurtable** shaders (not just REPO's shader)
- New option `EnableBridgeTinting` (default `true`) — enables tinting for **bridge** cosmetics specifically (per-cosmetic `Allow Coloring` takes priority)

### Per-cosmetic colors

- Paint each cosmetic — bridge, vanilla or modded — its **own color** in the in-game color picker, independent of the per-type color
- New master `EnablePerCosmeticColors` in `[General]` (default `true`) — switches the whole per-cosmetic color system on/off: palette overrides, custom RGB, animated colors and the color sync
- **Per-material-slot** coloring for cosmetics with multiple materials — a slot selector (`ALL · 1 · 2 · …`) appears below the palette
- **Original** button restores the author's material colors (whole-asset or a single slot)
- Colors (including per-slot) are **synced to other players**

### Color animations

- Animated colors for bridge cosmetics: **Cycle Smooth** (your own palette sequence) and **Rainbow** (HSV), with Loop / Ping-Pong and adjustable speed
- Animations are **synced** across players using the server clock, so everyone sees the same phase
- New option `EnableBridgeColorAnimations` (default `false`) — adds an "A" button to the color picker

### Death Head

- Bridge cosmetics (and modded non-bridge cosmetics) now appear on the in-game **death head** with correct colors, synced for remote players
- New **"Show on Death Head"** toggle per cosmetic in the Death Head offset editor — hide a specific cosmetic on the death head while keeping it on the live avatar
- Bridge cosmetics support a configurable **Impact Pose** (in the Death Head editor): when the hat contacts the ground, it springs to the configured pos/rot/scale and back

### Cosmetic Customizer (expanded)

**Shift + click** now opens a popup with a **live preview avatar** and many more per-cosmetic options:

- **Allow Coloring**, **Allow Custom Color** and **Allow Animated Color** overrides
- **Jiggle Physics** (sway) — Light / Moderate / Strong
- **Remove Physics**, **Loop Animation**, **Equip Animation** and **Vanilla Position Fixes** overrides per item
- **Main / Sub Category** — category split into two levels
- **Shape & Hide Conditions** — toggle condition triggers relevant to the cosmetic's type
- **Special Position Fixes** — conditional position/rotation/scale offsets. Automatic body-shape fit fixes show as **(auto)** rows you can Skip; the **Missing Right Side** head fix starts **(off)** — press **Use** to opt in per cosmetic
- **Crown Settings** — configure a crown target for Hat / Head-mesh cosmetics (plus a **Fix Crown Error** action)
- **Icon** actions per cosmetic — Recapture Icon, Delete Icon, per-cosmetic Use Isolated Icon Render
- **Add to Blacklist** straight from the popup
- Opt-in `AllowModdedCosmeticCustomizer` to also customize modded **non-bridge** cosmetics (from other mods)
- Per-cosmetic settings can be **exported/imported** to a JSON file (`ExportCosmeticCustomizer` / `ImportCosmeticCustomizer`), plus a per-cosmetic **Export Settings** action

### Menu & quality of life

- **GroupCosmeticVariants** (default `false`) — collapse variant families (RepoPride flags, MoreHead color variants) into a single button; click to pick the variant
- **Own bridge blacklist** (new `[Blacklist]` section) — a per-cosmetic blacklist independent of MoreHead's, with `BlacklistMode` (skip entirely / load hidden) and `BlacklistOnMoreHead` to mirror it into MoreHead; cosmetics can also be blacklisted straight from the **Shift + click** popup
- **HideMoreHeadDecorations** (default `false`) — hide the decorations you equipped through the MoreHead menu on avatars; owner-authoritative and synced
- **ExcludeMoreHeadFromPresetIcons** (default `false`) — leave MoreHead decorations out of saved preset preview thumbnails

### Tools button & Sync Customizer

- New **Tools** dropdown in the cosmetics menu (`ShowToolsButton`, default `true`): Generate Icons, Clear All Icons, and **Sync Customizer**
- **Sync Customizer** lets you browse and import the per-cosmetic settings of other players in your room (with a ★ for Steam friends)

### Icons

- **GenerateAllIcons** now shows on-screen progress, can be interrupted (ESC or closing the menu) and **resumes** where it left off
- New flags while generating: `HideClothesWhileGenerating`, `ResetBodyColorWhileGenerating`, `HideAvatarWhileGenerating`

### Compatibility

- `FixBridgedCosmetics` was split into `RemoveBridgePhysics`, `LoopBridgeAnimation` and `BridgeEquipAnimationMode` (each can also be overridden per cosmetic)
- New `FixCosmeticsMenuPerformance` for setups with many cosmetics mods
- Xuaun's own cosmetic packs get themed menu borders and sort **first within their group**: **RepoPride** (pride-flag gradients), **YoshiCarry** (Yoshi gradients), **MonsterCosmetics** (purple), and **XuaunCosmetics** / **FortniteSemibot** (coral)

### Under the hood (fixes & hardening)

- **Multi-equip is now native:** `AllowMultipleCosmetics` flips the game's own `canEquipMultiple` flag per cosmetic type instead of a manual extra-spawn system — vanilla handles the spawning, coloring, crown and conditions itself (~400 lines of workaround code removed)
- **Multiplayer:** per-player caches are now cleared when someone leaves the room — fixes a slow memory leak and prevents a stale override from showing on the wrong avatar if Photon reuses an actor number
- **Saves are now atomic** (write-temp-then-swap) for overrides, colors and favorites, so a crash mid-write can't corrupt the JSON; pending writes are flushed when the game closes
- RPC ordering fix (modded-before-vanilla) so equip changes no longer "lag by one" for friends, plus an anti-grief throttle on inbound override events
- Internal cleanup: removed dead code, de-duplicated popup/condition/color helpers, unified atomic-write logic

---

## v2.2.0

### Compatibility

- New option `FixBridgedCosmetics` (default: `true`, in the `[Compatibility]` config section) — automatically fixes common asset issues on bridge cosmetics **at load time**, applied directly to the prefab so every instance inherits the fix:
  - Removes `Collider` and `Rigidbody` components — eliminates the character-rotation bug in the cosmetics preview menu caused by cosmetics with physics components
  - Forces `Animation` clips to loop — cosmetics that only played once now loop correctly
  - Adds software-loop support for `Animator`-driven clips whose controller states are not marked as Loop Time
- At load time the mod now detects whether a bridge cosmetic uses `PartShrinker` (body-part hiding from [MoreHeadUtilities](https://thunderstore.io/c/repo/p/Maygik/MoreHeadUtilities/)) and logs whether it is active or whether MoreHeadUtilities needs to be installed

### Dependencies

- MoreHead is no longer a required dependency — MoreHead Bridge works standalone and treats MoreHead as optional
  - Someone asked me if I could remove this hard dependency on MoreHead - so I did it in this version
  - But you still need the `.hhh` cosmetic files to load in-game - so I recommend keeping the mods to use them

---

## v2.1.0

### Features / Fixes

- Individual color sync (_working correctly_) in multiplayer — other players now see the correct individual colors you have set per cosmetic
- Updated the RepoLIB version dependency (4.0.3 -> 4.0.4)

### Bug fixes

- Clothing not appearing after the first Confirm in multiplayer
- Reset All leaving the old outfit all white on the remote instead of clearing correctly
- Virtual tabs (FAV, HIDE, SEARCH, SELECTED) displaying "new" badges incorrectly
- Painting a cosmetic individually sometimes had strange behavior
- Loading multiple painted cosmetics from a saved outfit in the preset menu now correctly colors the cosmetics

---

## v2.0.0

### World Cosmetics

- `.hhh` cosmetics with the `World` tag are now fully supported — they appear in the vanilla menu under a dedicated **WORLD** category button
- World cosmetics (internally - hats) render independently of hats (no slot conflict), with correct highlight counters, and equip/unequip actions

### Multiple Cosmetics

- You can now equip multiple cosmetics of the same type simultaneously
- Configurable via `AllowMultipleCosmetics` (default: `true`)

### Favorites & Hidden

- **Ctrl + click** any cosmetic to toggle it as a **favorite**
- **Alt + click** any cosmetic to **hide** it from the menu
- Dedicated **FAV** and **HIDE** category tabs show only favorited / hidden cosmetics
- Hidden cosmetics are excluded from other categories and Randomize buttons
- Saved persistently to `MoreHeadBridge_Favorites.json`

### Cosmetic Customizer

- **Shift + click** any bridge cosmetic to open a per-cosmetic override popup
- Override rarity tier, category (Hat, BodyTop, World, …), and modded-highlight flag individually
- _Please note that changing the cosmetic category is only a local change and may alter the `bone` in which the cosmetic appears for you_
- Overrides are saved to `MoreHeadBridge_CosmeticOverrides.json` and applied on every launch
- Enable via `EnableCosmeticOverrideUI = true` (default: `false`)

### New Menu Tabs

- **SEARCH** — live search bar filters cosmetics by name as you type; position configurable (Top / Bottom)
- _You can see the names of the cosmetics (to search) now by hovering over them._
- **SELECTED** — shows only currently equipped cosmetics; updates after Randomize / Reset

### Visual

- Bridge cosmetics now show an **orange border** in the menu to distinguish them from vanilla cosmetics (configurable via `HighlightModdedCosmetics`)
- Default rarity tier for bridge cosmetics is now separately configurable (`DefaultRarity`)

### Icon Generation

- Icon cache moved to `Cache/Icons/CosmeticsModded/MoreHeadBridge_CosmeticsIcons/` — sits outside REPOLib's wipe zone; automatic migration from the old path on first launch

---

## v1.0.0

- MoreHead Bridge initial release
