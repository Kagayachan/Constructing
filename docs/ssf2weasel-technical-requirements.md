# ssf2weasel 技术需求文档

> 将搜狗输入法 `.ssf` 皮肤转换为小狼毫可用的近似配色与布局配置

| 项目 | 内容 |
|---|---|
| 文档版本 | 1.0 |
| 文档状态 | 开发基线 |
| 日期 | 2026-08-10 |
| 产品代号 | `ssf2weasel` |
| 首发平台 | Windows 10/11 x64 |
| 目标小狼毫版本 | 0.17.4 |
| 实现技术 | C# / .NET 10 LTS |
| 发布形式 | 自包含单文件命令行程序 |
| 许可证 | GPL-3.0-or-later |

## 1. 文档目的

本文档定义 `ssf2weasel` 首版的产品范围、技术架构、文件格式、转换规则、命令行接口、安装流程、错误处理、测试方案和验收标准。开发、测试和发布均应以本文档为基线。

## 2. 背景与结论

[KNOWN | Confidence: HIGH] 搜狗 `.ssf` 样本中至少存在两种容器格式：标准 ZIP 容器，以及以 ASCII `Skin` 开头的旧式加密容器。

[KNOWN | Confidence: HIGH] 小狼毫 0.17.4 的配置可表达字体、字号、颜色、圆角、阴影、间距和候选项方向等样式；候选窗位图背景、覆盖图和 GIF 动画不在现有配置模型中。

[INFERRED | Confidence: HIGH] 首版采用“配置近似转换”路线。程序应尽量保留颜色、字体、候选窗方向和几何关系，并对无法映射的图片、蒙版、覆盖图、动画和搜狗状态栏资源生成明确报告。

### 2.1 已验证样本

| 样本 | 大小 | 容器 | 内部文件 | 主要特征 |
|---|---:|---|---:|---|
| `半透明伊蕾娜v1.0（by巧味棉花糖）.ssf` | 429,863 字节 | ZIP | 51 | PNG、透明蒙版、覆盖图、H1/H2/V1/V2、状态栏 |
| `痛哭流涕.ssf` | 100,248 字节 | `Skin` 加密容器 | 13 | PNG、GIF 预览、H1/H2/V1/V2、状态栏 |
| `辐光光搜狗输入法皮肤.ssf` | 420,363 字节 | ZIP | 53 | PNG、BMP、GIF 状态栏、H1/H2/V1/V2 |

样本 SHA-256：

```text
B480644B79FD60B9003B9116E4F3C6049F0158B51D19A6869D2AD9560FA1273D  半透明伊蕾娜v1.0（by巧味棉花糖）.ssf
9472BF2CB852D7620B7AC8F75DB128FECC50CCF67FC6183E83FBC326CDD0103C  痛哭流涕.ssf
238A78CB20DBE223EABFCF1DD3C372BDC40E9A3050AA6C31AAB0D44318C0133B  辐光光搜狗输入法皮肤.ssf
```

## 3. 目标

### 3.1 产品目标

1. 从命令行读取 `.ssf` 文件并自动识别容器格式。
2. 解析皮肤元数据、`skin.ini` 和引用的图片资源。
3. 将可表达的样式映射为小狼毫 `weasel.custom.yaml`。
4. 生成转换预览和机器可读报告。
5. 在用户显式指定 `--install` 时，安全地备份、合并、部署和验证配置。
6. 首版确保三个已验证样本均能稳定完成解析和输出。

### 3.2 首版质量优先级

1. 程序可以构建、发布和启动。
2. 两种 SSF 容器均可正确解析。
3. 输出 YAML 语法和 Rime patch 结构正确。
4. 安装、备份、部署和失败回滚正确。
5. 再逐步提高颜色、布局和预览的视觉接近程度。

## 4. 范围

### 4.1 首版范围内

- Windows 10 和 Windows 11 x64。
- 小狼毫 0.17.4。
- ZIP 型 `.ssf`。
- 旧式 `Skin` 加密型 `.ssf`。
- UTF-16LE `skin.ini`，含 BOM 或无 BOM。
- `General`、`Display`、`Scheme_H1`、`Scheme_H2`、`Scheme_V1`、`Scheme_V2`、`StatusBar` 段。
- PNG、BMP、GIF 的识别和元数据读取。
- PNG/BMP 静态图像的颜色分析。
- GIF 第一帧预览和动画属性报告。
- 横排和竖排配置转换。
- YAML、JSON 报告和 PNG 预览输出。
- 用户配置备份、语义合并、部署及失败回滚。
- Unicode 文件名、目录名和皮肤元数据。
- 离线运行。

### 4.2 首版范围外

- 修改小狼毫候选窗渲染器。
- 在小狼毫候选窗中直接显示搜狗位图背景。
- 蒙版、覆盖图和 GIF 动画的运行时渲染。
- 搜狗状态栏、账号按钮、皮肤管理按钮和菜单栏的等价实现。
- macOS、Linux、Windows ARM64。
- 图形用户界面。
- 在线皮肤商店、下载、同步或遥测。
- 对所有历史和未来 SSF 变体作兼容承诺。
- 像素级视觉一致性验收。

## 5. 术语

