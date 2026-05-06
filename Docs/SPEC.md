# CathayCrossing — 多人虛擬辦公室 SPEC v0.1

> Draft / 待 review。本文件是 product + technical spec 合一版本，目的是把要做什麼、為什麼做、怎麼做、誰先做、有哪些風險講清楚。
> 標 **[Decision]** 的地方代表需要你拍板決定的方向；標 **[Open]** 是還沒收斂的問題。

---

## 1. 願景

把現在這個單機 HD-2D 辦公室 diorama，升級成「**同事真的在裡面上班**」的多人虛擬辦公室。
不是另一個 Gather/oVice，差別在於：

- **跟公司日曆連動**：誰請假、誰在開會、誰真的在座位，都在世界裡看得到。
- **保留遊戲感**：HD-2D 視角、像素角色、走路 bob、可換五官造型，社交動機高於工具感。
- **辦公與娛樂混合**：白天可看板留言互相感謝、午休下班可以開遊戲房連線玩。

成功的標準（北極星指標候選）：
- DAU / 公司編制人數 ≥ 25%（每天約 1/4 同事會打開）
- 平均每位上線使用者每週發出 ≥ 3 個社交互動（感謝、聊天、emote、進遊戲房）

---

## 2. 使用者角色與情境

| Persona | 想做什麼 | 痛點 / 動機 |
|---|---|---|
| **一般同事 Amy** | 知道誰今天在不在、丟訊息感謝同事、午休找人玩遊戲 | 遠距/混合辦公看不到人、想保留同事感 |
| **新人 Ben**  | 認識同事、看到大家的臉與位置 | 入職找不到人、不知道誰是誰 |
| **主管 Carol** | 看到團隊在線狀況、廣播公告、組會議 | 不想用 Slack 已讀不回、希望有「集合」感 |
| **HR / 行政 Dora** | 公佈生日/入職週年/活動、管理空間 | 公告永遠沒人看 |
| **訪客 Evan**（外部 PM/廠商） | 短期被邀請進來開會 | 不能存取內部系統，但想看到我方在線同事 |

典型一日流程（Amy）：
1. 早上開瀏覽器進虛擬辦公室 → 角色 spawn 在自己座位 → 頭上顯示「在座位」
2. 看到隔壁 Ben 的角色頭上有「請假」icon（從 Google Calendar 同步）
3. 走到感謝看板前，貼一張卡片 `謝謝 Carol 昨天幫我 cover 會議`
4. 12:00 走到遊戲室門口 → 進 GameRoom 場景 → 跟另外 3 個同事玩快打
5. 下班走到大門 → 角色淡出 + 在線狀態變 offline

---

## 3. 核心功能（你提的）

### 3.1 多人連線辦公室（基礎）

**做什麼**
- 多個玩家同時連線到同一個 OfficeScene，看到彼此走動。
- 同步：位置、朝向、走/跑狀態、外觀（只在 join / 換裝時 sync 一次）、聊天泡泡。
- 房間容量：MVP 50 人 / 房，未來支援多 shard（如 12F / 13F 各一）。

**設計重點**
- **權威模型**：採 server-authoritative 移動，避免位置作弊（雖然辦公室作弊沒什麼動機，但同步好處理）。
  - 客戶端送 input → server tick → broadcast 給所有人。
  - 客戶端 client-side prediction + server reconciliation（自己看得順、別人看得到正確位置）。
- **興趣管理（AOI）**：50 人量級可以全房廣播，>200 人才需要分 grid。MVP 不做。
- **斷線/重連**：30 秒 grace period，重連後保留位置與外觀。
- **跨場景**：辦公室 ↔ 遊戲房是不同 room（network instance），透過 Door 觸發切換。

**[Decision] 即時同步技術選型**

| 選項 | WebGL 支援 | 自架成本 | 適用 |
|---|---|---|---|
| **Photon Fusion 2** | ✅（WebSocket） | 低（雲服務付費） | **推薦** — 快、穩、CCU 計價，適合公司 internal 規模 |
| Mirror + WebSocket transport | ✅ | 中（要自架 Server） | 預算考量、想完全 open source |
| Unity Netcode for GameObjects + UTP WebSocket | ✅ | 中 | 想吃 Unity 官方支援 |
| Colyseus（TS room server） | ✅ | 中（Node.js） | 想跟 Web admin 後台共用 stack |

我的建議：**Photon Fusion 2 + WebGL**。原因：
- WebGL 是必要條件（README 已標 WebGL ready）。Fusion 2 對 WebGL 的 WebSocket transport 最成熟。
- 公司內部使用，CCU 不會爆（編制 ×1.5 上限），Photon 的 cloud 成本可控。
- Tick-based 同步、預設帶 lag compensation，動作遊戲房（3.4）也能用。

