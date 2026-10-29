<p align="center"><img src="images/icon.png" width="160" alt="Keybound Brawler Helper"></p>

# Keybound Brawler Helper

[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-Buy%20me%20a%20coffee-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/kewiv)

Dalamud plugin for the **Keybound Brawler** Gold Saucer event (FFXIV × FFVII Remake).

> ⚠️ Auto-play is against the FFXIV Terms of Service. Use at your own risk.

## Install

1. In game: `/xlsettings` → **Experimental** → **Custom Plugin Repositories**
2. Add this URL, tick it, and save:

   ```
   https://raw.githubusercontent.com/KEWI-hub/KeyboundBrawler/main/repo.json
   ```

3. `/xlplugins` → search **Keybound Brawler Helper** → Install
4. `/kbb` opens the window, `/kbb auto` toggles auto-play, `/kbb debug` shows the UI scanner

Chat and commands are blocked inside the minigame, so set things up before entering.
Tick **Start auto-play when a match begins** and it starts and stops by itself.

## What auto-play does

- Reads enemy words from the `KTGEWord` addon and types them
- Speed presets: Human 60, DEV 100, Monkey 120, KEWI 180 WPM
- Opens the input box with Enter before each word (the box closes after every submit)
- Never types while chat or another text box has focus
- Priority by label colour: green barrier phrases, then red, then gold casters, then the closest enemy
- Cursed Gaze / Dynamic Sensory Jammer (the crossed-out keyboard sign): keeps typing while the cast bar fills, then holds every key while it lands
- Finishes words the boss hides with its noise overlay, and remembers words before they get obscured
- Optional: skip the punctuation the game does not require ( ? ! - ' )
- Keeps the duty's inactivity timers, and the minigame's own idle timer, at zero while it types, so you are not expelled for "extended inactivity"
- **Word log**: every word it sent this session, with hit, missed or dropped, and how many typos it had to fix

## Play rounds

Plays a set number of rounds back to back: walks to the Keybound Registrar with
[vnavmesh](https://github.com/awgil/ffxiv_navmesh), joins, presses Commence, types the whole match, closes the
result screen, and goes again.

Be in the Gold Saucer event area, set the number of rounds, and press **Play N rounds**. Nothing to set up:
the Registrar and the MGC Trader are built in.

## Tracker and rewards

- Counts runs and MGC from chat, and shows how many runs you still need for the rewards you tick
- Syncs your real MGC and the rewards you already hold whenever the MGC Trader shop is open
- **Walk to vendor** uses [vnavmesh](https://github.com/awgil/ffxiv_navmesh) to walk to the MGC Trader «Prize Claim» and open the shop
- **Buy wanted rewards when the shop opens** buys the ticked rewards, cheapest first, while you have the MGC

## Build

```
dotnet build -c Release
```

Needs the .NET 10 SDK and XIVLauncher with Dalamud installed. Output: `KeyboundBrawler/bin/x64/Release/KeyboundBrawler/latest.zip`.

## Release

1. Test as a local dev plugin first (`/xlsettings` → Experimental → Dev Plugin Locations → the build output folder).
2. Only after it works in game: `tools/release.sh <version> testing|stable "notes"`.
   It bumps the csproj and `repo.json`, builds, commits, pushes and uploads the zip.
3. Check that `https://raw.githubusercontent.com/KEWI-hub/KeyboundBrawler/main/repo.json` shows the new version (raw cache is about 5 minutes).

- Stable zip: `releases/latest/download/latest.zip`. Testing zip: the `testing` prerelease.
- Native calls (hooks, addon callbacks) always go through a dev or testing build first: a wrong `ReceiveEvent` once crashed the game (0.6.1).
