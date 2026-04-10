# Un-Dead-Hotel-3D

Un-Dead-Hotel-3D is a Unity prototype where a hotel population simulation gradually breaks down into a zombie outbreak. You control a survivor, select guests to inspect their live behavior state, and watch guests and zombies interact through NavMesh-driven AI systems.

## Current Status

**Prototype / In Development**

Currently implemented:
- Playable scene with survivor spawn and population initialization.
- Runtime guest and zombie AI with perception, chase/flee, and attack loops.
- Guest-to-zombie conversion when guests die to zombie damage.
- Day/night progression with dynamic lighting and ambient color changes.
- Runtime debug UI for behavior tree state and in-game time.

## Gameplay Quickstart

1. Open the project in Unity Editor `6000.4.1f1`.
2. Open scene: `Assets/Scenes/SampleScene.unity`.
3. Press Play in the Unity Editor.

## Controls

- Left click: Select a guest actor (and focus camera follow on that guest).
- Right click: Move the survivor to clicked floor position.
- `WASD` / Arrow keys: Pan camera.
- Mouse at screen edge: Edge-scroll camera.
- Mouse wheel: Zoom camera in/out.

## Core Systems Overview

- **Map generation:** `MapGenerator` builds an 11x11 chunk grid using concentric rings to classify chunk types (edge, normal, atrium edge, atrium).
- **Time/day-night:** `GameManager` tracks in-game time, formats it as `HH:MM`, and updates directional light + ambient gradients across the day cycle.
- **Actor model:** `BaseActor` provides shared health, damage/death flow, and world-space health UI setup used by survivor, guests, and zombies.
- **Zombie AI:** `ZombieController` wanders, detects humans by vision/hearing, chases with persistence, and attacks on cooldown.
- **Guest AI:** `GuestController` runs a behavior tree (danger check, flee, seek shelter, wander) and tracks threat memory/panic persistence.
- **Conversion loop:** If a guest is killed by a zombie, `GameManager.TryConvertGuestToZombie(...)` spawns a replacement zombie on NavMesh.

## UI and Debug Overlays

`GameManager` creates these runtime UI managers on start:
- `BehaviorTreeUI`: Shows the selected guest's behavior tree state when camera follow is locked to that guest.
- `TimeOfDayUI`: Shows current in-game time from `GameManager.CurrentTimeFormatted`.

## Project Structure

High-level folders you will work with most:
- `Assets/Scripts/World` - game flow, map/grid generation, spatial index.
- `Assets/Scripts/Actors` - base actor, guest and zombie logic.
- `Assets/Scripts/Player` - survivor movement, interaction, camera control.
- `Assets/Scripts/AI` - behavior tree primitives.
- `Assets/Scripts/UI` - runtime debug/overlay UI scripts.
- `Assets/Prefabs` - actors, room modules, chunk prefabs, materials.
- `Assets/Scenes` - playable scenes (`SampleScene` is build-enabled).
- `ProjectSettings` - Unity editor/project configuration.
- `Packages` - package manifest and lock file.

## Dependencies / Tech Stack

From `Packages/manifest.json`:
- Unity 6 (`6000.4.1f1`) project setup.
- Universal Render Pipeline (`com.unity.render-pipelines.universal`).
- Input System (`com.unity.inputsystem`).
- AI Navigation (`com.unity.ai.navigation`).
- uGUI (`com.unity.ugui`) and TextMeshPro assets in project.
- Unity Test Framework (`com.unity.test-framework`) available in dependencies.

## Contributor Quick Notes

Common tuning points:
- **Population rates:** `GameManager.guestOccupancyRate`, `GameManager.initialZombificationRate`.
- **Time speed:** `GameManager.gameSecondsPerRealSecond` and `GameManager.startHour`.
- **Map size/layout:** `MapGenerator.mapWidthChunks` and `MapGenerator.mapHeightChunks`.
- **Camera bounds/speeds:** `CameraController.mapBoundsMin`, `mapBoundsMax`, `panSpeed`, `scrollSpeed`.
- **Perception cadence:** `ZombieController.perceptionInterval`, `GuestController.dangerScanInterval`, and spatial index settings in `GameManager`.

## Known Limitations and Next Milestones

Known limitations:
- No standalone build/release pipeline documented yet.
- No formal automated test suite committed under `Assets/Tests` yet.
- Single primary build-enabled scene (`Assets/Scenes/SampleScene.unity`).

Planned next milestones:
- Add dedicated test coverage for AI perception and conversion rules.
- Add additional gameplay objectives/win-loss state on top of simulation loop.
- Add a production-ready scene/bootstrap flow beyond the current prototype scene.