需要你確認：是否同意用 Photon？或公司有強制要求 self-host？

---

### 3.2 Google Calendar 同步「今天誰請假」

**做什麼**
- 抓每位同事 Google Workspace 日曆裡今天的事件，依關鍵字/事件類型分類：
  - **請假**（Out of office、特休、病假）→ 角色不出現或顯示 ghost + 「請假中」label
  - **開會**（Busy 事件且不是 OOO）→ 角色頭上顯示「會議中」icon
  - **空閒** → 正常顯示
- 看板 / 大廳一塊 UI 列出今天請假名單（含頭像、休到哪天）。

**技術做法**
- **後端**：Node.js / Go service，OAuth 2.0 接 Google Calendar API
  - 採 **Domain-wide delegation**（公司 GWS admin 授權 service account），不需要每個員工各自授權，但需要 IT 部門配合
  - 或退而求其次：**個人 OAuth**，每位員工首次登入授權，refresh token 存 DB
- **抓取頻率**：每 5 分鐘 cron 一次 + 員工首次上線時即時拉一次
- **快取**：Redis（key=`calendar:{userId}:{date}`，TTL 5 min）
- **隱私**：只讀 free/busy + OOO，**不讀事件標題與內容**（除非事件被打上特定 tag）

**[Open]**
- IT 是否能開 Domain-wide delegation？如果不行，OAuth flow 要走 SSO 同個過程。
- 隱私邊界：「會議中」icon 算個資揭露嗎？預設 opt-out 還是 opt-in？建議首次上線跳同意視窗。

---

### 3.3 感謝看板（Kudos Board）

**做什麼**
- 場景內一面實體看板（互動 prop）。走進 trigger collider 按 E → 開 UI。
- 卡片格式：`To: [同事] / Message: [文字, max 140 字] / [emoji 反應]`
- 卡片在看板上以紙條/便利貼形式顯示，有翻頁。
- 被感謝者下次上線會收到通知（角色頭上彈通知氣球 + 系統 toast）。
- 留言**不可匿名**（避免攻擊性訊息），但可選擇「只給對方看」或「公開」。

**技術做法**
- **資料**：後端 Postgres 一張 `kudos` 表（id, fromUserId, toUserId, message, visibility, createdAt, reactions[]）。
- **通知**：被感謝者上線時拉 unread → 渲染氣球。
- **內容過濾**：MVP 只做 client-side 不雅字過濾，server-side 用簡單 keyword list。後期接 OpenAI moderation API。

**[Decision]** 看板數量
- 一面公司大看板？還是各部門一面？
- 建議：MVP 一面就好，留 `boardId` 欄位以後可分。

---

### 3.4 多人遊戲房（GameRoom）

**做什麼**
- 大廳一道門 → 走進去 → 切到 `GameRoom.unity` 場景 → 房間內可選遊戲、湊隊開始。
- 房間結構：`GameRoom` 是 hub，內含多個 `MiniGame` 入口（沙發、桌子、彈珠台...）。
- 進 mini-game 後會把參與者隔離成獨立子房間（network room），結束回 hub。

**MVP 三款迷你遊戲建議**
| 遊戲 | 玩法 | 同步難度 |
|---|---|---|
| **掃雷對戰** | 兩人各一張盤，先掃完贏 | 低（回合式） |
| **打地鼠合作** | 多人合力，分數累積 | 中（同步輸入） |
| **大富翁/桌遊** | 4 人輪流 | 低（回合式） |

不建議 MVP 做：FPS / 動作遊戲（同步成本高、容易出鬼影）。

**架構**
- `IMiniGame` 介面已在 `Assets/Game/Core/IMiniGame.cs` —— 沿用。
- 各遊戲一個 asmdef（`CathayCrossing.MiniGames.<Name>`），由 Bootstrap 動態載入。
- Network：用同一 Photon AppId，但不同 Room name；切場景時 disconnect office room → connect game room。

---

### 3.5 角色客製化（五官 + 造型）

**做什麼**
- 進場時 / 隨時可開「換裝」UI，調整：
  - **五官**：眼睛大小/位置、眉毛角度、鼻子高低/寬、嘴形、臉型、膚色、瞳色
  - **髮型 + 髮色**
  - **上衣 / 下身 / 鞋子**（含主色/副色）
  - **配件**：眼鏡、帽子、識別證
- 預設模板（懶人模式）：6–8 個一鍵套用。
- 變更會即時同步給場上其他玩家。

