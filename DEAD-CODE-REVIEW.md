# Dead code review — ssf2weasel (`Constructing-main`)

Scope: dead code only. Correctness, security and performance are out of scope.
6,812 lines total: 4,767 lines of C# under `src/`, 1,884 under `tests/`, 161 of project
config. No `dotnet` on this machine, so every finding below is static: symbol declared,
zero readers.

---

## 0. The whole test tree is dead — it cannot compile

Not "unused" — *unbuildable*. Four independent mismatches, any one of them fatal:

- `ssf2weasel.slnx:L3-5`: `delete:` references `src/Ssf2Weasel.Cli/`, `src/Ssf2Weasel.Core/`,
  `src/Ssf2Weasel.Infrastructure/`. The projects on disk are `src/Cli/Cli.csproj`,
  `src/Core/Core.csproj`, `src/Infrastructure/Infrastructure.csproj`. The solution does not load.
- `tests/*/*.csproj`: every `ProjectReference` points at `..\..\src\Ssf2Weasel.*\Ssf2Weasel.*.csproj`. None exist.
- All 12 test files `using Ssf2Weasel.Core.*` / `Ssf2Weasel.Infrastructure.*` / `Ssf2Weasel.Cli`.
  Source declares `Core.*`, `Infrastructure.*`, `Cli` — no `Ssf2Weasel` prefix anywhere.
- `tests/Ssf2Weasel.UnitTests/LegacyReaderTests.cs:L44,55,83,96,107` assert on `Ssf2WeaselException`.
  The type is `ToolException` (`src/Core/ToolException.cs:L8`). It has no other name in the repo.
- `src/Core/Core.csproj:L6-7`, `src/Infrastructure/Infrastructure.csproj:L9-11`:
  `InternalsVisibleTo` names `UnitTests` / `IntegrationTests` / `TestSupport`; the assemblies are
  `Ssf2Weasel.UnitTests` etc. So even after the paths are fixed, every `internal` test target
  (`SkinIniParser.DecodeText`, `ZipSsfPackageReader.IsSafeEntryName`, `LegacyEncryptedSsfPackageReader.AesKey`,
  `WeaselYamlWriter.QuoteString`, `WeaselCustomMerger.SetPatchValue`, `WeaselMapper.HasMinimumContrast`,
  `WeaselEnvironment.ParseWeaselVersion`) is still inaccessible.

1,884 lines of tests that never run. **Do not delete these** — rewire them. That means one namespace
rename across src (or a `<RootNamespace>` change plus namespace edits), three `slnx` paths, seven
`ProjectReference` paths, six `InternalsVisibleTo` names, one exception-type rename. Until then the
repo has zero test coverage and every finding below is unverified by any test.

---

## 1. Declared, never read — delete outright

```
src/Core/Mapping/SkinIdGenerator.cs:L71        delete: IsValid(string). Zero callers. Nothing replaces it.
src/Infrastructure/Yaml/WeaselPatchBuilder.cs:L64-66  delete: ManagedStylePaths(theme). Zero callers. Nothing.
src/Core/Package/ISsfPackageReader.cs:L7       delete: CanRead(header). SsfContainerDetector.Detect does the
                                                       signature check itself; the interface method is never called.
src/Infrastructure/Ssf/ZipSsfPackageReader.cs:L20-21          delete: CanRead impl. Follows the interface member.
src/Infrastructure/Ssf/LegacyEncryptedSsfPackageReader.cs:L38-41  delete: CanRead impl. Same.
src/Core/Model/NormalizedSkin.cs:L63           delete: SkinAsset.NormalizedName. Written at
                                                       NormalizedSkinBuilder.cs:L158, never read. Lookup is already
                                                       OrdinalIgnoreCase everywhere.
src/Core/Model/NormalizedSkin.cs:L21           delete: SkinMetadata.Id. Read from skin_id at
                                                       NormalizedSkinBuilder.cs:L33, never read back. ReportSkin omits it.
src/Infrastructure/Pipeline/ConversionPipeline.cs:L20  delete: LoadedSkin.FilePath. Set at L102, never read.
src/Infrastructure/Pipeline/ConversionPipeline.cs:L26  delete: LoadedSkin.Ini. Set at L108, never read. `ini` is
                                                       already consumed inside Load; nothing downstream wants the
                                                       raw document.
src/Core/Package/SkinPackage.cs:L50            delete: SkinPackage.Container property (+ ctor param L25, L27).
                                                       Every consumer reads LoadedSkin.Container instead.
src/Core/Diagnostics/Diagnostic.cs:L8          delete: DiagnosticSeverity.Error. Never constructed anywhere.
                                                       Only Info and Warning are ever emitted.
```

