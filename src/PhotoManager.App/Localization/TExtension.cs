using System.Windows.Markup;

namespace PhotoManager.App.Localization;

/// <summary>Rozszerzenie XAML do napisów: <c>{loc:T Klucz}</c>. Wartość ustalana przy ładowaniu okna.</summary>
public sealed class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public TExtension() { }
    public TExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
