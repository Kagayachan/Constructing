// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Ssf2Weasel.Core;
using Ssf2Weasel.Core.Diagnostics;
using Ssf2Weasel.Core.Ini;
using Ssf2Weasel.TestSupport;
using Xunit;

namespace Ssf2Weasel.UnitTests;

public class SkinIniParserTests
{
    private const string Sample = """
        [General]
        skin_name=Test
        ; comment line
        # another comment

        [Display]
        font_size=14
        font_size=16

        [Custom_Section]
        whatever=1
        """;

    [Fact]
    public void Parses_utf16le_with_bom()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(SyntheticSsf.EncodeUtf16Le(Sample), diagnostics);
        Assert.Equal("Test", doc.GetSection("General")!.Get("skin_name"));
    }

    [Fact]
    public void Parses_utf16le_without_bom_via_heuristic()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(SyntheticSsf.EncodeUtf16Le(Sample, withBom: false), diagnostics);
        Assert.Equal("Test", doc.GetSection("General")!.Get("skin_name"));
    }

    [Fact]
    public void Parses_utf8()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(Sample), diagnostics);
        Assert.Equal("Test", doc.GetSection("General")!.Get("skin_name"));
    }

    [Fact]
    public void Invalid_encoding_throws_exit_code_6()
    {
        byte[] invalid = [0xC3, 0x28, 0xA0, 0x0A, 0xFF, 0x1F, 0x41, 0x42];
        var ex = Assert.Throws<Ssf2WeaselException>(
            () => SkinIniParser.Parse(invalid, new List<Diagnostic>()));
        Assert.Equal(ExitCode.IniError, ex.ExitCode);
        Assert.Equal("SSF_INI_ENCODING_UNSUPPORTED", ex.Code);
    }

    [Fact]
    public void Parses_legacy_gbk_ini_with_warning()
    {
        // Older real-world skins store skin.ini in the Chinese ANSI code page.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var gbk = System.Text.Encoding.GetEncoding(936);
        var bytes = gbk.GetBytes("[General]\r\nskin_name=维尼熊\r\n[Display]\r\nfont_ch=宋体\r\n");

        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(bytes, diagnostics);

        Assert.Equal("维尼熊", doc.GetSection("General")!.Get("skin_name"));
        Assert.Equal("宋体", doc.GetSection("Display")!.Get("font_ch"));
        Assert.Contains(diagnostics, d => d.Code == "INI_ENCODING_LEGACY_ANSI");
    }

    [Fact]
    public void Duplicate_key_uses_last_value_and_warns()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(Sample), diagnostics);
        Assert.Equal(16, doc.GetSection("Display")!.GetInt("font_size"));
        Assert.Contains(diagnostics, d => d.Code == "INI_DUPLICATE_KEY");
    }

    [Fact]
    public void Unknown_section_is_reported_not_fatal()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(Sample), diagnostics);
        Assert.NotNull(doc.GetSection("Custom_Section"));
        Assert.Contains(diagnostics, d => d.Code == "INI_UNKNOWN_SECTION");
    }

    [Fact]
    public void Trailing_garbage_produces_warning_and_parsing_continues()
    {
        var text = Sample + "\r\n\u0001\u0002 broken trailing bytes";
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(text), diagnostics);
        Assert.Equal("Test", doc.GetSection("General")!.Get("skin_name"));
        Assert.Contains(diagnostics, d => d.Code == "INI_TRAILING_GARBAGE");
    }

    [Fact]
    public void Section_and_key_lookup_is_case_insensitive()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(Sample), diagnostics);
        Assert.Equal("Test", doc.GetSection("GENERAL")!.Get("SKIN_NAME"));
    }

    [Fact]
    public void Accepts_lf_only_line_endings()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes(Sample.ReplaceLineEndings("\n")), diagnostics);
        Assert.Equal("Test", doc.GetSection("General")!.Get("skin_name"));
    }

    [Fact]
    public void Comma_lists_are_split_and_trimmed()
    {
        var diagnostics = new List<Diagnostic>();
        var doc = SkinIniParser.Parse(Encoding.UTF8.GetBytes("[S]\nvals= 1 , 2 ,3 "), diagnostics);
        Assert.Equal(new[] { "1", "2", "3" }, doc.GetSection("S")!.GetList("vals"));
    }
}
