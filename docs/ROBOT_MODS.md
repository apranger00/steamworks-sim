# Robot mods

Make a new robot by copying a robot folder and editing its `robot.json`. No code or Unity install needed.

## Where robots go

| Location | Use |
|---|---|
| `UnityProject/Mods/Robots/<id>/` | Your mods while developing in the editor |
| `<game folder>/Mods/Robots/<id>/` | Mods for the built game (next to the `.exe`) |
| `UnityProject/Assets/StreamingAssets/Robots/<id>/` | Robots that ship with the game |

A folder holds `robot.json` and optionally `model.glb` (the robot's 3D model) and `icon.png`. In the editor, **Steamworks > Robots > List Robot Mods** shows every robot found and any problems.

## robot.json

Units: lengths in **inches**, speeds in **ft/s**, accelerations in **ft/s²**, angles in **degrees**, rates **per second**, mass in **pounds**. Any field you leave out keeps its default.

| Field | Default | Meaning |
|---|---|---|
| `schema` | 1 | File format version |
| `id` | folder name | Unique id; a mod with a built-in robot's id replaces it |
| `name`, `author`, `team`, `description`, `version` | | Shown in robot selection |
| `frame.length / width / height` | 32 / 28 / 24 | Frame without bumpers; length is along the robot's forward direction |
| `bumpers.thickness / bottom / top` | 3.25 / 2 / 7 | Bumper depth and height range above the carpet |
| `mass` | 140 | Including bumpers and battery |
| `drivetrain.type` | `tank` | `tank` now; `swerve`, `mecanum` planned |
| `drivetrain.topSpeed` | 12 | Free speed |
| `drivetrain.acceleration` | 50 | From a stop; motor force fades to zero at top speed |
| `drivetrain.traction` | 1.1 | Wheel-carpet friction; caps acceleration and pushing force |
| `drivetrain.sideScrub` | 0.85 | Resistance to being pushed sideways, as a fraction of traction |
| `drivetrain.brake` | 0.9 | How hard an idle robot resists pushes, as a fraction of traction |
| `drivetrain.maxTurnRate` | 270 | |
| `drivetrain.trackWidth / wheelDiameter / wheelsPerSide` | 24 / 6 / 3 | Tank kinematics and visuals |
| `shooter.enabled / type` | true / `turret` | Turret auto-aims at your high goal |
| `shooter.rate` | 6 | FUEL per second |
| `shooter.exitHeight`, `shooter.exitOffset` | 30, [0, 0] | Where FUEL leaves: height, and [forward, left] from the robot centre |
| `shooter.maxExitSpeed` | 40 | |
| `shooter.minRange / maxRange` | 2.5 / 25 | Feet from the goal centre |
| `shooter.entryAngle` | 55 | How steeply shots drop into the goal |
| `shooter.aimError / speedError` | 0.6 / 1.2 | Accuracy: degrees and percent, 1 sigma |
| `shooter.capacity` | 0 | A hard FUEL limit (shooter and dump share it). 0 = what the open-top hopper physically holds (see `intake.hopperBalls`), or unlimited for a robot without one (testing) |
| `lowGoalDump.enabled / rate / height / speed / width` | true / 10 / 22 / 5.83 / 14 | Dumping out of the front into the low goal |
| `gear.enabled / back / slotOffset` | true / false / 0 | Gear slot on the front (or the back with `back: true`); offset to the robot's left of the centre line |
| `gear.maxDistance / maxLateral / maxAngle` | 4 / 3.5 / 15 | How precisely you must line up on a peg |
| `gear.capacity` | 0 | GEARS the robot holds; 0 = unlimited |
| `preload.fuel / gears` | 10 / 1 | On board at the start of a match (2017 limits; capped by capacity). Whatever an alliance's robots don't preload goes to its loading lane (the opening in its own alliance wall) |
| `intake.fuelFromChute` | true | Open-top hopper: FUEL falling from a loading-station chute onto the robot goes in |
| `intake.hopperDepth` | 12 | in above the robot's top that counts as inside the hopper |
| `intake.hopperBalls` | 0 | Sizes the open-top hopper: about this many FUEL, piled loosely, reach the rim. It then holds what physically fits (a ball whose centre would be above the rim rolls off) and the rest overflows |
| `intake.gearFromChute` | true | The front gear slot takes a GEAR sliding out of a chute (robot facing the station, slot lined up with the chute) |
| `intake.fuelFromFloor / gearFromFloor` | false / false | Front intake picks pieces up off the carpet while Intake is held. With an open-top hopper, FUEL goes in through a gap in the front bumper and rides a conveyor up into the hopper |
| `climber.enabled / rate` | true / 12 | Winch speed in in/s |
| `climber.setback / reachForward / reachLateral` | 3 / 10 / 12 | Where the rope is gripped and how close it must be to latch |
| `model.file` | "" | e.g. `model.glb`; empty = generated blockout |
| `model.scale / offset / rotation` | 1 / [0,0,0] / [0,0,0] | Metres per model unit; offset [forward, left, up] in inches; rotation in degrees |

## Validation

Errors stop a robot from loading (missing sizes, an unsupported mechanism type). Warnings flag robots that wouldn't pass 2017 inspection:

- **Size (R03):** with bumpers, the robot must fit 36 × 40 × 24 in tall, or 30 × 32 × 36 in tall.
- **Weight (R04):** 120 lb without bumpers and battery, about 150 lb with them.

Online hosts will be able to allow only legal robots.

## Online play

Robot files are small, so when you join a game your robot's definition is sent to everyone. Other players see and simulate your robot even without the mod installed; without your `model.glb` they see a generated blockout of the same size in your alliance colour.

## Example

`UnityProject/Mods/Robots/example-sprinter/robot.json` is a lighter, faster robot with no shooter or dump, made by changing a few values of the built-in `sim-6wd`.
