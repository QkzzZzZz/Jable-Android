using JableLens.Core.Http;
using JableLens.Mobile.Pages;

namespace JableLens.Mobile.Controls;

/// <summary>首页的一行：标题（可点「全部」）+ 横向滑动的卡片。加载中显示骨架卡片；没有内容时整行隐藏。</summary>
public sealed class MediaRow : VerticalStackLayout
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<CardItem>>> _loader;
    private readonly CollectionView _list;
    private readonly Label _status = new() { TextColor = Cards.C("Fg3"), FontSize = 13, Margin = new Thickness(16, 0, 16, 8), IsVisible = false };

    public MediaRow(string title, Func<CancellationToken, Task<IReadOnlyList<CardItem>>> loader, Action? seeAll, CardKind kind = CardKind.Video)
    {
        _loader = loader;
        var width = kind == CardKind.Comic ? 116 : kind == CardKind.Performer ? 104 : 172;
        var height = Cards.HeightOf(kind, width);

        var head = new Grid { Padding = new Thickness(16, 18, 16, 10), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new Label { Text = title, FontSize = 18, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center });
        if (seeAll is not null)
        {
            var all = new Label { Text = "全部 ›", FontSize = 13, TextColor = Cards.C("Fg2"), VerticalOptions = LayoutOptions.Center, Padding = new Thickness(10, 4, 0, 4) };
            Cards.Tap(all, seeAll);
            head.Add(all, 1);
        }
        Add(head);

        var real = new DataTemplate(() => { var v = Cards.Build(kind, Nav.Open); v.WidthRequest = width; return v; });
        var skeleton = new DataTemplate(() => { var v = Cards.Skeleton(kind); v.WidthRequest = width; return v; });
        _list = new CollectionView
        {
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal),
            ItemTemplate = new Picker(real, skeleton),
            HeightRequest = height,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Margin = new Thickness(11, 0),
            ItemSizingStrategy = ItemSizingStrategy.MeasureFirstItem,
            ItemsSource = Enumerable.Range(0, 5).Select(_ => CardItem.Skeleton()).ToList(),
        };
        _list.HandlerChanged += (_, _) => Lists.Tune(_list);
        Add(_list);
        Add(_status);
    }

    private sealed class Picker(DataTemplate real, DataTemplate skeleton) : DataTemplateSelector
    {
        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item is CardItem { IsSkeleton: true } ? skeleton : real;
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            var items = await _loader(ct);
            if (ct.IsCancellationRequested) return;
            if (items.Count == 0) { IsVisible = false; return; }
            _list.ItemsSource = items;
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _list.IsVisible = false;
            _status.Text = "读取失败：" + e.Message;
            _status.IsVisible = true;
        }
    }
}