**現況（已建好的部分）**
- A 軌：[`ProceduralCharacter.cs`](../Assets/Game/Characters/ProceduralCharacter.cs) — primitive 拼角色，五官 slider 已可調。
- B 軌：[`Character.cs`](../Assets/Game/Characters/Character.cs) + `CharacterAppearance` SO + `CharacterPartDefinition` — schema 完成，待美術底模。

**還要做**
- **客製化 UI Panel**（runtime，非 Inspector）
  - 左側 slider 群組 / 右側 3D 預覽
  - 「儲存我的 look」→ 寫進玩家 profile
- **Appearance 序列化**：把 `CharacterAppearance` 拆成可 JSON 化的 DTO（slot 都用 `partId` 字串），存後端 user profile。
- **多人同步**：玩家 join / 換裝 → broadcast appearance DTO → 各客戶端套用。
- **成本控制**：appearance change 不要每 frame 同步；只在「按下儲存」時 RPC 一次。

---

### 3.6 頭頂對話框

**做什麼**
- 按 Enter 開輸入框 → 打字 → Enter 發送 → 角色頭上彈出對話泡泡（fade in/out 5 秒）。
- 同房間（office room）所有人看得到。
- 距離衰減：>15m 看不到（鼓勵走過去聊）。
- 可選 voice chat 後話，**MVP 純文字**。
- 表情 emote：快捷鍵（如 1–9）觸發預設情緒（嗨、哈哈、讚、問號、ZZZ）。

**技術做法**
- World-space Canvas + `LookAtCamera` script，跟著角色 head bone。
- Network：`RPC_Speak(string message)` → broadcast，client 各自顯示。
- **Rate limit**：每 user 每 5 秒最多 3 則訊息，避免洗版。
- **Moderation**：同 3.3，server 過 keyword list。
- **Log**：聊天訊息存 30 天供管理員查（公司 compliance 需求）。

---

## 4. 加碼建議功能（我幫你想的）

依「**對日常使用價值** × **實作成本**」排序：

### S 級（強烈建議納 MVP 或 v1.1）

1. **在線狀態指示** — 角色頭上 icon：在座位 / 開會中 / 請假 / AFK / 勿擾。
   AFK 偵測：5 分鐘無輸入自動掛 ZZZ。
2. **Emote / 表情系統** — 比手勢、舉手、鼓掌、點頭。社交價值高、實作便宜。
3. **個人資料卡** — 走近同事按 Tab 看他的名片：名字 / 部門 / 入職日期 / 自介 / 聯絡方式（Slack handle / email）。
   解決新人「這人是誰」痛點。
4. **找人功能** — 大廳搜尋框輸入名字 → 鏡頭 ping 到對方位置 + mini-map 標記。
5. **生日 / 入職週年提醒** — 當日該同事頭上飄氣球；其他人可送祝福（直接寫進感謝看板）。
6. **私人對話 / Whisper** — 對著某人按 V → 只有他看得到的氣球（不公開廣播）。

### A 級（v1.2–v1.3 加碼）

7. **會議室預約面板** — 跟 Google Calendar 雙向：在 Unity 內預約會議室 prop → 寫回對方日曆。
8. **公佈欄 / 公司新聞** — Admin 可發公告，全員 toast + 看板上釘住。
9. **拍大合照 / 拍照模式** — 按 P 切到自由視角 + 擺 pose + 截圖存 Photo Wall。
10. **每日 Icebreaker** — 大廳一個牌子每日不同問題（「最近看的劇？」），同事貼便利貼回答。
11. **投票系統** — 「今天午餐吃什麼」即時投票，便宜又好用。
12. **隨機座位 / 漫遊座位** — 不固定工位，每天進來隨機 spawn，鼓勵跨組互動。
13. **桌面互動物件** — 自己工位上有植物（要澆水）、相框（傳照片）、擺飾收集。長線 retention。

### B 級（nice-to-have，看餘力）

14. **寵物 / 吉祥物** — 跟著主人走，可餵食（消耗每日簽到金幣）。
15. **成就系統** — 第一次發感謝、連續上線 7 天、認識 10 位同事...徽章。
16. **隱形模式** — 我看得到別人，別人看不到我（社恐救星）。
17. **跨樓層 portal** — 12F / 13F / 海外辦公室分 shard，可走 portal 互訪。
18. **Slack / Teams 整合** — 「Amy 在虛擬辦公室找你」會推 Slack 通知。
19. **螢幕分享 / 投影**（會議室） — WebRTC 把使用者螢幕投到會議室螢幕 prop 上。技術門檻高。
20. **自訂工位裝飾** — 可在自己工位擺自己買/兌換的家具（→ 接遊戲化金幣經濟）。
21. **VOIP / 空間音訊** — 走近聽得到聲音、走遠聽不到。技術門檻中高。

