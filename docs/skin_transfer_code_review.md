# Static Code Review: skin_transfer

Review date: 2026-08-10  
Target: C:\Users\86136\skin_transfer  
Method: read-only static analysis

## 1. Strongest limitation

[KNOWN | Confidence: HIGH] The explicit no-modification constraint prevented builds, tests, executable launches, package extraction, formatters, linters, and project imports. Every finding below follows from source control flow, deterministic arithmetic, local manifests, or primary platform and license documentation. Runtime behavior that required execution was excluded.

[COMPUTED | Confidence: HIGH] The inventory contained 1,041 files totaling 209,908,811 bytes, including Git metadata, generated build artifacts, publish artifacts, samples, documentation, and 99 C# files. Detailed review covered all handwritten C# sources, all project and build configuration, tests and test support, requirements, release documents, publish manifests, and acceptance JSON/YAML. Binary and image artifacts were inventoried without execution or visual decoding.

## 2. Conclusion

[INFERRED | Confidence: HIGH] Release should be held until H-01 through H-04 are fixed or explicitly risk-accepted. They expose untrusted-input memory exhaustion and configuration rollback failures. H-05 requires a distribution-compliance review before release.

[COMPUTED | Confidence: HIGH] This report contains 17 prioritized findings: 5 High and 12 Medium.

## 3. High-severity findings

### H-01: Archive, decompression, and image expansion have no resource limits

Evidence:

- [ConversionPipeline.cs:39](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Pipeline/ConversionPipeline.cs:39) reads the complete input with <code>File.ReadAllBytes</code>.
- [ZipSsfPackageReader.cs:22](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/ZipSsfPackageReader.cs:22) through [ZipSsfPackageReader.cs:47](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/ZipSsfPackageReader.cs:47) copy every ZIP entry into an unbounded <code>MemoryStream</code> and then duplicate it with <code>ToArray</code>.
- [LegacyEncryptedSsfPackageReader.cs:85](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/LegacyEncryptedSsfPackageReader.cs:85) through [LegacyEncryptedSsfPackageReader.cs:109](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/LegacyEncryptedSsfPackageReader.cs:109) check the declared output length only after unbounded zlib expansion.
- [GdiImageColorAnalyzer.cs:37](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Imaging/GdiImageColorAnalyzer.cs:37) through [GdiImageColorAnalyzer.cs:45](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Imaging/GdiImageColorAnalyzer.cs:45) allocate decoded pixel buffers and a list sized from the full pixel count. No dimension or pixel-count quota exists.

[INFERRED | Confidence: HIGH] A compact SSF can expand to very large entry buffers and decoded images during inspect, validate, or convert. The process can enter severe paging, throw <code>OutOfMemoryException</code>, or terminate.

Smallest viable fix:

- Define limits for input bytes, entry count, per-entry bytes, cumulative uncompressed bytes, compression ratio, legacy declared length, image dimensions, decoded pixels, and generated YAML.
- Copy through a counting stream that aborts before the limit. Reject excessive legacy <code>expectedLength</code> before decompression and stop after <code>expectedLength + 1</code> bytes.
- Remove duplicate full-buffer retention where possible.

Verification:

- Use injectable low limits and test ZIP, zlib, and image inputs that exceed each limit by one unit.
- Assert a controlled package or asset error, bounded peak memory, no output artifacts, and success at the exact boundary.

[KNOWN] Existing tests cover corrupt signatures, truncation, offsets, traversal, and length mismatch. They do not cover expansion ratios, cumulative size, entry count, or decoded-pixel limits.

### H-02: Unsigned overflow bypasses the legacy offset-table bounds check

Evidence:

- [LegacyEncryptedSsfPackageReader.cs:125](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/LegacyEncryptedSsfPackageReader.cs:125) through [LegacyEncryptedSsfPackageReader.cs:138](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Ssf/LegacyEncryptedSsfPackageReader.cs:138) validate <code>8 + headerSize</code> in unsigned 32-bit arithmetic and then allocate a list with <code>headerSize / 4</code> capacity.

[COMPUTED | Confidence: HIGH] An eight-byte pack with <code>totalSize = 8</code> and <code>headerSize = 0xFFFFFFFC</code> passes the modulo check. The addition wraps from 4,294,967,300 to 4, so the length comparison also passes. The resulting capacity is 1,073,741,823 entries.

[INFERRED | Confidence: HIGH] A tiny malformed input can trigger an allocation attempt measured in gigabytes before any offset is read.

