# 联机机器人 — Server→Bot 全帧解码

> **配套**：[传输编解码约束 §5.C](./联机机器人-传输编解码约束.md) · [架构约束 M2/M4](./联机机器人-架构约束.md)  
> **修订**：2026-07-26 — normalized app 必须先按首个 packed type 分类，禁止将 Play 的内部文本误作 Invite canonicalize/评分；2026-07-23 — Query + ClientHeader 大回包必须优先选择完整消费 wire 的 PopEvents 起点；2026-07-08 — RouteSend peel 与 DrainInbound 统一 PopEvents 全帧 walk

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
2. **RouteSend peel**：walk 出 **全部** peel 候选 app → `m_PeeledAppQueue`；**仅当** `fullyConsumed` 才丢弃 raw wire。
3. **Drain fallback**：peel 未拆完或未启用 → raw wire 进 `m_Queue` → Drain 用 **同一套 walk** 多帧 enqueue Inbox。
4. **热路径**：`BotRelayPopEventsWalkOps`（`[BurstCompile]`）供 peel / Drain / 单帧兼容 API 共用；禁止 per-type 字节 scan。
5. **命名**：walk 入口统一 `BotRelayPopEventsWalkOps.TryWalkServerToBotApps*`；peel 入口 `BotRelayRoutePeelOps.TryPeelAllServerToBotApps`。

---

## 3. 数据流

```
Server NetworkRelayServerSystem
    → RouteSend(UTP wire)
         ├─ [peel] TryPeelAllServerToBotApps
         │      → 0..N apps → m_PeeledAppQueue
         │      → fullyConsumed ? skip raw : m_Queue.Enqueue(raw)
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
| 非 pipeline | `0` |
| pipeline / Connect 壳 | 枚举 `[PipelinePrefixBytes, PipelinePrefixBytes + PipelineEnvelopeScanBytes]` |

对每个候选起点执行全帧 walk；完整消费 wire 的候选优先于只解析出前缀局部帧的候选，再按消息类别与 app 数量评分（平局取更靠前）。UTP envelope 自身可能偶然出现一个合法 `ushort size` 和合法 type；若后续长度非法，该候选只能作为 partial fallback，`fullyConsumed` 必须为 false，不能冒充真实 stream 起点。

**消息分类不变量（2026-07-29）**：normalization 后必须先读取 app 首个 packed type。只有显式 `type=104`（或 Invite 专用 shell 路径已经结构化确认）才允许进入 Invite canonicalize 与 Invite 高权重评分；`Connect(type=0)` 必须拒绝，`Play(type=21)`、`MatchStart(type=24)` 等 ClientMessage 必须原样保留。禁止对任意 app 先调用宽松 Invite reader——现场已证明 `ClientMessagePlay{levelName="青铜III", sceneName="Scenes/Level4-1.scene"}` 的内部 UTF-8/零填充可被误读为 Invite，并把 53–55B wire 扩写成 97B `type=104` 伪邀请。

### 4.2 全帧 walk

```
pos = streamStart
while pos + 2 <= wire.length:
    size = ReadUShort(pos)
    if size 非法: 此起点失败
    rawPayload = wire[pos+2 .. pos+2+size)
    app = TryNormalizePopEventsPayload(rawPayload)  // inner envelope ≤24B
    if app 为合法 Relay/Reply type 且 type != Connect:
        输出 / enqueue app
    pos += 2 + size
fullyConsumed = (pos == wire.length)
```

`NetworkRelayMessageType.Query` 的 Server `SendHeader` 回包在查询其他成员时还会追加该成员的 Connect payload（`ClientHeader`），整条 pipeline wire 通常为 99–104B，恰好落在历史 Invite shell 的长度范围。它仍是普通 PopEvents app：不得按长度当 Invite，也不得从 UTP 头部较早出现的偶然短帧起点解析。结构化 walk 精确命中 Query 后，RouteSend 可直接 peel `type=13`。

### 4.3 Peel 过滤

`BotRelayRoutePeelOps.__IsPeelCandidateApp`：Relay 控制面 + Invite + ClientMessage 范围；Connect 拒绝。

Drain **不过滤** peel 候选——凡 L1 可解析的合法 app 均 enqueue。

### 4.4 RouteSend 处置表

| walk 结果 | 行为 |
|-----------|------|
| `appCount > 0 && fullyConsumed` | 全部 peel 候选 → `m_PeeledAppQueue`；**不** enqueue raw |
| 其他 | **不** enqueue partial peeled；raw → `m_Queue`（Drain 全帧 walk） |

---

## 5. 实现清单

| 文件 | 变更 |
|------|------|
| `BotRelayPopEventsWalkOps.cs` | **新增** Burst 全帧 walk |
| `BotRelayRoutePeelOps.cs` | 多帧 peel；委托 WalkOps |
| `BotRelayManager.cs` | peel 多 enqueue + fullyConsumed gate |
| `BotRelayBurstWireOps.cs` | Drain raw wire 全帧 enqueue |
| `BotRelayWireBytes.cs` | `TryExtractAppViaTransportFraming` 复用 walk（首帧兼容） |
| `BotRelayTransportRegressionTests.cs` | L2 多帧 + peel + Drain 回归 |
| `BotRelayDesignGoalTests.cs` | Connect 壳双帧 → Status 213 端到端 |

---

## 6. 验收

### EditMode Regression（Agent 自跑）

1. `PopEventsWalk_TwoFramedApps_ExtractsBoth`
2. `RoutePeel_MatchHostPlayWithRankText_RemainsPlayInsteadOfFalseInvite`（锁定现场 `Play → false Invite`）
3. `RoutePeel_MultiFrameConnectShaped_FullyConsumed_SkipsRawQueue`
4. `ServerToBot_ConnectShapedMultiFrame_ChapterStageAndStatus213`
5. `RoutePeel_QueryHeaderShell_SelectsExactFrameAndReleasesFreshMatchStage`

### 人工 Play（全绿后再做）

`Server.unity` idle match：`Match Host entered replay ... remoteStatus=213`。

---

## 7. 禁止项

- 禁止 peel 成功但 `!fullyConsumed` 时丢弃 raw wire。
- 禁止在 peel / Drain 外新增 per-message offset scan。
- 禁止为消警告把 Drain 改回主线程同步循环（须保持 Job + Burst）。