### 不建議做

- **匿名留言**：抗濫用成本太高。
- **點數 / 真錢**：跟 HR / 法遵踩雷。
- **3D 全自由視角 FPS 風辦公室**：跟既有 HD-2D 美術衝突。

---

## 5. 技術架構

### 5.1 客戶端（Unity）

延伸現有 `Assets/Game/` 模組架構，**新增**以下 asmdef：

```
Assets/Game/
├── Core/                 # 既有，新增 INetworkPlayer, IChatChannel
├── Bootstrap/            # 既有，加入 NetworkBootstrap
├── Characters/           # 既有
├── Networking/           # 新：Photon Fusion 整合、NetworkPlayer prefab
├── Chat/                 # 新：頭頂泡泡、emote、whisper
├── KudosBoard/           # 新：感謝看板 UI + API client
├── PresenceBadge/        # 新：頭頂狀態 icon、Calendar 整合 client
├── Customization/        # 新：runtime 換裝 UI、appearance serialization
├── DirectoryCard/        # 新：個人資料卡 UI
├── MiniGames/            # 既有，加 Shared/ + 各 game asmdef
└── Backend/              # 新：HTTP client wrapper、auth、SignalR/SSE
```

**依賴方向**：
- `Backend` ← 由所有需要打 API 的模組 ref
- `Networking` ← 由所有需要同步的模組 ref
- 模組之間**不相互 ref**，透過 `Core` 介面或事件 bus 溝通。

### 5.2 後端服務

**[Decision]** 後端語言

| 選項 | 優 | 劣 |
|---|---|---|
| **Node.js + TypeScript** | 跟 Colyseus / Photon webhook 整合容易、JS 生態完整 | 公司若是 Java/Go shop 找人難 |
| **Go** | 部署單純、效能好 | 寫 web 比 TS 囉嗦 |
| **Python (FastAPI)** | 整合 Google API SDK 最快 | runtime 慢，但這 scale 不是問題 |

建議 **Node.js + TypeScript + NestJS**（如果之後想多人共寫）或 **FastAPI + Python**（如果你一個人寫得快）。

**服務分工**

```
                    ┌─────────────────────────────┐
                    │      Unity WebGL Client      │
                    └──────┬──────────────┬───────┘
                           │              │
                  WebSocket│              │HTTPS
                           ▼              ▼
                ┌──────────────┐  ┌──────────────────┐
                │ Photon Cloud │  │  Backend (REST)   │
                │ (realtime)   │  │  - Auth (SSO)     │
                └──────────────┘  │  - Profile        │
                                  │  - Kudos Board    │
                                  │  - Calendar Proxy │
                                  │  - Notifications  │
                                  └────┬─────────────┘
                                       │
                          ┌────────────┼────────────┐
                          ▼            ▼            ▼
                       Postgres      Redis      Google APIs
                       (主資料)    (快取/狀態)   (Calendar/People)
```

### 5.3 認證 / 授權

- **SSO**：Google Workspace OAuth 2.0（公司既有帳號）
- **JWT**：Backend 簽發，client 每次 API call 帶；Photon 用 custom auth callback 驗 token
- **Role**：`employee` / `admin`（HR/IT）/ `guest`（訪客）

### 5.4 資料模型（Postgres 草案）

```sql
-- 員工 profile (從 Google Directory API 同步)
users (
  id uuid PK,
  google_id text UNIQUE,
  email text,
  display_name text,
  department text,
  hire_date date,
  birthday date,
  bio text,
  appearance_json jsonb,        -- 序列化的 CharacterAppearance
  created_at, updated_at
)

-- 感謝看板
kudos (
  id uuid PK,
  from_user_id uuid FK,
  to_user_id uuid FK,
  message text CHECK (length <= 140),
  visibility text,              -- 'public' | 'private'
  reactions jsonb,              -- {"heart": 5, "fire": 2}
  created_at
)

-- 在線狀態快取（也可純 Redis）
presence (
  user_id uuid PK,
  status text,                  -- 'online' | 'away' | 'meeting' | 'leave' | 'offline'
  room_id text,
  position_hint jsonb,          -- 大概位置（用於找人）
  last_seen_at
)

-- 通知收件匣
notifications (
  id uuid PK,
  user_id uuid FK,
  type text,                    -- 'kudos' | 'mention' | 'birthday' ...
  payload jsonb,
  read_at,
  created_at
)

-- 聊天歷史（compliance）
chat_logs (
  id uuid PK,
  room_id text,
  user_id uuid FK,
  message text,
  created_at
)
-- 30 天 retention，用 cron 清。
```

