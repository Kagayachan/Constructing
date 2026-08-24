// SPDX-License-Identifier: GPL-3.0-or-later
using Core;
using Core.Package;
using Infrastructure.Ssf;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class ContainerDetectionTests
{
    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x05, 0x06 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x07, 0x08 })]
    public void Detects_zip_signatures(byte[] header)
        => Assert.Equal(SsfContainerKind.Zip, SsfContainerDetector.Detect(header));

    [Fact]
    public void Detects_legacy_skin_signature()
        => Assert.Equal(SsfContainerKind.LegacyEncrypted, SsfContainerDetector.Detect("Skin1234"u8.ToArray()));

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    [InlineData(new byte[] { (byte)'S', (byte)'K', (byte)'I', (byte)'N' })] // signature is case-sensitive
    [InlineData(new byte[] { 0x50, 0x4B })]
    public void Rejects_unknown_signatures_with_exit_code_4(byte[] header)
    {
        var ex = Assert.Throws<ToolException>(() => SsfContainerDetector.Detect(header));
        Assert.Equal(ExitCode.UnsupportedContainer, ex.ExitCode);
        Assert.Equal("SSF_UNSUPPORTED_CONTAINER", ex.Code);
    }
}
