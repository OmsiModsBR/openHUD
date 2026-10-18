# Chaves de idioma da interface

<!-- l10n: source=reference/ui-language-keys.md source-sha256=216dca458adc72fb869225f48a7d73386d459338aa987c4ca4bb3229eb3b855c -->
> Tradução da [página original em inglês](../../reference/ui-language-keys.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O texto da interface do openHUD está embutido no módulo como chaves que começam com `UI_`, cada uma com um texto em inglês, português (Brasil) e alemão. Esta página lista cada chave e os seus três textos. Ela é conferida pela suíte de testes com a tabela de `src/OpenHud.Core/I18n/Strings.cs` e com o catálogo próprio do menu de pausa em `src/OpenHud.Pause/PauseStrings.cs`, então não pode se afastar do código. Como o idioma é escolhido e como um texto ausente se comporta está em [internacionalização](../concepts/internationalization.md).

Páginas relacionadas: [internacionalização](../concepts/internationalization.md), [pacote de menu de pausa](pause-menu-package.md), [configurações](settings.md#language).

<a id="how-to-read-the-tables"></a>
## Como ler as tabelas

- `{0}`, `{1}` são placeholders preenchidos pelo módulo.
- Um texto igual em todos os idiomas (`km/h`, `openHUD`) é intencional.
- Uma chave desconhecida é mostrada como a própria chave; um texto vazio recorre ao inglês.
- Pacotes de mapa podem substituir alguns textos próprios (o título e as entradas da legenda `UI_LEGEND_*`, por meio das chaves `title` e `legend.<kind>`), mas não a interface.

<a id="the-interface-table"></a>
## A tabela da interface

| Chave | Inglês | Português (Brasil) | Alemão |
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

<a id="the-pause-menus-catalogue"></a>
## O catálogo do menu de pausa

O menu de pausa tem um catálogo próprio para as chaves que desenha. Onde uma chave está nas duas tabelas, **o texto do catálogo é o que o menu mostra**; o texto da tabela da interface para a mesma chave é usado por outras partes (e é a alternativa para uma chave que falta no catálogo). As linhas são escritas `menu:<key>`.

| Chave | Inglês | Português (Brasil) | Alemão |
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

Relacionado: [internacionalização](../concepts/internationalization.md) · [pacote de menu de pausa](pause-menu-package.md)
