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

- **Singleplayer** opens robot selection (robot, alliance, start position; your robot waits on its start line). **Start match** runs a 3-2-1 countdown, 15 s AUTO, a 3 s pause (timer 0:03 to 0:00), then 2:15 TELEOP; robots are disabled during the countdown and the pause. Both periods are driver-controlled.
- At 0:00 the **final score** slides in over the field: totals, AUTO / TELEOP breakdown (mobility, fuel, rotors, takeoff, penalty), robots on each alliance, ranking points and the winner. Everything scores: AUTO mobility, FUEL (boiler counts 5 per second per goal; high 1 kPa per ball in AUTO, 1 per 3 in TELEOP; low a third of that), GEARS (PILOTS lift, install and crank: ROTOR 1 needs 1 GEAR, then 2, 4, 6), and takeoff (TOUCHPAD held at least 1 s at 0:00). Playoff scoring: 40 kPa earns a 20-point BONUS and all four ROTORS a 100-point BONUS (no ranking points). The ROPES drop with 30 s left. **HOPPERS**: drive a bumper against a push panel in the guardrail and both containers next to it pour their 100 FUEL onto the field (once per match). The boiler net sits tight behind the high goal; FUEL that still ends up behind or on top of a boiler rolls back out beside the low goal. The only messages during a match are a ROTOR starting and an alliance reaching 40 kPa.

