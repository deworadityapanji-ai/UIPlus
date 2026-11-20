# Third-party notices

This repository contains code adapted from the projects below. Their notices are reproduced here as
required by their licenses.

---

## ChaseCamPlus

UI Plus was split out of ArcadeCam, which is derived from [ChaseCamPlus by CarloApri](https://github.com/CarloApri/NO-ChaseCamPlus).
Copyright (c) 2026 CarloApri. The full original MIT notice is preserved in LICENSE.

---

## Build dependencies

### Optional Ace Combat Style Reloads and KaceyTronic RWR interoperability

[Ace Combat Style Reloads](https://github.com/Dandy-Yak/Ace-Combat-Style-Reloads-For-Nuclear-Option),
by Dandy-Yak, and [KaceyTronic RWR](https://github.com/KcTries/KaceyTronic-RWR), by KcTries, are optional
separate mods. UI Plus's HUD styles reads the reload mod's reserve and reload-progress state, and moves,
recolours and restyles the RWR's scope and alert panel, at runtime through reflection. Both authors gave
permission for this compatibility work. Neither mod's code, DLL, assets or configuration files are bundled
or replaced; users install them separately.

### Optional WSO Yappinator interoperability

[WSO Yappinator](https://github.com/nikkorap/NO-WSO-Yappinator), by nikkorap (who also created its voice
packs), is an optional separate mod; neutralobserver maintains a fork that keeps it working on later game
versions. When it plays a voice line, UI Plus observes the playback through a Harmony postfix and shows the
matching line from the voice pack's `*_dialogue.txt` file as a subtitle. nikkorap gave permission for this
compatibility work. No code from the mod or its fork is used, and Yappinator's DLL, audio and configuration
files are not bundled, replaced or modified; users install it separately.

### Referenced build dependencies

Not redistributed — referenced at build time and expected to be present at runtime.

- [BepInEx](https://github.com/BepInEx/BepInEx) — LGPL-2.1
- [HarmonyX](https://github.com/BepInEx/HarmonyX) — MIT
- Game assemblies from Nuclear Option (Shockfront Studios), referenced for compilation only and not
  included in this repository or in any release.