| 术语 | 定义 |
|---|---|
| SSF | 搜狗输入法皮肤文件。本项目按文件签名区分 ZIP 型和旧式加密型。 |
| 源方案 | `skin.ini` 中的 `Scheme_H1/H2/V1/V2`。 |
| 目标方案 | 输出到小狼毫 `preset_color_schemes` 的配色方案。 |
| 转换 | 读取 SSF 并生成文件，不修改用户 Rime 目录。 |
| 安装 | 将生成配置合并到用户 Rime 目录并调用小狼毫重新部署。 |
| 降级 | 源皮肤功能无法由小狼毫配置表达时，使用近似值或忽略并报告。 |
| 语义合并 | 保留原 YAML 中未被本工具管理的键和值。 |

## 6. 技术基线

### 6.1 运行时与发布

- 目标框架：`net10.0-windows`。
- 运行时标识：`win-x64`。
- 发布模式：self-contained、single-file。
- 首版禁止启用 trimming。
- 首版禁止启用 NativeAOT，待兼容性测试完成后再评估。
- 发布产物至少包括：
  - `ssf2weasel.exe`
  - `LICENSE`
  - `THIRD_PARTY_NOTICES.md`
  - `README.md`

### 6.2 推荐项目结构

```text
src/
  Ssf2Weasel.Cli/
  Ssf2Weasel.Core/
  Ssf2Weasel.Infrastructure/
tests/
  Ssf2Weasel.UnitTests/
  Ssf2Weasel.IntegrationTests/
  fixtures/
docs/
```

职责：

- `Cli`：参数解析、控制台输出、退出码。
- `Core`：领域模型、转换规则、诊断信息，不依赖文件系统。
- `Infrastructure`：SSF 读取、图片解码、YAML/JSON、安装和部署。
- `UnitTests`：纯函数和边界测试。
- `IntegrationTests`：真实或合成 SSF、YAML 合并、发布产物测试。

### 6.3 依赖原则

- AES、SHA-256、ZIP、zlib、JSON 和进程调用优先使用 .NET 标准库。
- YAML 库必须支持读取映射、序列和标量，并允许保留未知键值。
- 图片解码层必须抽象为接口，避免转换逻辑绑定具体图像库。
- 所有第三方依赖必须锁定版本并记录许可证。

## 7. 总体架构

```text
CLI
  -> 输入验证
  -> 容器识别
      -> ZIP SSF Reader
      -> Legacy Skin SSF Reader
  -> Virtual Skin Package
  -> skin.ini Parser
  -> Normalized Skin Model
  -> Image Analyzer
  -> Weasel Mapper
  -> YAML / Report / Preview Writers
  -> Optional Installer
      -> Backup
      -> Merge
      -> Atomic Write
      -> Deploy
      -> Verify or Rollback
```

### 7.1 核心接口

```csharp
public interface ISsfPackageReader
{
    bool CanRead(ReadOnlySpan<byte> header);
    Task<SkinPackage> ReadAsync(Stream input, CancellationToken cancellationToken);
}

public interface ISkinIniParser
{
    SkinDefinition Parse(ReadOnlyMemory<byte> content);
}

public interface IWeaselThemeMapper
{
    ConversionResult Convert(NormalizedSkin skin, ConversionOptions options);
}

public interface IWeaselInstaller
{
    Task<InstallResult> InstallAsync(
        GeneratedTheme theme,
        InstallOptions options,
        CancellationToken cancellationToken);
}
```

实际类型名称允许调整，但职责边界不得合并到单一巨型类中。

## 8. SSF 容器识别与解析

### 8.1 自动识别

程序必须按文件内容识别格式，不得只依据 `.ssf` 扩展名。

| 签名 | 格式 | Reader |
|---|---|---|
| `50 4B 03 04`、`50 4B 05 06` 或 `50 4B 07 08` | ZIP | `ZipSsfPackageReader` |
| ASCII `Skin` | 旧式加密 | `LegacyEncryptedSsfPackageReader` |
| 其他 | 不支持 | 返回 `SSF_UNSUPPORTED_CONTAINER` |

### 8.2 ZIP 型 SSF

`ZipSsfPackageReader` 必须：

1. 使用 `ZipArchive` 读取条目。
2. 以不区分大小写的方式定位 `skin.ini`。
3. 保留原始条目名称，同时建立不区分大小写的资源索引。
4. 在内存中读取所需资源，转换模式下无需完整解压到磁盘。
5. 对重复名称产生警告，并使用首次出现的条目。
6. 对无法读取的条目返回结构化错误。

### 8.3 旧式 `Skin` 加密 SSF

[KNOWN | Confidence: HIGH] 旧式格式的解析流程和常量来自 `ssf2fcitx` 的公开实现，并已使用 `痛哭流涕.ssf` 验证。

#### 8.3.1 外层格式

- 字节 `0..3`：ASCII `Skin`。
- 字节 `4..7`：版本或保留字段；首版读取并记录，不对具体值作强约束。
- 字节 `8..EOF`：AES-256-CBC 密文。
- AES padding：`None`。

AES key：

```text
52 36 46 1A D3 85 03 66 90 45 16 28 79 03 36 23
DD BE 6F 03 FF 04 E3 CA D5 7F FC A3 50 E4 9E D9
```

AES IV：

```text
E0 7A AD 35 E0 90 AA 03 8A 51 FD 05 DF 8C 5D 0F
```

#### 8.3.2 解密后压缩流

