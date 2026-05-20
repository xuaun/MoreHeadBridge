# MoreHead Bridge

Translates `.hhh` cosmetics (from [MoreHead](https://thunderstore.io/c/repo/p/YMC_MHZ/MoreHead/)) into the vanilla R.E.P.O. cosmetics system, so they appear alongside your vanilla cosmetics in the game.

---

## What does it do?

- Scans `BepInEx/plugins` for `.hhh` files and registers them as vanilla cosmetics
- Cosmetics show up in the vanilla shop with rarity tiers, icons, and unlock tracking
- Auto-unlocks all bridge cosmetics on game start (configurable)
- Supports every cosmetic type including **World** cosmetics
- Favorite your preferred cosmetics and hide the ones you're not interested in - and easily find them in their own tabs in the cosmetics menu
- Equip multiple cosmetics of the same type, each one can have its own individual color
- Search cosmetics or view all the equipped cosmetics
- Hover over a cosmetic item to see its name
- Automatically removes physics components and fixes animation looping on bridge cosmetics at load time (configurable)
- Optional: hide the MoreHead button if you prefer to use only the vanilla UI
- Optional: change the rarity or category per-cosmetic

---

## Menu features

### Favorites & Hidden
- **Ctrl + click** any cosmetic to toggle it as a **favorite**
- **Alt + click** any cosmetic to **hide** it from the menu
- Dedicated **FAV** and **HIDE** tabs appear in the category strip
- Hidden cosmetics are excluded from other categories and Randomize buttons

### New tabs
| Tab | Description |
|---|---|
| **SEARCH** | Live search bar — filters cosmetics by name as you type |
| **SELECTED** | Shows only your currently equipped cosmetics |
| **WORLD** | Shows world cosmetics in their own category |
| **FAV** | Shows only favorited cosmetics |
| **HIDE** | Shows only hidden cosmetics |

### Cosmetic Customizer
**Shift + click** any bridge cosmetic to open a per-cosmetic override popup where you can change:
- **Rarity** — Common, Uncommon, Rare, UltraRare
- **Category** — Hat, Eyewear, Face Upper, Bodywear Top, World, and more
- **Modded highlight** — force the orange border on or off per cosmetic

Overrides are saved to `BepInEx/config/MoreHeadBridge_CosmeticOverrides.json` and applied automatically on every launch.

> _Please note that changing the cosmetic category is only a local change and may alter the `bone` in which the cosmetic appears for you_.
> 
> To enable it, change the `EnableCosmeticOverrideUI = true` in the config file (default: `false`)

---

## How icons work

Since `.hhh` cosmetics don't have built-in icons, the mod generates one for each cosmetic using the following fallback chain:

1. **Placeholder** — a generic icon is generated so the cosmetic slot is never blank in the menu
2. **Texture overlay** — if the cosmetic has a texture, it is applied on top of the placeholder as a preview (can be disabled via `UseTextureAsPlaceholder`)
3. **Captured icon** — once you hover the cosmetic in the menu, the mod snapshots the in-game avatar preview and saves a PNG icon, which replaces the placeholder permanently

> **Tip for best results:** Before generating icons, remove all cosmetics from your Semibot (vanilla and MoreHead), set it to a neutral color, and make sure it is facing forward. This gives each icon the cleanest background possible. If an icon doesn't look right, you can delete it individually using `DeleteIconsMatching` in the config, or wipe all icons at once with `DeleteIconCache`.

---

## Slot mapping

Since the vanilla cosmetics menu has more specific slots than MoreHead, the following mapping is used:

| MoreHead | Vanilla |
|---|---|
| Head | Hat |
| Neck | Face Lower |
| Body | Bodywear Top |
| Hip | Bodywear Bottom |
| Left Arm | Armwear Left |
| Right Arm | Armwear Right |
| Left Leg | Legwear Left |
| Right Leg | Legwear Right |
| World | World *(dedicated WORLD category)* |

---

## Installation

1. Install via Thunderstore / r2modman (dependencies will be resolved automatically).
2. Launch the game — MoreHead bridged cosmetics will appear in the vanilla cosmetics menu on first load.

---

## Configuration

Config file: `BepInEx/config/Xuaun.MoreHeadBridge.cfg`

### General

| Option | Default | Description |
|---|---|---|
| **UnlockAll** | `true` | Auto-unlock all bridge cosmetics on every game load. Set to `false` to require earning them like vanilla cosmetics. |
| **AllowMultipleCosmetics** | `true` | Allow equipping multiple cosmetics of the same type simultaneously (e.g. two hats). |
| **EnableMenuEnhancements** | `true` | Enables all extended cosmetics menu features: virtual tabs (SEARCH, SELECTED, FAV, HIDE), Ctrl+click to favorite, Alt+click to hide, live search bar, and cosmetic name tooltip on hover. Set to `false` to use the unmodified vanilla cosmetics menu. |
| **HideMoreHeadButton** | `false` | Remove the MoreHead button from all menus. Requires restart. |
| **SpecificFolders** | *(empty)* | Comma-separated subfolder names under `BepInEx/plugins` to scan for `.hhh` files. Empty = scan all. Example: `Some-MoreHeadPack,Another-Pack`. |
| **SearchFieldPosition** | `Top` | Where the search bar appears in the cosmetics menu. `Top` = above the category strip. `Bottom` = at the bottom of the Semibot. |

### Appearance

| Option | Default | Description |
|---|---|---|
| **HighlightModdedCosmetics** | `true` | Show an orange border on bridge cosmetics in the menu to distinguish them from vanilla ones. Per-cosmetic overrides (Cosmetic Customizer) take priority. |
| **DefaultRarity** | `Common` | Rarity tier assigned to bridge cosmetics. Controls sort position in the menu. Values: `Common`, `Uncommon`, `Rare`, `UltraRare`. Per-cosmetic overrides take priority. |

### Icons

| Option | Default | Description |
|---|---|---|
| **UseTextureAsPlaceholder** | `true` | Use the cosmetic's texture as the icon placeholder until a captured icon replaces it. |
| **AutoCaptureIcons** | `true` | Reactively capture icons as you hover cosmetics in the menu. Icons are saved as PNGs and reused on future visits. |
| **GenerateAllIcons** | `false` | **One-shot trigger.** When `true`, the next menu open cycles through all bridge cosmetics without a cached icon and snapshots each one. Auto-resets to `false`. Can take a few minutes. |

### Cosmetic Customizer

| Option | Default | Description |
|---|---|---|
| **EnableCosmeticOverrideUI** | `false` | Enable the Shift + click popup to override rarity, category, and modded flag per cosmetic. Requires MenuLib. |

### Compatibility

| Option | Default | Description |
|---|---|---|
| **FixBridgedCosmetics** | `true` | At load time, automatically removes `Collider` and `Rigidbody` components from bridge cosmetics (prevents the rotation bug in the preview menu) and forces animation looping. Applied directly to the prefab so every instance — including MoreHead's own rendering — inherits the fix. Set to `false` only if a specific cosmetic breaks due to these changes. |

### Reset

| Option | Default | Description |
|---|---|---|
| **ResetUnlocks** | `false` | **⚠ Destructive one-shot trigger.** Removes all bridge cosmetics from your unlocks, outfits, and history on next launch. Auto-resets to `false`. |
| **ResetCosmeticCustomizer** | `false` | **One-shot trigger.** Clears all per-cosmetic overrides and deletes `MoreHeadBridge_CosmeticOverrides.json` on next launch. Auto-resets to `false`. |
| **DeleteIconCache** | `false` | **One-shot trigger.** Deletes cached bridge icon PNGs on the next launch. Use `DeleteIconsMatching` to filter which ones. Auto-resets to `false`. |
| **DeleteIconsMatching** | *(empty)* | Optional filter for `DeleteIconCache`. Comma-separated substrings matched against icon filenames (case-insensitive). Empty = delete all. |

### Debug

| Option | Default | Description |
|---|---|---|
| **ShowBridgeDebugLogs** | `false` | Show detailed debug logs for bridge-specific events. |

---

## Icon cache location

Icons are stored outside the vanilla cosmetics cache (which REPOLib wipes on every launch):

```
%userprofile%\AppData\LocalLow\semiwork\REPO\Cache\Icons\CosmeticsModded\MoreHeadBridge_CosmeticsIcons\
```

Delete this folder manually to wipe all generated icons, or use the `DeleteIconCache` config option.

> **Upgrading from v1.0.0?** The icon cache was previously stored at `MoreHeadBridge_Icons\` in the same root. The mod migrates existing icons automatically on first launch — no manual action needed.

---

## Colorful console (optional)

If you have [BepInEx Console Extensions (BCE)](https://thunderstore.io/c/dyson-sphere-program/p/innominata/BepInEx_Console_Extensions/) installed, MoreHead Bridge will use it to print colored messages in the BepInEx console. It is a soft dependency - the mod works perfectly without it - but if you like a more lively terminal, you can install BCE manually and drop it in your plugins folder.

> *I think that colors make the console feel more human and I love it! lol*

---

## Credits

- **Xuaun** — MoreHead Bridge
- **Masaicker & YurisCat** — [MoreHead](https://thunderstore.io/c/repo/p/YMC_MHZ/MoreHead/) (original mod)
- **Maygik** - [MoreHeadUtilities](https://thunderstore.io/c/repo/p/Maygik/MoreHeadUtilities/)
- **Zehs** — [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/)
- **innominata** — [BepInEx Console Extensions](https://thunderstore.io/c/dyson-sphere-program/p/innominata/BepInEx_Console_Extensions/) (optional mod)
