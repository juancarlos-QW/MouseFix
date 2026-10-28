using System.Runtime.CompilerServices;
using MouseFix.Core;

namespace MouseFix.Core.Tests;

internal static class TestLanguage
{
    // The tests check Portuguese texts; don't let the machine's Windows language change them.
    [ModuleInitializer]
    internal static void UsePortuguese() => L.Use(L.Portuguese);
}
