# Operações e eventos

<!-- l10n: source=reference/operations-and-events.md source-sha256=5630ab2b5ec0c7934e5427354555826e28d0db20045265d4ee86f09338f217f6 -->
> Tradução da [página original em inglês](../../reference/operations-and-events.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD expõe quatro operações enquanto uma sessão roda e informa por eventos de runtime e avisos na bandeja. Esta página lista cada uma com seus argumentos, resultados e erros, com exemplos de chamadas, e cada evento com os seus dados. As operações são acessadas com `/runtime:module.openhud.<operation>` e `/runtime-arg:<name>=<value>`; os eventos são lidos com `OmsiLaunch.exe events read` ou `events watch`. As regras de operações, eventos e avisos são as da referência de Runtime Modules do OmsiLaunch.

Páginas relacionadas: [primeira sessão](../getting-started/first-session.md), [solução de problemas](../guides/troubleshooting.md), [erros e degradação](errors-and-degradation.md), [desempenho](../concepts/performance.md).

<a id="operations"></a>
## Operações

As operações existem apenas em uma sessão que carregou o módulo; caso contrário, o OmsiLaunch responde `OL_E_MODULE_NOT_LOADED` ou `OL_E_MODULE_OPERATION_UNKNOWN`. Argumentos não declarados são recusados (`OL_E_RUNTIME_ARGUMENT_REQUIRED`). Uma operação que o módulo não conhece responde `OL_E_MODULE_OPERATION_UNKNOWN`. Depois que o módulo parou ou falhou, toda chamada responde `OL_E_MODULE_DISABLED`.

| Operação | Tipo | Argumentos | Resultado |
| --- | --- | --- | --- |
| `status` | leitura | nenhum | o estado do módulo (abaixo) |
| `radar.zoom` | ação | `direction` (obrigatório): `in` ou `out` | `radar_zoom_step`, `radar_radius_m` |
| `radar.toggle` | ação | `visible` (opcional): `true`, `false`, `1`, `0` | `radar_visible`, `radar_state` |
| `pause.state` | leitura | nenhum | o estado do menu de pausa (abaixo) |

<a id="status"></a>
### `status`

```text
OmsiLaunch.exe /runtime:module.openhud.status
```

| Chave | Significado |
| --- | --- |
| `module_state` | `new`, `armed`, `running` ou `stopped` |
| `draw` | `available` ou `unavailable` (a camada de desenho nativa) |
| `radar_setting`, `radar_visible`, `radar_state` | a configuração `radar`; se `radar.toggle` o deixou visível; por que ele desenha ou não ([o radar](../concepts/the-radar.md#when-the-radar-shows-nothing)) |
| `radar_zoom_step`, `radar_radius_m` | o zoom atual (0 a 5) e o seu raio em metros |
| `radar_corner`, `units`, `language`, `language_code`, `ui_scale_percent` | as configurações efetivas como o módulo as usa |
| `map` | a identidade do mapa para o qual o módulo está carregando pacotes (`maps\<folder>\global.cfg`), vazia antes de um mapa ser visto |
| `packages` | `none`, `loading` ou `loaded` |
| `realmap_status`, `pausemenu_status` | `Absent`, `Ignored` ou `Loaded` |
| `radar_features`, `fullmap_features` | recursos na janela de radar e na janela de mapa mais recentes |
| `fonts_registered`, `images_registered`, `uploads_pending`, `uploads_failed` | o que foi enviado à camada de desenho |
| `pause_menu`, `pause_state` | `off`, `on` ou `degraded`; o estado da máquina de estados |
| `scenes_published`, `scenes_skipped`, `scene_failures` | cenas enviadas, puladas por não terem mudado, recusadas |
| `ticks`, `tick_last_ms`, `tick_max_ms`, `slow_ticks` | contagem e tempos dos ticks ([desempenho](../concepts/performance.md)) |
| `frames_with_problems`, `faults`, `last_fault` | leituras do jogo que falharam em um tick; exceções contidas e a última (`area: Type`) |
| `native_calls`, `native_failures`, `native_problem` | chamadas à camada de desenho, falhas e o último problema |
| `text_measure` | `native` (larguras de texto vindas da camada de desenho) ou `approximate` |
| `view` | tamanho em pixels da view do jogo como a camada de desenho o informa (`1920x1080`; `0x0` antes do primeiro frame) |
| `notices` | os avisos mostrados até agora (chaves separadas por vírgula) |

<a id="pausestate"></a>
### `pause.state`

```text
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

| Chave | Significado |
| --- | --- |
| `pause_menu` | `off`, `on` ou `degraded` |
| `pause_reason` | por que está desligado (`turned off in the settings`, `no draw layer`, `ui.pause-menu.replace is not available in this build`, `input capture is not available`), vazio caso contrário |
| `pause_state` | `Running`, `TakeOverPending`, `Open`, `Resuming`, `Modal`, `Ending`, `Provisional`, `Stopped`, ou vazio quando desligado |
| `pause_last_error` | a última falha tratada: um código do catálogo (`OL_E_UI_BUSY` ...) com detalhe, ou vazio |
| `pause_cleanup_complete` | `true` quando nada está retido (captura, fundo, menu nativo oculto) |
| `pause_unavailable` | `true` depois de `OL_E_CAPABILITY_UNRESOLVED` (o módulo parou de perguntar) |
| `key_release_timeouts` | quantas retomadas não esperaram por uma tecla presa |

<a id="radarzoom"></a>
### `radar.zoom`

Move o radar um passo. Os seis passos são 100, 200, 400, 800, 1600 e 3200 m; a chamada em uma extremidade não muda nada e responde o passo atual.

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in
```

Erros: `OL_E_RUNTIME_ARGUMENT_REQUIRED` (sem `direction`), `OL_E_RUNTIME_VALUE_INVALID` (não é `in` nem `out`).

<a id="radartoggle"></a>
### `radar.toggle`

Mostra ou oculta o radar durante a sessão; sem `visible`, alterna. Mostrá-lo também desfaz um desligamento automático após erros repetidos. Ele não pode mostrar um radar que a configuração `radar` desligou.

```text
OmsiLaunch.exe /runtime:module.openhud.radar.toggle /runtime-arg:visible=false
```

Erro: `OL_E_RUNTIME_VALUE_INVALID` (`visible` não é booleano).

<a id="events"></a>
## Eventos

Os eventos se chamam `module.openhud.<name>`, com um objeto JSON de strings como dados.

| Nome | Quando | Dados |
| --- | --- | --- |
| `armed` | O módulo está armado, antes de o mundo iniciar | `omsi_version`, `draw`, `pause_menu` |
| `settings-issue` | Uma configuração era inutilizável | `issues`: `key=value (reason); ...` |
| `hooks` | A camada de desenho instalou seus hooks Direct3D (componente nativo) | `present`, `reset` (`installed`) |
| `gameplay-ready` | O mundo está em execução | nenhum |
| `packages` | Os pacotes de um mapa foram carregados | `map`, `realmap`, `pausemenu` (status), `fonts`, `images`, `diagnostics` (primeiras linhas, no máximo 700 caracteres) |
| `package-problem` | O carregamento dos pacotes falhou | `detail` |
| `read-problem` | Uma parte do jogo não pôde ser lida (cada tipo uma vez, no máximo 12) | `problem` |
| `draw-failed` | A camada de desenho informou uma falha | `detail` |
| `draw_failed` | O mesmo, dito pelo componente nativo quando ele falha | `code`, `detail` |
| `pause-degraded` | O menu de pausa desistiu pelo resto da sessão | `reason`, `state` |
| `fault` | Uma exceção contida (uma vez por área) | `area`, `type`, `message` |
| `notice` | Uma mensagem para o usuário | `title`, `text` |
| `stopping` | O módulo está parando | nenhum |

<a id="notices"></a>
### Avisos

Um evento `notice` é mostrado pela bandeja do OmsiLaunch como um balão intitulado "openHUD". O texto está no idioma da interface e cada aviso aparece uma vez por sessão: a camada de desenho não está disponível; o radar foi desligado após erros repetidos; o menu de pausa não está disponível e o do próprio OMSI permanece; um pacote de mapa não pôde ser usado e foi ignorado ([solução de problemas](../guides/troubleshooting.md#reading-the-modules-events-and-notices)).

<a id="what-the-module-reads-from-omsilaunch"></a>
## O que o módulo lê do OmsiLaunch

O módulo declara estas operações do catálogo (`runtime_operations`) e as executa no processo: `time.read`, `map.read`, `map.tiles.list`, `camera.read`, `player-vehicle.read`, `road-vehicles.list`, `road-vehicle.read`, `timetable.read`, `weather.read`, `ui.pause.state`, `ui.pause.take-over`, `ui.pause.restore-native` e `ui.pause.action`. O plano as lista, e as de pausa carregam a sua política de mutação.

Relacionado: [solução de problemas](../guides/troubleshooting.md) · [erros e degradação](errors-and-degradation.md) · [compatibilidade](compatibility.md)
