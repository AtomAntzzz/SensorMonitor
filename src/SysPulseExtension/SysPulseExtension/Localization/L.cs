using System.Globalization;
using System.Resources;

namespace SysPulseExtension.Localization;

/// <summary>
/// 统一取串层（i18n）。资源 = Localization/Strings.resx（英文=中性/默认，随主程序集内嵌）
/// + Strings.zh-CN.resx（中文，出卫星程序集 zh-CN/SysPulseExtension.resources.dll）。
/// 语言跟随系统 UI 语言：扩展跑在用户会话里，CultureInfo.CurrentUICulture 即用户 Windows 显示语言，
/// ResourceManager 据此选卫星或回落内嵌英文中性；v1 无设置页手动覆盖。
/// baseName = RootNamespace + 文件夹 + 文件名 = "SysPulseExtension.Localization.Strings"。
/// </summary>
internal static class L
{
    private static readonly ResourceManager Rm =
        new("SysPulseExtension.Localization.Strings", typeof(L).Assembly);

    /// <summary>取本地化串；缺键不抛异常，回落键名本身（便于发现漏翻）。
    /// 串的选取按 CurrentUICulture（UI 语言）。</summary>
    public static string Get(string key) =>
        Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>取本地化格式串并套入运行时参数（如 "Next {0}" / "⚠ No update for {0:F0}s"）。
    /// 串选取用 CurrentUICulture；数值/日期的**格式化**用 CurrentCulture（与原插值串一致，CA1305）。</summary>
    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
