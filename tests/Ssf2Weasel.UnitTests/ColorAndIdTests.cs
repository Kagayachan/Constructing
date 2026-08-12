// SPDX-License-Identifier: GPL-3.0-or-later
using Ssf2Weasel.Core.Colors;
using Ssf2Weasel.Core.Mapping;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class ColorNormalizerTests
{
    [Theory]
    [InlineData("0", "0x000000")]
    [InlineData("ff", "0x0000ff")]
    [InlineData("3e9eff", "0x3e9eff")]
    [InlineData("0x3E9EFF", "0x3e9eff")]
    [InlineData("1234567", "0x01234567")]
    [InlineData("80ffffff", "0x80ffffff")]
    [InlineData(" 0xABC ", "0x000abc")]
    public void Normalizes_1_to_8_hex_digits(string input, string expected)
        => Assert.Equal(expected, ColorNormalizer.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("123456789")]
    [InlineData("0x")]
    [InlineData("12 34")]
    public void Rejects_invalid_values(string? input)
        => Assert.Null(ColorNormalizer.Normalize(input));

    [Fact]
    public void ToRgba_reads_bgr_order()
    {
        // 0x3e9eff in BGR = orange: R=0xff, G=0x9e, B=0x3e (verified against sample skin).
        var (r, g, b, a) = ColorNormalizer.ToRgba("0x3e9eff");
        Assert.Equal((0xff, 0x9e, 0x3e, 0xff), (r, g, b, a));
    }

    [Fact]
    public void ToRgba_reads_alpha_from_8_digit_colors()
    {
        var (_, _, _, a) = ColorNormalizer.ToRgba("0x40123456");
        Assert.Equal(0x40, a);
    }

    [Fact]
    public void FromRgb_roundtrips()
    {
        var s = ColorNormalizer.FromRgb(0xff, 0x9e, 0x3e);
        Assert.Equal("0x3e9eff", s);
    }
}

public class SkinIdGeneratorTests
{
    private const string Sha = "b480644b79fd60b9003b9116e4f3c6049f0158b51d19a6869d2ad9560fa1273d";

    [Fact]
    public void Latin_names_become_snake_case_ids()
        => Assert.Equal("my_cool_skin_2", SkinIdGenerator.Generate("My Cool Skin 2", Sha));

    [Fact]
    public void Cjk_only_names_fall_back_to_hash_id()
        => Assert.Equal("ssf_b480644b79fd", SkinIdGenerator.Generate("痛哭流涕", Sha));

    [Fact]
    public void Mixed_names_keep_ascii_part_when_valid()
        => Assert.Equal("skin_v1_0", SkinIdGenerator.Generate("皮肤 skin v1.0", Sha));

    [Fact]
    public void Digit_leading_names_fall_back_to_hash_id()
        => Assert.Equal("ssf_b480644b79fd", SkinIdGenerator.Generate("2077", Sha));

    [Fact]
    public void Generated_ids_always_match_the_documented_pattern()
    {
        string[] names = ["Test", "半透明伊蕾娜v1.0（by巧味棉花糖）", "a", "___", "ALL CAPS!!!", new string('x', 300)];
        foreach (var name in names)
        {
            Assert.Matches("^[a-z][a-z0-9_]{2,63}$", SkinIdGenerator.Generate(name, Sha));
        }
    }
}
