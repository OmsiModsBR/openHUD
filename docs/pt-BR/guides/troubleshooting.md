# Solução de problemas

<!-- l10n: source=guides/troubleshooting.md source-sha256=7629456bd8646bc588d3d623b03644178d1650ddc7807484714000142356d0ff -->
> Tradução da [página original em inglês](../../guides/troubleshooting.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página ajuda com os problemas habituais: o módulo não carrega, o radar não aparece, o menu de pausa é o do próprio OMSI, o texto mostra caracteres errados, o idioma está errado, o jogo parece lento. Para cada um, ela diz como descobrir o que o módulo decidiu e o que mudar. O módulo se explica por meio dos eventos de runtime do OmsiLaunch, de duas operações somente de leitura e dos avisos da bandeja; a última seção mostra como lê-los.

Páginas relacionadas: [operações e eventos](../reference/operations-and-events.md), [erros e degradação](../reference/errors-and-degradation.md), [limitações conhecidas](../reference/known-limitations.md), [criando um pacote de menu de pausa](making-a-pause-menu-package.md), [FAQ](../reference/faq.md).

<a id="reading-the-modules-events-and-notices"></a>
## Lendo os eventos e avisos do módulo

Enquanto uma sessão roda, a partir de outro console:

```text
OmsiLaunch.exe events read
OmsiLaunch.exe events watch
OmsiLaunch.exe /runtime:module.openhud.status
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

`events read` lista os eventos de runtime recentes; `events watch` imprime cada novo até você pressionar Ctrl+C. Os eventos do módulo se chamam `module.openhud.<name>`; os que você vai procurar primeiro:

| Evento | Informa |
| --- | --- |
| `armed` | O módulo iniciou. Dados: `omsi_version`, `draw` (`available` ou `unavailable`) e `pause_menu` (`on`, ou `off: <reason>`). |
| `settings-issue` | Uma configuração não pôde ser usada e o padrão dela foi adotado: `radar_zoom=big (not one of near, medium, far)`. |
| `hooks` | Os hooks da camada de desenho no dispositivo Direct3D do jogo estão instalados (`present` e `reset` `installed`). Enviado pelo componente nativo. |
| `gameplay-ready` | O mundo está rodando; os ticks começam. |
| `packages` | Os pacotes do mapa foram lidos: status de cada um, fontes e imagens enviadas, e as primeiras entradas rejeitadas. |
| `package-problem` | O carregamento dos pacotes falhou de forma inesperada; os pacotes foram ignorados. |
| `read-problem` | Uma parte do jogo não pôde ser lida (`player: player-vehicle.read answered OL_E_...`). Cada tipo é informado uma vez. |
| `draw-failed`, `draw_failed` | A camada de desenho informou uma falha e parou de desenhar. |
| `pause-degraded` | O menu de pausa desistiu na sessão e devolveu o menu do OMSI. |
| `fault` | Uma parte do módulo lançou uma exceção (ela foi contida). Dados: a área e o tipo. |
| `notice` | Uma mensagem para o usuário; a bandeja do OmsiLaunch a mostra como um balão com o título "openHUD". |
| `stopping` | O módulo está parando. |

Os avisos da bandeja são curtos, no idioma da sua interface:

| Aviso | Quando |
| --- | --- |
| The draw layer is not available, so openHUD shows nothing. | O componente nativo não pôde ser usado. |
| The radar was turned off after repeated errors. | Cinco falhas seguidas. `radar.toggle` o liga de novo. |
| openHUD's pause menu is not available; OMSI's own menu stays. | O menu de pausa desistiu. |
| A map package could not be used and was ignored. | Um pacote está `Ignored`, ou o carregamento falhou. |

Cada aviso é mostrado uma vez por sessão.

<a id="the-module-is-not-listed-or-not-loaded"></a>
## O módulo não está listado ou não foi carregado

Execute `OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules`. Se `openhud` não estiver lá, ou estiver marcado como inválido:

| Causa | O que verificar |
| --- | --- |
| Local ou nome errado | A pasta deve ser `<OMSI root>\plugins\OmsiLaunch.Modules\openhud\` e conter `module.json` ([instalação](../getting-started/installation.md#installing)). |
| Um arquivo foi alterado | `OmsiLaunch.exe /verify-module:"<folder>"` termina com código `7` e nomeia o arquivo cujo pin não confere (`OL_E_MODULE_INTEGRITY`). Reinstale a pasta. |
| Protocolo do bridge | O módulo precisa do protocolo 3; um OmsiLaunch mais antigo responde `OL_E_MODULE_PROTOCOL_UNSUPPORTED`. |
| Capacidades | O módulo exige `bridge.events`, `bridge.runtime`, `bridge.tick`, `bridge.hooks.d3d9` e `bridge.input`. Se esta build do OMSI não consegue fornecer uma delas (por exemplo a captura de entrada, capacidade nativa `ui.input.capture`, ou a capacidade Direct3D `d3d.texture`), o plano informa `OL_E_MODULE_CAPABILITY_UNRESOLVED` e o módulo não é carregado ([compatibilidade](../reference/compatibility.md)). |

Se ele está listado mas uma sessão não o usa: a ativação do openHUD é `requested`; adicione `/module:openhud` ou `enabled=true` em `modules.ini` ([ativando o módulo](enabling-the-module.md)). `/plan` mostra o que o OmsiLaunch decidiu.

<a id="the-radar-does-not-appear"></a>
## O radar não aparece

Pergunte ao módulo:

```text
OmsiLaunch.exe /runtime:module.openhud.status
```

Leia `radar_state`:

| `radar_state` | O que fazer |
| --- | --- |
| `off` | A configuração `radar` é false, ou o radar foi ocultado com `radar.toggle`. Defina `radar=true` ou execute `OmsiLaunch.exe /runtime:module.openhud.radar.toggle`. |
| `disabled` | Cinco falhas seguidas o desligaram; `status` traz `last_fault` e `native_problem`. Execute `radar.toggle` para tentar de novo; se falhar outra vez, envie os eventos. |
| `no player vehicle` | Você não está em um veículo. Entre em um. |
| `player unreadable` | O jogo não forneceu a posição do veículo; procure um evento `read-problem`. |
| `the player's tile is not in the map's tile table` | O módulo não conseguiu situar o seu veículo no mapa. Ele tenta de novo mais ou menos a cada segundo. Se persistir, os tiles do mapa não puderam ser lidos; procure eventos `read-problem` sobre `map.tiles.list` ([o radar](../concepts/the-radar.md#positions-and-headings)). |
| `no-frame` | O jogo não pôde ser lido de modo algum. Procure eventos `read-problem`. |
| `idle` | O módulo está armado, mas ainda não está no jogo em execução. |
| `on` | Está desenhando. Veja abaixo. |

Se `radar_state` é `on` e você ainda não vê nada:

- `status` diz `draw: unavailable`: a camada de desenho não pôde ser usada (o componente nativo está ausente ou falhou). Reinstale a pasta e leia o evento `armed`.
- Nenhum evento `hooks`: a camada de desenho não conseguiu instalar o hook no dispositivo Direct3D. Outro componente pode ter instalado hook no mesmo método do dispositivo; leia os eventos de hook do OmsiLaunch para ver a recusa ([convivência](coexisting.md)).
- `native_failures` sobe: a camada de desenho recusa as cenas; `native_problem` traz o código.
- O radar está sob outro overlay, ou em um canto para o qual você não olha: mude `radar_corner`, `radar_size_percent`, `radar_opacity_percent`.
- O mapa não tem pacote realmap: o círculo fica vazio, exceto por você e pelos veículos próximos. Veja `packages` em `status` ([pacotes por mapa](../concepts/per-map-packages.md#what-happens-without-them)).

<a id="the-pause-menu-is-the-native-one"></a>
## O menu de pausa é o nativo

```text
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

| `pause_menu` | `pause_reason` / estado | Significado e correção |
| --- | --- | --- |
| `off` | `turned off in the settings` | `pause_menu=false`. |
| `off` | `no draw layer` | A camada de desenho não está disponível; veja acima. |
| `off` | `ui.pause-menu.replace is not available in this build` | Esta build do OMSI não consegue ceder o menu de pausa. O menu nativo permanece; nada a corrigir do seu lado. |
| `off` | `input capture is not available` | O OmsiLaunch não conseguiu capturar a entrada do jogo nesta build. |
| `degraded` | | O menu desistiu na sessão após falhas repetidas; veja `pause_last_error` e o evento `pause-degraded` (o `reason` dele). Reinicie a sessão. |
| `on` | `Provisional` | Uma chamada ao OMSI falhou: o menu nativo é mostrado com o mapa atrás. `pause_last_error` nomeia o código (`OL_E_UI_BUSY`, `OL_E_CAPABILITY_UNRESOLVED` ...). A próxima pausa recomeça do zero. |
| `on` | `Running` | O jogo não está pausado, ou o menu ainda está esperando para assumir (um diálogo nativo está aberto, ou o OMSI está dentro do seu frame). |

Se `pause_unavailable` é `true`, o OmsiLaunch respondeu que a capacidade não pode ser resolvida; o módulo parou de pedi-la pelo resto da sessão.

<a id="text-shows-wrong-characters"></a>
## O texto mostra caracteres errados

| Você vê | Causa e correção |
| --- | --- |
| O título ou a legenda do mapa com `?` ou caixas | A fonte do pacote não tem o caractere e a fonte de interface do sistema também não, ou nenhuma fonte está registrada. Verifique `packages` (`fonts`): `0` significa que as fontes do pacote foram recusadas; os diagnósticos dizem por quê (`fonts/FONTS-LICENSE.txt is missing or empty` ...). |
| Letras acentuadas viraram dois caracteres estranhos | Um arquivo `.cfg` foi salvo em uma página de código (Windows-1252) em vez de UTF-8. Um arquivo que não é UTF-8 válido é recusado por inteiro: `Dropped: identity.cfg: not valid UTF-8`. Salve-o como UTF-8 ([pacote de menu de pausa](../reference/pause-menu-package.md#how-the-files-are-read)). |
| Emoji ou símbolos raros em branco | Caracteres fora do Basic Multilingual Plane são desenhados em branco. |
| Texto árabe, hebraico ou indico na ordem errada ou com junções erradas | Escritas da direita para a esquerda e escritas com formas contextuais não são suportadas ([limitações conhecidas](../reference/known-limitations.md)). |
| A fonte não é a do pacote | `FONTS-LICENSE.txt` ausente, um arquivo de fonte recusado (fonte variável, acima de 4 MiB, um `.ttc`), ou um papel não listado em `fonts.cfg` (ele assume o peso mais próximo). Veja as linhas `Dropped:` no evento `packages`. |

<a id="the-wrong-language"></a>
## O idioma errado

Execute `status` e leia `language` e `language_code`.

- A interface está em inglês embora o seu OMSI esteja em outro idioma: `language` é `auto`, e `auto` acompanha o idioma do OMSI apenas onde o módulo consegue lê-lo. Defina-o explicitamente: `language=pt-BR` ou `language=de` ([internacionalização](../concepts/internationalization.md)).
- O título do mapa não está traduzido: o pacote não tem `title.<LANG>` para o seu código. `language_code` é o código que se pede ao pacote (`PTB`, `DEU`, ...). Adicione a chave, ou um `strings/<LANG>.cfg` ([criando um pacote de menu de pausa](making-a-pause-menu-package.md#step-6-add-per-language-texts-optional)).
- Um idioma que a interface não tem (`es`, `fr`): o texto da interface fica em inglês; só o texto do próprio mapa pode estar nesse idioma.
- Um evento `settings-issue` com `language=...`: o valor não é um código (letras, `-` ou `_`, no máximo 8 caracteres).

<a id="high-cpu-or-a-slow-game"></a>
## CPU alta ou jogo lento

`status` traz os tempos do próprio módulo: `tick_last_ms`, `tick_max_ms`, `slow_ticks`.

1. Se `slow_ticks` fica em 0 e `tick_max_ms` é de poucos milissegundos, o módulo não é a causa; teste iniciando a mesma sessão com `/no-module:openhud`.
2. Se os ticks estão lentos, descubra pelo que eles esperam: `frames_with_problems` alto significa leituras falhando; `uploads_pending` alto significa imagens ainda sendo enviadas (um custo único por mapa).
3. Reduza o trabalho: `radar=false`, um `radar_size_percent` menor, ou `radar_zoom=near` ([desempenho](../concepts/performance.md#what-costs-the-most-and-what-to-do)).
4. Um pacote realmap muito grande custa tempo de leitura em uma thread de trabalho quando você entra em áreas novas; ele não bloqueia o tick.

<a id="package-problems"></a>
## Problemas de pacote

Um pacote que não parece certo: leia o evento `packages`; cada entrada rejeitada é uma linha `Dropped:` ou `Ignored:` com o arquivo, a linha e o motivo. Compare com [o que o log diz para cada entrada rejeitada](making-a-pause-menu-package.md#what-the-log-says-for-each-rejected-entry) e com os [erros de realmap](using-a-realmap-package.md#mistakes-the-reader-reports). O evento guarda as primeiras doze linhas (700 caracteres); para uma lista longa, corrija as primeiras e reinicie.

Quando tudo mais falhar, reúna as saídas de `events read`, `status` e `pause.state` e o plano da sessão (`/plan --json`) antes de pedir ajuda.

Relacionadas: [operações e eventos](../reference/operations-and-events.md) · [erros e degradação](../reference/errors-and-degradation.md) · [FAQ](../reference/faq.md)