### 5.5 Network 同步資料

| Object | 同步頻率 | 內容 |
|---|---|---|
| Player Transform | 20 Hz | pos, rot.y, anim state (idle/walk/run) |
| Player Appearance | on change | full DTO（換裝時才一次） |
| Chat Bubble | RPC | 文字 + 玩家 id |
| Emote | RPC | emote id |
| Presence Badge | on change | status enum |
| Kudos new card | server-pushed event | card payload |

---

## 6. 階段里程碑

### M0 — 已完成（截至 2026-05-04）
- HD-2D 場景一鍵建場
- 單機角色控制 + Procedural 五官
- 模組化 asmdef 結構

### M1 — 多人連線骨幹（4 週）
- [ ] Photon Fusion 2 整合，office room spawn 多人
- [ ] Player transform 同步 + 走路動畫同步
- [ ] SSO 登入 + JWT
- [ ] 頭頂顯示名字
- [ ] 基本 chat（頭頂泡泡）
- **Demo**：5 人同時連線，互相看得到、可聊天

### M2 — 社交核心（4 週）
- [ ] 感謝看板（POST + 列表 UI）
- [ ] Google Calendar 整合 → presence badge（請假/會議中）
- [ ] 個人資料卡（Tab 鍵）
- [ ] 角色客製化 runtime UI + 同步
- [ ] Emote 系統（5 個內建表情）
- **Demo**：可日常使用一週的 alpha

### M3 — 遊戲房（3 週）
- [ ] GameRoom 場景 + 跨場景切換
- [ ] 第一款 mini-game：掃雷對戰
- [ ] 第二款：打地鼠合作
- [ ] Game lobby UI

### M4 — 抛光與上線（3 週）
- [ ] 找人 / mini-map
- [ ] 生日/週年提醒
- [ ] 通知收件匣
- [ ] AFK 偵測
- [ ] Admin 後台（看 chat log、停權、發公告）
- [ ] WebGL 部署 + 公司內 DNS

---

## 7. 風險與未決問題

### 風險

| 風險 | 影響 | 緩解 |
|---|---|---|
| **WebGL 同步效能** | 高 | 早期 prototype 壓力測 30 人 / 房 |
| **Google Calendar 隱私爭議** | 中 | 預設只抓 free/busy，opt-in 才抓事件標題；接前先過法遵 |
| **濫用聊天 / 看板** | 中 | 實名 + moderation + admin 停權 |
| **CCU 成本爆掉** | 低 | Photon 計價可預估；50 CCU/天 大概 USD 95/月 |
| **美術資產跟不上** | 中 | A 軌 ProceduralCharacter 先頂，B 軌可延後 |
| **公司 IT 不開 Google API 權限** | 高 | 早期就跟 IT 對齊；最壞退化成個人 OAuth |

### Open Questions（請你決定）

1. **網路技術選型**：Photon Fusion 2 OK 嗎？還是公司有 self-host 政策？
2. **後端語言**：TS / Go / Python？
3. **登入範圍**：只限公司內部員工 (@cathay.com.tw)？還是要支援訪客？
4. **看板數量**：一面大的 vs 各部門各一面？
5. **聊天記錄保留**：30 天 OK 嗎？法遵有指定？
6. **MVP 同房上限**：50 人夠嗎？還是要規劃 200 人？
7. **遊戲房 MVP 三款選哪些**：我的建議（掃雷 / 打地鼠 / 桌遊）OK 嗎？還是有別的想做？
8. **預算 / 時程**：M1–M4 共 14 週，可以投入幾人？

---

## 8. 附錄

### A. 名詞表

- **HD-2D**：2D 角色 + 3D 場景的視覺風格，《八方旅人》代表作。
- **AOI**（Area of Interest）：玩家視野內的同步範圍。
- **Authoritative server**：以 server 狀態為準，client 只是顯示與輸入。
- **CCU**（Concurrent Users）：同時在線人數，計價單位。
- **OOO**（Out of Office）：Google Calendar 的請假事件類型。
- **DTO**（Data Transfer Object）：跨網路 / 跨層傳輸用的純資料物件。

### B. 參考

- [Photon Fusion 2 docs](https://doc.photonengine.com/fusion/current/)
- [Google Calendar API — Free/Busy](https://developers.google.com/calendar/api/v3/reference/freebusy)
- 既有專案 README：[../README.md](../README.md)
- 角色系統設計：[../Assets/Game/Characters/README.md](../Assets/Game/Characters/README.md)
