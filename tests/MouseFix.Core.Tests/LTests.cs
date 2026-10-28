using System.Globalization;
using MouseFix.Core;
using Xunit;

namespace MouseFix.Core.Tests;

public class LTests
{
    [Theory]
    [InlineData("pt-BR", true)]
    [InlineData("pt-PT", true)]
    [InlineData("en-US", false)]
    [InlineData("es-ES", false)]
    public void OnlyPortugueseWindowsGetsPortuguese(string culture, bool portuguese) =>
        Assert.Equal(portuguese, L.IsPortuguese(CultureInfo.GetCultureInfo(culture)));
}
