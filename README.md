<p align="center"><img src="images/icon.png" width="160" alt="Keybound Brawler Helper"></p>

# Keybound Brawler Helper

Dalamud plugin for the **Keybound Brawler** Gold Saucer event (FFXIV × FFVII Remake).

> ⚠️ Auto-play is against the FFXIV Terms of Service. Use at your own risk.

## Install

1. In game: `/xlsettings` → **Experimental** → **Custom Plugin Repositories**
2. Add this URL, tick it, and save:

   ```
   https://raw.githubusercontent.com/KEWI-hub/KeyboundBrawler/main/repo.json
   ```

3. `/xlplugins` → search **Keybound Brawler Helper** → Install
4. `/kbb` opens the window, `/kbb auto` toggles auto-play

Chat and commands are blocked inside the minigame, so set things up before entering.
Tick **Start auto-play when a match begins** and it starts and stops by itself.

## What auto-play does

- Reads enemy words from the `KTGEWord` addon and types them, about 62 WPM by default
- Opens the input box with Enter before each word (the box closes after every submit)
- Types red / gold casting labels first (`Needles`, `Burst`)
- Stops pressing keys while a no-typing cast is up, if listed (e.g. `Cursed Gaze, Sensory Jammer`)
- Remembers boss words before they get obscured
- Keeps the duty's inactivity timer reset, so you are not expelled for "extended inactivity"

## Tracker

Counts runs and MGC from chat, and shows how many runs you still need for the rewards you tick.

## Build

```
dotnet build -c Release
```

Needs the .NET 10 SDK and XIVLauncher with Dalamud installed. Output: `KeyboundBrawler/bin/x64/Release/KeyboundBrawler/latest.zip`.
