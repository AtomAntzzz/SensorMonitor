# R7 — Host 空闲自退 Design

> 状态：已获用户批准（2026-07-19，方案 A）。对应路线 `docs/plans/2026-07-18-verification-and-next-phase.md` 的 R7。

## 目标

把常驻的**提权** Host 进程从"登录即常驻"收敛为"按需存活"：**末次管道请求后 5 分钟无请求则自退**。
band 固定在 dock 时扩展每 1s 轮询、Host 永不空闲；无人使用时 Host 自退回收资源，再次固定 band 时
静默通道自动拉回。纯 Host 侧改动，扩展零改动。

## 需求定型（澄清结论）

- **空闲判据 = 末次管道 GET 请求后 5 分钟无新请求**（N=5min）。
- `LastRequestUtc` **初始 = Host 启动时刻**——完全没人连的 Host 启动 5min 后也自退（"没人用就回收"）。
- 方案 A：独立空闲检查 Timer + `PipeJsonServer.LastRequestUtc`（职责清晰、管道侧可测），
  优于把空闲判断塞进 1s 刷新 Timer（C，耦合刷新与生命周期）或事件回调（B，多一层管道）。

## 前提事实（已核实）

- Host `Program.cs`：`server.Start()` 后阻塞在 `exit.Wait()`（`ManualResetEventSlim`）永不退出；
  refresh Timer 每 1s 刷传感器缓存；`using` 持有 server/timer，退出时自动释放。
- `PipeJsonServer.ServeOneAsync`：收到一行 `"GET"` → 回一行 JSON 快照。当前**不记录**请求时间。
  accept 循环在后台 `Task.Run`（`AcceptLoopAsync`）。
- 扩展 `SnapshotCache` 每 1s 一次 `TryFetch`（有 band 固定时）；Host 未运行时静默通道 30s 内 `schtasks /Run` 拉回。
- 计划任务 `SensorMonitor.Host`（`ONLOGON /RL HIGHEST`）自退后仍注册，靠它拉回，自退不注销。
- `PipeJsonServerTests`（`tests/SensorMonitor.Host.Tests`）已有测试设施，可 TDD。

## 设计（方案 A）

### ① PipeJsonServer 记录末次请求时间

`src/SensorMonitor.Host/Ipc/PipeJsonServer.cs`：
- 新增 `public DateTimeOffset LastRequestUtc { get; private set; }`，构造函数中初始化为 `DateTimeOffset.UtcNow`。
- 跨线程：accept 循环任务写、Host 检查 Timer 读。`DateTimeOffset` 非原子，用 `long _lastRequestTicks`
  （`Interlocked.Exchange` 写 / `Interlocked.Read` 读）承载 `UtcNow.UtcTicks`，`LastRequestUtc` 由其换算。
- `ServeOneAsync` 中判定 `request == "GET"` 成立时（回快照之前或之后均可），刷新末次请求时间。

### ② Host 空闲检查 Timer + 优雅退出

`src/SensorMonitor.Host/Program.cs`：
- 常量 `const int IdleTimeoutMinutes = 5;`、`const int IdleCheckMs = 60_000;`（每 60s 查一次，廉价）。
- `server.Start()` 之后、`exit.Wait()` 之前，建空闲检查 Timer：
  每 `IdleCheckMs` 检查 `DateTimeOffset.UtcNow - server.LastRequestUtc > TimeSpan.FromMinutes(IdleTimeoutMinutes)`，
  成立则 `HostLog.Write("空闲 5 分钟无管道请求，自退");` + `exit.Set();`。Timer 以 `using` 持有。
- `exit.Set()` → `exit.Wait()` 返回 → `using` 依次释放 server / refreshTimer / idleTimer → `return 0`。

### ③ 与既有机制配合（无需改动，仅说明）

| 场景 | 行为 |
|------|------|
| band 固定在 dock | SnapshotCache 每 1s 请求 → Host 永不空闲、不退 |
| 无 band / 不看 dock | 5min 无请求 → Host 自退，回收常驻提权进程 |
| 自退后再固定 band | SnapshotCache 检测 null → 静默通道 30s 内 `schtasks /Run` 拉回 |
| 浏览页打开 | `GetItems` 一次 `TryFetch` → 刷新末次请求时间 |

## 边界 / 错误处理

- 空闲检查 Timer 回调需自身防崩（`try/catch`，同 refresh Timer 的 F6 处理），异常不带崩 Host。
- 停机竞态：`exit.Set()` 后 `using` 释放 Timer，回调可能与释放并发 → `Change`/回调内吞 `ObjectDisposedException`（同 refresh Timer 现有处理）。
- 5min 用 `const`，将来 R2 设置页可提为可配（本期不做）。

## 测试

- **单测（TDD，`PipeJsonServerTests`）**：新增用例——`LastRequestUtc` 构造后 ≈ 启动时刻；服务一次 GET 后
  `LastRequestUtc` 前进到更晚的时刻（用现有"连管道发 GET 收响应"的测试模式，断言请求前后 `LastRequestUtc` 变大）。
- **手动验证（Host 侧）**：管理员启动 Host（或计划任务拉起）→ **不连管道** → 观察 host.log 在约 5min 后
  出现"空闲…自退"、进程消失。再验证有 band 固定时 Host 持续存活不退。

## 验收清单

1. `PipeJsonServerTests` 新用例通过（`LastRequestUtc` 随 GET 前进）；11 → 12 单测全绿。
2. Host 启动后不连管道 → 约 5min 后 host.log 出现自退记录、进程消失。
3. Dock 固定 band（每 1s 轮询）时 Host 持续存活，不会 5min 自退。
4. 自退后固定/查看 band → 静默通道 30s 内拉回，Dock 恢复读数、全程无 UAC。

## 明确不做（YAGNI）

- 空闲时长可配（提为设置项）→ R2 设置页时再做。
- Host 侧手动 `--stop` 命令 → 非本需求（自退已覆盖回收目标）。
- 扩展侧任何改动 → 纯 Host 侧即可达成。