## 2. Values that are structurally constant — the field is decoration

```
src/Infrastructure/Yaml/WeaselCustomMerger.cs:L17  delete: MergeResult.ReplacedExistingScheme. Never read by any
                                                     caller. Cascades: CheckConflict (L83) can return void, Merge
                                                     can return string, the MergeResult record disappears.
src/Infrastructure/Install/WeaselInstaller.cs:L19  delete: InstallResult.RolledBack. Constructed `false` at L110 and
                                                     L115 — the only two return paths. Every rollback path throws
                                                     instead of returning. It is serialized to the documented JSON
                                                     contract (ConvertCommand.cs:L106) as a permanent `false`.
src/Infrastructure/Imaging/GdiImageColorAnalyzer.cs:L160  delete: DominantColor's `excludeNear` parameter. Both call
                                                     sites (L95, L220) pass `excludeNear: null`, so the guard at
                                                     L166-169 never fires. Drop the param and the branch.
src/Infrastructure/Ssf/SsfContainerDetector.cs:L40  delete: the `_ => throw ArgumentOutOfRangeException` arm.
                                                     SsfContainerKind has exactly two members, both handled above.
```

## 3. Dead flexibility — plumbing with no source

```
src/Infrastructure/Pipeline/ConversionPipeline.cs:L40, src/Core/Package/ISsfPackageReader.cs:L9,
src/Infrastructure/Ssf/ZipSsfPackageReader.cs:L23,L87, LegacyEncryptedSsfPackageReader.cs:L43,L136,L161,
src/Cli/CliApplication.cs:L54-58, src/Core/ExitCode.cs:L18
  yagni: the entire cancellation path. All three call sites pass CancellationToken.None
         (ConvertCommand.cs:L35, InspectCommand.cs:L22, ValidateCommand.cs:L47). Nothing constructs a
         CancellationTokenSource, no Console.CancelKeyPress handler exists. So the five
         ThrowIfCancellationRequested() calls can never throw, `catch (OperationCanceledException)` is
         unreachable, and ExitCode.Cancelled = 12 is undocumented-in-practice.
         Either wire Ctrl-C in Program.cs (~4 lines) or drop the parameter chain (~20 lines).

src/Core/Model/NormalizedSkin.cs:L59, src/Core/Model/NormalizedSkinBuilder.cs:L110-124,L218-225
  yagni: StatusBarDefinition. Both fields are dead — BackgroundAsset and ReferencedAssets have zero
         readers. The only two consumers (WeaselMapper.cs:L377, InspectCommand.cs:L38) test
         `StatusBar is not null`. LooksLikeAssetName exists solely to populate the dead list.
         Replace the record, BuildStatusBar and LooksLikeAssetName with
         `bool hasStatusBar = ini.GetSection("StatusBar") is not null;` — ~30 lines gone.

src/Cli/Commands/InspectCommand.cs:L16, ValidateCommand.cs:L16, RestoreCommand.cs:L14
  delete: the `stderr` parameter. Unused in all three bodies. Only ConvertCommand writes to it.
```

## 4. Duplicated code — one copy is dead weight

```
src/Cli/Commands/ConvertCommand.cs:L47-58   delete: the pre-flight conflict check. WriteOutputsTransactionally
                                              re-checks the identical condition at L159-168 and throws the same
                                              ToolException with the same code and hint.
src/Infrastructure/Install/AtomicFileWriter.cs:L54-85  shrink: WriteBytesAtomic duplicates WriteAtomic's body
                                              verbatim except for WriteAllBytes vs WriteAllText. Make WriteAtomic
                                              call `WriteBytesAtomic(path, Utf8NoBom.GetBytes(content), verify)`.
src/Infrastructure/Install/RestoreService.cs:L95-131   shrink: SafeDeploy and TryRollback are copy-pasted from
                                              WeaselInstaller.cs:L139-173, comments included. One shared helper.
src/Cli/Commands/InspectCommand.cs:L24-46   shrink: rebuilds the degradation list that
                                              WeaselMapper.CollectUnsupportedFeatures (WeaselMapper.cs:L331-383)
                                              already produces, with different wording for the same conditions.
                                              Two sources of truth that will drift.
src/Core/Ini/SkinIniParser.cs:L10-13 + src/Core/Model/NormalizedSkinBuilder.cs:L14-17
                                            shrink: KnownSections declared twice, identical. One const in Core.
                                              (The unknown-section set is also computed twice: a diagnostic at
                                              SkinIniParser.cs:L44 and a list at NormalizedSkinBuilder.cs:L69.)
src/Infrastructure/Reporting/ReportWriter.cs:L20-21   delete: Write(ConversionReport). One caller
                                              (ConvertCommand.cs:L66), and it is `WriteAny` with a narrower type.
```

