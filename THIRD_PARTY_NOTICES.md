# Third-Party Notices

This file lists third-party software used by ssf2weasel and the scope of
reuse, as required by GPL-3.0-or-later compliance (requirements §23).

## ssf2fcitx

- Project: https://github.com/VOID001/ssf2fcitx
- Author: VOID001
- License: GPL-3.0-or-later
- Reference commit: `a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b`
- Reused scope: the legacy `Skin` container AES-256-CBC key and IV, the
  outer/inner layout of the encrypted package, and the field-mapping
  insights that informed the Weasel conversion rules. The algorithms were
  reimplemented in managed C#; no C++ source was copied verbatim.
- Primary sources:
  - https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/ssfextract.cpp
  - https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/convert.cpp
  - https://github.com/VOID001/ssf2fcitx/blob/a8e7e1d7bb7287582c184d4a8dd64473ad94aa2b/LICENSE

## YamlDotNet

- Project: https://github.com/aaubry/YamlDotNet
- Package: YamlDotNet 18.1.0
- License: MIT
- Used for: reading and validating weasel.custom.yaml during install/merge.

## System.Drawing.Common

- Project: https://github.com/dotnet/runtime (Windows Forms / GDI+ bindings)
- Package: System.Drawing.Common 10.0.10
- License: MIT
- Used for: PNG/BMP/GIF decoding, color analysis, font detection, and
  preview.png rendering on Windows.

## .NET Runtime (self-contained publish)

- Project: https://github.com/dotnet/runtime
- Version: .NET 10 LTS (bundled into the win-x64 single-file publish)
- License: MIT
- Used for: the self-contained runtime embedded in `ssf2weasel.exe`.

## Note on skin samples

Real Sogou skin files (`.ssf`) used for local acceptance testing are **not**
redistributed with this project. They remain the property of their respective
authors and must not be shipped in release archives.
