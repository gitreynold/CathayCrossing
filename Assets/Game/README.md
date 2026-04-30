# Assets/Game/ — 模組化分工結構

這層是 4–5 人並行開發的新主目錄。每個子資料夾對應一個負責人 / 一組人馬，
透過 asmdef 邊界與 `Core/` 內的介面解耦。

```
Assets/Game/
├── Core/           # 共用介面、資料型別 (IInteractable, IMiniGame, Door, SpawnPoint…)
│                   # asmdef: CathayCrossing.Core   ── 不依賴任何模組
├── Bootstrap/      # 場景切換、入口注入 (SceneSwitcher, OfficeDoorSpawner)
│                   # asmdef: CathayCrossing.Bootstrap ── 只 ref Core
│                   # 透過 RuntimeInitializeOnLoadMethod 自動啟動，不需要 Bootstrap scene
│
│  ── 以下為各組進來時自行建立 ──
├── Map/            # asmdef: CathayCrossing.Map        ── 地圖組
├── Characters/     # asmdef: CathayCrossing.Characters ── 人物組
├── Furniture/      # asmdef: CathayCrossing.Furniture  ── 家具/物品組
└── MiniGames/
    ├── Shared/     # asmdef: CathayCrossing.MiniGames.Shared
    └── <YourGame>/ # asmdef: CathayCrossing.MiniGames.<YourGame>
```

## 規則

1. **只有 Bootstrap 可以 reference 所有模組**。其他模組之間透過 `Core` 的介面溝通。
2. **新加模組請新建 asmdef**，不要把所有 .cs 丟進同一個 asmdef。
3. **跨模組資料**：建模/美術產 Prefab + ScriptableObject，程式組消費。
4. **場景切換用 Door + SpawnPoint**：Door 元件 trigger collider，玩家走入即觸發
   `SceneSwitcher` 切換到目標場景並傳送到對應 SpawnPoint。

## 場景進入點

**直接 Play `Assets/Scenes/SampleScene.unity`**——就跟改造前一樣。
[GameInfraBootstrap.cs](Bootstrap/GameInfraBootstrap.cs) 會在遊戲啟動前自動產生
`__GameInfra` GameObject（DontDestroyOnLoad），上面掛 `SceneSwitcher` 與
`OfficeDoorSpawner`。不需要 Bootstrap 場景。

各遊戲間（mini-games）也是獨立的場景檔（如 `GameRoom.unity`），列在 Build Settings 即可。

## 既有 Scripts/ 資料夾

`Assets/Scripts/Runtime/` 與 `Assets/Scripts/Editor/` 內的舊腳本維持原樣，
asmdef 為 `CathayCrossing.HD2D.Runtime` / `CathayCrossing.HD2D.Editor`。
之後可逐步遷移到此 `Game/` 樹下，但不需要一次到位。
