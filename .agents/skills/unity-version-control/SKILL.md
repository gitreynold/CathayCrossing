---
name: unity-version-control
description: CathayCrossing Unity 專案的版本控制守則（多人協作）。觸發時機：使用者要新增/刪除/搬移 Asset、修改 .gitignore 或 .gitattributes、處理 scene/prefab 衝突、合併分支、檢查 Editor 設定（Asset Serialization、Visible Meta Files）、設定 Git LFS、討論分支策略，或在 commit/push 前做專案健檢。包含 commit/ignore 清單、.meta 處理規則、scene 衝突預防、Smart Merge 設定。
---

# Unity 版本控制守則（CathayCrossing 專案）

> 來源：Unity 官方 Best practices for version control systems + Unity Manual《Version control integration》。本檔做為本專案的單一事實來源（single source of truth），與官方文件矛盾時以本檔優先。

## 前置鐵則（先檢查再動手）

以下兩項 Editor 設定必須維持原樣，否則 git diff/merge 會徹底失效：

| 設定 | 路徑 | 必要值 | 在本專案的位置 |
|---|---|---|---|
| Asset Serialization Mode | `Edit ▸ Project Settings ▸ Editor ▸ Asset Serialization` | **Force Text** | `ProjectSettings/EditorSettings.asset` 的 `m_SerializationMode: 2` |
| Version Control Mode | `Edit ▸ Project Settings ▸ Version Control` | **Visible Meta Files** | `ProjectSettings/VersionControlSettings.asset` 的 `m_Mode: Visible Meta Files` |

→ Codex 在改動 `EditorSettings.asset` / `VersionControlSettings.asset` 前必須讀過確認沒動到這兩個欄位。
→ 開發者切到別的 Unity 版本/匯入舊 package 後，第一件事是回 Editor 視窗確認這兩項仍正確。

## Commit / Ignore 清單

### ✅ 必須進 git

- `Assets/`（**包含每個檔案旁邊的 `.meta`，缺一不可**）
- `Packages/manifest.json`、`Packages/packages-lock.json`
- `ProjectSettings/`（整個資料夾）
- 根目錄：`.gitignore`、`.gitattributes`、`README.md`、`.Codex/`（見最末段「協作配置」）

### ❌ 必須忽略（已在 `.gitignore`）

`Library/`、`Temp/`、`Logs/`、`obj/`、`Build/`、`Builds/`、`UserSettings/`、`MemoryCaptures/`、`Recordings/`、`*.csproj`、`*.sln`、`*.unitypackage`、`.vs/`、`.idea/`、IDE 暫存。

→ 如果有人 PR 把 `Library/` 或 `*.csproj` 推上來，**直接退回**，不要試圖部分接受。這代表他們的 `.gitignore` 沒生效（多半是先 commit 才加 ignore，需要 `git rm --cached -r Library/`）。

## .meta 檔的鐵律

Unity 的 `.meta` 存著 GUID + import settings，**遺失等於專案內所有引用變空指標**。

1. **永遠和對應的 asset 一起 commit**：新增 `Foo.png` 必同時 commit `Foo.png.meta`。
2. **永遠和對應的 asset 一起刪除**：刪 `Foo.png` 必同時刪 `Foo.png.meta`。**不要**手動只刪 asset 留 meta，反之亦然。
3. **絕對在 Unity 的 Project window 內搬移/改名**，讓 Unity 自己同步 `.meta`。**不要**用 Finder/檔案總管/`mv`，會讓 GUID 對不上。
4. **資料夾也有 `.meta`**：新增空資料夾 → Unity 不會建 `.meta`，但放任何檔進去後就會有。刪資料夾要連 `Foo/.meta`（其實是 `Foo.meta`，與資料夾同層）一起刪。

→ Codex 收到「刪除/搬移某個 Asset」的指令時：用 `Bash` 同時處理 `Foo.xxx` 和 `Foo.xxx.meta`，或建議使用者透過 Unity Editor 操作。

## Scene / Prefab 衝突預防

`.unity` 和 `.prefab` 在 Force Text 下是 YAML，理論上可 diff/merge，**實務上極難和解**。預防 > 治療：

### 預防

- **一個 scene 同一時間只一人改**。在團隊聊天室喊一聲再開始（例：「我要動 GameRoom 30 分鐘」）。
- 大功能拆 prefab：把場景中可獨立的單元（家具群、UI panel、角色 rig）做成 prefab，不同人改不同 prefab → 沒有衝突。
- 場景骨架穩定後不要再大改順序（GameObject 順序變動會在 YAML 產生大量假 diff）。

### 萬一還是衝突了