- 前 4 字节：小端序 `UInt32`，表示预期解压长度。
- 从偏移 4 开始：zlib 数据流。
- 使用 `ZLibStream` 解压。
- 解压结果长度必须与前 4 字节声明值一致；不一致返回 `SSF_DECOMPRESSED_LENGTH_MISMATCH`。

#### 8.3.3 解压后的文件包

所有整数均为小端序 `UInt32`：

```text
UInt32 total_size
UInt32 header_size
UInt32 offsets[header_size / 4]

at each offset:
  UInt32 filename_byte_length
  Byte[filename_byte_length] filename_utf16le
  UInt32 content_length
  Byte[content_length] content
```

解析要求：

- `filename_byte_length` 必须为偶数。
- 文件名按 UTF-16LE 解码并移除末尾 NUL。
- 每个偏移、名称和内容范围必须处于解压缓冲区内。
- 文件名索引不区分大小写。
- 未知文件类型允许保留并写入报告。

## 9. `skin.ini` 解析

### 9.1 编码

解析顺序：

1. 检测 UTF-16LE BOM `FF FE`。
2. 无 BOM 时，若奇数位置的大量字节为 `00`，按 UTF-16LE 解析。
3. 其余情况尝试 UTF-8。
4. 解码失败返回 `SSF_INI_ENCODING_UNSUPPORTED`。

### 9.2 解析规则

- 段名和键名不区分大小写。
- 保留原始名称和值，便于报告和未来扩展。
- 允许缺失可选段。
- 重复键采用最后一个值，并生成警告。
- 忽略空行和以 `;` 或 `#` 开头的注释行。
- 接受 CRLF 和 LF。
- 接受末尾损坏或不可识别的附加字符，并生成 `INI_TRAILING_GARBAGE` 警告。
- 数组字段按逗号拆分并去除周围空白。
- 数值解析使用 invariant culture。

### 9.3 已知段

```text
[General]
[Display]
[Scheme_H1]
[Scheme_H2]
[Scheme_V1]
[Scheme_V2]
[StatusBar]
```

未知段必须进入报告的 `unknown_sections`，不得导致转换失败。

## 10. 标准化领域模型

转换器必须先构建与目标平台无关的标准化模型，再生成 Weasel 配置。

```csharp
public sealed record NormalizedSkin(
    SkinMetadata Metadata,
    SkinTypography Typography,
    SkinColors Colors,
    IReadOnlyDictionary<SkinSchemeKind, SkinScheme> Schemes,
    StatusBarDefinition? StatusBar,
    IReadOnlyDictionary<string, SkinAsset> Assets,
    IReadOnlyList<Diagnostic> Diagnostics);
```

最低字段：

```text
SkinMetadata
  id?
  name
  version?
  author?
  email?
  created_at?
  description?

SkinTypography
  chinese_font?
  latin_font?
  font_size?

SkinColors
  pinyin
  first_candidate
  other_candidate
  composition_hint

SkinScheme
  kind: H1 | H2 | V1 | V2
  background_asset?
  background_mask_asset?
  pinyin_background_asset?
  candidate_background_asset?
  horizontal_layout[]
  vertical_layout[]
  pinyin_margin[]
  candidate_margin[]
  separators[]
  overlays[]

SkinAsset
  original_name
  normalized_name
  media_type
  width?
  height?
  frame_count?
  sha256
```

## 11. 源方案选择

### 11.1 命令行选项

```text
--layout horizontal | vertical
```

默认值：`horizontal`。

### 11.2 选择规则

横排：

1. 优先 `Scheme_H1`。
2. `H1` 缺失时回退到 `Scheme_H2`。
3. 两者均缺失时，回退到可用的 `V1/V2`，并生成高优先级警告。

竖排：

1. 优先 `Scheme_V1`。
2. `V1` 缺失时回退到 `Scheme_V2`。
3. 两者均缺失时，回退到可用的 `H1/H2`，并生成高优先级警告。

首版每次转换只激活一个布局。报告必须列出其他方案及其资源，但无需生成多套活动配置。

## 12. 字段映射

### 12.1 元数据

| SSF 字段 | 目标 | 规则 |
|---|---|---|
| `General/skin_name` | `preset_color_schemes/<id>/name` | 必填；缺失时使用输入文件名 |
| `General/skin_author` | `.../author` | 可选 |
| `skin_version` | JSON 报告 | Weasel 配色无对应字段 |
| `skin_email` | JSON 报告 | 可选元数据 |
| `skin_time` | JSON 报告 | 保留原始字符串 |
| `skin_info` | JSON 报告 | 保留原始字符串 |

### 12.2 配色 ID

配色 ID 生成规则：

1. 使用皮肤名进行 Unicode 规范化 NFKC。
2. 拉丁字母转小写。
3. 空格和连续标点替换为 `_`。
4. 保留 ASCII 字母、数字和下划线。
5. 无法产生 ASCII ID 时使用 `ssf_` 加源文件 SHA-256 前 12 位小写字符。
6. ID 必须匹配 `^[a-z][a-z0-9_]{2,63}$`。
7. 冲突时默认报错；`--force` 允许替换同名工具管理项。

### 12.3 字体

| SSF 字段 | Weasel 字段 | 规则 |
|---|---|---|
| `Display/font_size` | `style/font_point` | 有效正整数；首版直接使用 |
| `font_size` | `style/label_font_point` | 与主字号相同 |
| `font_size` | `style/comment_font_point` | `max(font_size - 1, 8)` |
| `font_ch`、`font_en` | `style/font_face` | 按 `font_ch, font_en, Microsoft YaHei` 组成 fallback 列表 |
| `font_ch` | `style/label_font_face` | 缺失时回退 `Microsoft YaHei` |
| `font_ch` | `style/comment_font_face` | 缺失时回退 `Microsoft YaHei` |

