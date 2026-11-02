# NotCounters

HUD mod for Beat Saber 1.44.1. You can move, resize and restyle the vanilla HUD and add your own counters (accuracy,
misses, PB, notes left, BeatLeader/ScoreSaber pp and a bunch more). Everything is set up in a live editor that runs in
the actual game environment, so you see exactly what it looks like in play.

## Install

Needs BSIPA, BSML and SiraUtil.

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
