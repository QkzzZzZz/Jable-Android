using JableLens.Core.Models;
using JableLens.Core.Sources.Jable;
using JableLens.Core.Sources.MissAv;
using static JableLens.Core.Tests.ParserTests;

namespace JableLens.Core.Tests;

/// <summary>Jable / MissAV 页面样本是 2026-10-24 用 WebView2 读到的渲染后 HTML。</summary>
public class SiteParserTests
{
    [Fact]
    public void JableList_ParsesCards()
    {
        var html = Fixture("jable_latest.html");
        var cards = JableParser.ParseList(html);
        Assert.Equal(24, cards.Count);
        var c = cards[0];
        Assert.Equal("https://jable.tv/videos/cjod-540/", c.PageUrl);
        Assert.Equal("CJOD-540", c.Code);
        Assert.StartsWith("CJOD-540 【待望】", c.Title);
        Assert.EndsWith("320x180/1.jpg", c.CoverUrl);
        Assert.EndsWith("_preview.mp4", c.PreviewUrl);
        Assert.Equal("1:58:00", c.Duration);
        Assert.True(JableParser.HasNextPage(html, 1));
    }

    [Fact]
    public void JableSearch_SingleResult_NoNextPage()
    {
        var html = Fixture("jable_search.html");
        var c = Assert.Single(JableParser.ParseList(html));
        Assert.Equal("MFYD-202", c.Code);
        Assert.False(JableParser.HasNextPage(html, 1));
        Assert.True(JableParser.HasNextPage(Fixture("jable_search2.html"), 1));
    }

    [Fact]
    public void JableVideo_ParsesDetail()
    {
        var d = JableParser.ParseVideo(Fixture("jable_video.html"), "https://jable.tv/videos/mfyd-202/");
        Assert.Equal("MFYD-202", d.Code);
        Assert.StartsWith("MFYD-202 元美人", d.Title);
        Assert.Equal(new DateOnly(2026, 10, 15), d.ReleaseDate);
        var p = Assert.Single(d.Performers);
        Assert.Equal("和香なつき", p.Name);
        Assert.Contains("/models/", p.PageUrl);
        Assert.Contains(d.Tags, t => t.Name == "中出");
        Assert.EndsWith(".m3u8", d.StreamUrl);
        Assert.EndsWith("preview.jpg", d.CoverUrl);
        Assert.NotEmpty(d.Related);
        Assert.DoesNotContain(d.Related, r => r.PageUrl == d.PageUrl);
    }

    [Fact]
    public void JableModelPage_TitleAndCards()
    {
        var html = Fixture("jable_model.html");
        Assert.Equal("和香なつき", JableParser.ParseListTitle(html));
        Assert.Equal(18, JableParser.ParseList(html).Count);
    }

    [Fact]
    public void JableCategoriesAndModels()
    {
        var cats = JableParser.ParseCategories(Fixture("jable_cats.html"));
        Assert.Contains(cats, c => c.Name == "主奴調教" && c.VideoCount == 5479);
        var groups = JableParser.ParseTagGroups(Fixture("jable_cats.html"));
        Assert.Equal("衣著", groups[0].Name);
        Assert.DoesNotContain(groups, g => g.Name == "選片");
        Assert.Contains(groups[0].Tags, t => t.Name == "黑絲" && t.PageUrl == "https://jable.tv/tags/black-pantyhose/");
        Assert.True(groups.Sum(g => g.Tags.Count) > 80);
        var models = JableParser.ParseModels(Fixture("jable_models.html"));
        Assert.Contains(models, m => m.Name == "仲間あずみ" && m.VideoCount == 1);
    }

