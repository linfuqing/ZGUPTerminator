# 联机机器人 — Server→Bot 全帧解码

> **配套**：[传输编解码约束 §5.C](./联机机器人-传输编解码约束.md) · [架构约束 M2/M4](./联机机器人-架构约束.md)  
> **修订**：2026-07-31 — `MatchStart` 改由 `LoginManager.__Start` 在 `Waiting` 边界为普通组队与匹配统一发送；完整体验证必须接受普通组队 `matchID=0`，状态层只在 Join/Match 当前代次成立后消费，早到拒绝且不缓存；已提交的非零 matchID 不得被另一条非零 Match 覆盖，Server→Bot 的 Mismatch 通知也只能取消同一当前代次。Play/ChapterStage 仍是当前真人协议，Bot 协议层必须完整解码并消费，但不得生成进关状态事件。原始 UTP Connect 握手与应用层 `NetworkRelayMessageType.Connect(type=0)` 是两种不同结构：前者只在 Transport 层处理，后者是当前远端成员重连状态，必须按现行 app 结构精确解析。下行 framing 收紧为 WireCatalog 从 live Null-pipeline 当前 Invite Capture 测得的单一 `inboundAppPayloadOffset`：它指向首个 app，唯一 stream 起点为 `offset - sizeof(ushort)`，Capture 后不保存 Invite body。不再内置 0/8 双分支、枚举起点或评分；Invite 只接受当前完整 SendRelay 布局，删除 continuous/truncated、junk/anchor、文本与历史 offset 恢复。2026-07-08 — RouteSend peel 与 DrainInbound 统一 PopEvents 全帧 walk

---

## 1. 问题

Server→Bot 下行（Create / Join / Status / ChapterStage 等）经 `RouteSend` 投递 UTP wire。Connect 形壳（~365B）内可含 **多个** PopEvents 帧（`ushort size + payload` 循环）。

**旧行为（错误）**：

| 位置 | 行为 | 后果 |
|------|------|------|
| `BotRelayWireBytes.__TryPopEventsWalkRelayApp` | 找到 **第一个** app 即 `return` | 同 wire 后续帧（如 Status 213）丢失 |
| `BotRelayManager.RouteSend` peel | 抠 **1** 个 app 后 `continue`，**丢弃 raw wire** | Drain 无法兜底 |
| `BotRelayBurstWireOps.DrainInbound` | 每条 raw wire 只 `__TryUnwrapInbound` **一个** app | 多帧 wire 只处理首帧 |

典型症状：真人发 Status 213，Bot 在显式开启 `BotRelayDefines.VerboseTelemetry` 后有 `RouteSend peeled type=19`，但无 `remoteStatus=213`，idle match 卡在 stage 213。逐包 peel 日志生产默认关闭，禁止为常驻诊断开启，否则 Move/Camera 会刷日志和调用栈并拖低 Server Tick。

---

## 2. 目标（不可偏离）

1. **L2 唯一算法**：PopEvents `ushort + payload` **全帧 walk**，与 `BotRelaySlotOps.__EnqueueTransportPayload` / ZGUP `NetworkClient.PopEvents` 对称。
2. **RouteSend peel**：只按当前 wire 形态的唯一 stream 起点 walk；完整消费后把**全部** app 放入 `m_PeeledAppQueue`，否则不提交 partial app。
3. **Drain**：未 peel 的 raw wire 进入 `m_Queue`，Drain 仍用**同一套固定 framing**多帧解析；失败即拒绝，不改试其他 offset。
4. **热路径**：`BotRelayPopEventsWalkOps`（`[BurstCompile]`）供 peel / Drain / 单帧 helper 共用；禁止 per-type 字节 scan 或旧 Bot body fallback。
5. **命名**：walk 入口统一 `BotRelayPopEventsWalkOps.TryWalkServerToBotApps*`；peel 入口 `BotRelayRoutePeelOps.TryPeelAllServerToBotApps`。

---

## 3. 数据流

```
Server NetworkRelayServerSystem
    → RouteSend(UTP wire)
         ├─ [peel] TryPeelAllServerToBotApps
         │      → 0..N apps → m_PeeledAppQueue
         │      → fullyConsumed ? skip raw : m_Queue.Enqueue(raw，不提交 partial app)
         └─ [no peel / non-bot port] m_Queue.Enqueue(raw)

PostTick BotRelayBurstWireOps.DrainInbound
    ├─ TryDequeuePeeledAppBurst → Inbox（已是 app）
    └─ TryDequeueToBotBurst(raw)
           → TryWalkServerToBotAppsAndEnqueueInbound（全帧）
           → hello / link-ack 模板（不变）
    → BotRelaySlotInbox.ParseDataPayload
```

---

