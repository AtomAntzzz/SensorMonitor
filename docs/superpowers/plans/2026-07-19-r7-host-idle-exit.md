# R7 Host 空闲自退 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Host 在末次管道请求后 5 分钟无新请求则自退，把常驻提权进程收敛为按需存活（有 band 固定时每 1s 请求→永不退；无人用→自退→再固定时静默拉回）。

**Architecture:** 纯 Host 侧。`PipeJsonServer` 记录 `LastRequestUtc`（Interlocked ticks，构造时=启动时刻，每 GET 刷新）；`Program.cs` 加独立空闲检查 Timer（每 60s 查，超 5min 无请求则优雅 `exit.Set()`）。扩展零改动。

**Tech Stack:** 同 Host（.NET 8、xUnit）。无新依赖。

**Spec:** `docs/superpowers/specs/2026-07-19-r7-host-idle-exit-design.md`（含 4 项验收，勿重开设计）

**已核实事实（勿重新查证）：**
- `PipeJsonServer.ServeOneAsync`（`src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`）：`var request = await reader.ReadLineAsync(connCts.Token); if (request == "GET") { await writer.WriteLineAsync(...); await reader.ReadLineAsync(...); }`。
- `Program.cs`：`server.Start();` → `HostLog.Write("Host 启动…");` → `var exit = new ManualResetEventSlim();` → `Console.CancelKeyPress += …;` → `exit.Wait(); return 0;`。refresh Timer 用 one-shot 重排 + `catch (ObjectDisposedException)`。
- 测试工程 `tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs`，xUnit，现有测试用"连管道→写 GET→读响应"模式；当前 11 单测。
- **坑 #6（Host 锁 bin）**：跑 Host 测试/构建前须停 Host；因扩展 SnapshotCache 会自动重拉，需连 CmdPal 一并停：`taskkill //f //im Microsoft.CmdPal.UI.exe` +（提权任务拉起的 Host）`schtasks //End //TN SensorMonitor.Host`。

---

## 文件结构

| 文件 | 动作 | 职责 |
|------|------|------|
| `src/SensorMonitor.Host/Ipc/PipeJsonServer.cs` | 修改 | 加 `LastRequestUtc`（Interlocked ticks），GET 时刷新 |
| `tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs` | 修改 | 加 `LastRequestUtc` 随 GET 前进的用例 |
| `src/SensorMonitor.Host/Program.cs` | 修改 | 加空闲检查 Timer + 优雅退出 |
| `CLAUDE.md` / 路线计划 | 修改 | 状态收口 |

> 命令均在仓库根 `D:/Workspace/SensorMonitor` 执行。

---

## Task 1: PipeJsonServer 记录末次请求时间（TDD）

**Files:**
- Modify: `src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`
- Test: `tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs`

- [ ] **Step 1: 写失败测试**

`PipeJsonServerTests.cs` 追加：

```csharp
    [Fact]
    public async Task LastRequestUtc_Advances_On_Get()
    {
        var pipeName = $"SensorMonitor.Test.{Guid.NewGuid():N}";
        var snapshot = new SensorSnapshot(DateTimeOffset.UtcNow,
            [new SensorReading("/cpu/0/clock/1", "CPU", "Core #1", "Clock", 5200f, "MHz")]);
        using var server = new PipeJsonServer(pipeName, () => snapshot);
        var before = server.LastRequestUtc;   // 构造时 ≈ 启动时刻
        server.Start();

        await Task.Delay(50);                 // 跨过系统时钟粒度（~15ms），确保时间戳可辨
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
        await client.ConnectAsync(2000);
        using var reader = new StreamReader(client);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        await writer.WriteLineAsync("GET");
        await reader.ReadLineAsync();          // 收到响应 = 服务端已在写响应前记下请求时间

        Assert.True(server.LastRequestUtc > before,
            $"LastRequestUtc 应在 GET 后前进：before={before:o} after={server.LastRequestUtc:o}");
    }
```

- [ ] **Step 2: 跑测试确认失败（编译失败：无 LastRequestUtc）**

先停 Host 与 CmdPal 避免锁 bin（坑 #6）：

```bash
cd "D:/Workspace/SensorMonitor" && powershell -NoProfile -Command "Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null; schtasks //End //TN SensorMonitor.Host 2>/dev/null; dotnet test tests/SensorMonitor.Host.Tests --filter LastRequestUtc_Advances_On_Get 2>&1 | tail -5
```
Expected: 编译失败（`PipeJsonServer` 无 `LastRequestUtc`）。

