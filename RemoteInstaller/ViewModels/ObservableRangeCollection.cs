using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace RemoteInstaller.ViewModels;

/// <summary>
/// 支持批量替换和批量重排的可观察集合。
/// 用于降低 WPF 列表在过滤、刷新和排序时产生的 CollectionChanged 通知次数。
/// </summary>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    /// <summary>
    /// 使用新数据一次性替换集合内容。
    /// </summary>
    public void ReplaceRange(IEnumerable<T> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var newItems = items.ToList();
        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in newItems)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        RaiseReset();
    }

    /// <summary>
    /// 一次性追加多条数据。
    /// </summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        var newItems = items.ToList();
        if (newItems.Count == 0)
        {
            return;
        }

        _suppressNotifications = true;
        try
        {
            foreach (var item in newItems)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        RaiseReset();
    }

    /// <summary>
    /// 将当前集合重排为目标顺序。
    /// 当前实现使用一次 Reset 换取更少的 UI 移动通知，适合任务列表刷新这种高频路径。
    /// </summary>
    public void MoveRangeToMatch(IEnumerable<T> orderedItems)
    {
        ReplaceRange(orderedItems);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suppressNotifications)
        {
            return;
        }

        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_suppressNotifications)
        {
            return;
        }

        base.OnPropertyChanged(e);
    }

    /// <summary>
    /// 发出一次 Reset 通知，让 WPF 只刷新一次绑定视图。
    /// </summary>
    private void RaiseReset()
    {
        base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