## 4. L2 算法（`BotRelayPopEventsWalkOps`）

### 4.1 定位 stream 起点

| wire 形态 | streamStart |
|-----------|-------------|
| 当前 RouteSend wire | `catalog.inboundAppPayloadOffset - sizeof(ushort)`；其中 offset 由 live Null-pipeline 当前 Invite Capture 精确测得，指向首个 app 字节 |

Capture 只利用当前完整 Invite 的已知 app 测量 offset，不保存 Invite body/shell。每次运行只有一个合法起点：先在 `offset - sizeof(ushort)` 读取小端长度，首个 app 从 offset 开始，随后按 `ushort length + exact payload` 循环并恰好消费到 EOF；长度越界、尾部残留或 app 结构失败即拒绝整条解析。不得内置 0/8 等多分支、枚举邻近 offset、比较候选分数或返回 partial app。Transport/pipeline 变化时必须重新 Capture；缺少当前 offset 时 fail-close。

**消息分类不变量（当前唯一协议）**：分帧后先读取 app 首个 packed type。原始 UTP Connect 握手不属于 app，不会从这里进入 Inbox；已由 PopEvents framing 剥出的 `NetworkRelayMessageType.Connect(type=0)` 则是当前 Server→Bot 远端成员重连/在线控制消息，必须严格按 `[Connect, channelFlag, userID] + EOF` 解析，不能一律拒绝，也不能把它解释成链路握手或用来设置 Bot 自身的 `Connected`。`Play(type=21)`、`ChapterStage(type=20)`、`MatchStart(type=24)` 必须按各自当前结构完整校验并恰好消费到 EOF：前两者不生成 Bot 进关事件，只有 MatchStart 交给状态门禁；绝不扫描 level/scene 文本。`Invite(type=104)` 还必须满足唯一现行布局：`Invite + relayType + senderID`，Flush 到字节边界后为 `levelID + stage + channel + FixedString512 + 完整 FixedBytes80`，并恰好 EOF。sender 仅要求非零，stage/channel 非负；完整 app 自带的 sender/channel 是该邀请唯一权威，合法 `channel=0` 不改写，不预存 live Create/Join 做乱序关联。不存在 shell、anchor、junk、ASCII/文本、历史长度或 Catalog offset 恢复。

**MatchStart 模式字段与 Play 边界（当前唯一协议）**：`ClientMessageMatchStart(matchID,userStageID,isRestart,levelID,stage,levelName,sceneName)` 是 Bot 在普通组队和匹配中的统一权威描述，由 `LoginManager.__Start` 在 `Waiting` 边界单次发出。L2 完整体验证必须把 `matchID=0` 视为合法普通组队消息，把非零 `matchID` 视为匹配消息。L2 只校验完整 envelope/body，状态层再执行门禁：普通组队须先由 `Join` 确认 current squad/Host，匹配须先由 `MatchToRead` 建立当前非零 matchID；门禁成立后只当场消费新到且一致的消息，任何早到、跨队、旧代次或 ID 不符消息都拒绝且不缓存。`session.matchID` 一旦提交为非零值，同 ID 重复消息只能幂等处理，不同非零 ID 必须拒绝，不能覆盖当前关卡描述或清空其状态；Server→Bot 的 `Mismatch` 通知必须携带并匹配当前代次，旧/外来代次不能取消当前会话。`ClientMessagePlay` 与 `ChapterStage` 保持当前真人协议原有结构和语义，协议层必须完整读取并消费以保证同一 wire 后续帧仍可解析，但 Bot 状态层不从中取得 Stage/tuple，也不启动回放；不存在旧 Bot body 兼容入口。

### 4.2 全帧 walk

```
pos = streamStart
while pos + 2 <= wire.length:
    size = ReadUShort(pos)
    if size 非法: 此起点失败
    rawPayload = wire[pos+2 .. pos+2+size)
    app = ValidateCurrentApp(rawPayload)  // 不剥 inner envelope，不扫描 offset
    if app 为当前合法 Relay/Reply type 且完整结构校验通过:
        输出 / enqueue app
    pos += 2 + size
fullyConsumed = (pos == wire.length)
```

`NetworkRelayMessageType.Query` 的 Server `SendHeader` 回包在查询其他成员时还会在 **Query app 内**追加该成员的 Connect payload（`ClientHeader`）。它不是另一条独立 type=0 app；必须按 Query 的当前结构精确读取，不得按长度当 Invite，也不得从其他 offset 尝试解析。固定 framing 精确命中 Query 后，RouteSend 可直接 peel `type=13`。

`TryAcceptCurrentInboundApp` 一类入口只接收已经由 framing 层剥离出的完整 app，并执行当前结构的严格校验；它是层间交接/单测入口，不得拿来把任意 raw wire 当 app，也不构成 wire fallback。