Smallest viable fix:

- Replace the addition check with a subtraction-safe comparison such as <code>headerSize &gt; (uint)span.Length - 8</code>.
- Enforce a separate maximum entry count and use checked conversions before allocating.

Verification:

- Pass the eight-byte case above directly to the parser and assert a controlled package-structure error with no large allocation.
- Add boundary tests for the largest accepted table and one entry beyond it.

### H-03: Thrown deployer errors bypass installation rollback

Evidence:

- [WeaselInstaller.cs:83](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselInstaller.cs:83) replaces <code>weasel.custom.yaml</code> before deployment.
- [WeaselInstaller.cs:104](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselInstaller.cs:104) through [WeaselInstaller.cs:114](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselInstaller.cs:114) enter rollback only when <code>Deploy()</code> returns false.
- [IWeaselDeployer.cs:23](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/IWeaselDeployer.cs:23) through [IWeaselDeployer.cs:51](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/IWeaselDeployer.cs:51) do not translate process-launch or wait exceptions into a failed result.
- [Microsoft Process.Start documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.start?view=net-10.0) documents operational exceptions including <code>Win32Exception</code>.

[INFERRED | Confidence: HIGH] If the deployer becomes inaccessible, is removed, or is not executable after initial discovery, the exception escapes after configuration replacement. [CliApplication.cs:59](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/CliApplication.cs:59) then returns internal error 70 while leaving the new configuration installed.

Smallest viable fix:

- Treat expected launch, wait, and termination exceptions as deployment failure at the deployer boundary.
- Guard every deployer call at the transaction boundary so failure enters rollback.
- Ensure the best-effort redeploy after rollback cannot mask the rollback result.

Verification:

- Inject a deployer that throws on its first call. Assert exact restoration of the previous bytes and exit 10.
- Inject failures during rollback and the second deployment. Assert exit 11 only when file restoration fails.

[KNOWN] [InstallTests.cs:14](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/InstallTests.cs:14) uses a boolean-only stub and has no throwing-deployer case.

### H-04: Restore reports successful rollback without performing rollback

Evidence:

- [RestoreService.cs:46](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/RestoreService.cs:46) through [RestoreService.cs:53](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/RestoreService.cs:53) create a safety backup and replace the active configuration.
- [RestoreService.cs:60](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/RestoreService.cs:60) through [RestoreService.cs:65](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/RestoreService.cs:65) throw <code>DeployFailedRolledBack</code> when deployment returns false. No code restores the safety backup.
- [ExitCode.cs:16](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/ExitCode.cs:16) defines exit 10 as deployment failed and rollback succeeded.

[KNOWN | Confidence: HIGH] Restoring backup A over current configuration B with a false-returning deployer leaves A active and B only in the backup directory, while exit 10 claims B was recovered.

[INFERRED | Confidence: HIGH] Automation and users can rely on a false recovery signal. A later deployment can activate the failed restore unexpectedly.

Smallest viable fix:

- On deployment failure, atomically restore the safety backup, or delete the target when no prior file existed.
- Return 10 only after file rollback succeeds. Return 11 when rollback fails.

Verification:

- Cover existing-target and absent-target restores with false-returning and throwing deployers.
- Assert exact target bytes, deployment call count, safety-backup path, and exit codes 10 and 11.

[KNOWN] [InstallTests.cs:162](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/InstallTests.cs:162) through [InstallTests.cs:183](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/InstallTests.cs:183) cover only successful restore without deployment.

### H-05: Release notices appear incomplete and misclassify the embedded Windows runtime

Evidence:

