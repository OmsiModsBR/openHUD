using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenHud.I18n;

// The interface text of openHUD: English, Portuguese (Brazil) and German, embedded in
// code so nothing is loaded from disk. Every key starts with UI_ and exists in all
// three languages (a gate checks it, including that placeholders match).
public static class Strings
{
    public const string Prefix = "UI_";

    internal readonly record struct Entry(string Key, string English, string Portuguese, string German);

    // Placeholders: {0}, {1} ... filled by Format. Units are separate keys so a language can reorder them.
    internal static readonly Entry[] Table =
    {
        // Pause menu
        new("UI_PAUSE_TITLE", "Paused", "Pausado", "Pausiert"),
        new("UI_PAUSE_RESUME", "Resume", "Continuar", "Fortsetzen"),
        new("UI_PAUSE_NEW_GAME", "New game", "Novo jogo", "Neues Spiel"),
        new("UI_PAUSE_SAVE", "Save", "Salvar", "Speichern"),
        new("UI_PAUSE_OPTIONS", "Options", "Opções", "Optionen"),
        new("UI_PAUSE_QUIT", "Quit", "Sair", "Beenden"),
        new("UI_PAUSE_BACK", "Back", "Voltar", "Zurück"),
        new("UI_PAUSE_LAYOUT", "Layout", "Layout", "Layout"),
        new("UI_PAUSE_LAYOUT_CLASSIC", "Classic", "Clássico", "Klassisch"),
        new("UI_PAUSE_LAYOUT_OPENHUD", "openHUD", "openHUD", "openHUD"),
        new("UI_PAUSE_THEME", "Theme", "Tema", "Design"),
        new("UI_PAUSE_LANGUAGE", "Language", "Idioma", "Sprache"),
        new("UI_PAUSE_MAP", "Map", "Mapa", "Karte"),
        new("UI_PAUSE_LEGEND", "Legend", "Legenda", "Legende"),
        new("UI_PAUSE_CREDITS", "Credits", "Créditos", "Mitwirkende"),
        new("UI_PAUSE_FOOTER_VERSIONS", "openHUD {0} · OMSI {1}", "openHUD {0} · OMSI {1}", "openHUD {0} · OMSI {1}"),
        new("UI_PAUSE_CONFIRM_QUIT", "Quit to the desktop?", "Sair para a área de trabalho?", "Zum Desktop beenden?"),
        new("UI_PAUSE_CONFIRM_YES", "Yes", "Sim", "Ja"),
        new("UI_PAUSE_CONFIRM_NO", "No", "Não", "Nein"),
        new("UI_PAUSE_UNAVAILABLE", "Not available in this build", "Indisponível nesta versão", "In dieser Version nicht verfügbar"),
        new("UI_PAUSE_BUSY", "Please wait...", "Aguarde...", "Bitte warten..."),
        // Legend of map markers (a package may supply its own text)
        new("UI_LEGEND_STATION", "Station", "Estação", "Bahnhof"),
        new("UI_LEGEND_TERMINAL", "Terminal", "Terminal", "Endhaltestelle"),
        new("UI_LEGEND_GARAGE", "Depot", "Garagem", "Betriebshof"),
        new("UI_LEGEND_STOP", "Stop", "Ponto de parada", "Haltestelle"),
        new("UI_LEGEND_LANDMARK", "Landmark", "Ponto de referência", "Wahrzeichen"),
        // Radar and HUD
        new("UI_RADAR_NEXT_STOP", "Next stop", "Próxima parada", "Nächste Haltestelle"),
        new("UI_RADAR_END_OF_LINE", "End of line", "Fim da linha", "Endhaltestelle"),
        new("UI_RADAR_YOU_ARE_IN", "You are in", "Você está em", "Sie sind in"),
        new("UI_RADAR_DESTINATION", "Destination", "Destino", "Ziel"),
        new("UI_RADAR_PASSENGERS", "Passengers", "Passageiros", "Fahrgäste"),
        new("UI_RADAR_SPEED", "Speed", "Velocidade", "Geschwindigkeit"),
        new("UI_RADAR_SPEED_LIMIT", "Speed limit", "Limite de velocidade", "Höchstgeschwindigkeit"),
        new("UI_RADAR_DELAY", "Delay", "Atraso", "Verspätung"),
        new("UI_RADAR_ON_TIME", "On time", "No horário", "Pünktlich"),
        new("UI_RADAR_EARLY", "{0} early", "{0} adiantado", "{0} zu früh"),
        new("UI_RADAR_LATE", "{0} late", "{0} atrasado", "{0} verspätet"),
        new("UI_RADAR_REQUESTED_STOP", "Stop requested", "Parada solicitada", "Haltewunsch"),
        new("UI_RADAR_LINE", "Line", "Linha", "Linie"),
        new("UI_RADAR_CLOCK", "Time", "Hora", "Uhrzeit"),
        new("UI_RADAR_NO_TIMETABLE", "No timetable", "Sem tabela horária", "Kein Fahrplan"),
        new("UI_RADAR_NO_DATA", "No data", "Sem dados", "Keine Daten"),
        new("UI_RADAR_UNAVAILABLE", "Unavailable", "Indisponível", "Nicht verfügbar"),
        new("UI_RADAR_NORTH", "N", "N", "N"),
        // Units
        new("UI_UNIT_KMH", "km/h", "km/h", "km/h"),
        new("UI_UNIT_MPH", "mph", "mph", "mph"),
        new("UI_UNIT_M", "m", "m", "m"),
        new("UI_UNIT_KM", "km", "km", "km"),
        new("UI_UNIT_FT", "ft", "pés", "ft"),
        new("UI_UNIT_MI", "mi", "mi", "mi"),
        new("UI_UNIT_MIN", "min", "min", "Min."),
        new("UI_UNIT_SEC", "s", "s", "s"),
    };

    private static readonly Dictionary<string, Entry> Index = Build();

    private static Dictionary<string, Entry> Build()
    {
        var map = new Dictionary<string, Entry>(Table.Length, StringComparer.Ordinal);
        foreach (var entry in Table) map.TryAdd(entry.Key, entry);
        return map;
    }

    public static IEnumerable<string> Keys => Table.Select(x => x.Key);

    public static bool Has(string key) => Index.ContainsKey(key);

    // The text of `key` in `language`. An unknown key returns the key itself, so a missing
    // translation is visible but never throws or draws blank.
    public static string Get(string key, UiLanguage language)
    {
        if (!Index.TryGetValue(key, out var entry)) return key;
        var text = language switch { UiLanguage.PortugueseBr => entry.Portuguese, UiLanguage.German => entry.German, _ => entry.English };
        return string.IsNullOrEmpty(text) ? entry.English : text;
    }

    // Same, with {0}, {1} ... replaced. Missing arguments leave the placeholder untouched.
    public static string Format(string key, UiLanguage language, params object?[] args)
    {
        var text = Get(key, language);
        if (args.Length == 0) return text;
        return Placeholder.Replace(text, m =>
        {
            var i = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return i < args.Length ? Convert.ToString(args[i], CultureInfo.InvariantCulture) ?? "" : m.Value;
        });
    }

    internal static readonly Regex Placeholder = new(@"\{(\d{1,2})\}", RegexOptions.CultureInvariant);
}