### 4.3 Peel 过滤

`BotRelayRoutePeelOps.__IsPeelCandidateApp`：只接受 `TryAcceptCurrentInboundApp` 已按当前结构严格验证的 Relay/Reply app；应用层 Connect 与其他当前控制消息同样可 peel，原始 UTP Connect 握手不会走该入口。

Drain **不过滤** peel 候选——凡 L1 可解析的合法 app 均 enqueue。

### 4.4 RouteSend 处置表

| walk 结果 | 行为 |
|-----------|------|
| `appCount > 0 && fullyConsumed` | 全部 peel 候选 → `m_PeeledAppQueue`；**不** enqueue raw |
| 其他 | **不** enqueue partial peeled；raw 可进入 `m_Queue`，Drain 仍按同一固定 framing 验证，失败即拒绝 |

---

## 5. 实现清单

| 文件 | 变更 |
|------|------|
| `BotRelayPopEventsWalkOps.cs` | **新增** Burst 全帧 walk |
| `BotRelayRoutePeelOps.cs` | 多帧 peel；委托 WalkOps |
| `BotRelayManager.cs` | peel 多 enqueue + fullyConsumed gate |
| `BotRelayBurstWireOps.cs` | Drain raw wire 全帧 enqueue |
| `BotRelayWireBytes.cs` | 复用本次 Capture 的唯一 `inboundAppPayloadOffset`，从 `offset - sizeof(ushort)` 完整 walk；按当前结构精确校验应用层 Connect、Play、ChapterStage、MatchStart 等 app；MatchStart 接受普通组队 `matchID=0`，Play/ChapterStage 完整消费后不生成 Bot 进关事件 |
| `BotRelayInviteDecodeOps.cs` | 仅接受当前完整 Invite；sender 非零、stage/channel 非负，直接使用完整 app 自带的 sender/channel |
| `BotRelayTransportRegressionTests.cs` | L2 多帧 + peel + Drain 回归 |
| `BotRelayDesignGoalTests.cs` | Connect 壳双帧 → Status 213 端到端 |

---

## 6. 验收

### EditMode Regression（Agent 自跑）

1. `PopEventsWalk_TwoFramedApps_ExtractsBoth`
2. `RoutePeel_MatchHostPlayWithRankText_RemainsPlayInsteadOfFalseInvite`（锁定现场 `Play → false Invite`）
3. `RoutePeel_MultiFrameConnectShaped_FullyConsumed_SkipsRawQueue`
4. `ServerToBot_ConnectShapedMultiFrame_ChapterStageAndStatus213`
5. Query + ClientHeader 仍按固定 framing 解析为 Query，且不能授权进关
6. 当前完整 Invite 可进入 PendingInvite；continuous/truncated/junk/偏移/尾部残留变体全部拒绝，紧凑非零 userID 与 `channel=0` 保持合法
7. MatchStart 在 Match/Join 代际前拒绝且不缓存
8. 应用层 `Connect(type=0)` 按 `[Connect, channelFlag, userID] + EOF` 更新当前远端成员在线状态；截断/尾随字节拒绝，且不得把它当作 UTP 握手
9. 已提交非零 matchID 后，另一条不同非零 Match 不能覆盖；下行 Mismatch 仅在其 matchID 等于当前代次时生效

### 人工 Play（全绿后再做）

`Server.unity` idle match：`Match Host entered replay ... remoteStatus=213`。

---

## 7. 禁止项

- 禁止 peel 成功但 `!fullyConsumed` 时丢弃 raw wire。
- 禁止在 peel / Drain 外新增 per-message offset scan。
- 禁止枚举 streamStart、返回 partial app，或用消息评分选择候选。
- 禁止 Invite continuous/truncated、junk/anchor、ASCII/文本、历史长度或 Catalog mapping 恢复；禁止改写合法 `channel=0`。
- 禁止扩展 `ClientMessagePlay` body，或让 Bot 从 Play、ChapterStage、remote Status 取得/补齐 Stage 或 level/scene tuple。
- 禁止以 `matchID==0` 为由在 L2 丢弃完整合法的普通组队 MatchStart；模式关联属于状态层门禁。
- 禁止状态层缓存任何门禁成立前早到的零/非零 MatchStart；只能在 Join/Match 当前代际成立后当场消费。
- 禁止把原始 UTP Connect 握手与应用层 `NetworkRelayMessageType.Connect(type=0)` 合并分类；前者只属于 Transport，后者必须按当前控制消息精确解析。
- 禁止用不同非零 Match 覆盖已提交代次，或让旧/外来的下行 Mismatch 清除当前代次。
- 禁止为消警告把 Drain 改回主线程同步循环（须保持 Job + Burst）。
