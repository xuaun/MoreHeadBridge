# MoreHead Bridge

Translates `.hhh` cosmetics (from [MoreHead](https://thunderstore.io/c/repo/p/Mhz/REPOMoreHead/)) into the vanilla R.E.P.O. cosmetics system, so they appear alongside your vanilla cosmetics in the game.

---

## What does it do?

- Scans `BepInEx/plugins` for `.hhh` files and registers them as vanilla cosmetics
- Cosmetics show up in the vanilla shop with rarity tiers, icons, and unlock tracking
- Auto-unlocks all bridge cosmetics on game start (configurable)
- Optional: hide the MoreHead button if you prefer to use only the vanilla UI

---

## How icons work

Since `.hhh` cosmetics don't have built-in icons, the mod generates one for each cosmetic using the following fallback chain:

1. **Placeholder** — a generic icon is generated so the cosmetic slot is never blank in the menu
2. **Texture overlay** — if the cosmetic has a texture, it is applied on top of the placeholder as a preview (can be disabled via `UseTextureAsPlaceholder`)
3. **Captured icon** — once you hover the cosmetic in the menu, the mod snapshots the in-game avatar preview and saves a PNG icon, which replaces the placeholder permanently

> **Tip for best results:** Before generating icons, remove all cosmetics from your Semibot (vanilla and MoreHead), set it to a neutral color, and make sure it is facing forward. This gives each icon the cleanest background possible. If an icon doesn't look right, you can delete it individually using `DeleteIconsMatching` in the config, or wipe all icons at once with `DeleteIconCache` (or in the icon cache locacion - *see below*).

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
| World | *(not included — see below)* |

---

## Known limitations

- **World cosmetics are not supported.** The vanilla R.E.P.O. cosmetics system currently has no slot equivalent to the `world` tag, so `.hhh` files using that tag are skipped and will not appear in the vanilla menu.

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
| **HideMoreHeadButton** | `false` | Remove the MoreHead button from all menus. Useful if you want to use only the vanilla cosmetics UI. Requires restart. |
| **DefaultRarity** | `Common` | Rarity tier assigned to bridge cosmetics in the vanilla shop. Values: `Common`, `Uncommon`, `Rare`, `UltraRare`. |
| **SpecificFolders** | *(empty)* | Comma-separated subfolder names under `BepInEx/plugins` to scan for `.hhh` files. Empty = scan all folders. Example: `Some-MoreHeadPack,Another-Pack`. |

### Icons

| Option | Default | Description |
|---|---|---|
| **UseTextureAsPlaceholder** | `true` | When `true`, uses the cosmetic's texture as the icon, overlaid on the placeholder. When `false`, shows the plain placeholder until a captured icon replaces it. |
| **AutoCaptureIcons** | `true` | Reactively capture icons as you hover cosmetics in the menu. Icons are saved as PNGs and loaded instantly on future visits. |
| **GenerateAllIcons** | `false` | **One-shot trigger.** When `true`, the next menu open will cycle through all bridge cosmetics without a cached icon and snapshot each one. Auto-resets to `false`. It can take some minutes to finish. |
| **DeleteIconCache** | `false` | **One-shot trigger.** When `true` on the next launch, deletes cached bridge icon PNGs. Use `DeleteIconsMatching` to filter which ones. Auto-resets to `false`. |
| **DeleteIconsMatching** | *(empty)* | Optional filter for `DeleteIconCache`. Comma-separated substrings matched against icon filenames (case-insensitive). Empty = delete all. |

### Reset

| Option | Default | Description |
|---|---|---|
| **ResetUnlocks** | `false` | **⚠ Destructive one-shot trigger.** When `true`, removes all bridge cosmetics from your unlocks, outfits, and history on next launch. Auto-resets to `false`. |

### Debug

| Option | Default | Description |
|---|---|---|
| **ShowBridgeDebugLogs** | `false` | When `true`, does NOT suppress NullReferenceExceptions for bridge cosmetics. Use to diagnose bridge-specific issues. |

---

## Icon cache location

Icons are stored outside the vanilla cosmetics cache (which REPOLib wipes on every launch):

```
%userprofile%\AppData\LocalLow\semiwork\REPO\MoreHeadBridge_Icons\
```

Delete this folder manually to wipe all generated icons, or use the `DeleteIconCache` config option.

---

## Colorful console (optional)

If you have [BepInEx Console Extensions (BCE)](https://thunderstore.io/c/dyson-sphere-program/p/innominata/BepInEx_Console_Extensions/) installed, MoreHead Bridge will use it to print colored messages in the BepInEx console. It is a soft dependency - the mod works perfectly without it - but if you like a more lively terminal, you can install BCE manually and drop it in your plugins folder.

> *I think that colors make the console feel more human and I love it! lol*

---

## Credits

- **Xuaun** — MoreHead Bridge
- **Masaicker & YurisCat** — [MoreHead](https://thunderstore.io/c/repo/p/Mhz/REPOMoreHead/) (original mod)
- **Zehs** — [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/)
- **innominata** — [BepInEx Console Extensions](https://thunderstore.io/c/dyson-sphere-program/p/innominata/BepInEx_Console_Extensions/) (optional)