若检测到字体未安装：

- 保留源字体名于报告。
- 输出配置使用 `Microsoft YaHei` 回退。
- 生成 `FONT_NOT_INSTALLED` 警告。

### 12.4 颜色

[KNOWN | Confidence: HIGH] 已验证样本中的搜狗颜色使用 BGR 十六进制形式，小狼毫 0.17.4 默认颜色表示亦采用 BGR/ABGR 方向。因此 24 位颜色可在补齐位数后直接写入。

颜色规范化：

1. 移除可选 `0x` 前缀。
2. 接受 1 至 8 位十六进制数字。
3. 1 至 6 位左侧补零到 6 位。
4. 7 至 8 位左侧补零到 8 位。
5. 非法值产生警告并使用回退值。
6. 输出统一使用小写 `0x` 加 6 位或 8 位十六进制。

| SSF 字段 | Weasel 字段 | 回退 |
|---|---|---|
| `pinyin_color` | `text_color` | `0x000000` |
| `pinyin_color` | `hilited_text_color` | 与 `text_color` 相同 |
| `zhongwen_color` | `candidate_text_color` | `0x000000` |
| `zhongwen_first_color` | `hilited_candidate_text_color` | `candidate_text_color` |
| `comphint_color` | `comment_text_color` | `candidate_text_color` |
| `comphint_color` | `hilited_comment_text_color` | `hilited_candidate_text_color` |

由图片分析产生：

| Weasel 字段 | 推导规则 |
|---|---|
| `back_color` | 选中源方案背景图的主背景色 |
| `border_color` | 图片最外侧像素的稳健中位色 |
| `candidate_back_color` | 默认与 `back_color` 相同 |
| `hilited_candidate_back_color` | 图片中的高饱和强调色；不可得时生成与高亮文字有足够对比的派生色 |
| `hilited_back_color` | 默认与 `back_color` 相同 |
| `shadow_color` | `glow=1` 时使用低透明度边缘色，否则全透明 |

首版颜色分析算法必须确定性运行。同一输入和参数应产生相同颜色。

### 12.5 背景图片和蒙版

- `pic`、`pinyin_pic`、`zhongwen_pic` 用于颜色和几何分析。
- `pic_mask` 及对应蒙版用于确定有效像素和透明区域。
- 蒙版缺失时使用图片自身 alpha 通道。
- 无 alpha 通道时所有像素视为不透明。
- GIF 用于候选窗背景时，首版只分析第一帧并生成 `ANIMATED_ASSET_DEGRADED` 警告。
- 图片资源不写入最终 Weasel 运行配置。

### 12.6 布局

映射目标：

```text
style/horizontal
style/layout/min_width
style/layout/min_height
style/layout/margin_x
style/layout/margin_y
style/layout/spacing
style/layout/candidate_spacing
style/layout/hilite_spacing
style/layout/hilite_padding
style/layout/border_width
style/layout/corner_radius
style/layout/shadow_radius
style/layout/shadow_offset_x
style/layout/shadow_offset_y
```

基础规则：

- 横排：`style/horizontal: true`。
- 竖排：`style/horizontal: false`。
- `layout_horizontal` 的左右边距用于估算 `margin_x`。
- `layout_vertical` 的上下边距用于估算 `margin_y`。
- `pinyin_marge` 与 `zhongwen_marge` 的间隔差用于估算 `spacing` 和 `candidate_spacing`。
- 背景图宽高用于估算 `min_width` 和 `min_height`。
- 负值、缺失值或明显不合理值必须回退并写入警告。
- H2/V2 的分离背景结构无法直接表达时，只使用其几何信息。

首版回退值：

```yaml
min_width: 160
min_height: 0
margin_x: 12
margin_y: 12
spacing: 10
candidate_spacing: 5
hilite_spacing: 4
hilite_padding: 2
border_width: 1
corner_radius: 4
shadow_radius: 0
shadow_offset_x: 4
shadow_offset_y: 4
```

### 12.7 无法映射的字段

以下内容进入 `unsupported_features`：

- `custom*_display` 和 `custom*` 覆盖图。
- `*_mask` 的运行时效果。
- `StatusBar` 背景和按钮。
- 搜狗账号、搜索、软键盘、全半角、简繁切换按钮。
- 动画背景和多帧状态。
- `aero`、`use_gdip` 的搜狗专属渲染语义。
- H2/V2 的双背景运行时组合。

## 13. 输出文件

每次成功转换必须产生：

```text
<output>/
  weasel.custom.yaml
  conversion-report.json
  preview.png
```

### 13.1 `weasel.custom.yaml`

最小结构：

```yaml
patch:
  "style/color_scheme": ssf_example
  "style/horizontal": true
  "style/font_face": "Microsoft YaHei, Arial"
  "style/font_point": 16
  "style/layout/margin_x": 12
  "style/layout/margin_y": 12
  "preset_color_schemes/ssf_example":
    name: "Example"
    author: "Author"
    text_color: 0x000000
    back_color: 0xffffff
    border_color: 0xcccccc
    candidate_text_color: 0x000000
    candidate_back_color: 0xffffff
    hilited_text_color: 0x000000
    hilited_back_color: 0xffffff
    hilited_candidate_text_color: 0xffffff
    hilited_candidate_back_color: 0x000000
    comment_text_color: 0x666666
    hilited_comment_text_color: 0xffffff
```