- [ ] **Step 3: 实现 —— LastRequestUtc（Interlocked ticks）+ GET 时刷新**

`PipeJsonServer.cs` 改三处：

(a) 字段区（`private Task? _loop;` 之后）加：

```csharp
    // 末次 GET 请求的 UTC ticks（accept 循环写、Host 空闲 Timer 读，Interlocked 保证 64 位原子可见）。
    private long _lastRequestTicks;

    /// <summary>末次收到 GET 请求的时刻；构造时初始化为启动时刻（未连接过也从启动起计）。</summary>
    public DateTimeOffset LastRequestUtc =>
        new(Interlocked.Read(ref _lastRequestTicks), TimeSpan.Zero);
```

(b) 构造函数末尾（`_log = log ?? (_ => { });` 之后）加初始化：

```csharp
        _lastRequestTicks = DateTimeOffset.UtcNow.UtcTicks;
```

(c) `ServeOneAsync` 中 `if (request == "GET")` 成立后**首行**（写响应之前，确保客户端读到响应前已记时，测试无竞态）：

```csharp
            if (request == "GET")
            {
                Interlocked.Exchange(ref _lastRequestTicks, DateTimeOffset.UtcNow.UtcTicks);
                await writer.WriteLineAsync(JsonSerializer.Serialize(_snapshotProvider()));
                // 等客户端读完并主动断开（EOF）后再关管道，避免客户端 flush 撞已关闭管道
                //（MVP Task 4 引入的收尾握手，保持不变）。
                await reader.ReadLineAsync(connCts.Token);
            }
```

文件顶部 `using System.Threading;`——`Interlocked` 在 `System.Threading`，若报未引用则补（现有文件已用 `CancellationTokenSource` 等，通常已隐式经全局 using 覆盖；报错才加）。

- [ ] **Step 4: 跑全部测试确认通过**

```bash
cd "D:/Workspace/SensorMonitor" && dotnet test tests/SensorMonitor.Host.Tests 2>&1 | tail -3
```
Expected: `已通过! … 通过: 12`（旧 11 + 新 1）。

- [ ] **Step 5: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitor.Host/Ipc/PipeJsonServer.cs tests/SensorMonitor.Host.Tests/PipeJsonServerTests.cs && git commit -m "feat(host): track LastRequestUtc on pipe GET (R7)"
```

---

## Task 2: Host 空闲检查 Timer + 优雅退出

**Files:**
- Modify: `src/SensorMonitor.Host/Program.cs`

- [ ] **Step 1: 加空闲检查 Timer**

`Program.cs` 中 `var exit = new ManualResetEventSlim();` 之后、`Console.CancelKeyPress += …;` 之前插入：

```csharp
// R7：末次管道请求后空闲超 IdleTimeoutMinutes 分钟则自退，回收常驻提权进程。
// 有 band 固定时扩展每 1s 请求 → 永不空闲；无人用才触发。LastRequestUtc 初始=启动时刻，
// 完全没人连的 Host 启动 5min 后也自退。
const int IdleTimeoutMinutes = 5;
const int IdleCheckMs = 60_000;   // 每 60s 查一次，廉价
using var idleTimer = new Timer(_ =>
{
    try
    {
        if (DateTimeOffset.UtcNow - server.LastRequestUtc > TimeSpan.FromMinutes(IdleTimeoutMinutes))
        {
            HostLog.Write($"空闲 {IdleTimeoutMinutes} 分钟无管道请求，自退");
            exit.Set();
        }
    }
    catch (Exception ex) { HostLog.Write($"空闲检查失败: {ex}"); }  // Timer 线程异常不许带崩 Host
}, null, IdleCheckMs, IdleCheckMs);
```

（`exit.Set()` 幂等；回调与停机竞态无害——`exit` 在 `using` 作用域内，`Wait()` 返回后才随 `idleTimer` 一并释放。）

- [ ] **Step 2: 构建验证**

```bash
cd "D:/Workspace/SensorMonitor" && powershell -NoProfile -Command "Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null; schtasks //End //TN SensorMonitor.Host 2>/dev/null; dotnet build src/SensorMonitor.Host 2>&1 | tail -3
```
Expected: `0 个错误`。

- [ ] **Step 3: 手动验证空闲自退（Host 侧）**

> 5min 观察较久。**快速冒烟可选**：临时把 `IdleTimeoutMinutes = 5` 改为 `1`、`IdleCheckMs = 60_000` 改为 `10_000`，构建后跑一遍确认 ~1min 自退，再**改回 5 / 60_000**。以下按最终值 5min 描述。

管理员终端启动 Host（不连管道）：
```bash
cd "D:/Workspace/SensorMonitor" && powershell -NoProfile -Command "Start-Process 'src\SensorMonitor.Host\bin\Debug\net8.0\SensorMonitor.Host.exe' -Verb RunAs"
```
- 立刻确认进程在：`tasklist //FI "IMAGENAME eq SensorMonitor.Host.exe"`。
- 等约 5min 不做任何连接 → 进程消失、`%ProgramData%\SensorMonitor\host.log` 出现"空闲 5 分钟无管道请求，自退"。
- **反向验证**：再启动 Host，同时保持 Dock 有 band 固定（每 1s 轮询）→ 超 5min 仍**不**退出（有请求）。

