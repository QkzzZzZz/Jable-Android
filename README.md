# Jable 安卓版

.NET MAUI（C#）写的安卓客户端，功能和 Windows 版（桌面 `Jable` 文件夹）一致。两边完全独立，互不引用。

## 目录

| 位置 | 内容 |
|---|---|
| `src/JableLens.Mobile` | 安卓界面：`Pages` 页面、`Controls` 卡片 / 图片 / 动画、`Platforms/Android` 网页读取和主界面、`Resources/Raw` 播放器脚本 |
| `src/JableLens.Core` | 网站解析、推荐、观看记录、下载（从 Windows 版复制过来的一份，改动不会影响 Windows 版） |
| `tests/JableLens.Core.Tests` | Core 的单元测试（`dotnet test`） |
| `docs` | 产品与数据源说明 |
| `android-studio/Jable.apk` | 给 Android Studio「Profile or Debug APK」运行用 |
| `release` | 正式安装包 |

## 编译

工具链装在 `C:\Users\Mindray\AndroidDev`（.NET 10 + 安卓工作负载、JDK 17、Android SDK）。在 VS Code 终端里：

```powershell
.\build.ps1            # Android Studio 用的 APK（手机 + 模拟器）
.\build.ps1 release    # 正式安装包（改版本号：JableLens.Mobile.csproj 里的 ApplicationDisplayVersion 和 ApplicationVersion，后者每次 +1）
.\build.ps1 emulator   # 只给模拟器的快速调试版
```

签名密钥 `AndroidDev\jable-release.keystore` 必须一直用同一个，丢了以后手机上只能卸载重装（观看记录会丢）。

## 注意

- 不绕过 / 破解 Cloudflare：遇到人机验证时把浏览器显示出来让用户自己通过。
- 只访问白名单站点（`PageScripts.AllowedHosts`），账号、cookie、观看记录不发给任何第三方。
