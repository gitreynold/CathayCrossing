# CathayCrossing 🏝️✈️

> 一個用 Unity 打造的 **HD-2D 辦公室 diorama**，靈感來自《Octopath Traveler》的傾斜鏡頭與 tilt-shift 模型感。
> 用 WASD 在像素小人裡晃晃，把上班族的格子間變成《八方旅人》的冒險場景吧。

```
   ┌──────────────────────────────────────────┐
   │   ✨ HD-2D · Tilt-Shift · Diorama Vibes   │
   │      🚶 WASD  🏃 Shift  🖱️ Right-drag      │
   └──────────────────────────────────────────┘
```

---

## 🎮 這是什麼？

把 **2D 像素角色** 立在 **3D 場景** 裡，加上低 FOV 的 perspective camera、Bokeh 景深、Bloom 跟 ACES tonemapping——這就是 **HD-2D**。
本專案則是把這個風格丟進一間「上班族辦公室」：地毯、隔板、辦公椅、植物、窗戶捲簾——全都是用程式一鍵長出來的。

是的，你的辦公室也可以很《八方旅人》。

---

## ✨ 特色 Highlights

| | |
|---|---|
| 🪄 **一鍵建場** | `Tools ▸ Octopath ▸ Build Office Scene` 直接生出整個辦公室（地板、牆面、桌椅、窗戶、植物、招牌全包）。|
| 📷 **Octopath 風鏡頭** | 33° 俯角 + 22° FOV 的 diorama 視角，[OctopathCamera.cs](Assets/Scripts/Runtime/OctopathCamera.cs) 內建滑鼠 orbit、滾輪 zoom、平滑跟隨。|
| 🧍 **四方向 Billboard** | [DirectionalBillboardSprite.cs](Assets/Scripts/Runtime/DirectionalBillboardSprite.cs) 依角色面向自動切換 Front / Back / Side 材質，左右翻轉一氣呵成。|
| 🚶 **走路 bob 動畫** | 用三角函數讓角色一步一步「跳」，跑步時節奏自動加速（[OctopathPlayerController.cs:99](Assets/Scripts/Runtime/OctopathPlayerController.cs#L99)）。|
| 🎨 **電影級後處理** | Bloom + Bokeh DoF + Vignette + ACES + Film Grain，整套 URP Volume Profile 一鍵生成（[OctopathPostProcessSetup.cs](Assets/Scripts/Editor/OctopathPostProcessSetup.cs)）。|
| 🌐 **WebGL 就緒** | 內附 `FullPage` 自訂 WebGL 模板，build 完直接丟瀏覽器跑。|

---

## 🚀 Quick Start

### 1. 環境

- **Unity 6** (URP 17.3 / Input System 1.18)
- macOS / Windows / Linux 皆可

### 2. 開啟專案

```bash
git clone <this-repo>
cd CathayCrossing
# 用 Unity Hub 打開這個資料夾
```

### 3. 一鍵建場

在 Unity 編輯器選單列點：

```
Tools ▸ Octopath ▸ Build Office Scene
```

整間辦公室會在你眼前長出來，主角站在中央，相機自動框好。按 ▶ Play 直接玩。

### 4. 操作方式

| 動作 | 按鍵 |
|---|---|
| 移動 | `W` `A` `S` `D` 或方向鍵 |
| 跑步 | 按住 `Shift` |
| 旋轉鏡頭 | 滑鼠右鍵拖曳 |
| 縮放鏡頭 | 滾輪 |

---

## 🧱 專案結構

```
Assets/
├── Scripts/
│   ├── Runtime/              # 跑在遊戲裡的腳本
│   │   ├── OctopathCamera.cs              📷 HD-2D 鏡頭
│   │   ├── OctopathPlayerController.cs    🚶 角色控制 + 走路動畫
│   │   ├── BillboardSprite.cs             🪧 基本 Y 軸 billboard
│   │   └── DirectionalBillboardSprite.cs  🧍 四方向材質切換
│   └── Editor/               # 編輯器工具（不會打包進遊戲）
│       ├── OctopathSceneBuilder.cs        🏗️ 一鍵建辦公室
│       └── OctopathPostProcessSetup.cs    🎨 後處理 Volume 生成
├── Scenes/SampleScene.unity  # 主場景
├── Settings/                 # URP 設定 + 自動生成的材質/貼圖
└── WebGLTemplates/FullPage/  # 全頁 WebGL 模板
```

---

## 🛠 技術棧

- **Unity URP** — 17.3.0
- **Input System** — 1.18.0（純新版 API，不依賴舊 Input Manager）
- **AI Navigation** — 預留給 NPC 跑來跑去
- **MCP for Unity** — 讓 AI 可以直接操作 Unity Editor（見 [Packages/com.coplaydev.unity-mcp](Packages/com.coplaydev.unity-mcp)）

---

## 🎨 想自己改？

- **改鏡頭手感**：選 `Camera`，調 `OctopathCamera` 的 `pitch` / `fov` / `distance`。把 FOV 拉低、距離拉遠，diorama 感更強。
- **改後處理**：跑一次 `OctopathPostProcessSetup.CreateOrUpdateProfile()` 重生 profile，或直接在 Inspector 拖。
- **加家具**：到 [OctopathSceneBuilder.cs](Assets/Scripts/Editor/OctopathSceneBuilder.cs) 找 `BuildOfficeFurniture` / `BuildDecor`，照著 pattern 多加幾張桌子、幾盆植物。
- **換角色**：把你自己的像素圖丟給 `frontMaterial` / `backMaterial` / `sideMaterial`，瞬間變主角。

---

## 🐛 常見狀況

- **建場後看不到東西？** 檢查 Camera 是否啟用了 URP，且 `Render Pipeline Asset` 已設定。
- **角色不會動？** 確認 Scene 裡有 Input System 已啟用，並且角色身上掛了 `CharacterController`（腳本會自動補上）。
- **WebGL 黑屏？** 到 `Build Settings ▸ WebGL ▸ Player Settings ▸ Resolution` 確認模板選了 `FullPage`。

---

## 📜 License

目前是個人玩玩的 side project，請隨意 fork、開腦洞、做 meme。
裡面用到的 Unity 內建資源遵循 Unity 自身條款。

---

```
                     ╔══════════════════════════╗
                     ║   按 Shift 加速人生 🏃💨   ║
                     ╚══════════════════════════╝
```
