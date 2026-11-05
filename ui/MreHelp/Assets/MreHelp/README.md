# MRE /help — Unity source

Standalone Unity project for a modal in-game MRE command guide. Unity **2022.3.62f3**, uGUI package **1.0.0**, Windows Standalone asset bundle, 1920×1080 reference canvas. Typography uses bundled PT Sans under OFL.

The generated effect contract is ID **48625**, GUID **6e4a31b49c874f428a622e1a68e4d203**, key **14825**, prefab address **Effect**. Close button: `HelpClose`. Category buttons: `HelpTabBasics`, `HelpTabTravel`, `HelpTabGroups`, `HelpTabCombat`; selection indicators: `HelpSelectedBasics`, `HelpSelectedTravel`, `HelpSelectedGroups`, `HelpSelectedCombat`. Title/subtitle: `HelpTitle`, `HelpSubtitle`. Ten command and description text elements: `HelpCommand01..10`, `HelpDescription01..10`; row visibility: `HelpRow01..10`.

To build using the matching local editor:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-MreHelpAssets.ps1
```

Output stays under `ui/MreHelp/Build/Windows/MreHelp/`. The build creates `MreHelp.unity3d`, `MreHelp.dat`, and `effect-manifest.json`, then reloads the bundle and verifies the prefab and UI element names. `-RenderPreview` additionally writes `Build/MreHelp-preview.png` from Unity. The current committed visual preview is `ui/preview/mre-help.png`. The bundle is not installed, packed into Workshop, or published by this script.

The prefab has sample command text for the Unity preview. At runtime the server fills each category from the registered commands and the player's permissions, so unavailable commands are omitted. A client must receive the matching effect asset (for example via the existing MRE Workshop item) before the server can open it. Update an existing local Workshop package and test a clean client and server before publishing. Do not put this asset in the shared root `Bundles` directory: MRE UI assets use an isolated Workshop package to avoid GUID collisions.

The preview and bundle build do not prove in-game click delivery, resolution scaling, keyboard close behavior, platform support beyond Windows, or Workshop distribution; these need client acceptance.
