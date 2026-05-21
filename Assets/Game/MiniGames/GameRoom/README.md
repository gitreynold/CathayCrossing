# GameRoom — 遊戲間（獨立模組）

這個資料夾是**遊戲間（GameRoom）**的負責人專屬空間。整個模組與辦公室完全隔離：
辦公室組改不到你這裡，你也改不到辦公室。透過 `Assets/Game/Core/` 的介面跟外面溝通。

## 你擁有什麼

| 路徑 | 說明 |
|---|---|
| `Assets/Game/MiniGames/GameRoom/` | 你的程式、Prefab、ScriptableObject |
| `Assets/Game/MiniGames/GameRoom/CathayCrossing.MiniGames.GameRoom.asmdef` | 你的 asmdef，已 ref `CathayCrossing.Core` |
| `Assets/Scenes/GameRoom.unity` | 遊戲間場景，已內建：地板、燈光、回辦公室的門、玩家進場 spawn 點 |

## 已經接好的東西（不要拆掉）

`Assets/Scenes/GameRoom.unity` 內已經放好：

1. **`Floor`** — 地板（cube），玩家會踩在上面
2. **`Light`** — Directional Light，提供照明
3. **`OfficeEntrySpawn`** — 玩家從辦公室進來時出現的位置（`SpawnPoint` 元件，pointName=`OfficeEntry`）
4. **`ReturnDoor`** — 回辦公室的門（`Door` 元件，targetSceneName=`SampleScene`、spawnPointName=`OfficeReturn`）

只要不刪掉這 4 個 GameObject，回程機制就會正常工作。視覺你可以隨意換（換 Mesh、換材質、放 Prefab 蓋上去）。

## 你要怎麼開發

### 1. 在 Unity 開啟你的場景
Project 視窗 → `Assets/Scenes/GameRoom.unity` → 雙擊。

### 2. 加 GameObject、Prefab、Mesh
直接在 Hierarchy 內隨意拉。所有美術資源建議放在 `Assets/Game/MiniGames/GameRoom/Art/` 裡。

### 3. 加程式
新建 .cs 檔放在 `Assets/Game/MiniGames/GameRoom/Scripts/`。namespace 用：

```csharp
namespace CathayCrossing.MiniGames.GameRoom
{
    public class MyGameLogic : MonoBehaviour { ... }
}
```

你的 asmdef 已經 ref `CathayCrossing.Core`，可以用 `IInteractable`、`IMiniGame`、`SpawnPoint`、`Door` 等共用元件。**不要 ref `CathayCrossing.HD2D.Runtime`** 或其他模組——保持隔離。

### 4. 想觸發小遊戲

實作 `CathayCrossing.Core.IMiniGame`：

```csharp
using CathayCrossing.Core;

public class TypingGame : MonoBehaviour, IMiniGame
{
    public string MiniGameId => "typing";
    public void Begin(MiniGameContext ctx) { /* 開始遊戲 */ }
}
```

完成時呼叫 `ctx.OnComplete?.Invoke(new MiniGameResult(success: true, score: 100))`。

## 不該碰的東西

| 不要動 | 為什麼 |
|---|---|
| `Assets/Scenes/SampleScene.unity` | 辦公室組擁有 |
| `Assets/Scripts/` 下的任何檔案 | 辦公室原生程式 |
| `Assets/Game/Core/` | 共用介面，要改請開 PR review |
| `Assets/Game/Bootstrap/` | 程式 lead 擁有，門/場景切換邏輯 |

## 場景間切換的機制（理解就好，不用碰）

1. 遊戲啟動：`GameInfraBootstrap` 自動產生 `__GameInfra` GameObject
   （DontDestroyOnLoad），上面掛 `SceneSwitcher` 與 `OfficeDoorSpawner`。
2. SampleScene 載入後：`OfficeDoorSpawner` 在右上角注入橘色門 + `OfficeReturn` spawn 點。
3. 玩家走進門的 trigger → `Door.AnyPlayerEntered` 事件 → `SceneSwitcher`：
   把玩家+相機 `DontDestroyOnLoad` → Single-mode 載入 GameRoom → 傳送玩家到 `OfficeEntry`。
4. 玩家走進 `ReturnDoor` → 反向流程：載入 SampleScene → 銷毀場景內預載的 Player/Camera 重複物件
   → 把持久玩家+相機搬進 SampleScene → 傳送到 `OfficeReturn`。

玩家 GameObject、相機跨場景持續存在（DontDestroyOnLoad）。每次切場景都會清掉新場景內的重複 Player/MainCamera。
