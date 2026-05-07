# AR Ping Pong — Final Project

An augmented reality ping pong game built for the Meta Quest using Unity 6, Photon Fusion 2, and the Meta XR SDK. The game supports both single-player (vs. an AI robot) and two-player multiplayer over the internet. The table is anchored to the real world through passthrough video, and a procedural star skybox replaces the background when passthrough is disabled. We also added a meteor shower mechanic that gets more intense as the game goes on.

---

## Table of Contents

1. [Project Overview](#project-overview)
2. [Hardware & Software Requirements](#hardware--software-requirements)
3. [Getting the Project Running](#getting-the-project-running)
4. [Scene Setup & Inspector Wiring](#scene-setup--inspector-wiring)
5. [Architecture Overview](#architecture-overview)
6. [Script Reference](#script-reference)
7. [Multiplayer System](#multiplayer-system)
8. [Meteor Shower System](#meteor-shower-system)
9. [Scoring & UI](#scoring--ui)
10. [Building for Meta Quest](#building-for-meta-quest)
11. [Known Issues & Notes](#known-issues--notes)

---

## Project Overview

The game is a physics-accurate ping pong simulation running on Meta Quest hardware. The ball uses a fully custom physics integration (no Unity Rigidbody) so that we have precise control over bounces and spin. The paddle is tracked via the controller position and velocity, and a robot AI returns the ball using pre-generated stroke patterns. 

In multiplayer, two headsets connect via Photon Fusion 2 in a Host/Client topology. The host has state authority over the ball, and the client's paddle can request authority on contact. Score is synced via RPCs.

The meteor mechanic adds randomness to rallies — meteors fly through the play area, deflecting the ball on contact. Spawning is fully deterministic (seeded RNG) so both players in multiplayer see the exact same meteors without needing any network traffic for the meteors themselves.

---

## Hardware & Software Requirements

- **Headset**: Meta Quest 2, 3, or Pro
- **Unity**: 6000.0.74f1 (Unity 6)
- **Photon Fusion 2**: Imported via Unity Package Manager (see `Packages/manifest.json`)
- **Meta XR SDK**: OVR Plugin & OVR Camera Rig (included in project)
- **TextMeshPro**: Included via Unity package
- **Build Target**: Android (ARM64, IL2CPP)
- **Minimum API Level**: Android 10 (API 29)
- **A Photon AppID**: Free account at [photonengine.com](https://www.photonengine.com) — paste your AppID into `PhotonAppSettings` in `Assets/Resources/`

---

## Getting the Project Running

1. Clone or download the repo.
2. Open the project in **Unity 6000.0.74f1** (other versions will probably break things).
3. Open **Edit → Project Settings → Player** and make sure the build target is Android.
4. Go to `Assets/Resources/PhotonAppSettings` and enter your Photon Fusion AppID.
5. Open the main scene at `Assets/Scenes/` (or `Assets/scenes.unity`).
6. Hit **Play** in the editor to test single-player — the robot will serve automatically.
7. For multiplayer, build to two headsets (see [Building for Meta Quest](#building-for-meta-quest)) and use the in-game lobby UI to host/join.

---

## Scene Setup & Inspector Wiring

The main scene has a few GameObjects you need to wire in the Inspector. Most of the networking and UI builds itself at runtime, but the following need to be set manually:

### `Play` Component
| Field | What to drag in |
|---|---|
| `paddle_hand` | The Hand component on the player's paddle hand |
| `free_hand` | The other Hand component |
| `robot` | The Robot GameObject |
| `balls` | The Balls container |
| `ball_tracking` | The BallTracking component |
| `marked_surfaces` | List of all Bouncer components in the scene |
| `settings` | The SettingsUI component |
| `vr_camera` | The OVRCameraRig camera |
| `pass_through_video` | The OVRPassthroughLayer component |
| `floor` | The floor plane's Renderer (collider stays active, renderer gets hidden) |
| `skybox_material` | Drag `StarSkyboxMat` from `Assets/Materials/` |
| `path_tracer` | The PathTracer component |

The `score_display` (NeonScoreUI) and `MeteorManager` are created automatically in `Play.Start()` — you don't need to wire those.

### `NetworkConnectionManager` Component
| Field | What to drag in |
|---|---|
| `_runnerPrefab` | A prefab with only a `NetworkRunner` component |
| `_play` | The Play component |
| `_bridge` | The MultiplayerBridge component |
| `_sideASpawn` / `_sideBSpawn` | Two empty transforms for player spawn points |
| `_robotRoot` | The robot's root GameObject (gets disabled in multiplayer) |

### `SettingsUI` Component
The settings UI controls passthrough video, room visibility, robot difficulty, etc. It reads/writes a JSON file. The default is set to `show_room = false` so the star skybox shows by default.

---

## Architecture Overview

```
Play.cs  ──────────────────────────────────────────────────────────────────
  │  Core game loop: state machine, scoring, ball serve/hold, meteor init
  │
  ├── Balls.cs / Ball.cs  ──────────────────────────────────────────────────
  │     Ball pool, physics integration (BallState: pos/vel/angular)
  │
  ├── Bouncer.cs  ──────────────────────────────────────────────────────────
  │     Per-surface bounce detection using crossing + edge-strike math
  │     Handles paddle, table near/far, net, walls
  │
  ├── Robot.cs  ────────────────────────────────────────────────────────────
  │     AI: reads BallTracking predictions, moves paddle, executes strokes
  │
  ├── MeteorManager.cs  ───────────────────────────────────────────────────
  │     Pre-generates deterministic meteor waves; spawns/moves/destroys them
  │     Applies elastic impulse to ball on contact
  │
  ├── NeonScoreUI.cs  ─────────────────────────────────────────────────────
  │     Procedural World Space Canvas scoreboard (cyan YOU / magenta OPP)
  │
  └── Networking/
        ├── NetworkConnectionManager.cs  ─────────────────────────────────
        │     Starts Fusion runner, hosts/joins rooms, handles callbacks
        ├── MultiplayerBridge.cs  ─────────────────────────────────────────
        │     Bridges Play.cs ↔ Fusion without coupling them directly
        │     Routes authority transfers and score RPCs
        └── NetworkedBall.cs  ─────────────────────────────────────────────
              NetworkBehaviour on ball prefab; syncs pos/vel; RpcSyncScore
```

The key design decision is that `Play.cs` has zero Fusion imports. All networking goes through `MultiplayerBridge`, which subscribes to events (`on_ball_hit`, `on_score_updated`) that `Play` fires. This made it a lot easier to keep single-player working without touching the networking code.

---

## Script Reference

### `Play.cs`
The main game controller. Manages a state machine of `PlayState` objects that track the expected sequence of bounces in a rally (player serves → table near → table far → robot returns, etc.). 

- `start_game()` — resets scores, starts the meteor manager, puts the ball in serve position
- `keep_score(PlayState last_state)` — called when a rally ends; figures out who scored based on which state the ball was in when it left play
- `report_score()` — pushes the current score to NeonScoreUI; stops meteors if game is over
- `receive_mp_point(bool host_scored)` — called on the proxy peer via RPC to apply the score from the authority peer's perspective
- `enable_show_room(bool show_room)` — shows/hides passthrough and skybox; hides the floor renderer (keeps collider for ball bouncing)

### `Ball.cs` / `Balls.cs`
`Ball` holds a `BallState` (position, velocity, angular velocity, time) and a radius/mass. `set_motion()` updates the state and moves the GameObject. `Balls` manages a pool of Ball instances and calls `move_balls()` each frame, which runs all the `Bouncer` checks.

### `Bouncer.cs`
Attached to every surface the ball can hit (table halves, net, paddle, floor). Each frame `Balls` calls `check_for_bounce()` on every bouncer. It does a crossing test (did the ball path cross this plane?) and an edge-strike test. On a hit it computes the rebound velocity using `perpendicular_rebound`, `parallel_rebound`, and `friction` coefficients. The paddle bouncer gets the wall's velocity factored in so topspin and backspin work correctly.

### `Robot.cs`
The AI reads `BallTracking` predictions to figure out where the ball will be at table height, then moves its paddle to that position and executes a stroke. You can adjust difficulty via `speed`, `pattern` (Forehand, Backhand, Random, Diagonal, etc.), and `spin` (Topspin, Backspin, No spin, etc.) in the Inspector or through `SettingsUI`.

### `BallTracking.cs` / `BallTrack.cs`
Runs a forward simulation of the ball's trajectory (using the same Bouncer physics) to predict where it will bounce next. The robot and PathTracer both use this. `BallState` is the core struct: position, velocity, angular velocity, time.

### `NeonScoreUI.cs`
Creates a World Space Canvas at runtime above the net. Left side is cyan ("YOU"), right side is magenta ("OPP"), bottom bar shows game status in yellow. Neon glow is done with TextMeshPro's `ShaderUtilities` keywords (`GLOW_ON`, `OUTLINE_ON`). Call `UpdateScore(you, opp, status)` to refresh it.

### `MeteorManager.cs`
See [Meteor Shower System](#meteor-shower-system) below.

### `SettingsUI.cs`
Reads/writes a JSON settings file. Exposes toggles for passthrough, show room, and controls for robot difficulty. Calls back into `Play` when settings change. Default `show_room` is `false` so the star skybox is used.

---

## Multiplayer System

We used **Photon Fusion 2** in **Host/Client** (shared state authority) mode, not Shared Mode. Here's how the networking works:

### Connection Flow
1. Player presses "Host" or "Join" in the lobby UI (`NetworkConnectionManager`)
2. `NetworkConnectionManager` starts the Fusion runner and joins/creates a session
3. When a second player joins, `OnPlayerJoined` fires, the host spawns both player avatars, disables the robot, and calls `Play.start_game()`
4. `MultiplayerBridge.Activate()` is called, which wires the event subscriptions

### Ball Authority
- The **host** starts with state authority over the ball
- When the **player** (client) hits the ball, `NetworkedBall.RequestAuthorityOnHit()` is called, which sends `RPC_TakeStateAuthority` to the host to transfer authority to the client
- When the **robot** (host) hits the ball, authority is returned to the host via `AssignAuthorityToPlayer(host)`
- Only the peer with state authority runs physics — the other peer just renders the synced position

### Score Syncing
Scoring only runs on the authority peer (the one that detected the ball going out of play). When a point is scored:
1. `Play.keep_score()` fires `on_score_updated` with a `bool host_scored` (absolute fact about who scored)
2. `MultiplayerBridge.HandleScoreUpdated()` calls `NetworkedBall.RpcSyncScore(hostScored)` 
3. The RPC fires on the proxy peer, which calls `Play.receive_mp_point(hostScored)`
4. Each peer interprets `host_scored` from their own perspective (if I'm the host and host scored, increment my score; otherwise increment opponent's score)

This approach avoids the bug where sending raw score values from local perspective would cause one player's score to update on both machines incorrectly.

---

## Meteor Shower System

`MeteorManager.cs` handles the meteor mechanic. Everything is pre-generated at game start using a seeded `System.Random` so both peers see identical meteors without any network syncing.

### How It Works

**Pre-generation (`Pregenerate()`):**  
At game start, 200 waves of meteors are pre-generated. Each wave:
- Has 1–5 meteors (ramps up from 1-2 early game to 3-5 late game)
- Fires at intervals that shrink from 4s to 0.8s as the game goes on
- Individual meteors within a wave have a random stagger of 0–0.35s so they don't all appear simultaneously

**Spawn Direction:**  
Each meteor picks a random direction using spherical coordinates (azimuth 0–360°, elevation −80° to +80°). It then picks a random target point inside the play volume and traces backwards from that point to an enclosing ellipsoid surface. This guarantees the meteor always starts outside the play area and flies through it from any angle — sides, ends, or from above.

**Ball Collision:**  
Each frame, active meteors check their distance to all active balls. If the distance is less than `meteorRadius + ballRadius`, an elastic impulse is applied to the ball (velocity reflected along the contact normal, plus a fraction of the meteor's momentum). The meteor is treated as much heavier than the ball (~60× mass factor) so the ball gets knocked around pretty hard. In multiplayer, only the peer with state authority over the ball applies the deflection.

**Determinism:**  
All random values use `new System.Random(seed: 42)` — **not** `UnityEngine.Random`. Unity's Random is not guaranteed to produce the same sequence across two different devices. `System.Random` with a fixed seed is fully deterministic.

### Tuning the Meteors (Inspector fields on `MeteorManager`)
| Field | Default | Description |
|---|---|---|
| `_seed` | 42 | RNG seed — change this to get a different pattern |
| `_baseInterval` | 4s | Seconds between waves at game start |
| `_minInterval` | 0.8s | Floor for wave interval |
| `_decayPerStep` | 0.06 | How fast the interval shrinks per wave |
| `_startMaxPerWave` | 2 | Max meteors per wave early game |
| `_endMaxPerWave` | 5 | Max meteors per wave late game |
| `_waveRampOver` | 60 | Waves before count reaches the max |
| `_speedRange` | 5–13 m/s | Meteor speed range |
| `_radiusRange` | 5.5–14 cm | Meteor collision/visual radius |

---

## Building for Meta Quest

1. In Unity go to **File → Build Settings**
2. Make sure **Android** is the active platform (switch if needed — it will take a few minutes)
3. Go to **Edit → Project Settings → Player → Other Settings**
   - Scripting Backend: **IL2CPP**
   - Target Architectures: **ARM64** only
   - Minimum API Level: **Android 10 (API 29)**
4. Connect your Quest via USB with developer mode enabled
5. Hit **Build and Run**

If the build fails with a `classes.dex` file lock error (this happened to us a lot), do the following:
```powershell
Stop-Process -Name "java" -Force
Remove-Item -Recurse -Force "Library\Bee\Android\Prj\IL2CPP\Gradle\launcher\build"
```
Then try building again.

For the star skybox to appear you can run the editor tool: **Tools → Apply Star Skybox** (only needs to be done once). The shader is at `Assets/Materials/StarSkybox.shader`.

---

## Known Issues & Notes

- **Passthrough is disabled by default** — the `pass_through_video.enabled = false` line in `Play.enable_show_room()` disables OVR passthrough so the star skybox shows instead. If you want passthrough, flip that in the code or expose it as a setting
- **The floor collider is always active** even though the renderer is hidden — this is intentional so the ball still bounces off the floor
- **Multiplayer requires both headsets to be on the same Photon AppID** — make sure both builds used the same AppID in `PhotonAppSettings`
- **The MeteorManager is not a prefab** — it's created programmatically in `Play.Start()` so you don't need to put it in the scene manually
- **NeonScoreUI is also created at runtime** — same deal, no prefab needed
- **Score resets on every `start_game()` call** — in multiplayer this is called when the second player joins, so both start at 0:0
- **Robot is disabled in multiplayer** — `NetworkConnectionManager` disables the robot's root GameObject when a second player joins so it doesn't interfere with the human opponent

