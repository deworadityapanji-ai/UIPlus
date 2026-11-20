# UI Plus

HUD mod for Nuclear Option, split out of ArcadeCam on 2026-11-19. It holds everything drawn on screen:

- the flight HUD in chase view, and where each instrument group sits (aircraft, camera or hidden)
- the HUD styles (Native, Arcade, Wingman, Dashboard, Thunder) and the layout editor
- the targeting screen, weapon range, weapon pop-up and Wingman star weapon wheel
- callouts, the kill feed, mission banners, subtitles and minimap zoom
- the Ace Combat Style Reloads, KaceyTronic RWR, WSO Yappinator and fuel-overlay integrations

It works on its own with the game's chase camera. With ArcadeCam installed it also hides the
targeting screen during ArcadeCam's weapon camera and shows ArcadeCam's HIT / MISS / DESTROYED
callouts; the link is made at runtime (`Helpers/ArcadeCamBridge.cs` reads `ArcadeCam.ArcadeCamApi`),
so neither mod needs the other.

On its first run UI Plus copies its settings from `ArcadeCam.cfg` into `UIPlus.cfg`
(`Helpers/ConfigMigration.cs`). ArcadeCam.cfg is only read.

Build, test and install (game closed):

    scripts/build.ps1
    dotnet run --project tests/UIPlus.Tests
    scripts/install-mod.ps1