    [Fact]
    public void MissAvDirectories()
    {
        // 样本 2026-10-25 抓取
        var actresses = MissAvParser.ParseActresses(Fixture("missav_actresses.html"));
        Assert.Equal(24, actresses.Count);
        Assert.Contains(actresses, a => a.Name == "波多野结衣" && a.Info.Contains("5701 条影片") && a.AvatarUrl!.StartsWith("https://fourhoi.com/"));
        var ranking = MissAvParser.ParseActresses(Fixture("missav_ranking.html"));
        Assert.Equal(100, ranking.Count);
        Assert.Equal("逢泽みゆ", ranking[0].Name);
        Assert.Contains("第 1 名", ranking[0].Info);
        var genres = MissAvParser.ParseTerms(Fixture("missav_genres.html"), "genres");
        Assert.Contains(genres, g => g.Name == "中出" && g.VideoCount == 223836);
        Assert.DoesNotContain(genres, g => g.Name == "VR" && g.VideoCount is null);
        var makers = MissAvParser.ParseTerms(Fixture("missav_makers.html"), "makers");
        Assert.Contains(makers, m => m.Name == "Moody's" && m.VideoCount == 18493);
        Assert.Equal(36, makers.Count);
    }

    [Theory]
    [InlineData("[中字]PRED-816 标题", "https://jable.tv/videos/pred-816-c/", "PRED-816")]
    [InlineData("没有番号的标题", "https://jable.tv/videos/abc-123/", "ABC-123")]
    public void JableCodeFrom(string title, string url, string expected) =>
        Assert.Equal(expected, JableParser.CodeFrom(title, url));

    [Fact]
    public void JableUrls_Paging()
    {
        Assert.Equal("https://jable.tv/latest-updates/", JableUrls.Latest());
        Assert.Equal("https://jable.tv/hot/3/", JableUrls.Hot(3));
        Assert.Equal("https://jable.tv/tags/creampie/2/", JableUrls.WithPage("https://jable.tv/tags/creampie/", 2));
        Assert.Equal("https://jable.tv/tags/creampie/5/", JableUrls.WithPage("https://jable.tv/tags/creampie/2/", 5));
        Assert.Equal("https://jable.tv/videos/mfyd-202/", JableUrls.Video("MFYD-202"));
    }

    [Fact]
    public void MissAvList_ParsesCards()
    {
        var html = Fixture("missav_madou.html");
        var cards = MissAvParser.ParseList(html);
        Assert.True(cards.Count >= 12);
        var c = cards.First(x => x.Code == "CUS-2594");
        Assert.Equal(VideoSource.MissAv, c.Source);
        Assert.Equal("https://fourhoi.com/cus-2594/cover-t.jpg", c.CoverUrl);
        Assert.Equal("0:20:35", c.Duration);
        Assert.Contains("粉红色情趣内衣", c.Title);
        Assert.True(MissAvParser.HasNextPage(html, 1));

        Assert.Contains(MissAvParser.ParseList(Fixture("missav_klive.html")), x => x.Code.StartsWith("KBJ-"));
    }

    [Fact]
    public void MissAvVideo_ParsesDetail()
    {
        var d = MissAvParser.ParseVideo(Fixture("missav_video.html"), "https://missav.ws/cn/mfyd-202");
        Assert.Equal("MFYD-202", d.Code);
        Assert.Equal(new DateOnly(2026, 10, 16), d.ReleaseDate);
        Assert.Equal("和香なつき", Assert.Single(d.Performers).Name);
        Assert.Contains(d.Tags, t => t.Name == "熟女");
        Assert.Equal("https://fourhoi.com/mfyd-202/cover-n.jpg", d.CoverUrl);
        Assert.Null(d.StreamUrl);
    }

    [Fact]
    public void MissAvStreamUrl_UnpacksPackedScript()
    {
        Assert.Equal("https://surrit.com/e92bfb39-eb09-4c39-948a-f6d332e79fa2/playlist.m3u8",
            MissAvParser.StreamUrl(Fixture("missav_video.html")));
        Assert.Null(MissAvParser.StreamUrl("<html>没有脚本</html>"));
    }

    [Fact]
    public void JableHiResCover()
    {
        Assert.Equal("https://assets-cdn.jable.tv/contents/videos_screenshots/62000/62857/preview.jpg",
            JableUrls.HiResCover("https://assets-cdn.jable.tv/contents/videos_screenshots/62000/62857/320x180/1.jpg"));
        Assert.Equal("https://fourhoi.com/x/cover-t.jpg", JableUrls.HiResCover("https://fourhoi.com/x/cover-t.jpg"));
    }
}
