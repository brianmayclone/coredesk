using CoreDesk.Abstractions.Services;

namespace CoreDesk.Application.Localization;

public sealed class DictionaryLocalizationService : ILocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> _resources = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "CoreDesk",
            ["SearchPlaceholder"] = "Search apps",
            ["AppDrawer"] = "App Drawer",
            ["Settings"] = "Settings",
            ["Desktop"] = "Desktop",
            ["TouchMode"] = "Touch mode",
            ["DesktopMode"] = "Desktop mode",
            ["ControlCenter"] = "Control Center",
            ["StatusKeyboard"] = "Keyboard",
            ["StatusNetwork"] = "Network",
            ["General"] = "General",
            ["Appearance"] = "Appearance",
            ["Behavior"] = "Behavior",
            ["Apps"] = "Apps",
            ["Gestures"] = "Gestures",
            ["System"] = "System"
        },
        ["de"] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AppTitle"] = "CoreDesk",
            ["SearchPlaceholder"] = "Apps suchen",
            ["AppDrawer"] = "AppDrawer",
            ["Settings"] = "Einstellungen",
            ["Desktop"] = "Desktop",
            ["TouchMode"] = "Touch-Modus",
            ["DesktopMode"] = "Desktop-Modus",
            ["ControlCenter"] = "Kontrollzentrum",
            ["StatusKeyboard"] = "Tastatur",
            ["StatusNetwork"] = "Netzwerk",
            ["General"] = "Allgemein",
            ["Appearance"] = "Darstellung",
            ["Behavior"] = "Verhalten",
            ["Apps"] = "Apps",
            ["Gestures"] = "Gesten",
            ["System"] = "System"
        }
    };

    public string CurrentLanguage { get; private set; } = "en";

    public string this[string key]
    {
        get
        {
            if (_resources.TryGetValue(CurrentLanguage, out var language) && language.TryGetValue(key, out var value))
            {
                return value;
            }

            return _resources["en"].GetValueOrDefault(key, key);
        }
    }

    public void SetLanguage(string language)
    {
        CurrentLanguage = _resources.ContainsKey(language) ? language : "en";
    }
}

