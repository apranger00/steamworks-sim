# Architecture

## Principles

1. **Data drives the field and the robots.** Field geometry, colliders and scoring targets come from `Assets/Steamworks/Data/*.json` (exported from the prototype). Robots come from `robot.json` files. Game code reads data; it does not hard-code dimensions.
2. **Rules are plain C#.** Match timing and scoring (port of `prototype/web/game.js`) live in a class with no Unity dependencies, so they can be unit-tested with the Unity Test Framework exactly like the prototype's Node tests.
3. **Everything a player does is an input struct.** Keyboard, gamepad, a network client or (later) an autonomous routine all produce the same `RobotInput` (throttle, turn, buttons) each physics tick. Only the authority (the host) turns inputs into simulation. This keeps single player and online play on one code path.
4. **The host simulates; everyone else watches.** Physics (PhysX) and scoring run on the host. Clients send inputs and receive state.

## Assemblies

| Assembly | Contents | Depends on |
|---|---|---|
| `SteamworksSim.Runtime` | Units, field builder, cameras, robots, mechanisms, match, networking | Unity, Input System, (later) Netcode |
| `SteamworksSim.Editor` | Steamworks menu (field creation, CAD overlay, robot checks) | Runtime |
| `SteamworksSim.Tests` (step 3) | Rules and mechanism tests ported from `prototype/tests` | Runtime |

## Robots as mods

- A robot is a folder with `robot.json` (and optional `model.glb`, `icon.png`). See `ROBOT_MODS.md`.
- **Mechanism types are code; robots are data.** The game implements a registry of mechanism types (step 2–3): `tank` drivetrain, `turret` shooter, low-goal dump, front gear slot, rope climber. A robot file lists the mechanisms it has and their tuning. Adding robots never touches game code. Adding a new kind of mechanism (swerve, an intake, a fixed-hood shooter) is one new component registered under a type name, after which any robot file can use it.
- Built-in robots live in `StreamingAssets/Robots`; mods in `Mods/Robots` next to the game. A mod with the same `id` replaces a built-in robot.
- Mods contain **no code**, so they are safe to share and to accept from other players online.
- Each definition gets a hash of its canonical JSON. Online, a joining player's robot definition is sent to the host (a few KB) and passed to everyone, so all players simulate and see the same robot even if they don't have that mod installed. Models are optional: a player without the model sees a generated blockout from the frame size, in the robot's alliance bumpers. (Transferring models can come later.)
- The host can require competition-legal robots (2017 R03 volumes and weight) via `RobotRules.Validate`.

## Online multiplayer plan (step 4)

- **Netcode for GameObjects 3.x** with **Multiplayer Services** sessions (lobby + relay), so players join with a code and nobody configures port forwarding. Requires linking the project to a Unity Cloud project (free tier to start).
- **Host-authoritative:** one player hosts and runs PhysX, the match and scoring. Up to six robots (3 v 3).
- **Clients** send `RobotInput` every tick and render replicated state. Own-robot prediction can be added with NGO's prediction support if latency feels bad.
- **Robots** replicate with network transforms. **FUEL** (up to ~600 balls) does not get one network object per ball: the host sends batched, quantized snapshots of only the balls that are moving; resting balls cost nothing.
- **Match state** (clock, period, scores, rotor and touchpad state) replicates as a small network variable set from the host's `Match`.
- Practice and single player run the same host code locally with no network.

## Field build

`FieldBuilder` reads `field.json` and merges geometry per material and group (Field, Tape, Staged Pieces) into a few meshes, then builds one convex `MeshCollider` per prism in `colliders.json` (276 prisms, including 24"-thick perimeter slabs that stop tunnelling). Ropes are not static geometry; they become simulated objects in step 3.

## Frames and units

Data: inches, right-handed, Z up (spec frame). Unity: metres, left-handed, Y up. `Units.ToUnity` swaps Y and Z and scales by 0.0254; `MeshBuilder` swaps two vertices per triangle to keep faces pointing outward after the mirror.
