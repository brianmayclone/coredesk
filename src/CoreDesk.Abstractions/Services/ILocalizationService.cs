namespace CoreDesk.Abstractions.Services;

public interface ILocalizationService
{
    string CurrentLanguage { get; }

    string this[string key] { get; }

    void SetLanguage(string language);
}

