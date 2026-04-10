using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RemoteInstaller.Models;

namespace RemoteInstaller.Converters;

/// <summary>
/// 布尔到可见性转换器
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}

/// <summary>
/// Null转Visibility转换器 - null时显示，否则隐藏
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Null转Visibility转换器 - null时隐藏，否则显示
/// </summary>
public class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Null转Bool转换器 - null时返回false，否则返回true
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状态到颜色转换器
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HostStatus status)
        {
            return status switch
            {
                HostStatus.Online => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")),
                HostStatus.Offline => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                HostStatus.Connecting => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
                HostStatus.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
            };
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状态到图标转换器
/// </summary>
public class StatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HostStatus status)
        {
            return status switch
            {
                HostStatus.Online => MaterialDesignThemes.Wpf.PackIconKind.CheckCircle,
                HostStatus.Offline => MaterialDesignThemes.Wpf.PackIconKind.CloseCircle,
                HostStatus.Connecting => MaterialDesignThemes.Wpf.PackIconKind.Loading,
                HostStatus.Error => MaterialDesignThemes.Wpf.PackIconKind.AlertCircle,
                _ => MaterialDesignThemes.Wpf.PackIconKind.Help
            };
        }
        return MaterialDesignThemes.Wpf.PackIconKind.Help;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔到颜色转换器
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"));
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 日志级别到样式转换器
/// </summary>
public class LogLevelToStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is LogLevel level)
        {
            return level switch
            {
                LogLevel.Info => Application.Current?.FindResource("LogInfoStyle"),
                LogLevel.Success => Application.Current?.FindResource("LogSuccessStyle"),
                LogLevel.Warning => Application.Current?.FindResource("LogWarningStyle"),
                LogLevel.Error => Application.Current?.FindResource("LogErrorStyle"),
                _ => Application.Current?.FindResource("LogTextBlockStyle")
            };
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值取反转换器
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// 枚举到布尔转换器
/// </summary>
public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;
        
        var enumValue = Enum.Parse(value.GetType(), parameter.ToString());
        return Equals(value, enumValue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter != null)
        {
            return boolValue ? Enum.Parse(targetType, parameter.ToString()) : null;
        }
        return null;
    }
}

/// <summary>
/// 枚举到可见性转换器
/// </summary>
public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return Visibility.Collapsed;
        
        var enumValue = Enum.Parse(value.GetType(), parameter.ToString());
        return Equals(value, enumValue) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 字符串到布尔转换器（用于 RadioButton 的 IsChecked 绑定）
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && parameter is string param)
        {
            return str == param;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // 当 RadioButton 被选中时，将 parameter 值返回给绑定的字符串属性
        if (value is bool boolValue && parameter is string param && boolValue)
        {
            return param;
        }
        return Binding.DoNothing;
    }
}

/// <summary>
/// 字符串到可见性转换器（空字符串不可见）
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            return string.IsNullOrWhiteSpace(str) ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔到背景色转换器
/// </summary>
public class BoolToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E8F5E9"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFEBEE"));
        }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F5F5F5"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔到图标转换器
/// </summary>
public class BoolToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? MaterialDesignThemes.Wpf.PackIconKind.CheckCircle
                : MaterialDesignThemes.Wpf.PackIconKind.CloseCircle;
        }
        return MaterialDesignThemes.Wpf.PackIconKind.Help;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔到颜色转换器 2（用于不同场景）
/// </summary>
public class BoolToColorConverter2 : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue 
                ? Brushes.Green
                : Brushes.Red;
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 日志级别到颜色转换器 (用于 MainWindow.xaml 中的日志着色)
/// </summary>
public class LogLevelToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Models.LogLevel level)
        {
            return level switch
            {
                Models.LogLevel.Info => (Color)ColorConverter.ConvertFromString("#3B82F6"),    // 蓝色
                Models.LogLevel.Success => (Color)ColorConverter.ConvertFromString("#10B981"),  // 绿色
                Models.LogLevel.Warning => (Color)ColorConverter.ConvertFromString("#F59E0B"),  // 黄色
                Models.LogLevel.Error => (Color)ColorConverter.ConvertFromString("#EF4444"),    // 红色
                _ => (Color)ColorConverter.ConvertFromString("#E5E7EB")
            };
        }
        return (Color)ColorConverter.ConvertFromString("#E5E7EB");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 步骤状态到颜色转换器
/// </summary>
public class StepStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isComplete)
        {
            return isComplete 
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"))  // 绿色
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")); // 灰色
        }
        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 步骤状态到字重转换器
/// </summary>
public class StepStatusToWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isRunning)
        {
            return isRunning ? FontWeights.SemiBold : FontWeights.Normal;
        }
        return FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 终端行类型到可见性转换器
/// </summary>
public class TerminalLineTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TerminalLineType lineType && parameter is string targetType2)
        {
            var isMatch = lineType switch
            {
                TerminalLineType.Command => targetType2 == "Command",
                TerminalLineType.Output => targetType2 == "Output",
                TerminalLineType.Error => targetType2 == "Error",
                TerminalLineType.System => targetType2 == "System",
                _ => false
            };
            return isMatch ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 字符串颜色名称到颜色转换器
/// </summary>
public class StringToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string colorName && !string.IsNullOrEmpty(colorName))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorName);
                return color;
            }
            catch
            {
                return Colors.Gray;
            }
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
