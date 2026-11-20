# UI Plus

**A HUD overhaul for Nuclear Option: four HUD styles, and an in-game editor that makes almost everything on screen yours to move, resize, restyle or hide.**

> **Beta.** UI Plus is being tested before it goes on NOMM (planned for December). Bug reports, screenshots and layout ideas are very welcome: [open an issue](https://github.com/deworadityapanji-ai/UIPlus/issues).

[**Download the latest release**](https://github.com/deworadityapanji-ai/UIPlus/releases)

---

## HUD styles

| Style | Inspired by | In short |
|---|---|---|
| **Arcade** | Ace Combat | Clean corners, speed and altitude boxes, weapon list beside the aircraft silhouette, missile arcs |
| **Wingman** | Project Wingman | Slanted readouts, the HULL throttle and fuel bar, the weapon star |
| **Dashboard** | Cockpit instrument panels | Every instrument in one strip along the bottom, made to glance at |
| **Thunder** | War Thunder | A dense text readout, faction score bar, map in the top-right corner |
| **Native** | The game itself | The game's own HUD, with your positions |

## The editor

Open it from the **UI PLUS** tab in the game's settings (or from Configuration Manager), then:

- **Move** anything by dragging it, or select it and use the arrows. Hold **Shift** and drag to move the selected element from anywhere on screen.
- **Resize and rotate** with the square and ↻ handles on the selected element.
- **Show or hide** any element from the list.
- **Skins:** many elements have alternative looks. For example, the weapon list (Arcade list or Wingman cluster), speed and altitude boxes, map backing, damage silhouette, targeting screen, RWR alerts and kill feed.
- **Extras:** add elements from another style. For example, Arcade's fuel tapes in Wingman, Thunder's data block in Arcade, or the Wingman star on its own.

Every style keeps its own saved layout. **Reset** puts the style you're editing back to its defaults, and **Reset element** does the same for one element.

## Also included

- The HUD in **chase, cockpit and orbit** views (each can be switched off).
- **Targeting camera feed** on the HUD.
- **Kill feed**, mission banners and subtitles.
- **Minimap zoom** (bind "UI Plus Minimap Zoom" in Flight Controls).
- **Hide HUD** hotkey (bind "UI Plus Hide HUD"): one press hides the whole HUD while flying, for screenshots and clean views.
- **Missile arcs:** Ace Combat-style red arcs around your aircraft showing where incoming missiles come from, growing as they close in. Hidden in player-versus-player games.
- **High contrast:** a soft dark halo behind HUD text keeps it readable over bright sky (Off / Soft / Strong).
- **Fix for a game bug:** the target arrow pointed the wrong way when your target was behind you.

## Installation

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) for Nuclear Option, if you haven't already.
2. Download `UIPlus-vX.Y.Z-beta.zip` from the [releases page](https://github.com/deworadityapanji-ai/UIPlus/releases).
3. Extract the `UIPlus` folder into `Nuclear Option/BepInEx/plugins/`.
4. Start the game, open **Settings → UI PLUS**, and pick a HUD style.

To update, replace the `UIPlus` folder. Your settings and layouts are kept (they live in `BepInEx/config/UIPlus.cfg`).

## Works with other mods

UI Plus needs none of these, but it works with each of them when installed. The **Mod compatibility** box in the UI PLUS tab shows which ones it found and whether each is working, with a switch to turn each integration off.

| Mod | What UI Plus does with it |
|---|---|
| **ArcadeCam** | HIT / MISS / DESTROYED callouts; the targeting screen hides during ArcadeCam's weapon camera |
| **KaceyTronic RWR** | The scope and its alerts follow the HUD style, and both can be moved in the editor |
| **Ace Combat Style Reloads** | Weapon reserves and reload progress on the HUD |
| **NO Missile Indicators** | Its style follows the HUD style (Project Wingman's 3D indicator in Wingman, arrows elsewhere), and its arrows step aside where the missile arcs show |
| **WSO Yappinator** | Voice lines as subtitles ([see below](#voice-line-subtitles-wso-yappinator)) |
| **NeutralQoL**, **NOAutopilot** | Their fuel overlays step aside while the Thunder style shows its own |

## Voice line subtitles (WSO Yappinator)

UI Plus can show what the WSO says as subtitles, but it needs to know the words. Each voice pack needs a dialogue file listing its lines.

1. Open the voice pack's folder: `BepInEx/plugins/WSOYappinator/audio/<pack name>/` (where its `.wav` files are).
2. Create a text file there whose name ends in `_dialogue.txt`, for example `mypack_dialogue.txt`.
3. Add one line per sound file:

```
<sound file name>, <Speaker>: <what is said> [<mm:ss>]
```

For example:

```
missile_1.wav, Viper: Fox two, fox two! [00:02]
missile_2.wav, Viper: Missile away. [00:02]
kill_1.wav, Viper: Splash one! [00:01]
spawn_1.wav, Viper: All systems green, let's go.
```

- **Sound file name:** exactly as the file is named in the folder.
- **Speaker** (optional): shown above the subtitle. Leave out `Speaker:` to show the line alone.
- **Duration** (optional): how long the subtitle stays, in minutes and seconds. Without it, the time is worked out from the line's length.

Restart the game after adding or changing a dialogue file. The game's log (`BepInEx/LogOutput.log`) says how many lines UI Plus loaded.

## Credits

- Split out of [ArcadeCam](https://github.com/deworadityapanji-ai/ArcadeCam), itself a fork of [ChaseCamPlus by CarloApri](https://github.com/CarloApri/NO-ChaseCamPlus). The original MIT licence is kept.
- Integrations made with the permission of the authors of Ace Combat Style Reloads (Dandy-Yak), KaceyTronic RWR (KcTries) and WSO Yappinator (nikkorap). Full details in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Made with substantial AI assistance.

UI Plus is a community mod, not affiliated with Shockfront Studios. The style names refer to the games that inspired them; this is not an official tie-in with any of them.

## Building from source

Requires the .NET SDK and a Nuclear Option install (path in `UIPlus/GameDir.targets`; override it in a `GameDir.local.targets` next to it). With the game closed:

```powershell
scripts/build.ps1
dotnet run --project tests/UIPlus.Tests
scripts/install-mod.ps1
```

## License

MIT. See [LICENSE](LICENSE).
