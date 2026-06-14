using System.Windows;

namespace DS2Mouse.Services;

public sealed class LocalizationService
{
    public const string Chinese = "zh-CN";
    public const string English = "en";

    private readonly Application _app;
    private ResourceDictionary? _current;

    public string CurrentLanguage { get; private set; } = English;
    public event Action? LanguageChanged;

    public LocalizationService(Application app) { _app = app; }

    public void SetLanguage(string lang)
    {
        if (lang != Chinese && lang != English) lang = Chinese;
        if (_current != null && CurrentLanguage == lang) return;

        var uri = new Uri(
            $"pack://application:,,,/Resources/Strings.{lang}.xaml",
            UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };

        if (_current != null)
            _app.Resources.MergedDictionaries.Remove(_current);
        _app.Resources.MergedDictionaries.Add(dict);
        _current = dict;

        CurrentLanguage = lang;
        LanguageChanged?.Invoke();
    }

    /// <summary>Look up a localized string by key, returning the key itself if missing.</summary>
    public string Get(string key) =>
        _app.TryFindResource(key) as string ?? key;
}
