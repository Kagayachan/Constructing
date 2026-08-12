// SPDX-License-Identifier: GPL-3.0-or-later
namespace Ssf2Weasel.Core.Diagnostics;

/// <summary>Stable diagnostic codes (requirements §16, §18.4).</summary>
public static class DiagnosticCodes
{
    // Container / package level
    public const string SsfUnsupportedContainer = "SSF_UNSUPPORTED_CONTAINER";
    public const string SsfDecryptFailed = "SSF_DECRYPT_FAILED";
    public const string SsfDecompressedLengthMismatch = "SSF_DECOMPRESSED_LENGTH_MISMATCH";
    public const string SsfPackageStructureInvalid = "SSF_PACKAGE_STRUCTURE_INVALID";
    public const string SsfDuplicateEntry = "SSF_DUPLICATE_ENTRY";
    public const string SsfUnsafeEntryPath = "SSF_UNSAFE_ENTRY_PATH";
    public const string SsfEntryUnreadable = "SSF_ENTRY_UNREADABLE";

    // skin.ini level
    public const string SsfIniMissing = "SSF_INI_MISSING";
    public const string SsfIniEncodingUnsupported = "SSF_INI_ENCODING_UNSUPPORTED";
    public const string IniDuplicateKey = "INI_DUPLICATE_KEY";
    public const string IniTrailingGarbage = "INI_TRAILING_GARBAGE";
    public const string IniUnknownSection = "INI_UNKNOWN_SECTION";
    public const string IniEncodingLegacyAnsi = "INI_ENCODING_LEGACY_ANSI";

    // Conversion level
    public const string ColorInvalid = "COLOR_INVALID";
    public const string FontNotInstalled = "FONT_NOT_INSTALLED";
    public const string SchemeFallback = "SCHEME_FALLBACK";
    public const string SchemeMissing = "SCHEME_MISSING";
    public const string LayoutValueInvalid = "LAYOUT_VALUE_INVALID";
    public const string AnimatedAssetDegraded = "ANIMATED_ASSET_DEGRADED";
    public const string AssetMissing = "ASSET_MISSING";
    public const string AssetUndecodable = "ASSET_UNDECODABLE";
    public const string UnsupportedFeature = "UNSUPPORTED_FEATURE";

    // Output / install level
    public const string OutputConflict = "OUTPUT_CONFLICT";
    public const string ColorSchemeIdConflict = "COLOR_SCHEME_ID_CONFLICT";
    public const string YamlInvalid = "YAML_INVALID";
    public const string InstallFailed = "INSTALL_FAILED";
    public const string DeployFailed = "DEPLOY_FAILED";
    public const string RollbackFailed = "ROLLBACK_FAILED";
    public const string BackupInvalid = "BACKUP_INVALID";
    public const string WeaselNotFound = "WEASEL_NOT_FOUND";
}
