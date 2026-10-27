# ModAPI notes (official API, verified 2026-10-25)

Sources: the official `BigAmbitions.ModAPI.dll` and
`BigAmbitions.ModsInternal.dll` in the game install. Re-verify with
`monodis --typedef` after game updates.

## Entry contract

```csharp
interface IModBigAmbitions {
    string[] RelativeAssetBundlePaths { get; }
    Task OnLoadAsync(ModContext context);
    Task OnUnloadAsync();
}
abstract class ModBigAmbitionsBase : IModBigAmbitions { ... }
// Base class is OPTIONAL: a mod may implement IModBigAmbitions directly.
```

`ModContext`: `ModRootPath`, `ModId`, `IModLogger Logger`
(`Info`/`Warn`/`Error` → `Player.log` + `AP_Mods/<Name>/<Name>.log`).

## Attributes

```csharp
[assembly: RegisterModClass(typeof(MyMod))]  // REQUIRED, else:
  // [ModDiscovery] No RegisterModClassAttribute attributes were found in 'X.dll'
[ModEntryOnInitializationLoad]  // earliest entry point
[ModEntryOnCityLoad]
[ModEntryMainMenu]
[ModEntryOnIntroLoad]
[ModEntryOnBlueprintCreatorLoad]
```

`RelativeAssetBundlePaths` → `Array.Empty<string>()` unless shipping bundles.
`AssetService`: `LoadBundleAsync` / `LoadAssetAsync<T>` / `SpawnAsync`.

## Settings UI (`BigAmbitions.Mods`)

`OptionsService.Register(modId, ModOptions)` with
`ToggleOption` / `SliderOption` / `DropdownOption` / `ButtonOption` /
`HeaderOption` / `SplitterOption`. Locale keys resolve from the mod's
`Locales/*.json`. Exact `OptionsService.Register` key mapping still open.

## Loader internals (`BigAmbitions.ModsInternal`)

`ModDiscoveryRegistry` (scan), `ModLifecycleLoader` (scoped load/unload:
`LoadScopeAsync`, `UnloadChangedModEntriesAsync`, `FileSystemWatcher` on
`ModsLocal`), `ModThumbnailLoader` (Workshop `thumbnailUrl`),
`steam_mod_manifest.txt` governs Workshop mods; local mods need no manifest.

## Workshop item preview (verified 2026-10-27 via IL)

The item's main preview can NOT be changed from the Steam website:
`managepreviews` only uploads gallery images / reorders / deletes
(no set-preview call in `sharedfiles_previews_functions.js`). The preview
is set only by the in-game uploader (`UploadModPanel` →
`SteamModUploader.{Upload,Update}ExistingModOnWorkshop` →
`Editor.WithPreviewFile`):

- Picking a mod folder in the upload panel (`OnModFolderSelected`) scans
  the folder root for the FIRST `.png`/`.jpg`/`.jpeg` within the size
  limit and uses it as preview (`_selectedThumbnailPath` →
  `ModInfo.thumbnailUrl` → `WithPreviewFile`).
- The thumbnail is moved OUT of the folder during upload and restored
after (`MoveThumbnailOutsideModFolder`), so it never ships as content.
- To change a live preview: put the new cover (only image) in
  `ModsLocal/<mod>/`, pick the folder in the upload panel, Update.
  Subscribed-copy updates never touch the preview (`thumbnailUrl` is the
  remote URL there, so `WithPreviewFile` is skipped).

Skeleton rules: copy live objects, untag clones, rewire clicks, never touch
`appButtons`.
