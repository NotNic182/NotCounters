# NotCounters

HUD mod for Beat Saber 1.44.1 and [1.40.8](https://github.com/NotNic182/NotCounters/releases/tag/BS-1.40.8-v1.2.0). You can move, resize and restyle the vanilla HUD and add your own counters (accuracy,
misses, PB, notes left, BeatLeader/ScoreSaber pp and a bunch more). Everything is set up in a live editor that runs in
the actual game environment, so you see exactly what it looks like in play.

<img width="2560" height="2560" alt="Untitled-1" src="https://github.com/user-attachments/assets/18302a84-7897-4c84-a94d-3eb9eba7068e" />

<img width="2560" height="1440" alt="vlcsnap-2026-11-02-13h38m01s704" src="https://github.com/user-attachments/assets/b24ebed2-c086-4029-8234-40e82933835c" />

<img width="2560" height="1440" alt="vlcsnap-2026-11-02-13h38m19s753" src="https://github.com/user-attachments/assets/8028123c-85f1-4644-877f-57ae1d28f0eb" />


## Install

Needs BSIPA, BSML and SiraUtil.

Not compatible with Counters+, so remove that first if you have it.

Drop `NotCounters.dll` in your `Plugins` folder, then hit **NotCounters** in the MODS panel in the main menu.

Settings are saved in `UserData/NotCounters/`.

## Using it

- Grab an element's frame and drag it (trigger, or left mouse in fpfc)
- While dragging, stick up/down pushes it away or closer and left/right scales it. On desktop that's the mouse wheel,
  shift + wheel to scale and alt + wheel to rotate
- **Add** on the Elements tab adds a counter, the Settings tab has its options
- The right panel has environments, profiles and options
- **Save & Exit** when you're done (or Esc on desktop)

## Building

Needs the .NET SDK and a Beat Saber 1.44.1 install with the mods above.

```
dotnet build NotCounters.sln -c Release
```

It looks for the game two folders up from the repo (I keep it in `<game>/ModSource/NotCounters`). Otherwise set
`BEAT_SABER_DIR` to your game folder. Add `-p:InstallToGame=true` to copy the dll into Plugins after the build.

Not affiliated with BeatLeader, ScoreSaber or Beat Games.
