using System.Collections.ObjectModel;
using JableLens.Core.Http;
using JableLens.Core.Sites;
using JableLens.Mobile.Pages;

namespace JableLens.Mobile.Controls;

/// <summary>
/// 卡片网格：按屏幕宽度自动分列，滑到底自动读下一页，下拉刷新。
/// 先显示骨架卡片，内容到了以后卡片依次浮现。
/// </summary>
public sealed class CardGrid : ContentView
{
    private readonly CollectionView _list;
    private readonly GridItemsLayout _layout;
    private readonly RefreshView _refresh;
    private readonly Label _footer = new() { TextColor = Cards.C("Fg3"), FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 30) };
    private readonly Label _empty = new() { TextColor = Cards.C("Fg3"), FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(20, 60) };
    private readonly RangeCollection<CardItem> _items = [];
    private CancellationTokenSource? _cts;
    private int _page;
    private bool _hasMore;
    private bool _loading;
    private CardKind _kind;

    public Func<int, CancellationToken, Task<PageResult<CardItem>>>? Loader { get; set; }

    public double MinItemWidth { get; set; } = 170;

    public string EmptyText { get; set; } = "没有内容";

    /// <summary>网格上方跟着一起滚动的内容（标题、筛选……）。</summary>
    public View? Header
    {
        get => _list.Header as View;
        set => _list.Header = value;
    }

    public CardKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            _list.ItemTemplate = Cards.Template(value, Nav.Open);
            // 列表只量第一张卡片的大小；换了卡片种类要让它重新量
            _list.ItemsSource = null;
            _list.ItemsSource = _items;
            MinItemWidth = value switch { CardKind.Comic => 108, CardKind.Performer => 104, CardKind.Live => 160, CardKind.Term => 150, _ => 170 };
            UpdateSpan();
        }
    }

    public CardGrid()
    {
        _layout = new GridItemsLayout(2, ItemsLayoutOrientation.Vertical) { VerticalItemSpacing = 0, HorizontalItemSpacing = 0 };
        _list = new CollectionView
        {
            ItemsSource = _items, ItemsLayout = _layout, RemainingItemsThreshold = 12, Footer = _footer, EmptyView = _empty,
            // 卡片高度固定（见 Cards），只量第一张；骨架和真卡片一样大
            Margin = new Thickness(7, 0), ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
        };
        _list.HandlerChanged += (_, _) => Lists.Tune(_list);
        _list.RemainingItemsThresholdReached += (_, _) => { if (_hasMore && !_loading) _ = LoadNextAsync(); };
        _refresh = new RefreshView { Content = _list };
        _refresh.Refreshing += (_, _) => { Reload(); _refresh.IsRefreshing = false; };
        Kind = CardKind.Video;
        Content = _refresh;
        SizeChanged += (_, _) => UpdateSpan();
    }

    private void UpdateSpan()
    {
        if (Width <= 0) return;
        var span = Math.Max(1, (int)((Width - 14) / MinItemWidth));
        if (_layout.Span != span) _layout.Span = span;
    }

    public void Cancel() => _cts?.Cancel();

    /// <summary>从第 1 页重新读（换了筛选 / 标签时调用）。</summary>
    public void Reload()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _page = 0;
        _hasMore = true;
        _empty.Text = "";
        _footer.Text = "";
        _items.ReplaceAll(Enumerable.Range(0, 8).Select(_ => CardItem.Skeleton()));
        _ = LoadNextAsync();
    }

    /// <summary>直接给出全部内容（本地列表：片单、观看记录……）。</summary>
    public void SetItems(IEnumerable<CardItem> items)
    {
        _cts?.Cancel();
        _hasMore = false;
        _items.ReplaceAll(items);
        _empty.Text = EmptyText;
        _footer.Text = "";
    }

    private async Task LoadNextAsync()
    {
        if (Loader is null || _cts is null) return;
        var ct = _cts.Token;
        _loading = true;
        var next = _page + 1;
        if (next > 1) _footer.Text = "加载中…";
        try
        {
            var r = await Loader(next, ct);
            if (ct.IsCancellationRequested) return;
            // 一整页一次加进去（列表只收到一次通知、排一次版）
            if (next == 1) _items.ReplaceAll(r.Items);
            else _items.AddRange(r.Items);
            _page = next;
            _hasMore = r.HasMore && r.Items.Count > 0;
            _footer.Text = !_hasMore && _items.Count > 0 ? "— 到底了 —" : "";
            _empty.Text = EmptyText;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // 网站改版、返回了半截数据之类：显示出错，别一直停在骨架屏
            if (ct.IsCancellationRequested) return;
            if (next == 1) _items.Clear();
            _hasMore = false;
            var msg = "读取失败：" + e.Message;
            if (_items.Count == 0) _empty.Text = msg + "\n下拉重试";
            else _footer.Text = msg;
        }
        finally
        {
            _loading = false;
        }
    }
}

/// <summary>能一次加入 / 替换一批的 ObservableCollection（列表只收到一次通知）。</summary>
public sealed class RangeCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        CheckReentrancy();
        var start = Count;
        foreach (var i in list) Items.Add(i);
        Changed(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Add, list, start));
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        Changed(new System.Collections.Specialized.NotifyCollectionChangedEventArgs(
            System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
    }

    private void Changed(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(e);
    }
}

/// <summary>原生列表（RecyclerView）的滑动调优。</summary>
public static class Lists
{
    public static void Tune(CollectionView list)
    {
        if (list.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView rv) return;
        // 刚滑出屏幕的几张卡片留着不回收，来回小幅滑动不用重新填内容
        rv.SetItemViewCacheSize(8);
        // 关掉系统的增删动画（卡片有自己的浮现动画），加载下一页时不再整批重排做动画
        rv.SetItemAnimator(null);
    }
}
