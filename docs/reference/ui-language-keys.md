# UI language keys

openHUD's interface text is embedded in the module as keys that start with `UI_`, each with a text in English, Portuguese (Brazil) and German. This page lists every key and its three texts. It is checked against the table in `src/OpenHud.Core/I18n/Strings.cs` and against the pause menu's own catalogue in `src/OpenHud.Pause/PauseStrings.cs` by the test suite, so it cannot drift from the code. How the language is chosen and how a missing text behaves is in [internationalization](../concepts/internationalization.md).

Related pages: [internationalization](../concepts/internationalization.md), [pause-menu package](pause-menu-package.md), [settings](settings.md#language).

## How to read the tables

- `{0}`, `{1}` are placeholders filled by the module.
- A text that is the same in all languages (`km/h`, `openHUD`) is intentional.
- An unknown key is shown as the key itself, a text that is empty falls back to English.
- Map packages can replace a few texts of their own (the title and the legend entries `UI_LEGEND_*`, through `title` and `legend.<kind>` keys) but not the interface.

## The interface table

| Key | English | Portuguese (Brazil) | German |
| --- | --- | --- | --- |
| `UI_PAUSE_TITLE` | Paused | Pausado | Pausiert |
| `UI_PAUSE_RESUME` | Resume | Continuar | Fortsetzen |
| `UI_PAUSE_NEW_GAME` | New game | Novo jogo | Neues Spiel |
| `UI_PAUSE_SAVE` | Save | Salvar | Speichern |
| `UI_PAUSE_OPTIONS` | Options | Opções | Optionen |
| `UI_PAUSE_QUIT` | Quit | Sair | Beenden |
| `UI_PAUSE_BACK` | Back | Voltar | Zurück |
| `UI_PAUSE_LAYOUT` | Layout | Layout | Layout |
| `UI_PAUSE_LAYOUT_CLASSIC` | Classic | Clássico | Klassisch |
| `UI_PAUSE_LAYOUT_OPENHUD` | openHUD | openHUD | openHUD |
| `UI_PAUSE_THEME` | Theme | Tema | Design |
| `UI_PAUSE_LANGUAGE` | Language | Idioma | Sprache |
| `UI_PAUSE_MAP` | Map | Mapa | Karte |
| `UI_PAUSE_LEGEND` | Legend | Legenda | Legende |
| `UI_PAUSE_CREDITS` | Credits | Créditos | Mitwirkende |
| `UI_PAUSE_FOOTER_VERSIONS` | openHUD {0} · OMSI {1} | openHUD {0} · OMSI {1} | openHUD {0} · OMSI {1} |
| `UI_PAUSE_CONFIRM_QUIT` | Quit to the desktop? | Sair para a área de trabalho? | Zum Desktop beenden? |
| `UI_PAUSE_CONFIRM_YES` | Yes | Sim | Ja |
| `UI_PAUSE_CONFIRM_NO` | No | Não | Nein |
| `UI_PAUSE_UNAVAILABLE` | Not available in this build | Indisponível nesta versão | In dieser Version nicht verfügbar |
| `UI_PAUSE_BUSY` | Please wait... | Aguarde... | Bitte warten... |
| `UI_LEGEND_STATION` | Station | Estação | Bahnhof |
| `UI_LEGEND_TERMINAL` | Terminal | Terminal | Endhaltestelle |
| `UI_LEGEND_GARAGE` | Depot | Garagem | Betriebshof |
| `UI_LEGEND_STOP` | Stop | Ponto de parada | Haltestelle |
| `UI_LEGEND_LANDMARK` | Landmark | Ponto de referência | Wahrzeichen |
| `UI_RADAR_NEXT_STOP` | Next stop | Próxima parada | Nächste Haltestelle |
| `UI_RADAR_END_OF_LINE` | End of line | Fim da linha | Endhaltestelle |
| `UI_RADAR_YOU_ARE_IN` | You are in | Você está em | Sie sind in |
| `UI_RADAR_DESTINATION` | Destination | Destino | Ziel |
| `UI_RADAR_PASSENGERS` | Passengers | Passageiros | Fahrgäste |
| `UI_RADAR_SPEED` | Speed | Velocidade | Geschwindigkeit |
| `UI_RADAR_SPEED_LIMIT` | Speed limit | Limite de velocidade | Höchstgeschwindigkeit |
| `UI_RADAR_DELAY` | Delay | Atraso | Verspätung |
| `UI_RADAR_ON_TIME` | On time | No horário | Pünktlich |
| `UI_RADAR_EARLY` | {0} early | {0} adiantado | {0} zu früh |
| `UI_RADAR_LATE` | {0} late | {0} atrasado | {0} verspätet |
| `UI_RADAR_REQUESTED_STOP` | Stop requested | Parada solicitada | Haltewunsch |
| `UI_RADAR_LINE` | Line | Linha | Linie |
| `UI_RADAR_CLOCK` | Time | Hora | Uhrzeit |
| `UI_RADAR_NO_TIMETABLE` | No timetable | Sem tabela horária | Kein Fahrplan |
| `UI_RADAR_NO_DATA` | No data | Sem dados | Keine Daten |
| `UI_RADAR_UNAVAILABLE` | Unavailable | Indisponível | Nicht verfügbar |
| `UI_RADAR_NORTH` | N | N | N |
| `UI_MAP_LINES` | Lines | Linhas | Linien |
| `UI_MAP_WATER` | Water | Água | Wasser |
| `UI_MAP_GREEN` | Green areas | Áreas verdes | Grünflächen |
| `UI_MAP_BUILT` | Built-up | Área construída | Bebaut |
| `UI_MAP_ROADS` | Roads | Vias | Straßen |
| `UI_MAP_RELIEF` | Relief | Relevo | Relief |
| `UI_MAP_ROUTE` | Route | Trajeto | Strecke |
| `UI_MAP_YOU` | You | Você | Sie |
| `UI_MAP_MORE` | +{0} more | +{0} | +{0} weitere |
| `UI_NOTICE_TITLE` | openHUD | openHUD | openHUD |
| `UI_NOTICE_DRAW_UNAVAILABLE` | The draw layer is not available, so openHUD shows nothing. | A camada de desenho não está disponível; o openHUD não mostra nada. | Die Zeichenebene ist nicht verfügbar; openHUD zeigt nichts an. |
| `UI_NOTICE_RADAR_OFF` | The radar was turned off after repeated errors. | O radar foi desligado após erros repetidos. | Das Radar wurde nach wiederholten Fehlern ausgeschaltet. |
| `UI_NOTICE_PAUSE_FALLBACK` | openHUD's pause menu is not available; OMSI's own menu stays. | O menu de pausa do openHUD não está disponível; o menu do OMSI permanece. | Das Pausenmenü von openHUD ist nicht verfügbar; das Menü von OMSI bleibt. |
| `UI_NOTICE_PACKAGE_IGNORED` | A map package could not be used and was ignored. | Um pacote do mapa não pôde ser usado e foi ignorado. | Ein Kartenpaket konnte nicht verwendet werden und wurde ignoriert. |
| `UI_UNIT_KMH` | km/h | km/h | km/h |
| `UI_UNIT_MPH` | mph | mph | mph |
| `UI_UNIT_M` | m | m | m |
| `UI_UNIT_KM` | km | km | km |
| `UI_UNIT_FT` | ft | pés | ft |
| `UI_UNIT_MI` | mi | mi | mi |
| `UI_UNIT_MIN` | min | min | Min. |
| `UI_UNIT_SEC` | s | s | s |

## The pause menu's catalogue

The pause menu has a catalogue of its own for the keys it draws. Where a key is in both tables, **the catalogue's text is the one the menu shows**; the interface table's text of the same key is used by other parts (and is the fallback for a key the catalogue lacks). The rows are written `menu:<key>`.

| Key | English | Portuguese (Brazil) | German |
| --- | --- | --- | --- |
| `menu:UI_PAUSE_TITLE` | Paused | Pausado | Pausiert |
| `menu:UI_PAUSE_RESUME` | Back to the game | Voltar ao jogo | Zurück zum Spiel |
| `menu:UI_PAUSE_NEW_GAME` | New game | Novo jogo | Neues Spiel |
| `menu:UI_PAUSE_SAVE` | Save game | Salvar jogo | Spiel speichern |
| `menu:UI_PAUSE_OPTIONS` | Options | Opções | Optionen |
| `menu:UI_PAUSE_QUIT` | Quit OMSI | Sair do OMSI | OMSI beenden |
| `menu:UI_PAUSE_TAB_MENU` | Menu | Menu | Menü |
| `menu:UI_PAUSE_TAB_MAP` | Map | Mapa | Karte |
| `menu:UI_PAUSE_TAB_LINES` | Lines | Linhas | Linien |
| `menu:UI_PAUSE_LEGEND` | Legend | Legenda | Legende |
| `menu:UI_PAUSE_NO_LINES` | No lines to show | Nenhuma linha para mostrar | Keine Linien vorhanden |
| `menu:UI_PAUSE_HINT` | Arrows and Enter to choose, Tab to change page, Esc to resume | Setas e Enter para escolher, Tab para trocar de página, Esc para voltar | Pfeile und Enter zum Wählen, Tab wechselt die Seite, Esc setzt fort |
| `menu:UI_PAUSE_MAP_HINT` | Wheel to zoom, drag to move, double-click to center | Roda para ampliar, arraste para mover, duplo clique para centralizar | Mausrad zoomt, Ziehen verschiebt, Doppelklick zentriert |

Related: [internationalization](../concepts/internationalization.md) · [pause-menu package](pause-menu-package.md)
