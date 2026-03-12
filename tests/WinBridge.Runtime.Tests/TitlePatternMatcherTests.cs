using System.Text.RegularExpressions;
using WinBridge.Runtime.Windows.Shell;

namespace WinBridge.Runtime.Tests;

public sealed class TitlePatternMatcherTests
{
    [Fact]
    public void IsMatchUsesBoundedRegexTimeout()
    {
        string title = new string('a', 4000) + "!";

        Assert.Throws<RegexMatchTimeoutException>(
            () => TitlePatternMatcher.IsMatch(title, "(a+)+$", TimeSpan.FromMilliseconds(1)));
    }
}
