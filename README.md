# Jable · Android

影片浏览 / 播放客户端：把 Jable、MissAV 的影片，以及 Netflav、AV01、R18.dev 的作品资料整合到一个干净、无广告的界面里。基于 .NET MAUI，针对高刷新率屏幕优化了滑动体验。

Windows 版：[Jable](https://github.com/QkzzZzZz/Jable)

| | | |
|---|---|---|
| ![首页](promo/Jable-Android-1.png) | ![列表](promo/Jable-Android-2.png) | ![观看画像](promo/Jable-Android-3.png) |

## 功能

- **首页**：大图轮播、继续观看、最近更新、本周热门 TOP 10、新片上市
- **发现**：搜索、分类、标签、演员、MissAV 栏目
- **漫画 / 直播**
- **播放**：续播
- **下载**：后台下载到本地
- **我的**：观看记录、片单、观看画像、猜我喜欢
- **流畅**：原生动画、图片后台解码、AOT 编译、高刷新率

## 安装

在 [Releases](../../releases) 下载 `Jable-Android-<版本>.apk` 安装。支持 Android 7.0 及以上的 64 位 ARM 设备。

## 编译

需要 .NET 10 SDK（含 `maui-android` 工作负载）、JDK 17 和 Android SDK，放在同一个工具目录下：

```
<工具目录>\dotnet\
<工具目录>\jdk17\
<工具目录>\android-sdk\
```

工具目录默认为 `%USERPROFILE%\AndroidDev`，可用环境变量 `JABLE_ANDROID_DEV` 修改。

```powershell
.\build.ps1 emulator   # 模拟器调试版
.\build.ps1 release    # 正式安装包
```

正式包需要在工具目录下放签名文件 `jable-release.keystore` 和密码文件 `jable-release.password`。

## 目录结构

```
src/JableLens.Mobile   安卓界面
  Pages/                 页面
  Controls/              卡片、图片、列表、动画
  Platforms/Android/     网页读取、主界面
  Resources/Raw/         播放器脚本
src/JableLens.Core     数据层：站点解析、观看记录、推荐、下载
tests/                 单元测试
docs/                  产品与数据源说明
promo/                 宣传图源文件
build.ps1              编译脚本
```

## 隐私

- 只访问白名单内的站点，广告和统计请求一律拦截
- 账号、cookie、观看记录只保存在手机上，不发送给任何第三方

## 第三方组件

[hls.js](https://github.com/video-dev/hls.js)（Apache-2.0）、[Open Sans](https://fonts.google.com/specimen/Open+Sans)（OFL-1.1）、[.NET MAUI](https://github.com/dotnet/maui)（MIT）、[AngleSharp](https://github.com/AngleSharp/AngleSharp)（MIT）

## 免责声明

1. **仅限成年人**：本项目涉及成人内容，仅供已满 18 周岁（或所在地法定成年年龄）的用户使用。所在地区禁止此类内容的，请勿下载、编译或使用。
2. **不提供任何内容**：本项目不存储、不上传、不分发任何影片、图片或其他媒体内容，所有内容均来自第三方网站，其版权与合法性由对应网站及权利人负责。
3. **不绕过保护措施**：本项目不破解人机验证，不伪造浏览器指纹，不绕过付费墙或 DRM。
4. **仅供学习交流**：代码仅用于学习移动开发、网页解析和流媒体播放技术，请勿用于商业用途。
5. **风险自负**：使用本项目产生的一切后果（包括违反当地法律法规、第三方网站服务条款、版权纠纷等）由使用者自行承担，作者不承担任何责任。
6. **与相关网站无关**：本项目与文中提到的任何网站均无关联，也未获其授权；相关名称和商标归各自所有者所有。
7. **侵权处理**：权利人如认为本项目侵犯其合法权益，请通过 Issues 联系，核实后会及时处理。

本软件按「现状」提供，不附带任何担保。下载、编译或使用本项目即表示同意以上条款。

## 许可证

[MIT](LICENSE)
