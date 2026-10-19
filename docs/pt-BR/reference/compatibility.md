# Compatibilidade e capacidades

<!-- l10n: source=reference/compatibility.md source-sha256=3966682382bbb773b78e8ab680c8ebacf4e619507ed2e2c90a7a4e479f393377 -->
> Tradução da [página original em inglês](../../reference/compatibility.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página diz em que o openHUD roda e o que ele precisa do OmsiLaunch: o protocolo do bridge, os recursos do bridge que ele exige, as capacidades que usa ou dispensa, as operações que executa e o que faz quando uma delas não está disponível. O sistema de capacidades é do OmsiLaunch (a referência de capacidades dele as lista por build do OMSI); esta página diz o que cada uma significa para o openHUD.

Páginas relacionadas: [instalação](../getting-started/installation.md), [erros e degradação](errors-and-degradation.md), [o menu de pausa](../concepts/the-pause-menu.md), [convivência](../guides/coexisting.md), [limitações conhecidas](known-limitations.md), [status de validação](../status/validation.md).

<a id="at-a-glance"></a>
## Visão geral

| | |
| --- | --- |
| Id do módulo, versão | `openhud`, `0.1.0` |
| Protocolo do bridge | 3 (`min` 3, `max` 3) |
| Ativação | `requested` |
| Política de falha | `degrade` |
| Tick | a cada 50 ms |
| Componentes | `OpenHud.dll` gerenciada (entrada `OpenHud.HudModule`, .NET 6, carregável pelo plugin de 32 bits), `openhud-draw.dll` nativa (x86) |
| Entrada de conteúdo | `realmap-package`, não obrigatória |
| OMSI | As builds que o OmsiLaunch suporta; o que funciona em cada build é decidido pelas capacidades, abaixo |
| Windows | As plataformas que o OmsiLaunch suporta |
| Idiomas | Interface em inglês, português (Brasil) e alemão; texto do mapa em qualquer código de idioma do OMSI |

<a id="bridge-features"></a>
## Recursos do bridge

| Recurso | Obrigatório | Usado para |
| --- | :---: | --- |
| `bridge.events` | sim | Os eventos e avisos do módulo |
| `bridge.runtime` | sim | Executar operações do catálogo no processo |
| `bridge.tick` | sim | O tick de 50 ms |
| `bridge.hooks.d3d9` | sim | Interceptar `Present` e `Reset` do dispositivo Direct3D 9 do jogo (exige a capacidade nativa `d3d.texture`) |
| `bridge.input` | sim | Capturar o teclado e o mouse para o menu de pausa (exige a capacidade nativa `ui.input.capture`) |

Um recurso obrigatório que a build do OMSI em execução não pode fornecer faz o módulo não carregar: o plano diz `OL_E_MODULE_CAPABILITY_UNRESOLVED`. Para um módulo solicitado, isso torna o plano não executável; a própria referência de capacidades do OmsiLaunch diz quais builds resolvem quais capacidades.

<a id="optional-capabilities"></a>
## Capacidades opcionais

| Capacidade | Se disponível | Se não |
| --- | --- | --- |
| `ui.pause-menu.replace` | O módulo pode assumir o menu de pausa nativo | O menu nativo permanece; o radar funciona. `armed` diz `pause_menu: off: ui.pause-menu.replace is not available in this build` |
| `camera.read` | A câmera é lida a cada tick (não é desenhada na 0.1.0) | A parte da câmera do snapshot fica indisponível |
| `timetable.read` | As contagens da tabela horária são lidas a cada 20º tick (não são desenhadas na 0.1.0) | Essa parte fica indisponível |
| `weather.read` | O clima é lido a cada 20º tick (não é desenhado na 0.1.0) | Essa parte fica indisponível |

<a id="operations-the-module-runs"></a>
## Operações que o módulo executa

Declaradas no manifesto (`runtime_operations`), executadas no processo, na thread de UI do OMSI:

| Operação | Usada para | Se responde com erro |
| --- | --- | --- |
| `time.read` | O relógio na faixa do radar; a data que seleciona as linhas | A faixa fica sem relógio; todas as linhas são listadas |
| `map.read` | Qual mapa está em jogo (e a busca de pacotes) | Nenhum pacote é carregado; o radar desenha sem mapa |
| `map.tiles.list` | A tabela de tiles que coloca os veículos na grade do mapa | A posição do jogador não é resolvida; o radar espera (`the player's tile is not in the map's tile table`) |
| `player-vehicle.read` | Onde você está, sua direção e sua velocidade | Sem radar (`no player vehicle` / `player unreadable`) |
| `road-vehicles.list`, `road-vehicle.read` | Os veículos ao seu redor | Sem veículos no radar |
| `camera.read`, `timetable.read`, `weather.read` | Lidas para o snapshot | Essa parte fica indisponível |
| `ui.pause.state`, `ui.pause.take-over`, `ui.pause.restore-native`, `ui.pause.action` | O menu de pausa | O menu de pausa usa a alternativa; veja [o menu de pausa](../concepts/the-pause-menu.md#when-something-fails) |

Uma resposta tipada (`OL_E_CAPABILITY_UNRESOLVED`, `OL_E_RUNTIME_OPERATION_UNAVAILABLE`) mantém o seu código e torna indisponível apenas aquela parte. O módulo nunca tenta de novo em laço: uma parte que não pode ser lida é informada uma vez (`read-problem`) e tentada de novo no seu ritmo normal.

<a id="operations-the-module-offers"></a>
## Operações que o módulo oferece

`status`, `radar.zoom`, `radar.toggle` e `pause.state` ([operações e eventos](operations-and-events.md)).

<a id="other-modules-and-plugins"></a>
## Outros módulos e plugins

Nenhum plugin é declarado como conflito. Veja [convivência](../guides/coexisting.md) para overlays que interceptam os mesmos métodos Direct3D e para módulos que capturam entrada ou substituem o menu de pausa.

<a id="maps"></a>
## Mapas

Qualquer mapa funciona. As camadas de mapa do radar e a identidade do menu de pausa precisam dos pacotes opcionais do mapa ([pacotes por mapa](../concepts/per-map-packages.md)). O módulo descobre o mapa por `map.read`; um mapa cujo nome de arquivo não tem a forma `maps\<folder>\global.cfg` não recebe pacotes. Uma situação salva, um mapa novo e o último estado são tratados da mesma forma depois que o jogo está em execução.

Relacionado: [instalação](../getting-started/installation.md) · [erros e degradação](errors-and-degradation.md) · [limitações conhecidas](known-limitations.md)
