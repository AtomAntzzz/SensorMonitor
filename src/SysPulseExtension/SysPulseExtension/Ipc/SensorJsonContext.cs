using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SysPulseExtension.Ipc;

/// <summary>
/// Source-generated JSON 上下文（trim/AOT 安全）。打包 Release 裁剪构建会置
/// JsonSerializerIsReflectionEnabledByDefault=false，此时反射式 JsonSerializer.Deserialize
/// 抛 InvalidOperationException（A2 实测根因：4 控件全"Host 未运行"）。
/// PipeSensorClient / SlotStore 的 (反)序列化统一走本上下文的 JsonTypeInfo 重载。
/// </summary>
[JsonSerializable(typeof(SensorSnapshot))]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "StringMap")]
internal sealed partial class SensorJsonContext : JsonSerializerContext;
