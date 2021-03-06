using System.Text;
using JsonBourne.DocumentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JsonBourne.Tests
{
    [TestClass]
    public class CharEscapeTests
    {
        [DataTestMethod]
        [DataRow('"', @"\""", true)]
        [DataRow('\\', @"\\", true)]
        [DataRow('/', @"\/", true)]
        [DataRow('\b', @"\b", true)]
        [DataRow('\f', @"\f", true)]
        [DataRow('\n', @"\n", true)]
        [DataRow('\r', @"\r", true)]
        [DataRow('\t', @"\t", true)]
        [DataRow('\0', @"\u0000", true)]
        [DataRow('a', @"", false)]
        public void TestEscape(char input, string expectedOutput, bool expectedResult)
        {
            var buff = new byte[6];

            var escapeResult = JsonTokens.TryEscape(input, buff, out var written);
            Assert.AreEqual(expectedResult, escapeResult);
            if (!expectedResult)
                return;

            Assert.AreEqual(expectedOutput.Length, written);

            var str = JsonUtilities.UTF8.GetString(buff[0..written]);
            Assert.AreEqual(expectedOutput, str);
        }

        [DataTestMethod]
        [DataRow(@"\""", '"', true)]
        [DataRow(@"\\", '\\', true)]
        [DataRow(@"\/", '/', true)]
        [DataRow(@"\b", '\b', true)]
        [DataRow(@"\f", '\f', true)]
        [DataRow(@"\n", '\n', true)]
        [DataRow(@"\r", '\r', true)]
        [DataRow(@"\t", '\t', true)]
        [DataRow(@"\u0000", '\0', true)]
        [DataRow(@"\a", '\0', false)]
        [DataRow(@"a", '\0', false)]
        public void TestUnescape(string input, char expectedOutput, bool expectedResult)
        {
            var buff = JsonUtilities.UTF8.GetBytes(input);

            var escapeResult = JsonTokens.TryUnescape(buff, out var output, out var consumed);
            Assert.AreEqual(expectedResult, escapeResult);
            if (!expectedResult)
                return;

            Assert.AreEqual(input.Length, consumed);
            Assert.AreEqual(expectedOutput, output);
        }
    }
}