输出要求：

- UTF-8，无 BOM。
- 换行使用 CRLF 或 LF，但同一文件内必须一致。
- 所有动态字符串必须正确 YAML 转义。
- 生成后必须再次解析验证。
- 禁止向输出写入空键、NaN 或平台相关数字格式。

### 13.2 `conversion-report.json`

最低结构：

```json
{
  "schema_version": "1.0",
  "tool_version": "1.0.0",
  "source": {
    "file_name": "example.ssf",
    "size": 123456,
    "sha256": "...",
    "container": "zip"
  },
  "skin": {
    "name": "Example",
    "version": "1.0",
    "author": "Author"
  },
  "selection": {
    "requested_layout": "horizontal",
    "source_scheme": "H1"
  },
  "mappings": [],
  "warnings": [],
  "unsupported_features": [],
  "outputs": []
}
```

每条诊断必须包含：

```text
code
severity: info | warning | error
message
source_section?
source_key?
asset?
fallback?
```

### 13.3 `preview.png`

[INFERRED | Confidence: HIGH] 预览表示转换后的小狼毫近似样式，不表示搜狗原始位图皮肤。

预览要求：

- 使用输出配置中的颜色、字体和布局参数绘制。
- 横排和竖排与 `--layout` 一致。
- 使用固定测试内容，确保可复现：
  - 预编辑：`xiaolanghao`
  - 候选：`小狼毫`、`小狼嚎`、`小浪号`
  - 标签：`1`、`2`、`3`
- 字体不可用时使用与 YAML 相同的回退字体。
- 默认背景透明，候选窗本体按 `back_color` 绘制。

## 14. 命令行接口

### 14.1 基本命令

```text
ssf2weasel convert <input.ssf> [options]
ssf2weasel inspect <input.ssf> [options]
ssf2weasel validate <path> [options]
ssf2weasel restore <backup-file> [options]
ssf2weasel --version
ssf2weasel --help
```

### 14.2 `convert`

```text
ssf2weasel convert <input.ssf>
  --output <directory>
  [--layout horizontal|vertical]
  [--install]
  [--force]
  [--no-deploy]
  [--rime-dir <directory>]
  [--weasel-dir <directory>]
  [--json]
  [--verbose]
```

默认值：

- `--output`：当前目录下 `<skin-id>-weasel`。
- `--layout`：`horizontal`。
- `--rime-dir`：`%AppData%\Rime`。
- `--weasel-dir`：从已安装小狼毫目录自动发现。
- 默认不安装。
- 默认不覆盖同名输出目录中的现有文件。

示例：

```powershell
ssf2weasel convert ".\皮肤.ssf" --output ".\out"
ssf2weasel convert ".\皮肤.ssf" --layout vertical --output ".\out-v"
ssf2weasel convert ".\皮肤.ssf" --output ".\out" --install
```

### 14.3 `inspect`

只读取并报告：

- 容器格式。
- 文件数和资源类型。
- 元数据。
- 可用 H1/H2/V1/V2 方案。
- 图片尺寸和帧数。
- 未知字段及预期降级项。

`--json` 时标准输出必须为单个合法 JSON 文档，日志写入标准错误流。

### 14.4 `validate`

- 输入 `.ssf` 时验证容器、INI、资源引用和可转换性。
- 输入 `.yaml` 时验证 YAML 语法及必要 patch 键。
- 不生成预览，不安装。

### 14.5 `restore`

- 只接受本工具创建且具有有效备份元数据的文件。
- 恢复前再次备份当前配置。
- 使用原子替换。
- 默认执行重新部署，可用 `--no-deploy` 禁用。

## 15. 安装流程

### 15.1 触发条件

只有用户提供 `--install` 时才允许修改 `%AppData%\Rime` 或启动小狼毫部署程序。

### 15.2 目标文件

- 只修改 `%AppData%\Rime\weasel.custom.yaml`。
- 禁止修改部署生成的 `%AppData%\Rime\weasel.yaml`。
- 禁止修改 `C:\Program Files\Rime` 下的共享配置。

### 15.3 安装步骤

1. 完成转换并验证生成 YAML。
2. 定位 Rime 用户目录和小狼毫安装目录。
3. 读取现有 `weasel.custom.yaml`；不存在时创建空 patch 模型。
4. 检查目标配色 ID 是否冲突。
5. 未提供 `--force` 且冲突时停止。
6. 创建备份：

   ```text
   %AppData%\Rime\backups\ssf2weasel\weasel.custom.<UTC_TIMESTAMP>.<SHA256_8>.yaml
   ```

7. 在同一目录创建临时文件。
8. 语义合并新配置并写入临时文件。
9. 重新读取临时文件并验证 YAML。
10. 原子替换 `weasel.custom.yaml`。
11. 未提供 `--no-deploy` 时调用已安装的小狼毫部署程序。
12. 检查部署程序退出状态。
13. 部署失败时恢复备份，并尝试重新部署恢复后的配置。
14. 输出最终状态、备份位置和回滚状态。

### 15.4 合并规则