- [ ] **Step 4: Commit**

```bash
cd "D:/Workspace/SensorMonitor" && git add src/SensorMonitor.Host/Program.cs && git commit -m "feat(host): self-exit after 5min idle (R7)"
```

---

## Task 3: 端到端复验 + 文档收口 + 推送

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/plans/2026-07-18-verification-and-next-phase.md`

- [ ] **Step 1: 端到端复验（与静默重拉配合）**

1. 确保计划任务注册（`schtasks //Query //TN SensorMonitor.Host` 可见）。
2. 让 Host 因空闲自退（或手动 `schtasks //End //TN SensorMonitor.Host` 模拟已退）。
3. 部署松散 dev 扩展并在 Dock 固定一个 band → 30s 内 SnapshotCache 静默通道拉回 Host、band 恢复读数、**无 UAC**（验证自退不破坏重拉链路）。

- [ ] **Step 2: 全量单测回归**

```bash
cd "D:/Workspace/SensorMonitor" && powershell -NoProfile -Command "Get-Process 'Microsoft.CmdPal.UI' -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null; schtasks //End //TN SensorMonitor.Host 2>/dev/null; dotnet test tests/SensorMonitor.Host.Tests 2>&1 | tail -3
```
Expected: `通过: 12`。

- [ ] **Step 3: CLAUDE.md 状态补一行**

`CLAUDE.md` 当前状态段的 A1 增强/A2 行之后加：

```markdown
- ✅ R7（2026-07-19）：Host 末次管道请求后 5min 无请求自退（`PipeJsonServer.LastRequestUtc` +
  Program.cs 空闲 Timer），把常驻提权进程收敛为按需；有 band 固定时每 1s 轮询永不退，自退后静默通道拉回。
```

- [ ] **Step 4: 路线计划标记 R7 完成**

`docs/plans/2026-07-18-verification-and-next-phase.md` 的按需表中 R7 行改为：

```markdown
| ✅ R7 | Host 空闲自退（5min 无管道请求自退，静默通道会拉回） | 已完成（2026-07-19）：见 `docs/superpowers/plans/2026-07-19-r7-host-idle-exit.md` |
```

- [ ] **Step 5: Commit + push**

```bash
cd "D:/Workspace/SensorMonitor" && git add CLAUDE.md docs/plans/2026-07-18-verification-and-next-phase.md && git commit -m "docs: R7 host idle self-exit complete" && git push
```

---

## Self-Review 结论

- **Spec 覆盖**：设计 ① LastRequestUtc → Task 1；② 空闲 Timer + 优雅退出 → Task 2；③ 与静默重拉配合 → Task 3 Step 1；测试（LastRequestUtc 随 GET 前进）→ Task 1 Step 1；4 项验收 → Task 1 Step 4（单测）/ Task 2 Step 3（自退 + 反向不退）/ Task 3 Step 1（重拉）。无缺口。
- **占位符**：无 TBD/TODO；每个改代码 Step 均含完整可粘贴代码；手动验证给了确切命令与预期（含 5min 观察的快速冒烟替代）。
- **一致性**：`LastRequestUtc`（Task 1 定义）被 Task 2 空闲 Timer 读取；`_lastRequestTicks`（Interlocked 读/写/初始化）三处一致；常量 `IdleTimeoutMinutes`/`IdleCheckMs` 仅 Task 2 内使用；坑 #6 停 Host/CmdPal 前置在所有构建/测试步骤一致。
