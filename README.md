# CathayCrossing (Unity WebGL)

Third-person, Animal Crossing-inspired office hangout with light tasks and decor placement.

## Unity Version
- `6000.3.6f1`

## MVP Features
- Cozy third-person controller
- Small office scene to explore
- Interactions: talk to NPCs, inspect items
- Simple task loop (fetch, deliver, tidy)
- Furniture placement mode (grid + rotation)
- WebGL build

## Folder Layout
- `Assets/Scenes` - Unity scenes
- `Assets/Scripts` - Gameplay code
- `Assets/Prefabs` - Prefabs
- `Assets/Resources` - Dialogue and task data
- `Assets/Art` / `Assets/Materials` / `Assets/Audio` / `Assets/UI`

## Scene Setup (Office MVP)
1. Open the project in Unity `6000.3.6f1`.
2. Create a new scene `Office.unity` in `Assets/Scenes` and save.
3. Create a `Game` object and attach `GameBootstrap`.
4. Create a `Player` object and add:
   - `CharacterController`
   - `PlayerController`
   - `InteractionController`
   - `Inventory`
5. Create a `Main Camera` and attach `CameraFollow`, set `target` to Player.
6. Create a `Canvas` and add UI elements:
   - `InteractionPromptUI` (root panel + text)
   - `DialogueUI` (panel with speaker text, line text)
   - `TaskUI` (title and body text)
7. Create an NPC (capsule or prefab) and add:
   - `DialogueTrigger` or `TaskGiver`
   - Set `prompt` and `dialogueId` or `npcId`
8. Create interactable items (cube or prefab) and add:
   - `OfficeItem` with `itemId` (ex: `stapler` or `coffee_beans`)
9. Create task target objects and add:
   - `TaskTarget` with `targetId` (ex: `npc_alex` or `object_coffee`)

## Data Files
- Dialogue JSON: `Assets/Resources/Dialogue/dialogue_office.json`
- Task JSON: `Assets/Resources/Tasks/tasks.json`

## WebGL Build (One Click)
- Use Unity menu: `Build -> Build WebGL (Docs)`
- Output folder: `docs/`

## GitHub Pages Deployment
1. Build WebGL from Unity (output to `docs/`).
2. In GitHub repo: `Settings -> Pages`.
3. `Source` = `Deploy from a branch`.
4. `Branch` = `main`, `Folder` = `docs/`.
5. Wait for Pages to publish.

## Notes
- `Brotli` + `Decompression Fallback` is enabled by the build menu to avoid blank pages on hosts without brotli.
- Unity will generate additional settings files once opened.