- 保留 `patch` 下所有与本工具目标无关的键和值。
- 更新 `style/color_scheme` 为新配色 ID。
- 更新本次选择的字体和布局键。
- 添加或替换 `preset_color_schemes/<id>`。
- 未提供 `--force` 时禁止替换已存在的同名配色。
- 保留未知顶层键。
- YAML 注释和键顺序为尽力保留项；语义完整性为强制要求。
- 任何写入前都必须生成备份。

## 16. 错误模型与退出码

### 16.1 退出码

| 退出码 | 含义 |
|---:|---|
| 0 | 成功，允许包含 warning |
| 2 | 命令行参数错误 |
| 3 | 输入文件不存在或不可读 |
| 4 | 不支持的 SSF 容器 |
| 5 | SSF 解密、解压或包结构错误 |
| 6 | `skin.ini` 缺失或无法解析 |
| 7 | 转换失败或无法生成有效 YAML |
| 8 | 输出冲突，未提供 `--force` |
| 9 | 安装或配置合并失败 |
| 10 | 小狼毫部署失败，已成功回滚 |
| 11 | 部署失败且回滚也失败 |
| 12 | 用户取消操作 |
| 70 | 未处理的内部错误 |

### 16.2 错误输出

普通模式：

```text
error SSF_INI_MISSING: skin.ini was not found in the package.
hint: Verify that the input is a Sogou skin file.
```

JSON 模式：

```json
{
  "ok": false,
  "error": {
    "code": "SSF_INI_MISSING",
    "message": "skin.ini was not found in the package."
  }
}
```

禁止向用户显示完整异常堆栈，除非提供 `--verbose`。

## 17. 最低正确性与安全要求

用户已同意首版暂缓完整安全加固。以下检查同时属于解析正确性，必须实现：

- 所有长度、偏移和读取范围必须验证。
- 禁止绝对路径、盘符路径、UNC 路径和 `..` 路径逃逸。
- 禁止执行、加载或注册 SSF 内的可执行文件和动态库。
- 图片解码失败只能产生受控错误或警告。
- 安装使用备份、临时文件和原子替换。
- 进程崩溃或取消不得留下半写入配置。
- 不向网络发送皮肤、元数据、路径、报告或使用数据。
- 日志默认不输出用户目录的完整绝对路径；`--verbose` 可输出。

完整资源上限、模糊测试、恶意压缩包配额和沙箱隔离列入后续版本。

## 18. 非功能需求

### 18.1 兼容性

- 支持包含中文、全角括号、空格和非 BMP Unicode 字符的路径。
- 支持当前用户无管理员权限运行。
- 允许通过 `--rime-dir` 和 `--weasel-dir` 覆盖自动发现结果。
- 在未安装小狼毫时仍可完成非安装转换。

### 18.2 可重复性

- 相同程序版本、输入、选项和字体环境必须产生相同 YAML 和预览。
- 报告中的运行时间戳不参与确定性比较。
- JSON 属性顺序可以固定，以便生成易读 diff。

### 18.3 性能

- 三个已验证样本的单次转换应在普通桌面环境中于 5 秒内完成。
- 转换过程不应持续占用小狼毫服务。
- 图片应按需解码，避免重复读取。

### 18.4 可维护性

- 每种容器实现独立 `ISsfPackageReader`。
- 字段映射使用可测试纯函数。
- 诊断代码为稳定 API，不随错误文案变化。
- 转换报告包含 `schema_version`。
- 新增 SSF 格式时不得破坏现有 reader。

### 18.5 可观察性

- 默认输出简洁进度和最终文件位置。
- `--verbose` 输出阶段、耗时和异常详情。
- `--json` 保证标准输出无非 JSON 文本。
- 日志不得包含 SSF 二进制数据或 AES key 之外的敏感凭据。

## 19. 测试需求

### 19.1 单元测试

最低覆盖：

- ZIP 和 `Skin` 签名识别。
- AES 解密向量。
- zlib 解压和长度验证。
- 文件包偏移表解析。
- UTF-16LE BOM 与无 BOM INI。
- 重复键、未知段和尾部乱码。
- 1 至 8 位颜色规范化。
- BGR/ABGR 输出。
- 字体 fallback。
- H1/H2/V1/V2 选择和回退。
- 配色 ID 生成、Unicode 名称和冲突。
- YAML 转义和再次解析。
- YAML 语义合并。
- 临时写入、备份和回滚。
- 退出码映射。

### 19.2 集成测试

| ID | 场景 | 预期结果 |
|---|---|---|
| IT-001 | 检查“半透明伊蕾娜” | 识别 ZIP、51 个文件、解析全部已知段 |
| IT-002 | 检查“痛哭流涕” | 识别 `Skin`、AES 解密、得到 13 个文件 |
| IT-003 | 检查“辐光光” | 识别 ZIP、53 个文件、报告 GIF 状态栏 |
| IT-004 | 三样本横排转换 | 均输出可解析 YAML、JSON 和 PNG |
| IT-005 | 三样本竖排转换 | 均选择 V1 或按规则回退 |
| IT-006 | Unicode 输入和输出路径 | 成功，无乱码 |
| IT-007 | 同名输出且无 `--force` | 不覆盖，退出码 8 |
| IT-008 | 合并现有 `style/color_scheme: macau` | 保留无关配置，激活新配色 |
| IT-009 | 模拟部署失败 | 自动恢复原配置，退出码 10 |
| IT-010 | 模拟写入中断 | 原配置保持完整 |
| IT-011 | 截断的旧式 SSF | 受控失败，无崩溃 |
| IT-012 | ZIP 内含 `../` 条目 | 拒绝危险条目，无目录逃逸 |
| IT-013 | `--json` 成功与失败 | stdout 始终为单个合法 JSON 文档 |

