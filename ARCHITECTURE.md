# MoreHeadBridge — Architecture

Translates `.hhh` cosmetics (MoreHead format) into the vanilla R.E.P.O. cosmetics system via
REPOLib, then layers a customizer, per-cosmetic colours, world cosmetics and the Mini-Semibot on
top. Flat namespace (`MoreHeadBridge`), one type per file.

## Lifecycle (Plugin.Awake, order matters)

1. `BridgePaths.Init` → `BindConfig` → MenuLib detection.
2. `CustomizerStore.Load()` + one-shot config triggers (Reset / Import / Export).
3. `HhhCosmeticLoader.LoadAll()` — scans `BepInEx/plugins/**/*.hhh`, registers each prefab through
   REPOLib (`assetId = "morehead-bridge:" + name`, see `BridgeIds`).
4. `MiniSemibotCosmetic.Register()` — synthetic WORLD cosmetic, joins the normal pipeline.
5. `PerCosmeticColors.Load()`, icon-cache cleanup, **`BridgePatcher.ApplyAll()`**, config handlers.
6. Reflection-based compat patches via `TryApply` (targets may be absent: REPOLib internals,
   MoreHeadUtilities, MenuLib, CustomGrabColor).

`BridgePatcher` applies each `[HarmonyPatch]` class individually (a game update that removes one
member disables one feature, not the mod); the cosmetics-menu takeover cluster fails as a unit.

## Subsystems (by folder)

| Folder                                                                                  | Owns                                                                                                                                                        |
| --------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Patches/`                                                                              | Mounting (`MoreHeadCosmeticMountPatch` — the core InstantiateCosmetic postfix), unlocks, REPOLib RPC-order fixes, NPE guards                                |
| `Menus/`                                                                                | Menu takeover (virtual tabs SEARCH/SELECTED/FAV/HIDE, search, tools), the Cosmetic Customizer popups, `CustomizerStore` (per-cosmetic override persistence) |
| `FavHide/`                                                                              | Favourites & hidden cosmetics (markers, toggle patch, icons, persistence)                                                                                   |
| `PerCosmeticColors/`                                                                    | Per-cosmetic/per-slot palette + custom RGB + colour animations, their sync                                                                                  |
| `Sync/`                                                                                 | `BridgeNetMux` (the wire), `CustomizerSync`, `MoreHeadHideSync`, room callbacks/purges                                                                      |
| `World/`                                                                                | WORLD-tab cosmetics (follower, spawn patch) and `MiniSemibot/` (the mini avatar)                                                                            |
| `Tint/`                                                                                 | `BridgeTintMaterial` injection (bridge tinting across shaders), vanilla/modded custom RGB                                                                   |
| `CosmeticEquip/`, `Sway/`, `Conditions/`, `Icons/`, `PartShrinker/`, `Menus/DeathHead/` | Multi-equip flags, jiggle springs, hide/offset conditions, icon capture, MoreHeadUtilities bridge, death-head mounting                                      |

## Networking

Everything rides **`BridgeNetMux`**: one fixed Photon event (code 187) with envelope
`["MHB1", channel, body]`, routed by channel NAME. Stateful channels travel in a single buffered
room-cache **snapshot** per sender (late joiners get one atomic event); `MimicAudio` is transient.

The **channel table** (`BridgeNetMux.Channels`) is the single registration point — one row wires a
channel's build/dispatch/purge. Wire contract for a section body:

- **absent** (`Build` returned `null`) → remotes keep their last state (used by the browse gate);
- **`""`** → explicit clear of the remote cache (feature toggled off);
- anything else → payload.

Every inbound payload is untrusted: size caps, `Enum.IsDefined` checks and numeric clamps live in
`BridgeSyncPayload.ClampValues` / `MiniSemibotSyncPayload.ClampValues`. Per-actor caches are purged
on `OnPlayerLeftRoom` / `OnDisconnected` (actor numbers are recycled).

Equip sync itself is vanilla + REPOLib RPCs; `REPOLibRpcOrderPatch` and
`SetupCosmeticsModdedRpcPatch` only fix their ordering.

## Multiplayer invariants (the load-bearing rules)

1. **Owner-authoritative, no local fallback.** A remote avatar renders from the owner's broadcast
   payload or the asset's pre-override defaults — never the viewer's local stores.
2. **Never read the shared `CosmeticAsset` on a remote path.** `asset.type` / `asset.tintable` /
   `HhhCosmeticLoader.WorldAssetIds` may carry the _viewer's_ overrides. The mount pipeline resolves
   type/world/tintable once into `ResolvedOverride`; world membership elsewhere goes through
   `MoreHeadCosmeticMountPatch.IsWorldFor(asset, remoteActor)`.
3. **Identity goes through `AvatarIdentity`.** `AvatarIdentity.Of(pc)` classifies
   Local / Menu / RemoteMini / Remote (order matters — a remote mini is a local menu-avatar clone
   with no reliable PhotonView). Use `IsLocalStyleTarget` before dressing/colouring from local stores.
4. **No Photon access at plugin Awake.** Subscribing at load breaks lobby hosting; the mux hooks in
   at `RunManager.Awake` (`BridgeNetMuxSubscribePatch`), mirroring REPOLib.
5. **WORLD cosmetics are registered as `CosmeticType.Hat`** (keeps type-indexed structures
   in-bounds). `WorldCosmeticsSetupPatch` filters them out of the equip list and spawns them
   separately; never trust `asset.type == Hat` to mean "hat".

## Persistence (`BepInEx/config/MoreHeadBridge/` via `BridgePaths.Of`, atomic writes)

- `CosmeticOverrides.json` — `CustomizerStore` (per-cosmetic customizer settings). The sync subset
  is defined solely by `BridgeSyncPayload` (`FromOverrideData` / `ToOverrideData` / `ClampValues` —
  see the `ADD-OVERRIDE-FIELD` anchors).
- `PerCosmeticColors.json` — unified v2 colour store (all six maps, one `Save()`); v1 sibling files
  are migrated on first load. `PresetColors.json` stays separate.
- `BridgeBlacklist`, favourites/hidden, `AutoUnlockedModded.json`.
- Icon PNG cache lives outside config: `AppData/.../Cache/Icons/CosmeticsModded/`.

`PerCosmeticColors` stores `-1` (`OriginalColorSentinel`) inside the palette-index map meaning
"restore original" — never feed a stored index to `MetaManager.colors` without checking it.

## Logging

`BridgeLog` carries the rule in the name: `User*` (player-facing, coloured BCE console when
installed), `Debug` (gated by `ShowBridgeDebugLogs`), `Trace` (BepInEx logger only).
`BceConsole.*` is the implementation behind `User*` and appears directly in older call sites.

## Extension recipes

- **New sync channel** → add the name const + one row in `BridgeNetMux.Channels`. Honour the
  absent/`""` contract in your `Build` method.
- **New customizer field that must sync** → follow the `ADD-OVERRIDE-FIELD` anchors
  (`BridgeSyncPayload` + `MoreHeadCosmeticMountPatch.Resolve`).
- **New patch on a game method** → plain `[HarmonyPatch]` class (BridgePatcher picks it up); use
  `TryApply` only when the target type may not exist. Name injected parameters after the vanilla
  parameter names (see the decompiled sources), not `__0`.
