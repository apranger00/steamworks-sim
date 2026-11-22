# Steamworks Sim

A driving simulator for the 2017 FIRST Robotics Competition game, FIRST STEAMWORKS: an official-dimension field, drivable robots, game-piece physics and full match scoring, built in Unity for PC with online multiplayer planned.

| Folder | What it is |
|---|---|
| `UnityProject/` | The Unity game |
| `UnityProject/Assets/Steamworks/` | Game code (`Scripts/`), field data (`Data/`), official CAD reference (`Reference/`) |
| `UnityProject/Assets/StreamingAssets/Robots/` | Built-in robots (one folder per robot) |
| `UnityProject/Mods/Robots/` | Drop-in robot mods (also next to the built game) |
| `prototype/` | The browser prototype: the reference implementation for geometry, physics tuning and scoring rules, with its Node tests |
| `tools/` | `export_unity_data.mjs` regenerates `Assets/Steamworks/Data/*.json` from the prototype |
| `docs/` | Architecture, robot mod format |

## First-time setup (Unity 6.6 or newer)

The repo holds the game's own files. Unity generates the rest (project settings, packages, `.meta` files) the first time it opens.

1. Clone this repo.
2. In **Unity Hub**, choose **New project**, pick the **Universal 3D** template, name it anything (for example `SteamworksSim_tmp`), choose any location, and create it. Close Unity once it has opened.
3. Copy **everything** from that new project folder into this repo's `UnityProject/` folder, merging folders (`Assets/` merges with the existing `Assets/Steamworks`). You can delete the temporary project afterwards.
4. In Unity Hub, choose **Add > Add project from disk** and select `UnityProject/`. Open it.
5. In Unity, open the menu **Steamworks > Create Field in Scene**. The field appears in the Scene view.
6. Press **Play**. Right-drag to orbit, middle-drag to pan, scroll to zoom, keys **1–4** for preset views.
7. To play: **Steamworks > Set Up Drive Test**, save the scene, and press **Play** (see *Playing* below).
8. Optional: **Steamworks > Toggle CAD Reference** overlays the official field CAD (translucent blue) to compare against the generated field. **Steamworks > Robots > List Robot Mods** checks the robot files.
9. Commit the files Unity generated (`ProjectSettings/`, `Packages/`, `*.meta`). `.gitignore` already excludes `Library/`, `Temp/` and other caches.

## Playing

**Steamworks > Set Up Drive Test** adds a `Sim` object and the camera rig to the open scene (and, once the scene is saved, adds it to the build list). Press Play: the game opens on the main menu with the field behind it.

- **Singleplayer** opens robot selection (robot, alliance, start position; your robot waits on its start line). **Start match** begins at once: 15 s AUTO, then 2:15 TELEOP, both driver-controlled.
- **Multiplayer** is a placeholder until online play is built.
- **Options**: Controls (rebind every key and gamepad button, gamepad drive layout: split arcade, one-stick arcade or tank), Audio (master, match sounds), Graphics (display mode, resolution, VSync, frame rate limit, quality, field of view, drive info panel). Saved between sessions.
- The pause menu has Resume, Options, Return to robot selection, Quit to main menu and Exit game.

Default controls (rebindable):

| Action | Keyboard | Gamepad |
|---|---|---|
| Drive / turn | W S / A D (or arrows) | Split arcade: left stick Y, right stick X |
| Precision (40%) | Shift | LB |
| Next camera (chase, driver, free) | C | Y |
| Flip chase camera (behind / in front) | V | L3 |
| Reset robot to its start position | R | D-pad down |
| **Restart match** (robot to start line, clock to 2:30, go) | Enter | **Start** |
| **Pause menu** | Esc | **Select** (View / Back) |
| Back out of a menu | Esc | B |
| Practice: add / remove a parked defender | B | |
| Practice: reload robot files from disk | F5 | |

Edit a `robot.json` while playing and press **F5** (or **Reload robot files** on the robot screen) to try the change immediately. Tick **Skip Menus** on the `Sim` object to jump straight into a match while testing.

### Match sounds

The field cues (cavalry charge at the start, buzzer at the end of AUTO, steam whistle at 0:30, buzzer at 0:00) play from files in `UnityProject/Assets/StreamingAssets/Sounds/`. `sounds.json` there maps cues to files, and its `README.txt` lists the download links. A missing file plays a placeholder tone.

## Coordinate frames

All data files use the **spec frame**: inches, origin at field centre on the carpet, +X toward the blue alliance wall, +Y toward the scoring table, +Z up. Unity converts with `Units.ToUnity(x, y, z) = (x, z, y) × 0.0254`, so in Unity +x points to the blue wall, +z to the scoring table and +y up.

## Status

- [x] Step 1: field built in Unity from the prototype's data, with static colliders and a CAD overlay
- [x] Step 2: drivable robot from a robot mod file, tank drive, chase / driver / free cameras, gamepad
- [x] Step 2.5: main menu, robot selection, options (rebinding, audio, graphics), pause menu, match clock with field sounds
- [ ] Step 3: FUEL and GEARS on PhysX, goal sensors, shooter, gear placement, climbing, match timing and scoring (port of `prototype/web/game.js` with its tests)
- [ ] Step 4: online multiplayer (Netcode for GameObjects + Multiplayer Services), HUD, sounds

## Prototype tests

```
cd prototype/tests
node game.mjs    # 47 timing and scoring checks
node phys.mjs    # drivetrain and collision checks
node rope.mjs    # rope simulation checks
```

Field CAD in `Reference/FieldCAD.obj` is decimated from FIRST's official 2017 field model.