### 19.3 测试样本管理

- 若没有皮肤作者的再分发许可，三个真实样本不得提交到公开仓库。
- 集成测试可通过环境变量指定本地样本目录。
- 公开仓库应包含人工生成、无版权负担的最小 ZIP 和旧式格式 fixture。
- 真实样本通过文件名和 SHA-256 识别。

## 20. 首版验收标准

### 20.1 P0：程序正确运行

以下条件必须全部满足：

- `dotnet test` 全部通过。
- `win-x64` self-contained single-file 发布成功。
- 干净 Windows 10/11 x64 环境无需安装 .NET 即可运行。
- `--help` 和 `--version` 正常。
- 三个样本的 `inspect` 均成功。
- 三个样本的横排 `convert` 均成功。
- 三个样本的竖排 `convert` 均成功或按已定义规则产生受控回退。
- 每次成功转换均产生三个必需文件。
- 生成 YAML 能被解析，并具有有效 `patch` 和配色对象。
- `--install` 创建备份并保留无关配置。
- 模拟部署失败能够自动回滚。
- 所有预期失败均返回文档化退出码，无未处理异常。

### 20.2 P1：转换质量

P0 完成后执行：

- 对比源皮肤截图和生成预览。
- 调整背景色、边框色和高亮色提取算法。
- 调整 H1/V1 的边距和间距映射。
- 建立人工评分表：颜色、字体、方向、间距、可读性。
- 至少在三个样本上避免文字与背景无法辨识。

P1 不阻塞首个可运行版本。

## 21. 里程碑

### M0：工程与测试基线

- 创建解决方案和项目结构。
- 配置 .NET 10、测试、格式化和发布。
- 建立合成 fixture。
- 固定诊断模型和退出码。

### M1：SSF 读取

- ZIP reader。
- 旧式 AES/zlib reader。
- 虚拟文件包。
- INI 解析。
- `inspect` 命令。

### M2：转换与输出

- 标准化模型。
- 字段映射。
- 图片分析。
- YAML、JSON 和预览生成。
- `convert` 与 `validate`。

### M3：安装与恢复

- 小狼毫自动发现。
- YAML 语义合并。
- 备份和原子写入。
- 部署、验证和回滚。
- `restore` 命令。

### M4：发布

- 三个样本验收。
- 自包含单文件发布。
- README、许可证和第三方声明。
- 发布校验和。

### M5：视觉调优

- 根据搜狗截图校准颜色和布局。
- 扩充皮肤样本矩阵。
- 评估 ARM64 和完整安全加固。

## 22. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| SSF 格式缺乏正式公开规范 | 新样本解析失败 | reader 插件化；报告未知字段；增加 fixture |
| 配置模式无法显示位图皮肤 | 视觉差异明显 | 明确降级；使用图片取色；生成预览和报告 |
| H1/H2/V1/V2 语义存在变体 | 布局误差 | 明确选择规则；保留原始字段；样本驱动调优 |
| 字体未安装 | 预览和实际显示不同 | 字体检测、fallback 和警告 |
| YAML 合并改变注释或顺序 | 用户 diff 较大 | 语义保留、备份、最小修改、安装预览 |
| 小狼毫版本更新 | 字段行为变化 | 报告检测版本；建立版本兼容层 |
| 直接复用旧 C++ 实现 | 固定缓冲区和边界缺陷被带入 | 使用 .NET 重写解析层；保留算法来源和测试向量 |
| GPL 合规遗漏 | 发布受阻 | GPL-3.0-or-later、SPDX、第三方声明和来源链接 |
| 真实皮肤资源版权不明 | 无法公开测试样本 | 本地测试；公开合成 fixture；不随程序分发皮肤 |

## 23. 许可证与来源要求

[KNOWN | Confidence: HIGH] `ssf2fcitx` 采用 GPLv3-or-later；小狼毫仓库声明 GPLv3。

项目要求：

- 项目整体使用 `GPL-3.0-or-later`。
- 源文件使用 SPDX 标识。
- `THIRD_PARTY_NOTICES.md` 注明 `ssf2fcitx` 的作者、仓库和复用范围。
- 从 `ssf2fcitx` 复制或翻译的代码必须保留相应版权声明。
- 不随发布包分发用户提供的皮肤、图片和作者联系方式。
- 本节为工程合规要求，不替代具体司法辖区的法律意见。

## 24. 后续版本候选

- Windows ARM64 发布。
- 批量转换目录。
- `--all-layouts` 同时生成横排和竖排配置包。
- 交互式配色选择。
- 更强的图片聚类和对比度优化。
- 完整资源上限和解压配额。
- 覆盖率引导的模糊测试。
- GUI 拖放界面。
- 小狼毫位图背景渲染扩展，作为独立项目评估。

## 25. 参考资料

