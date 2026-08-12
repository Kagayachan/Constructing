# ssf2weasel

将搜狗输入法 `.ssf` 皮肤转换为小狼毫（Weasel / Rime）可用的近似配置。

本工具**不会**把搜狗位图嵌入小狼毫候选窗。它提取颜色、字体和布局，生成 `weasel.custom.yaml` 补丁、机器可读转换报告，以及确定性预览图。小狼毫无法表达的特性（蒙版、覆盖图、状态栏、GIF 动画）会明确报告并降级处理。

| 项目 | 内容 |
|---|---|
| 目标平台 | Windows 10/11 x64，小狼毫 0.17.4 |
| 技术栈 | C# / .NET 10 LTS |
| 发布形式 | 自包含单文件命令行程序 |
| 许可证 | [GPL-3.0-or-later](LICENSE) |

## 快速开始

```powershell
# 只读检查皮肤
.\ssf2weasel.exe inspect ".\皮肤.ssf"

# 转换，不修改你的 Rime 配置
.\ssf2weasel.exe convert ".\皮肤.ssf" --output ".\out"

# 转换并安装到 %AppData%\Rime（会先备份）
.\ssf2weasel.exe convert ".\皮肤.ssf" --output ".\out" --install
```

每次成功转换会生成三个文件：

```text
<output>/
  weasel.custom.yaml
  conversion-report.json
  preview.png
```

## 命令

```text
ssf2weasel convert <input.ssf> [options]
ssf2weasel inspect <input.ssf> [options]
ssf2weasel validate <path> [options]
ssf2weasel restore <backup-file> [options]
ssf2weasel --version
ssf2weasel --help
```

### `convert` 选项

| 选项 | 默认值 | 含义 |
|---|---|---|
| `--output <dir>` | `./<skin-id>-weasel` | 输出目录 |
| `--layout horizontal\|vertical` | `horizontal` | 源方案方向（横排 / 竖排） |
| `--install` | 关闭 | 合并到 `%AppData%\Rime\weasel.custom.yaml` |
| `--force` | 关闭 | 覆盖已有输出 / 本工具管理的同名配色 |
| `--no-deploy` | 关闭 | 安装后不调用小狼毫部署程序 |
| `--rime-dir <dir>` | `%AppData%\Rime` | 覆盖 Rime 用户目录 |
| `--weasel-dir <dir>` | 自动发现 | 覆盖小狼毫安装目录 |
| `--json` | 关闭 | 标准输出只写一个 JSON 文档 |
| `--verbose` | 关闭 | 在标准错误流输出阶段耗时和异常详情 |

只有提供 `--install` 时才会修改你的 Rime 配置。安装流程会备份原有 `weasel.custom.yaml`、语义合并（保留无关键）、原子写入，并在部署失败时自动回滚。

## 支持的容器格式

程序按文件内容识别格式，不依赖 `.ssf` 扩展名：

| 签名 | 格式 |
|---|---|
| `PK\x03\x04` / 空包 / 分卷 ZIP | ZIP 型 SSF |
| ASCII `Skin` | 旧式 AES-256-CBC 加密 SSF |

`skin.ini` 编码支持：UTF-16LE（有/无 BOM）、UTF-8，以及带警告的旧式 GBK（中文 ANSI）。

## 从源码构建

需要安装 .NET 10 SDK。

```powershell
dotnet build ssf2weasel.slnx
dotnet test ssf2weasel.slnx
dotnet publish src/Ssf2Weasel.Cli -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o .\publish
```

真实皮肤样本不会提交到仓库。将样本放在解决方案旁（或设置 `SSF2WEASEL_SAMPLES_DIR`）即可跑本地验收测试。设置 `SSF2WEASEL_REQUIRE_SAMPLES=1` 时，样本缺失会导致测试失败。

## 项目结构

```text
src/Ssf2Weasel.Cli            命令行、退出码
src/Ssf2Weasel.Core           领域模型、映射规则、诊断
src/Ssf2Weasel.Infrastructure SSF 读取、图片分析、YAML、安装部署
tests/                        单元 / 集成测试与合成 fixture
docs/                         开发与规划文档
```

## 文档

| 文档 | 说明 |
|---|---|
| [docs/ssf2weasel-technical-requirements.md](docs/ssf2weasel-technical-requirements.md) | 技术需求基线 |
| [docs/implementation-status.md](docs/implementation-status.md) | 已实现功能说明 |
| [docs/future-features.md](docs/future-features.md) | 未来可能添加的功能 |

## 许可证与致谢

本项目使用 GPL-3.0-or-later 许可。详见 [LICENSE](LICENSE) 与 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

旧式 SSF 解密算法源自 VOID001 的 [ssf2fcitx](https://github.com/VOID001/ssf2fcitx)，本项目以托管 C# 重新实现。
