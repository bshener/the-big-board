# Phone button pattern

Top-bar icons are NOT AssetBundles — mods clone a vanilla button at runtime:

1. Poll `FindObjectOfType<SmartphoneUI>()` on the main thread until
   the phone UI exists (skeleton polls in `Update`, every 30 frames).
2. Read its private `appButtonTemplate` Transform via reflection and
   `Instantiate(template.gameObject, template.parent, false)` — that is the
   strip template, not a visible button. Fallback: clone a
   `FullMenuAppButton` from `UI.Smartphone.FullMenu` (expanded grid).
3. Rename clone, `AppButton.SetIcon(runtimeSprite)` — icon is a `Texture2D` →
   `Sprite.Create` drawn in code, no art assets.
4. Title via `TextLocalizationComponent.Key = "<mod>_app_title"`, key defined
   in the mod's `Locales/*.json` (follows game language).
5. `Button.onClick`: `RemoveAllListeners` + own listener.
   `FullMenuAppButton.HideSelectedIcon()`. Reinstall if Unity destroys the
   clone on scene change; guard against duplicates by child-name lookup.

Reference: `src/TheBigBoard/PhoneButtonInstaller.cs` (teal tile + "B").

> Overflow: the row is centred for the 7 vanilla apps; extra buttons grow it
> under the money/date and off-screen. Last slot is clipped, so the skeleton
> takes the FIRST slot. Proper fix later: a pager (never add to
> `SmartphoneUI.appButtons` — gamepad casts break).