- [Rime Weasel 仓库](https://github.com/rime/weasel)
- [Weasel 0.17.4 默认配置](https://github.com/rime/weasel/blob/f9203cae5e2b0796d94575b975f62a6be9614b00/output/data/weasel.yaml)
- [Weasel `UIStyle` 数据结构](https://github.com/rime/weasel/blob/f9203cae5e2b0796d94575b975f62a6be9614b00/include/WeaselIPCData.h)
- [Weasel 候选窗实现](https://github.com/rime/weasel/blob/f9203cae5e2b0796d94575b975f62a6be9614b00/WeaselUI/WeaselPanel.cpp)
- [`ssf2fcitx` 仓库](https://github.com/VOID001/ssf2fcitx)
- [`ssf2fcitx` 解密和包解析](https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/ssfextract.cpp)
- [`ssf2fcitx` 字段映射](https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/convert.cpp)
- [`ssf2fcitx` 许可证](https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/LICENSE)
- [.NET 官方支持策略](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [.NET 单文件发布](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 26. 决策记录

| 决策 | 结果 | 原因 |
|---|---|---|
| 产品方向 | 配置近似转换 | 无需修改小狼毫，首版风险较低 |
| 交互方式 | CLI | 便于自动化、测试和首版交付 |
| 技术栈 | C# / .NET 10 LTS | Windows 集成、格式库和可维护性 |
| 发布方式 | self-contained single-file | 用户无需安装 .NET |
| 默认行为 | 只转换 | 避免隐式修改用户配置 |
| 安装行为 | 显式 `--install` | 明确授权系统状态变更 |
| 安装保护 | 备份、原子写入、部署失败回滚 | 保证配置可恢复 |
| 首版目标 | 正确运行优先 | 视觉质量在 P1 迭代 |
| 许可证 | GPL-3.0-or-later | 与允许复用的参考实现兼容 |

## 27. 需求追踪矩阵

### 27.1 功能需求

| ID | 功能需求 | 主要实现章节 | 验证用例 |
|---|---|---|---|
| FR-001 | 按文件签名识别 ZIP 型和 `Skin` 型 SSF | 8.1 | IT-001、IT-002、IT-003 |
| FR-002 | 读取 ZIP 型 SSF 并定位 `skin.ini` | 8.2 | IT-001、IT-003 |
| FR-003 | 解密、解压并读取旧式 `Skin` 文件包 | 8.3 | IT-002、IT-011 |
| FR-004 | 解析 UTF-16LE 和 UTF-8 `skin.ini` | 9 | IT-001、IT-002、IT-003 |
| FR-005 | 构建与目标平台无关的标准化皮肤模型 | 10 | 单元测试、IT-001 至 IT-003 |
| FR-006 | 按横排或竖排选取源方案并执行回退 | 11 | IT-004、IT-005 |
| FR-007 | 映射元数据、字体、颜色和布局 | 12 | IT-004、IT-005 |
| FR-008 | 分析图片、蒙版和 GIF 第一帧 | 12.5 | IT-001、IT-003、IT-004 |
| FR-009 | 报告所有无法映射的源特性 | 12.7、13.2 | IT-001 至 IT-005 |
| FR-010 | 生成并自验证 `weasel.custom.yaml` | 13.1 | IT-004、IT-005 |
| FR-011 | 生成结构化 `conversion-report.json` | 13.2 | IT-004、IT-005、IT-013 |
| FR-012 | 生成确定性 `preview.png` | 13.3 | IT-004、IT-005 |
| FR-013 | 提供 `convert` 命令 | 14.2 | IT-004、IT-005、IT-007 |
| FR-014 | 提供 `inspect` 命令 | 14.3 | IT-001、IT-002、IT-003 |
| FR-015 | 提供 `validate` 命令 | 14.4 | 单元测试、IT-011、IT-012 |
| FR-016 | 提供 `restore` 命令 | 14.5 | IT-009、IT-010 |
| FR-017 | 只有显式 `--install` 才修改用户配置 | 15.1 | 安装集成测试 |
| FR-018 | 安装前备份并语义合并现有配置 | 15.3、15.4 | IT-008、IT-010 |
| FR-019 | 使用临时文件和原子替换写入配置 | 15.3 | IT-010 |
| FR-020 | 部署失败时自动恢复备份 | 15.3 | IT-009 |
| FR-021 | 普通模式和 JSON 模式均输出稳定错误代码 | 16 | IT-011、IT-013 |
| FR-022 | 支持 Unicode 输入、输出和元数据 | 18.1 | IT-006 |

### 27.2 非功能需求

| ID | 非功能需求 | 验证方式 |
|---|---|---|
| NFR-001 | Windows 10/11 x64、无需预装 .NET | 干净环境发布测试 |
| NFR-002 | 默认离线运行且不发送遥测 | 网络隔离测试和代码审查 |
| NFR-003 | 三个基线样本单次转换不超过 5 秒 | 性能测试 |
| NFR-004 | 相同输入和环境产生确定性 YAML 与预览 | 重复运行哈希比较 |
| NFR-005 | 所有输入错误均受控返回，无进程崩溃 | 负向测试和异常注入 |
| NFR-006 | 安装失败不留下半写入配置 | IT-009、IT-010 |
| NFR-007 | 诊断代码和报告 schema 具有版本稳定性 | 契约测试 |
| NFR-008 | 第三方依赖版本及许可证可追踪 | 发布审查 |
| NFR-009 | 真实皮肤样本不随公开程序分发 | 发布包检查 |
| NFR-010 | 源代码符合 GPL-3.0-or-later 合规要求 | SPDX 和第三方声明检查 |
