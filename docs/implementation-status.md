# 已实现功能说明

> 对照 [ssf2weasel-technical-requirements.md](ssf2weasel-technical-requirements.md) 记录首版（v1.0.0）已交付内容。  
> 日期：2026-08-10

## 1. 工程基线（M0）

| 项目 | 状态 | 说明 |
|---|---|---|
| .NET 10 SDK / `net10.0-windows` | 已完成 | 目标框架与发布标识 `win-x64` |
| 解决方案结构 | 已完成 | `Cli` / `Core` / `Infrastructure` + 单元 / 集成测试 |
| 诊断模型与退出码 | 已完成 | 稳定诊断码 + §16.1 退出码表 |
| 合成 fixture | 已完成 | ZIP 型与旧式加密型打包器（`SyntheticSsf`） |
| `.gitignore` | 已完成 | 排除 `*.ssf`、`Fonts/`、`publish/`、`_acceptance/` |

## 2. SSF 读取与检查（M1）

| 功能 | 状态 | 说明 |
|---|---|---|
| 容器签名识别 | 已完成 | ZIP / `Skin` 加密；未知容器退出码 4 |
| ZIP reader | 已完成 | 内存读取、大小写不敏感索引、拒绝 `../` 等危险路径 |
| 旧式加密 reader | 已完成 | AES-256-CBC → zlib → 偏移表；完整边界检查 |
| `skin.ini` 解析 | 已完成 | UTF-16LE（有/无 BOM）、UTF-8；重复键 / 未知段 / 尾部乱码按规则处理 |
| GBK/ANSI 回退 | 已完成* | 文档未写，因真实样本 `维尼熊.ssf` 需要；发出 `INI_ENCODING_LEGACY_ANSI` 警告 |
| `inspect` 命令 | 已完成 | 文本与 `--json` 模式；stdout 仅 JSON |

基线样本验收：

| 样本 | 容器 | 文件数 | 结果 |
|---|---|---:|---|
| 半透明伊蕾娜 | ZIP | 51 | 通过 |
| 痛哭流涕 | 旧式加密 | 13 | 通过 |
| 辐光光 | ZIP | 53 | 通过 |
| win7风格 / 维尼熊 | ZIP | — | 额外冒烟通过 |

## 3. 转换与输出（M2）

| 功能 | 状态 | 说明 |
|---|---|---|
| 标准化皮肤模型 | 已完成 | `NormalizedSkin` 及方案 / 资源 / 诊断 |
| H1/H2/V1/V2 选择与回退 | 已完成 | 按 §11.2 |
| 配色 ID 生成 | 已完成 | NFKC + ASCII 规则；CJK 名回退 `ssf_` + SHA-256 前 12 位 |
| 颜色规范化 | 已完成 | 1–8 位十六进制补零；BGR 直写（已人工核验） |
| 字体映射与回退 | 已完成 | 未安装字体 → `Microsoft YaHei` + 警告 |
| 图片元数据 | 已完成 | PNG / BMP / GIF 尺寸与帧数 |
| 图片取色 | 已完成 | 主背景色、边缘中位色、高饱和强调色；确定性算法 |
| `transparent_color` 色键 | 已完成* | 文档未写；排除声明为透明色的像素（维尼熊等 BMP 皮肤） |
| 过大 layout 内缩回退 | 已完成* | 含立绘区的 `layout_*` 超出合理范围时回退默认边距 |
| `weasel.custom.yaml` | 已完成 | 生成后再解析自验证 |
| `conversion-report.json` | 已完成 | schema 1.0，snake_case |
| `preview.png` | 已完成 | 固定测试内容，横排 / 竖排 |
| `convert` / `validate` | 已完成 | 含输出冲突、`--force`、Unicode 路径 |

\* 相对需求文档的实现期扩展，见下文「相对文档的扩展」。

## 4. 安装与恢复（M3）

| 功能 | 状态 | 说明 |
|---|---|---|
| 小狼毫目录发现 | 已完成 | 注册表 + 默认安装路径；可 `--weasel-dir` 覆盖 |
| 语义合并 | 已完成 | 保留无关键；更新 style / 配色；冲突需 `--force` |
| 备份 | 已完成 | `backups\ssf2weasel\...` + `.meta.json` 侧车 |
| 原子写入 | 已完成 | 临时文件 → 校验 → 替换 |
| 部署失败回滚 | 已完成 | 恢复备份并尝试重新部署；退出码 10 / 11 |
| 安装前解析部署路径 | 已完成* | 避免「配置已改但未部署」；缺失目录时不写入 |
| `restore` 命令 | 已完成 | 校验元数据 → 再备份当前配置 → 原子恢复 |

## 5. 发布与合规（M4）

| 项目 | 状态 | 说明 |
|---|---|---|
| self-contained single-file | 已完成 | `publish/ssf2weasel.exe`，禁 trimming / NativeAOT |
| `LICENSE` | 已完成 | GPL-3.0-or-later |
| `THIRD_PARTY_NOTICES.md` | 已完成 | ssf2fcitx / YamlDotNet / System.Drawing.Common / .NET |
| `README.md` | 已完成 | 中文使用说明 |
| SHA-256 校验和 | 已完成 | `publish/ssf2weasel.exe.sha256` |

## 6. 测试覆盖摘要

| 套件 | 数量（约） | 覆盖要点 |
|---|---:|---|
| 单元测试 | 98 | 签名、AES/zlib、INI、颜色、ID、方案选择、映射、YAML、备份原子写、取色 |
| 集成测试 | 40 | CLI 命令、IT-001～013 场景、安装合并 / 回滚 / restore、真实样本 |

运行：`dotnet test ssf2weasel.slnx`  
强制真实样本：`SSF2WEASEL_REQUIRE_SAMPLES=1`

## 7. 相对文档的扩展

实现过程中，为通过真实样本验收而增加了文档未明确写出的行为：

1. **GBK `skin.ini`**：部分旧皮肤使用中文 ANSI；按 GBK 解码并警告。
2. **`transparent_color` / `transparent_color_enable`**：BMP 色键透明，取色时排除对应像素。
3. **布局内缩上限**：搜狗 `layout_*` 含装饰画区域时，回退到文档 §12.6 默认边距。
4. **安装前定位 Weasel**：严格落实 §15.3 步骤顺序，避免半应用状态。

## 8. 发布产物位置

```text
publish/
  ssf2weasel.exe
  ssf2weasel.exe.sha256
  LICENSE
  README.md
  THIRD_PARTY_NOTICES.md
```
