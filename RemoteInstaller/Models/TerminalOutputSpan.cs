namespace RemoteInstaller.Models;

/// <summary>
/// 终端可渲染输出片段。
/// 用于把 ANSI 解析后的文本样式信息传给 RichTextBox。
/// </summary>
public class TerminalOutputSpan
{
    /// <summary>
    /// 当前片段对应的纯文本内容。
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// 前景色资源键。
    /// </summary>
    public string ForegroundKey { get; set; } = "TerminalForegroundBrush";

    /// <summary>
    /// 背景色资源键。
    /// 没有背景色时保持为空。
    /// </summary>
    public string? BackgroundKey { get; set; }

    /// <summary>
    /// 是否加粗显示。
    /// </summary>
    public bool IsBold { get; set; }
}