1. **不要直接 `git merge` 解 `.unity`**。先 `git checkout --ours` 或 `--theirs` 拿回一邊，然後在 Unity 內手動把另一邊的改動補回來。
2. 或設定 Unity Smart Merge（UnityYAMLMerge）做 mergetool —— 見下節。

## Smart Merge（UnityYAMLMerge）

Unity 內建 YAML-aware 合併工具，比 git 預設的逐行合併聰明很多。設定方式（在每位開發者的 local `.gitconfig` 做一次）：

```ini
[merge]
    tool = unityyamlmerge
[mergetool "unityyamlmerge"]
    trustExitCode = false
    cmd = '<UNITY_PATH>/Tools/UnityYAMLMerge' merge -p "$BASE" "$REMOTE" "$LOCAL" "$MERGED"
```

macOS 上 `<UNITY_PATH>` 通常是 `/Applications/Unity/Hub/Editor/<version>/Unity.app/Contents`。

並在專案的 `.gitattributes` 註記哪些檔走 mergetool（本專案目前**沒有** `.gitattributes`，需要補；見最末段）：

```
*.unity merge=unityyamlmerge
*.prefab merge=unityyamlmerge
*.asset merge=unityyamlmerge
```

## Git LFS（大型二進位）

Unity 專案常見大檔（.psd / .png / .fbx / .wav / .mp4），**超過 ~10MB 的二進位**請走 LFS，否則 git history 會爆。CathayCrossing 目前是純程式生成 + 少量小檔，**還用不到 LFS**；等開始引入美術 source（特別是 .psd / .fbx）再啟用。

啟用時在 `.gitattributes` 加：

```
*.psd  filter=lfs diff=lfs merge=lfs -text
*.fbx  filter=lfs diff=lfs merge=lfs -text
*.wav  filter=lfs diff=lfs merge=lfs -text
*.mp4  filter=lfs diff=lfs merge=lfs -text
*.png  filter=lfs diff=lfs merge=lfs -text  # 如果 PNG 普遍很大
```

→ Codex 看到使用者要 commit 一張 >10MB 的圖/模型時：**主動提醒** LFS 還沒設定，並列出需要的步驟。

## 分支策略

- `main` 永遠**可進 Unity 直接 Play**。push 前自己 clone 到別處測試 build 一次的成本很低、推爆 main 的成本很高。
- Feature 分支：`feat/<簡短描述>`，例：`feat/npc-pathfinding`。
- Bug fix：`fix/<簡短描述>`。
- 每個 PR 力求小：**一個 PR 不同時動多個 scene**。動一個 scene + N 個 script 是 OK 的。
- Squash merge 預設值，避免 feature 分支的 WIP commit 污染 main。

## 提交前 checklist

Codex 收到「commit / push / 開 PR」的指令時，先依序檢查：

1. `git status` 沒出現 `Library/`、`Temp/`、`Logs/`、`obj/`、`UserSettings/`、`*.csproj`、`*.sln`。出現就先處理 ignore。
2. 每個新增的 asset 都有對應 `.meta`，每個刪掉的 asset 也帶走了 `.meta`。
3. `ProjectSettings/EditorSettings.asset` 沒被誤改（特別是 `m_SerializationMode` 必須維持 `2`）。
4. `ProjectSettings/VersionControlSettings.asset` 的 `m_Mode` 維持 `Visible Meta Files`。
5. 動到 `.unity` / `.prefab` 時，commit message 要明確寫清楚改了什麼（YAML diff 不直觀，靠 message 補上 context）。

## 協作配置（一次性 setup）

`.gitignore` 規則：`.Codex/` 整個資料夾**會進 git**（讓團隊共用 skill 與專案層 Codex 設定），只忽略個人本地設定 `.Codex/settings.local.json`。實際規則：

```
/.Codex/settings.local.json
```

→ 新加入專案層的 `.Codex/skills/<name>/SKILL.md` 都會自動納入版控；只有本機個人偏好（API key、本地 permission）會留在本機。

### 缺的檔案：`.gitattributes`

本專案目前沒有 `.gitattributes`。建議在 repo 根目錄建立，至少包含 line endings 與 Smart Merge：

```
* text=auto eol=lf

*.unity   merge=unityyamlmerge -text
*.prefab  merge=unityyamlmerge -text
*.asset   merge=unityyamlmerge -text
*.meta    merge=unityyamlmerge -text
```

跨 Windows / macOS / Linux 開發時，這份檔案可以避免 CRLF / LF 造成假 diff。

## 參考資料

- Unity 官方：[Best practices for version control systems](https://unity.com/how-to/version-control-systems)
- Unity Manual：[Version control integration](https://docs.unity3d.com/Manual/Versioncontrolintegration.html)
- 本專案使用的 ignore template：[github/gitignore Unity.gitignore](https://github.com/github/gitignore/blob/main/Unity.gitignore)
