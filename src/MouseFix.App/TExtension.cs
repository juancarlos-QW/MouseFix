using System.Windows.Markup;
using MouseFix.Core;

namespace MouseFix.App;

/// <summary>XAML text in both languages: <c>Text="{l:T 'Texto', En='Text'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string pt) => Pt = pt;

    [ConstructorArgument("pt")]
    public string Pt { get; set; } = "";

    public string En { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Pt, En);
}