**Loading stations**: your return loading station is in the corner at the *other* end of the field (red's is at the blue end), with your retrieval zone taped in front of it. With any part of your bumpers in the zone, press F / L1 for FUEL or G / R1 for a GEAR and the chute nearest your robot drops it: 20 FUEL at a time (or whatever is left), one GEAR at a time. Each station starts with its loading lane (20 FUEL, 18 GEARS) plus the preloads its alliance's robots didn't take (10 FUEL and 1 GEAR for each of the three teams); in single player that is 40 FUEL and 20 GEARS. GEARS are never restocked. FUEL is: every FUEL the red boiler counts goes to the blue station, and the other way round. Pieces nobody catches land on the field. The loading lane behind the station shows what's left: a GEAR disappears from the stacks for each one delivered, and FUEL waits in boxes of 50 (the last box only partly full; up to six boxes are drawn).

A GEAR goes straight into a robot only if the robot can take one, faces the station, has its front within 8 in of the chute, and covers at least half the chute's width (left to right) with its front. Otherwise, or if the robot already has a GEAR, it drops onto the carpet. These rules are the same for every robot.

The default robot (Rookie Bot, folder `sim-6wd`) starts with 10 FUEL and 1 GEAR and holds 50 FUEL and 1 GEAR. Its top is an open hopper with clear walls: FUEL falling onto it (from a chute or a field hopper) drops in, its front intake picks FUEL up off the carpet while you hold Intake, and you can see how full it is by the loose pile of FUEL inside. The hopper is sized so 50 FUEL fill it to the rim; once it's full, more FUEL rolls off the top of the pile and over the sides. The turret sits in the back right corner. It can't pick GEARS up off the floor. The drive info panel shows whether the shooter is in range. **Steamworks > Run Scoring Self-Test** checks the scoring rules against the prototype's tests (results in the Console).
- **Multiplayer** is a placeholder until online play is built.
- **Options**: Controls (rebind every key and gamepad button, gamepad drive layout: split arcade, one-stick arcade or tank), Audio (master, match sounds, music), Graphics (display mode, resolution, VSync, frame rate limit, quality, field of view, drive info panel). Saved between sessions.
- The pause menu has Resume, Options, Return to robot selection, Quit to main menu and Exit game.

Default controls (rebindable):

| Action | Keyboard | Gamepad |
|---|---|---|
| Drive / turn | W S / A D (or arrows) | Split arcade: left stick Y, right stick X |
| **Shoot** at your high goal (hold; the turret aims itself) | Space | RT |
| **Intake** FUEL off the carpet (hold; robots with a floor intake) | Q | LT |
| **Dump** into the low goal (hold, front of the robot against the boiler) | Z | Y |
| **Place GEAR** on the lift peg your front slot is lined up with | E | A |
| **Climb**: hold to climb your rope (front bumper against it); let go and it slides back down | T (hold) | X (hold) |
| **FUEL from the loading station** (in your retrieval zone; nearest chute) | F | LB (L1) |
| **GEAR from the loading station** (in your retrieval zone; nearest chute) | G | RB (R1) |
| Precision (40%) | Shift | R3 (hold) |
| Next camera (chase, driver, free) | C | D-pad up |
| Flip chase camera (behind / in front) | V | L3 |
| Reset robot to its start position | R | D-pad down |
| **Restart match** (robot to start line, clock to 2:30, 3-2-1, go) | Enter | **Start** |
| **Pause menu** | Esc | **Select** (View / Back) |
| Back out of a menu | Esc | B |
| Dummy opponent: next spot (midfield, its launchpad, its retrieval zone, against the guardrail, under its rope, off) | B | |
| Practice: reload robot files from disk | F5 | |

Edit a `robot.json` while playing and press **F5** (or **Reload robot files** on the robot screen) to try the change immediately. Tick **Skip Menus** on the `Sim` object to jump straight into a match while testing.

### Starting position (SET AUTO)

In robot selection, **SET AUTO** shows a bird's-eye view of your end of the field. Drive the robot where it should start the match and press **SAVE**. 2017 robots must start touching their ALLIANCE WALL, so SAVE only keeps the position if some part of your bumpers touches it (a status line says whether it does). **RESET** puts the robot back where the match would start it, **CANCEL** (or Esc / B) leaves without saving. The saved start is mirrored for the other alliance; choosing a station under "Start position" goes back to the station starts.

### Rules and violations

The referees (`Violations.cs`) call FOULS (+5 to the other alliance) and TECH FOULS (+25) for G07 (touching an opponent on its own rope: it gets a free climb, +50), G11 (5-count on pins, with a pin-count bubble), G13 (contact while in the opponent's RETRIEVAL ZONE), G17 (more than 5 s in the opponent's KEY), G20 (touching an opponent's rope in the last 30 s) and A04 (entering the opponent's LAUNCHPAD in AUTO). Each call shows a VIOLATION card in the lower right. Robots can only shoot with part of their bumpers in their own LAUNCHPAD. Try them with the dummy opponent, which is on the field in every match; B moves it to the next spot for each rule.

### Match sounds

The field cues (cavalry charge at the start, buzzer at the end of AUTO, resume sound when TELEOP starts, steam whistle at 0:30, buzzer at 0:00) play from files in `UnityProject/Assets/StreamingAssets/Sounds/`. `sounds.json` there maps cues to files, and its `README.txt` lists the download links. A missing file plays a placeholder tone.

Title-screen music: put the track in the same folder as `title-music.mp3` (the `music` entry in `sounds.json`). It loops on the title, options and robot-selection screens, fades out when a match starts, and follows the Music slider. It isn't in the repo (not ours to redistribute).

### Title screen and fonts

The title screen wallpaper is `UnityProject/Assets/Steamworks/Resources/UI/TitleWallpaper.jpg` (replace it with a larger image of the same name for a sharper background). Fonts load from `Resources/Fonts`: drop a licensed **Brim Narrow** or **Haymaker** file in as `Title.ttf` / `Title.otf` for menus and headings; until then Anton is used. `Title.*` is git-ignored: personal-use font licences don't allow sharing the file, so each person keeps their own copy. Body text is Fira Sans. Both defaults are under the SIL Open Font License (texts included).

## Coordinate frames

All data files use the **spec frame**: inches, origin at field centre on the carpet, +X toward the blue alliance wall, +Y toward the scoring table, +Z up. Unity converts with `Units.ToUnity(x, y, z) = (x, z, y) × 0.0254`, so in Unity +x points to the blue wall, +z to the scoring table and +y up.

## Status

- [x] Step 1: field built in Unity from the prototype's data, with static colliders and a CAD overlay
- [x] Step 2: drivable robot from a robot mod file, tank drive, chase / driver / free cameras, gamepad
- [x] Step 2.5: main menu, robot selection, options (rebinding, audio, graphics), pause menu, match clock with field sounds
- [x] Step 3 (testing robot): shooter, low-goal dump, gear placement, climbing, FUEL on PhysX, goal sensors, ROPES, ROTORS / TOUCHPADS, full scoring and live score HUD
- [ ] Step 4: online multiplayer (Netcode for GameObjects + Multiplayer Services), HUD, sounds

## Prototype tests

```
cd prototype/tests
node game.mjs    # 47 timing and scoring checks
node phys.mjs    # drivetrain and collision checks
node rope.mjs    # rope simulation checks
```

Field CAD in `Reference/FieldCAD.obj` is decimated from FIRST's official 2017 field model.
