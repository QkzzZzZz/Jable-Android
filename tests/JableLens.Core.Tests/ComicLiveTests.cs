using JableLens.Core.Sources.Live;
using JableLens.Core.Sources.MyComic;
using Xunit;
using static JableLens.Core.Tests.ParserTests;

namespace JableLens.Core.Tests;

/// <summary>漫画（mycomic.com）和直播（myavlive）的样本，2026-10-25 抓取。</summary>
public class ComicLiveTests
{
    [Fact]
    public void MyComicList_ParsesCards()
    {
        var html = Fixture("mycomic_list.html");
        var cards = MyComicSite.ParseList(html);
        Assert.True(cards.Count >= 18);
        var c = cards.First(x => x.Id == 55355);
        Assert.Equal("猎人游戏W", c.Title);
        Assert.Equal("https://biccam.com/comics/55355-9e7018.jpg", c.CoverUrl);
        Assert.Equal("第07话", c.Latest);
        Assert.True(MyComicSite.HasNextPage(html, 1));
    }

    [Fact]
    public void MyComicDetail_ParsesInfoAndChapterGroups()
    {
        var d = MyComicSite.ParseDetail(Fixture("mycomic_detail.html"), 1128);
        Assert.Equal("ONE PIECE航海王", d.Title);
        Assert.Equal("尾田荣一郎", d.Author);
        Assert.Equal("连载中", d.Status);
        Assert.Equal("日本", d.Region);
        Assert.Contains("热血", d.Tags);
        Assert.Contains("1997", d.Summary);
        Assert.StartsWith("财富 名声 势力", d.Intro);
        Assert.Equal(["单话", "单行本", "番外篇"], d.Groups.Select(g => g.Name));
        Assert.Equal(new ComicChapter(817597, "第1150话"), d.Groups[0].Chapters[0]);
    }

    [Fact]
    public void MyComicChapter_ParsesAllPages()
    {
        var pages = MyComicSite.ParsePages(Fixture("mycomic_chapter.html"));
        Assert.True(pages.Count > 30);
        Assert.Equal("https://biccam.com/chapters/818097/1-bc9e34.jpg", pages[0].Url);
        Assert.Equal("https://biccam.com/chapters/818097/4-b57a9e.jpg", pages[3].Url); // 懒加载的 data-src
        Assert.Equal(1400 / 959.0, pages[0].Ratio, 3);
    }

    [Fact]
    public void LiveModels_ParsesJson()
    {
        var (models, total) = LiveSite.ParseModels(Fixture("live_models.json"));
        Assert.Equal(243, total);
        Assert.Equal(60, models.Count);
        var m = models.First(x => x.Username == "Clara-166");
        Assert.Equal(285034145, m.Id);
        Assert.Equal(96, m.Viewers);
        Assert.True(m.IsNew);
        Assert.Equal("https://img.doppiocdn.org/snapshot/285034145/1792928160", m.SnapshotUrl);
        Assert.Equal("https://zh.myavlive.com/Clara-166", m.RoomUrl);
        Assert.Contains("filterGroupTags=%5B%5B%22tagLanguageChinese%22%5D%5D", LiveUrls.Models("tagLanguageChinese", 2));
        Assert.Contains("offset=60", LiveUrls.Models(null, 2));
    }
}
