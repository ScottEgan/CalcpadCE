using Calcpad.Highlighter.Prettifier;

namespace Calcpad.Tests.Highlighter.Prettifier
{
    public class CalcpadPrettifierTests
    {
        [Fact]
        public void NestedDivBlocks_AreIndentedByDepth()
        {
            var source = Lf(
                "'<div>",
                "'outer",
                "'<div class=\"inner\">",
                "'inner",
                "'</div>",
                "'</div>");
            var expected = Lf(
                "'<div>",
                "\t'outer",
                "\t'<div class=\"inner\">",
                "\t\t'inner",
                "\t'</div>",
                "'</div>");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void AdjacentClosingAndOpeningDivs_PreserveSiblingDepth()
        {
            var source = Lf(
                "'<div class=\"grid\">",
                "'<div>",
                "'first",
                "'</div><div>",
                "'second",
                "'</div>",
                "'</div>");
            var expected = Lf(
                "'<div class=\"grid\">",
                "\t'<div>",
                "\t\t'first",
                "\t'</div><div>",
                "\t\t'second",
                "\t'</div>",
                "'</div>");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void InlineDiv_DoesNotChangeFollowingLineDepth()
        {
            var source = Lf(
                "'<div>",
                "'<div class=\"note\">inline content</div>",
                "'after inline div",
                "'</div>");
            var expected = Lf(
                "'<div>",
                "\t'<div class=\"note\">inline content</div>",
                "\t'after inline div",
                "'</div>");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ConfiguredSpaces_AreUsedForEachDivDepth()
        {
            var source = Lf(
                "'<div style=\"margin-left: 20px;\">",
                "'<div>",
                "'content",
                "'</div>",
                "'</div>");
            var expected = Lf(
                "'<div style=\"margin-left: 20px;\">",
                "  '<div>",
                "    'content",
                "  '</div>",
                "'</div>");
            var options = new PrettifierOptions { IndentUnit = "  " };

            var actual = CalcpadPrettifier.Prettify(source, options);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DivBlocksAndCalcpadControlBlocks_CombineTheirDepths()
        {
            var source = Lf(
                "#if x > 0",
                "'<div>",
                "value = 1",
                "'</div>",
                "#else",
                "'<div>",
                "value = 0",
                "'</div>",
                "#end if");
            var expected = Lf(
                "#if x > 0",
                "\t'<div>",
                "\t\tvalue = 1",
                "\t'</div>",
                "#else",
                "\t'<div>",
                "\t\tvalue = 0",
                "\t'</div>",
                "#end if");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DivFormatting_PreservesCrLfLineEndings()
        {
            var source = CrLf(
                "'<div>",
                "'content",
                "'</div>");
            var expected = CrLf(
                "'<div>",
                "\t'content",
                "'</div>");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DivFormatting_IsIdempotent()
        {
            var source = Lf(
                "'<div>",
                "\t'<div>",
                "\t\t'content",
                "\t'</div><div>",
                "\t\t'more content",
                "\t'</div>",
                "'</div>");

            var once = CalcpadPrettifier.Prettify(source);
            var twice = CalcpadPrettifier.Prettify(once);

            Assert.Equal(source, once);
            Assert.Equal(once, twice);
        }

        [Fact]
        public void Base64ImageLine_IsPreservedAsOnePhysicalLine()
        {
            var payload = new string('A', 4094) + "==";
            var imageLine = $"'<img src=\"data:image/png;base64,{payload}\" alt=\"diagram\">";
            var source = Lf(
                "'<div>",
                imageLine,
                "'</div>");
            var expected = Lf(
                "'<div>",
                "\t" + imageLine,
                "'</div>");

            var actual = CalcpadPrettifier.Prettify(source);

            Assert.Equal(expected, actual);
            Assert.Equal(3, actual.Split('\n').Length);
            Assert.Contains(payload, actual);
        }

        private static string Lf(params string[] lines) => string.Join("\n", lines);

        private static string CrLf(params string[] lines) => string.Join("\r\n", lines);
    }
}