---

## Verification notes

- No `dotnet` SDK on this machine and the target is `net10.0-windows`, so nothing was compiled.
  Every claim above is a grep over `src/` and `tests/` for readers of a declared symbol.
- Section 0's four mismatches are textual and unambiguous — no build needed to confirm them.
- Nothing in section 1–4 is referenced by the test tree either (checked), but since that tree does not
  compile, it could not have protected any of it regardless.
- Not flagged, deliberately: `ResourceLimits` (all seven fields read), `DiagnosticCodes` (all 41 constants
  used), `ExitCode` (all 13 used — `Cancelled` only via the unreachable path in section 3),
  `WeaselTheme.GetColor`/`GetLayout` (PreviewRenderer), `ColorNormalizer.FromRgba` (WeaselMapper.WithAlpha).

**net: -190 lines of src deletable, plus 1,884 lines of tests to rewire or lose.**

---

# Applied to `src/` — 4,767 → 4,631 lines of C# (−136)

Behaviour is unchanged: same CLI text, same JSON fields, same exit codes, same
diagnostics, same error ordering.

**Deleted outright** — `SkinIdGenerator.IsValid`, `WeaselPatchBuilder.ManagedStylePaths`,
`ISsfPackageReader.CanRead` and both implementations, `SkinAsset.NormalizedName`,
`SkinMetadata.Id`, `LoadedSkin.FilePath`, `LoadedSkin.Ini`, `SkinPackage.Container`
(and its constructor parameter), `DiagnosticSeverity.Error`, `ReportWriter.Write`
(callers use `WriteAny`), and the unused `stderr` parameter on `inspect`, `validate`
and `restore`.

**Collapsed** — `StatusBarDefinition` had two fields and no readers; it is now
`NormalizedSkin.HasStatusBar`, which deleted `BuildStatusBar` and `LooksLikeAssetName`.
`MergeResult` existed to carry `ReplacedExistingScheme`, which nobody read, so
`WeaselCustomMerger.Merge` returns `string` and `CheckConflict` returns `void`.
`DominantColor` lost its always-null `excludeNear` parameter and the branch behind it.
`KnownSections` is now one `internal` array on `SkinIniParser`.

**Deduplicated** — `AtomicFileWriter.WriteAtomic` delegates to `WriteBytesAtomic`
(same bytes: `Utf8NoBom.GetBytes` is what `WriteAllText` with that encoder produced).
`SafeDeploy` and `TryRollback` were copy-pasted between `WeaselInstaller` and
`RestoreService`; they now live once in `DeploymentSteps`. Note that `RestoreService`'s
`hadPreviousConfig` flag was redundant — a non-null safety backup already means exactly
that — so the two rollbacks turned out to be the same function.

**Removed the cancellation chain** — the `CancellationToken` parameter through
`ConversionPipeline.Load`, `ISsfPackageReader.Read`, `ReadEntryBounded`, `ReadBounded`
and `ParseFilePack`, the five `ThrowIfCancellationRequested` calls, the
`OperationCanceledException` handler, and `ExitCode.Cancelled`. Every caller passed
`CancellationToken.None` and nothing ever constructed a token source, so none of it
could fire. This is the one change that shrinks a public signature: if you want
Ctrl-C handling, add a `CancellationTokenSource` in `Program.cs` and thread it back
deliberately, rather than keeping a chain that only looks like it works.

## Deliberately not applied

- **`InstallResult.RolledBack`** — always `false`, but it is a field of the documented
  `--json` output. Deleting it changes the output contract, which is a functional
  change, not a cleanup. Left in place; drop it when you next revise the schema.
- **`ConvertCommand`'s pre-flight conflict check** — it looks redundant against the
  identical check inside `WriteOutputsTransactionally`, but it runs *before* the
  conversion and before `Directory.CreateDirectory`. Removing it would change which
  error wins when an output exists and the skin is also invalid, and would leave an
  empty output directory behind on failure. Kept.
- **`InspectCommand`'s degradation list** — it duplicates the conditions in
  `WeaselMapper.CollectUnsupportedFeatures`, but with different user-facing wording.
  Unifying them changes `inspect` output. Worth doing when you are willing to touch
  that text; not a silent refactor.
- **`SsfContainerDetector.CreateReader`'s `_ =>` arm** — unreachable, but removing it
  makes the switch expression non-exhaustive over the enum and raises CS8509. One line
  to keep the build warning-clean.

Still true, and unchanged by any of the above: the `tests/` tree does not compile, so
none of this is covered by a test. That is the repair worth doing next.
