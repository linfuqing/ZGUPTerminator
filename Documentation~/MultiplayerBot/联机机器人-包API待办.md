# 联机机器人 — 包 API（反射替代，已落地）

> 原 `MultiplayerBotPackageAccess` 已删除；Terminator 直接调用 ZG 包 API。

---

## 1. `NetworkClientSendBuffer`（ZGUPEntitiesNetworkingCommon）

```csharp
public readonly struct EndWriteCaptureStamp
{
    public readonly uint epoch;
    public readonly double timestamp;
}

public uint BeginEndWriteCapture(in EndWriteCaptureStamp initialStamp);
public bool SetEndWriteCaptureStamp(uint generation, in EndWriteCaptureStamp stamp);
public EndWriteCaptureReadStatus TryPeekCapturedEndWrite(
    out EndWriteCaptureToken token,
    out NativeArray<byte> bytes,
    out EndWriteCaptureStamp stamp);
public bool ConsumeCapturedEndWrite(in EndWriteCaptureToken token);
public bool EndWriteCapture(uint generation, bool discardUnread = false);

public int capturedEndWriteCount { get; }
public EndWriteCaptureFault endWriteCaptureFault { get; }
```

这是一个**录制时显式开启、默认关闭**的 completed-`EndWrite` journal：

- 每次非空 `EndWrite` 把 payload 副本、全局递增 sequence 与当时的 producer `epoch + double timestamp` 原子提交到 journal。
- journal 与 transport send/retry cursor 独立；`NetworkClientSendBuffer.Apply`、`Clear`、发送成功/失败都不能消费、重置或重排录制条目。
- `NetworkSendBufferCapture.TryCapture` 只执行 `TryPeekCapturedEndWrite → 拷贝 payload → ConsumeCapturedEndWrite`，不扫描 transport 槽、不维护 per-slot read index，也不改正常发送/重试状态。
- `Begin/Set/Peek/Consume/End` 必须在 producer jobs 已完成的同步边界调用；录制启动失败、journal fault、sequence/epoch/时间戳倒退、未读条目无法 clean end 均 fail-closed，并通过 `captureError` 阻止保存。
- `GetPendingSendSlotCount` / `GetPendingSendReadIndex` / `TryReadPendingSend` 仍可用于只读诊断，但**不是录制事实源**；已删除的 `TryReadPendingSendInWriteOrder` 不得恢复。

录制时间戳由 `LevelRecordingCaptureClockSystem` 在 producer window 前发布。Session 起点和 producer stamp 使用 `Time.unscaledTimeAsDouble` / `double`，写入 TRBT v2 时才做范围校验并转换为 `float`；禁止对积压条目插值、摊平或补时间。

---

## 2. `NetworkClient.TryEnqueueSyntheticData`（与初稿差异）

**实际签名**（非 `TryEnqueueData(NativeArray<byte>)`）：

```csharp
public void TryEnqueueSyntheticData(in NetworkPipeline pipeline, ref DataStreamReader reader);
```

**语义**：与 `PopEvents` 的 `NetworkEvent.Type.Data` 一致——`reader` 内为 **一个或多个** `ushort length + payload` 块；循环直到 `reader` 读完。

**Terminator 用法**（`NetworkClientReplayInjector`）：

1. `TryBuildCollectInboundWire` 得到 **应用层 Collect 帧**（无 UTP 头）
2. 再包一层 `WriteUShort(length) + WriteBytes(payload)` 写入 `NativeArray`
3. `new DataStreamReader(framed)` → `client.TryEnqueueSyntheticData(in pipeline, ref reader)`

```csharp
using var framed = new NativeArray<byte>(sizeof(ushort) + wirePayload.Length, Allocator.Temp);
var writer = new DataStreamWriter(framed);
writer.WriteUShort((ushort)wirePayload.Length);
writer.WriteBytes(wireNative);
var reader = new DataStreamReader(framed.GetSubArray(0, writer.Length));
client.TryEnqueueSyntheticData(in pipeline, ref reader);
```

---

## 3. 修订记录

| 日期 | 内容 |
|------|------|
| 2026-06-18 | 初版：待办与实现草案 |
| 2026-06-18 | 包 API 落地；`TryEnqueueSyntheticData` + DataStreamReader 分帧格式；删除 `MultiplayerBotPackageAccess` |
| 2026-07-31 | 录制读取改为 opt-in completed-EndWrite journal；删除 transport 槽游标方案，时间戳改为 producer `epoch + double`，并与 `Apply/Clear` 解耦 |