- [THIRD_PARTY_NOTICES.md:21](C:/Users/86136/skin_transfer/THIRD_PARTY_NOTICES.md:21) through [THIRD_PARTY_NOTICES.md:26](C:/Users/86136/skin_transfer/THIRD_PARTY_NOTICES.md:26) name YamlDotNet and MIT, while omitting the upstream copyright and permission text.
- The [YamlDotNet 18.1.0 license](https://github.com/aaubry/YamlDotNet/blob/v18.1.0/LICENSE.txt) requires its copyright and permission notice to accompany copies or substantial portions.
- [THIRD_PARTY_NOTICES.md:36](C:/Users/86136/skin_transfer/THIRD_PARTY_NOTICES.md:36) through [THIRD_PARTY_NOTICES.md:41](C:/Users/86136/skin_transfer/THIRD_PARTY_NOTICES.md:41) label the complete embedded .NET runtime as MIT.
- Microsoft states that CoreCLR and .NET runtimes embedded in Windows single-file binaries use the .NET Library License, while other files use MIT: [.NET license information for Windows](https://github.com/dotnet/core/blob/main/license-information-windows.md).

[INFERRED | Confidence: MED] The five-file release may distribute incomplete or inaccurate licensing material. This is an engineering compliance risk and requires qualified legal or release-compliance review.

Smallest viable fix:

- Generate notices from the exact dependency and runtime graph used by the release.
- Include the applicable YamlDotNet notice, .NET Library License, MIT material, and exact runtime third-party notice inventory.

Verification:

- Rebuild the dependency/license inventory from the exact published runtime version and complete a documented release-compliance review.

## 4. Medium-severity findings

### M-01: Concurrent install or restore operations can erase a successful update

Evidence:

- [WeaselInstaller.cs:63](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselInstaller.cs:63) through [WeaselInstaller.cs:114](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselInstaller.cs:114) perform read, merge, backup, replace, deploy, and rollback without a cross-process lock or content-version check.

[INFERRED | Confidence: HIGH] Two processes can read original state O. Process A can write and successfully deploy A. Process B can then write B, fail deployment, and restore its stale backup O, erasing A even though A already reported success.

Smallest viable fix:

- Acquire a named mutex or exclusive lock scoped to the canonical Rime directory before the first read, and hold it through deployment or rollback.
- Add an optimistic content-hash check immediately before replacement and rollback.

Verification:

- Use barriers in two installer instances to force the interleaving above. Assert serialization or conflict detection and preservation of the successfully committed state.

### M-02: BOM-less UTF-16LE detection fails for CJK-heavy metadata

Evidence:

- [SkinIniParser.cs:109](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Ini/SkinIniParser.cs:109) through [SkinIniParser.cs:119](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Ini/SkinIniParser.cs:119) fall through to strict UTF-8 when the UTF-16 heuristic fails.
- [SkinIniParser.cs:160](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Ini/SkinIniParser.cs:160) through [SkinIniParser.cs:179](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Ini/SkinIniParser.cs:179) require at least 60 percent of odd-position bytes in the first 512 bytes to be zero.
- [README.md:72](C:/Users/86136/skin_transfer/README.md:72) documents BOM-less UTF-16LE support.

[COMPUTED | Confidence: HIGH] The character 中 is <code>2D 4E</code> in UTF-16LE and contributes no odd-position zero. A long Chinese value early in the file can push the ratio below 60 percent. Strict UTF-8 accepts those ASCII-range bytes and embedded NUL bytes from the ASCII syntax, so the parser receives corrupted section and key names.

[INFERRED | Confidence: HIGH] A documented supported encoding can fail with <code>SCHEME_MISSING</code> for valid CJK-heavy skins.

Smallest viable fix:

- Attempt strict UTF-16LE decoding for even-length NUL-bearing input and validate basic INI structure before accepting UTF-8.

Verification:

- Add a BOM-less UTF-16LE fixture whose first 512 bytes contain a long Chinese value. Assert correct General and Scheme_H1 parsing.

[KNOWN] Existing encoding coverage uses ASCII-dominant content.

### M-03: A same-ID scalar, sequence, or null scheme is overwritten without force

Evidence:

- [WeaselCustomMerger.cs:98](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:98) through [WeaselCustomMerger.cs:114](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:114) recognize an existing same-ID scheme only when its value is a mapping.
- [WeaselCustomMerger.cs:122](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:122) through [WeaselCustomMerger.cs:156](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:156) replace an existing key during patch insertion.

[KNOWN | Confidence: HIGH] A user-owned value such as <code>"preset_color_schemes/test_skin": user_value</code> is treated as absent and replaced even when <code>force</code> is false.

Smallest viable fix:

- Detect key presence independently of node type. Treat any non-mapping same-ID node as a foreign conflict.

Verification:

- Add direct and nested scalar, sequence, and null cases. Assert exit 8 and byte-for-byte preservation without force. Foreign entries should remain protected with force under the current ownership policy.

[KNOWN] Existing YAML conflict tests cover mapping-valued schemes only.

### M-04: A wrong-type patch node becomes internal error 70

Evidence:

- [WeaselCustomMerger.cs:31](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:31) through [WeaselCustomMerger.cs:35](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Yaml/WeaselCustomMerger.cs:35) treat a missing patch mapping and an existing non-mapping patch node identically, then add another <code>patch</code> key.

[INFERRED | Confidence: HIGH] For syntactically valid YAML such as <code>patch: []</code> or <code>patch: scalar</code>, adding the duplicate mapping key raises an argument error. The generic CLI catch maps it to exit 70 instead of a controlled YAML/install error.

Smallest viable fix:

- Distinguish an absent key from a present key with the wrong node type and raise <code>YamlInvalid</code> with install exit 9.

Verification:

- Test scalar, sequence, and null patch nodes. Assert stable diagnostics, exit 9, and unchanged configuration.

### M-05: Conversion commits three outputs non-transactionally

Evidence:

- [ConvertCommand.cs:45](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Commands/ConvertCommand.cs:45) through [ConvertCommand.cs:56](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Commands/ConvertCommand.cs:56) perform a preflight existence check.
- [ConvertCommand.cs:61](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Commands/ConvertCommand.cs:61) through [ConvertCommand.cs:64](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Commands/ConvertCommand.cs:64) write YAML, JSON, and PNG directly and sequentially.

[KNOWN | Confidence: HIGH] Failure on the second or third write leaves a partial output set. With force, old and new artifacts can be mixed. A competing writer can also create a file between the preflight check and truncating write.

[INFERRED | Confidence: HIGH] A failed conversion can make its own retry fail with output-conflict exit 8 and can leave artifacts that appear complete when inspected separately.

Smallest viable fix:

- Stage all three files, validate them, and commit them as one defined transaction with rollback.
- Use create-new semantics when force is absent and map expected I/O failures to output error 7.

Verification:

- Inject failure after each staged write. Assert preservation of the previous complete set, removal of temporary files, and a stable documented exit code.

### M-06: A valued option consumes the next option token as data

Evidence:

- [CliOptions.cs:28](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/CliOptions.cs:28) through [CliOptions.cs:35](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/CliOptions.cs:35) accept any next token as an option value.
- [CliApplication.cs:20](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/CliApplication.cs:20) through [CliApplication.cs:23](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/CliApplication.cs:23) detect global JSON mode by scanning the raw argument list independently.

[KNOWN | Confidence: HIGH] <code>convert valid.ssf --output --json</code> stores <code>--json</code> as the output directory. The outer application believes JSON mode is active while the command does not, so it can create a directory named <code>--json</code> and emit human-readable standard output.

Smallest viable fix:

- Use one parser for global and command options.
- Reject a recognized option token when a value is required. Support an explicit end-of-options marker for literal option-shaped paths.

Verification:

- Cover every valued option followed by another option. Assert usage exit 2, no filesystem changes, and one valid JSON error document when JSON mode is selected correctly.

### M-07: Missing Chinese font can select the Latin font for labels and comments

Evidence:

- [WeaselMapper.cs:37](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:37) through [WeaselMapper.cs:46](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:46) choose the first usable font as <code>primaryCjkFont</code>.
- [WeaselMapper.cs:135](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:135) through [WeaselMapper.cs:146](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:146) assign it to label and comment font fields.
- [Technical requirements:437](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:437) through [technical requirements:445](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:445) require Microsoft YaHei when the Chinese font is missing or unavailable.

[KNOWN | Confidence: HIGH] If <code>font_ch</code> is absent and installed <code>font_en</code> is Arial, Arial is first and becomes the label and comment font despite the documented CJK fallback.

Smallest viable fix:

- Track the usable Chinese font independently and assign <code>installedChineseFont ?? FallbackFont</code> to label and comment fields.

Verification:

- Assert Microsoft YaHei for label and comment when only Arial is installed.

[KNOWN] Existing mapper coverage constructs this state but asserts only the combined fallback list.

### M-08: Malformed numeric lists lose positions and silently map the wrong values

Evidence:

- [NormalizedSkinBuilder.cs:195](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Model/NormalizedSkinBuilder.cs:195) through [NormalizedSkinBuilder.cs:208](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Model/NormalizedSkinBuilder.cs:208) discard every unparsable list token.
- [WeaselMapper.cs:224](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:224) through [WeaselMapper.cs:249](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Core/Mapping/WeaselMapper.cs:249) interpret the compacted list positionally and silently use fallback for missing positions.
- [Technical requirements:516](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:516) through [technical requirements:520](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:520) require invalid or missing layout values to fall back with a warning.

[COMPUTED | Confidence: HIGH] <code>zhongwen_marge=bad,6</code> becomes <code>[6]</code>. Six then occupies index zero and is accepted without an invalid-layout diagnostic.

[INFERRED | Confidence: HIGH] Corrupt input can produce plausible but incorrect geometry while the report claims no layout fallback.

Smallest viable fix:

- Preserve per-position validity or reject the complete field when any token fails.
- Emit <code>LAYOUT_VALUE_INVALID</code> for malformed and undersized lists.

Verification:

- Test invalid first, middle, and final tokens plus missing fields. Assert documented fallbacks and diagnostics.

### M-09: Missing, corrupt, and wrong-sized masks are silently ignored

Evidence:

- [ConversionPipeline.cs:183](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Pipeline/ConversionPipeline.cs:183) through [ConversionPipeline.cs:190](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Pipeline/ConversionPipeline.cs:190) turn a missing mask into null without a diagnostic.
- [GdiImageColorAnalyzer.cs:37](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Imaging/GdiImageColorAnalyzer.cs:37) through [GdiImageColorAnalyzer.cs:41](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Imaging/GdiImageColorAnalyzer.cs:41) also turn an undecodable or dimension-mismatched mask into null.
- [Technical requirements:715](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:715) through [technical requirements:719](C:/Users/86136/skin_transfer/docs/ssf2weasel-technical-requirements.md:719) require validation of SSF resource references.

[KNOWN | Confidence: HIGH] Conversion proceeds by analyzing the full base image, so generated colors can differ from the intended masked region while validate can still report success.

Smallest viable fix:

- Validate every referenced mask before analysis and return structured status for missing, undecodable, and dimension-mismatched cases.
- Emit a specific diagnostic and apply the documented alpha fallback explicitly.

Verification:

- Add missing, corrupt, and wrong-sized mask fixtures. Assert diagnostics, fallback selection, and validate behavior.

### M-10: Automatic Weasel discovery can select a stale or older installation

Evidence:

- [WeaselEnvironment.cs:31](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselEnvironment.cs:31) through [WeaselEnvironment.cs:39](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselEnvironment.cs:39) accept a registry directory based only on directory existence.
- [WeaselEnvironment.cs:65](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselEnvironment.cs:65) through [WeaselEnvironment.cs:67](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Infrastructure/Install/WeaselEnvironment.cs:67) sort fallback paths lexicographically.

[COMPUTED | Confidence: HIGH] Descending string order selects <code>weasel-0.9.0</code> ahead of <code>weasel-0.17.4</code>. A stale registry directory that lacks <code>WeaselDeployer.exe</code> is returned before valid fallback directories are considered.

Smallest viable fix:

- Require the deployer executable for every candidate.
- Parse version suffixes with <code>Version.TryParse</code>, sort semantically, and define deterministic handling for malformed names.

Verification:

- Test stale registry data and directories named 0.9.0, 0.17.4, 0.17.10, and malformed variants.

### M-11: The documented publish command cannot reproduce the claimed release package

Evidence:

- [README.md:78](C:/Users/86136/skin_transfer/README.md:78) through [README.md:83](C:/Users/86136/skin_transfer/README.md:83) document one <code>dotnet publish</code> command targeting the publish directory.
- [Ssf2Weasel.Cli.csproj:1](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Ssf2Weasel.Cli.csproj:1) through [Ssf2Weasel.Cli.csproj:20](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/Ssf2Weasel.Cli.csproj:20) contain no content-copy or checksum-generation targets.
- [PublishOutputs.09cde80c3b.txt:1](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/obj/Release/net10.0-windows/win-x64/PublishOutputs.09cde80c3b.txt:1) through [PublishOutputs.09cde80c3b.txt:4](C:/Users/86136/skin_transfer/src/Ssf2Weasel.Cli/obj/Release/net10.0-windows/win-x64/PublishOutputs.09cde80c3b.txt:4) record only the executable and three PDB files.
- [implementation-status.md:97](C:/Users/86136/skin_transfer/docs/implementation-status.md:97) through [implementation-status.md:105](C:/Users/86136/skin_transfer/docs/implementation-status.md:105) claim a five-file release containing documentation, notices, and a checksum.

[KNOWN | Confidence: HIGH] README, LICENSE, THIRD_PARTY_NOTICES, and the checksum are outside the recorded build graph. A clean publish can omit them, while publishing into a dirty directory can retain stale copies.

Smallest viable fix:

- Add a clean release script or build target that publishes into an empty staging directory, copies exact-version documents and notices, generates hashes, and validates an explicit manifest.

Verification:

- Run the release process into an empty directory and compare the exact file set and hashes against the manifest.

[COMPUTED | Confidence: HIGH] The currently present executable hash does match <code>publish/ssf2weasel.exe.sha256</code>; this confirms the current pair only.

### M-12: Acceptance tests can be green without samples or the shipped executable

Evidence:

- [RealSampleTests.cs:24](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/RealSampleTests.cs:24) through [RealSampleTests.cs:32](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/RealSampleTests.cs:32), plus the corresponding branches at lines 65, 94, 112, and 134, return normally when samples are absent.
- [CliTestHarness.cs:11](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/CliTestHarness.cs:11) through [CliTestHarness.cs:16](C:/Users/86136/skin_transfer/tests/Ssf2Weasel.IntegrationTests/CliTestHarness.cs:16) invoke <code>CliApplication.Run</code> in-process.

[COMPUTED | Confidence: HIGH] Five three-row baseline theories account for 15 cases, and the additional-sample theory accounts for two. When all samples are absent, these 17 cases return without assertions and are reported by xUnit as passed.

[KNOWN | Confidence: HIGH] The integration harness never launches the published executable. Program entry behavior, OS argument handling, actual process exit status, single-file runtime behavior, and source-to-artifact freshness remain outside the suite.

Smallest viable fix:

- Mark development-time sample absence as an explicit skip and add a release acceptance target that fails when required samples are unavailable.
- Add process-level tests against the exact staged release executable.

Verification:

- Remove all samples. Ordinary tests should report explicit skips, while release acceptance should fail.
- Run the staged executable for help, version, JSON success/error, Unicode paths, and deployment-failure cases, asserting real stdout, stderr, and process exit codes.

## 5. Review coverage and excluded claims

[KNOWN | Confidence: HIGH] The review included Core parsing, normalization, mapping and reporting; Infrastructure container readers, imaging, YAML, installation, restore, atomic-write helpers and pipeline; all CLI commands and option handling; all handwritten tests; project configuration; requirements; release documents; publish manifests; and acceptance JSON/YAML.

[KNOWN | Confidence: HIGH] No dependency vulnerability scan was performed because it can require network resolution and generated state. No claim about current CVEs is made.

[KNOWN | Confidence: HIGH] Native GDI+ behavior, actual Weasel deployment, preview visual fidelity, clean-machine compatibility, Authenticode status, and source-to-published-binary equivalence were not established.

[KNOWN | Confidence: HIGH] Lower-impact documentation, verbosity, cancellation, and report-shape gaps were reviewed but excluded from the prioritized list to keep the report focused on failure paths with greater user or release impact.

## 6. Target integrity

[COMPUTED | Confidence: HIGH] The full target fingerprint before and after review is identical:

- File count: 1,041
- Total bytes: 209,908,811
- Aggregate SHA-256: <code>211CEC54C4FEB745577046E912DD8A6A58B7209B4E227DA407D12B7270984381</code>
- Initial snapshot: 2026-08-10 21:30:37 UTC
- Final snapshot: 2026-08-10 21:47:13 UTC

[KNOWN | Confidence: HIGH] The aggregate hashes sorted records containing normalized relative path, file length, UTC modification-time ticks, and content SHA-256. Git metadata was included. No file under C:\Users\86136\skin_transfer was created, edited, deleted, renamed, or timestamp-modified by this review.

## 7. Skill installation record

[KNOWN | Confidence: HIGH] The supplied source was installed project-locally as <code>.agents/skills/vcoding/SKILL.md</code>. A read-only comparison with the supplied raw GitHub file returned exact normalized-text equality and equal length, 13,469 characters. The installed file SHA-256 is <code>A400EBD9FA37571EE096FE13592EB833CEFA9DBE6B51A566FB6D0F7EDE894BB7</code>.

## 8. Rules audit

[RULES I BROKE]: Multiple delegated and primary read-only PowerShell inventory commands used semicolons between statements, contrary to the command-formatting instruction. Two delegated Git attempts were rejected by Git safe-directory checks before repository access. One primary hash-reconstruction command produced excessive error output because the host PowerShell lacked Path.GetRelativePath. These were execution-formatting errors, were not caused by higher-priority instructions, and changed no target file.
