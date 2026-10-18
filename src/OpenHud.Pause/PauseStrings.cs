namespace OpenHud.Pause;

// The UI_ keys the pause menu asks the injected lookup for, with the text in the
// three shipped languages. The i18n table of the model owns the live strings;
// this list is the source for it and the fallback when a lookup misses a key.
public static class PauseStrings
{
    public sealed record Entry(string Key, string English, string Portuguese, string German);

    public const string Title = "UI_PAUSE_TITLE";
    public const string Resume = "UI_PAUSE_RESUME";
    public const string NewGame = "UI_PAUSE_NEW_GAME";
    public const string Save = "UI_PAUSE_SAVE";
    public const string Options = "UI_PAUSE_OPTIONS";
    public const string Quit = "UI_PAUSE_QUIT";
    public const string TabMenu = "UI_PAUSE_TAB_MENU";
    public const string TabMap = "UI_PAUSE_TAB_MAP";
    public const string TabLines = "UI_PAUSE_TAB_LINES";
    public const string Legend = "UI_PAUSE_LEGEND";
    public const string NoLines = "UI_PAUSE_NO_LINES";
    public const string Hint = "UI_PAUSE_HINT";
    public const string MapHint = "UI_PAUSE_MAP_HINT";

    public static readonly IReadOnlyList<Entry> Catalog = new Entry[]
    {
        new(Title, "Paused", "Pausado", "Pausiert"),
        new(Resume, "Back to the game", "Voltar ao jogo", "Zurück zum Spiel"),
        new(NewGame, "New game", "Novo jogo", "Neues Spiel"),
        new(Save, "Save game", "Salvar jogo", "Spiel speichern"),
        new(Options, "Options", "Opções", "Optionen"),
        new(Quit, "Quit OMSI", "Sair do OMSI", "OMSI beenden"),
        new(TabMenu, "Menu", "Menu", "Menü"),
        new(TabMap, "Map", "Mapa", "Karte"),
        new(TabLines, "Lines", "Linhas", "Linien"),
        new(Legend, "Legend", "Legenda", "Legende"),
        new(NoLines, "No lines to show", "Nenhuma linha para mostrar", "Keine Linien vorhanden"),
        new(Hint, "Arrows and Enter to choose, Tab to change page, Esc to resume", "Setas e Enter para escolher, Tab para trocar de página, Esc para voltar", "Pfeile und Enter zum Wählen, Tab wechselt die Seite, Esc setzt fort"),
        new(MapHint, "Wheel to zoom, drag to move, double-click to center", "Roda para ampliar, arraste para mover, duplo clique para centralizar", "Mausrad zoomt, Ziehen verschiebt, Doppelklick zentriert"),
    };

    private static readonly Dictionary<string, string> English = Catalog.ToDictionary(e => e.Key, e => e.English);

    // The text of a key: the lookup's answer, else the English text, so the menu never draws an empty label.
    internal static string Text(Func<string, string>? lookup, string key)
    {
        string? text = null;
        try { text = lookup?.Invoke(key); } catch (Exception) { /* a faulty table must not break the menu */ }
        if (!string.IsNullOrWhiteSpace(text) && text != key) return text!;
        return English.TryGetValue(key, out var english) ? english : key;
    }
}
