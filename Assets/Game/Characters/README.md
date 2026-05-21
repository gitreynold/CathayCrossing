# Characters/ — 模組化人物系統

寫實風格的人物，採用「**單一 Humanoid 骨架 + 可換 SkinnedMesh 部位**」架構。
所有部位（髮型、上衣、褲裙、鞋子、配件）共用同一副骨架，runtime 透過
**bone re-binding** 把每個部位的 `SkinnedMeshRenderer.bones` 重新指向角色的
實際骨頭，達成換裝。

## 架構

```
CharacterRoot (掛 Character.cs)
├── Skeleton/                      ← 共用 Humanoid 骨架（Hips → Spine → ...）
├── BaseBody (SkinnedMeshRenderer) ← 底模：身體、頭、手；頭含五官 BlendShapes
└── Slots/                         ← Character.cs 在 runtime 自動建立
    ├── Slot_Hair_*
    ├── Slot_Top_*
    ├── Slot_Bottom_*
    └── Slot_Shoes_*
```

## 程式介面

| 類型 | 用途 |
|------|------|
| `CharacterPartSlot` (enum) | 定義可換的 slot：Hair / Top / Bottom / Shoes / Accessory* |
| `CharacterPartDefinition` (SO) | 一個部位資產：prefab、preview、可染色的 material 索引 |
| `CharacterAppearance` (SO) | 一個完整造型：每個 slot 配什麼部位、髮色/主色/副色、五官資料 |
| `CharacterFaceData` (struct) | 五官 BlendShape 數值 + 膚色 + 瞳色 |
| `Character` (MonoBehaviour) | runtime 元件：`ApplyAppearance(appearance)` / `Equip(slot, part)` |

## 骨架命名規範（**重要**）

部位 prefab 的骨頭名稱**必須與底模骨頭完全相符**，否則 re-bind 會靜默失敗，
mesh 整個塌到原點。

- 全專案用 **Mixamo 風格命名**（`mixamorig:Hips`、`mixamorig:Spine`、...）
  或 **Unity Humanoid 命名**（`Hips`、`Spine`、...），**二擇一**
- 美術匯出 FBX 前確認骨頭名稱
- 多選一後寫進 `docs/art-pipeline.md`（待建立），所有資產一致

> 目前底模未到位，請依美術組決定填入此處。

## 新增一個髮型（範例工作流）

1. 美術產 FBX：rigged 到 shared skeleton（只需要 head 相關骨頭，根 bone 是 Head）
2. 拖進 `Assets/Game/Characters/Parts/Hair/Hair_LongStraight.fbx`
3. Inspector 設 Animation Type = Generic（因為它只是 mesh，動畫由角色驅動）
4. 拖到 Project → 右鍵 → **Create › CathayCrossing › Character › Part Definition**
5. 填寫：
   - `partId` = `hair_long_straight`（**永久 id，發布後不可改名**）
   - `slot` = Hair
   - `prefab` = 剛才匯入的 FBX（或包了它的 prefab）
   - `colorRoles` = 第 0 個 material 設 `Hair` role
6. 把這顆 SO 拖到目標 `CharacterAppearance` 的 `hair` 欄位

## Runtime 套用

```csharp
var character = playerGo.GetComponent<Character>();
character.ApplyAppearance(myAppearanceAsset);

// 單獨換髮型
character.Equip(CharacterPartSlot.Hair, hairPartDefinition);
```

## 換色（不產生 material instance）

部位 prefab 的 material 用**預設色**（白或灰）；runtime 透過
`MaterialPropertyBlock` 注入來自 `CharacterAppearance` 的顏色。
要讓某個 material 跟著染色：在 `CharacterPartDefinition.colorRoles` 設定
material index 對應到的角色（Hair / Primary / Secondary）。

膚色與瞳色直接走 `CharacterFaceData.skinColor` / `eyeColor`，套用到
`Character.skinMaterialIndex` 與 `eyeMaterialIndex` 指定的 material。

## 五官（BlendShapes）

底模 head mesh 由美術出 BlendShapes（建議：`EyeSize`、`EyeWidth`、`NoseHeight`、
`NoseWidth`、`MouthWidth`、`JawWidth`、`CheekVolume`...）。
`CharacterFaceData.blendShapes` 是 `[name, weight 0-100]` 對的 list；
runtime 對著 head mesh `GetBlendShapeIndex(name)` 找；找不到就跳過，
不會炸掉。意思是：**美術可以加新 shape，舊存檔不需要遷移**。

## 雙軌：Procedural 佔位 vs 真 Mesh

模組同時含**兩條軌道**，避免被資產卡住：

### 軌道 A — `ProceduralCharacter`（目前在跑）

[ProceduralCharacter.cs](ProceduralCharacter.cs)：用 Unity primitive 拼一個人，
五官（眼睛、眉、鼻、嘴）每個都有 Inspector slider。Play 模式中拖 slider 即時更新。

- 跑起來就能用，不需要美術資產
- 每個 slider 對應**未來真 mesh 的 BlendShape 名稱**：例如 `eyeSize` → `EyeSize`、
  `noseLength` → `NoseLength`、`mouthCurve` → 微笑/皺眉相關 shape
- 真 mesh 到位後，UI 跟參數**完全沿用**，只是把「縮放小球」換成「設 BlendShape weight」

掛載點：spawn 出來的 player → `SpriteRoot/Body` → `ProceduralCharacter`。
進 Play mode → 在 Inspector 拖 slider → 立刻在 Game view 看到結果。

### 軌道 B — `Character` + SkinnedMesh（待真資產）

底模到位前，B 軌的 [Character.cs](Character.cs) / `CharacterAppearance` /
`CharacterPartDefinition` 暫時沒人用，但 schema 已經就位。底模一進 repo
就直接接：`Character.baseBodyRenderer = baseBodyMeshRenderer`、
`character.ApplyAppearance(appearance)`。

## 目前進度（2026-05-04）

- [x] B 軌程式架構：slot 定義、SO schema、Character runtime
- [x] A 軌 ProceduralCharacter：primitive 身體 + 可調五官
- [x] OfficePlayerSpawner 接到 A 軌（spawn 出來就有可調臉）
- [ ] 底模 FBX（待美術組／Mixamo 來源） → B 軌啟用
- [ ] 角色客製化 UI（讓玩家拖 slider 而非 Inspector）
- [ ] 把 ProceduralCharacter 參數遷移到 SO（讓每位同事可以儲存自己的設定）

## 設計取捨備忘

- **不採用 UMA**：runtime cost 高、WebGL 上難 tune
- **不採用 Ready Player Me**：風格被綁死、要走網路
- **不做 body part hiding**：MVP 階段髮型/衣服直接疊在底模上，可能有 z-fight；
  要解時再加 `hideRegions` mask 機制（類似 UMA 做法）
- **不持久化 Appearance 到玩家存檔**：MVP 階段用 SO，存檔系統建立後再加
  序列化（part 用 `partId` 字串、不存 SO 引用）
