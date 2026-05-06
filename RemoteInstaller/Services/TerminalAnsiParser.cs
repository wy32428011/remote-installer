using System;
using System.Collections.Generic;
using System.Text;
using RemoteInstaller.Models;

namespace RemoteInstaller.Services;

/// <summary>
/// 终端 ANSI 子集解析器。
/// 仅处理第一版需要的 SGR 颜色、加粗与 reset，不实现完整终端模拟。
/// </summary>
public sealed class TerminalAnsiParser
{
    private readonly AnsiRenderState _state = new();

    /// <summary>
    /// 默认前景色资源键。
    /// </summary>
    public const string DefaultForegroundKey = "TerminalForegroundBrush";

    /// <summary>
    /// 默认背景色资源键。
    /// </summary>
    public const string DefaultBackgroundKey = "TerminalBackgroundBrush";

    /// <summary>
    /// 重置解析器状态。
    /// 在清屏或重建新会话时调用，避免旧样式泄露到新输出。
    /// </summary>
    public void Reset()
    {
        _state.Reset();
    }

    /// <summary>
    /// 解析一个终端输出片段。
    /// 解析器会保留跨 chunk 的样式状态，适合流式终端输出。
    /// </summary>
    public IReadOnlyList<TerminalOutputSpan> Parse(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return Array.Empty<TerminalOutputSpan>();
        }

        var spans = new List<TerminalOutputSpan>();
        var textBuffer = new StringBuilder();

        for (var index = 0; index < chunk.Length; index++)
        {
            var character = chunk[index];
            if (character != '\u001b')
            {
                textBuffer.Append(character);
                continue;
            }

            FlushBuffer(spans, textBuffer, _state);

            if (index + 1 >= chunk.Length)
            {
                break;
            }

            var nextCharacter = chunk[index + 1];
            if (nextCharacter == '[')
            {
                index = ConsumeControlSequence(chunk, index + 2, _state);
                continue;
            }

            if (nextCharacter == ']')
            {
                index = ConsumeOperatingSystemCommand(chunk, index + 2);
                continue;
            }
        }

        FlushBuffer(spans, textBuffer, _state);
        return spans;
    }

    private static void FlushBuffer(List<TerminalOutputSpan> spans, StringBuilder buffer, AnsiRenderState state)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        spans.Add(new TerminalOutputSpan
        {
            Text = buffer.ToString(),
            ForegroundKey = state.ForegroundKey,
            BackgroundKey = state.BackgroundKey,
            IsBold = state.IsBold
        });

        buffer.Clear();
    }

    private static int ConsumeControlSequence(string chunk, int startIndex, AnsiRenderState state)
    {
        var parameterBuffer = new StringBuilder();
        var index = startIndex;

        while (index < chunk.Length)
        {
            var current = chunk[index];
            if (current is >= '@' and <= '~')
            {
                if (current == 'm')
                {
                    ApplySelectGraphicRendition(parameterBuffer.ToString(), state);
                }

                return index;
            }

            parameterBuffer.Append(current);
            index++;
        }

        return chunk.Length - 1;
    }

    private static int ConsumeOperatingSystemCommand(string chunk, int startIndex)
    {
        for (var index = startIndex; index < chunk.Length; index++)
        {
            if (chunk[index] == '\a')
            {
                return index;
            }

            if (chunk[index] == '\u001b' && index + 1 < chunk.Length && chunk[index + 1] == '\\')
            {
                return index + 1;
            }
        }

        return chunk.Length - 1;
    }

    private static void ApplySelectGraphicRendition(string parameters, AnsiRenderState state)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            state.Reset();
            return;
        }

        var parts = parameters.Split(';', StringSplitOptions.None);
        if (parts.Length == 0)
        {
            state.Reset();
            return;
        }

        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var code))
            {
                continue;
            }

            switch (code)
            {
                case 0:
                    state.Reset();
                    break;
                case 1:
                    state.IsBold = true;
                    break;
                case 22:
                    state.IsBold = false;
                    break;
                case 39:
                    state.ForegroundKey = DefaultForegroundKey;
                    break;
                case 49:
                    state.BackgroundKey = null;
                    break;
                case >= 30 and <= 37:
                    state.ForegroundKey = MapForegroundCode(code);
                    break;
                case >= 40 and <= 47:
                    state.BackgroundKey = MapBackgroundCode(code);
                    break;
                case >= 90 and <= 97:
                    state.ForegroundKey = MapBrightForegroundCode(code);
                    break;
                case >= 100 and <= 107:
                    state.BackgroundKey = MapBrightBackgroundCode(code);
                    break;
            }
        }
    }

    private static string MapForegroundCode(int code)
    {
        return code switch
        {
            30 => "TerminalMutedBrush",
            31 => "ErrorBrush",
            32 => "SuccessBrush",
            33 => "WarningBrush",
            34 => "InfoBrush",
            35 => "TerminalPinkBrush",
            36 => "AccentBrush",
            37 => "TerminalForegroundBrush",
            _ => DefaultForegroundKey
        };
    }

    private static string MapBackgroundCode(int code)
    {
        return code switch
        {
            40 => "TerminalMutedBackgroundBrush",
            41 => "TerminalErrorBackgroundBrush",
            42 => "TerminalSuccessBackgroundBrush",
            43 => "TerminalWarningBackgroundBrush",
            44 => "TerminalInfoBackgroundBrush",
            45 => "TerminalPinkBackgroundBrush",
            46 => "TerminalAccentBackgroundBrush",
            47 => "TerminalLightBackgroundBrush",
            _ => DefaultBackgroundKey
        };
    }

    private static string MapBrightForegroundCode(int code)
    {
        return code switch
        {
            90 => "DarkSecondaryTextBrush",
            91 => "TerminalBrightErrorBrush",
            92 => "TerminalBrightSuccessBrush",
            93 => "TerminalBrightWarningBrush",
            94 => "TerminalBrightInfoBrush",
            95 => "TerminalBrightPinkBrush",
            96 => "TerminalBrightAccentBrush",
            97 => "TerminalBrightForegroundBrush",
            _ => DefaultForegroundKey
        };
    }

    private static string MapBrightBackgroundCode(int code)
    {
        return code switch
        {
            100 => "TerminalBrightMutedBackgroundBrush",
            101 => "TerminalBrightErrorBackgroundBrush",
            102 => "TerminalBrightSuccessBackgroundBrush",
            103 => "TerminalBrightWarningBackgroundBrush",
            104 => "TerminalBrightInfoBackgroundBrush",
            105 => "TerminalBrightPinkBackgroundBrush",
            106 => "TerminalBrightAccentBackgroundBrush",
            107 => "TerminalBrightLightBackgroundBrush",
            _ => DefaultBackgroundKey
        };
    }

    private sealed class AnsiRenderState
    {
        public string ForegroundKey { get; set; } = DefaultForegroundKey;

        public string? BackgroundKey { get; set; }

        public bool IsBold { get; set; }

        public void Reset()
        {
            ForegroundKey = DefaultForegroundKey;
            BackgroundKey = null;
            IsBold = false;
        }
    }
}
