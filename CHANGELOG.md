# v2.2.0

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

# v2.1.0

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

# v2.0.0

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

# v1.0.0
- MoreHead Bridge initial release
