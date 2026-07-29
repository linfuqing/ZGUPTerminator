# 联机机器人 — 包 API（反射替代，已落地）

> 原 `MultiplayerBotPackageAccess` 已删除；Terminator 直接调用 ZG 包 API。

---

## 1. `NetworkClientSendBuffer`（ZGUPEntitiesNetworkingCommon）

```csharp
public int GetPendingSendSlotCount();
public int GetPendingSendReadIndex(int slotIndex);
public bool TryReadPendingSend(int slotIndex, ref int readIndex, out NativeArray<byte> bytes);
public bool TryReadPendingSendInWriteOrder(ref int lastCapturedEndWriteIndex, out NativeArray<byte> bytes);
```

**Terminator**：`NetworkSendBufferCapture.TryCapture` 使用 `TryReadPendingSendInWriteOrder`，与 `Apply` EndWrite 顺序一致。

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

