# Configurações do radar e da interface, com um exemplo de cada

<!-- l10n: source=guides/radar-and-interface-settings.md source-sha256=d87299cc04d146dcd66cbf3829aefabc9ff9fff2f483acfd44b76388a634b6aa -->
> Tradução da [página original em inglês](../../guides/radar-and-interface-settings.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Este guia percorre as nove configurações do openHUD, uma a uma, com um exemplo completo para cada: o que escrever, onde e o que você vê. Cada exemplo funciona como flag de linha de comando para uma sessão, como linha em `modules.ini` para todas as sessões e como entrada em um preset de perfil de sessão ([ativando o módulo](enabling-the-module.md), [perfis de sessão](session-profiles.md)). A tabela completa com tipos, limites e padrões é a [referência de configurações](../reference/settings.md).

Páginas relacionadas: [o radar](../concepts/the-radar.md), [o menu de pausa](../concepts/the-pause-menu.md), [internacionalização](../concepts/internationalization.md), [solução de problemas](troubleshooting.md).

Os exemplos iniciam o mapa inventado `maps\Example City\global.cfg`; o que importa é a flag `/module-setting:`. Adicione `/module:openhud` para pedir o módulo quando ele ainda não estiver pedido.

<a id="radar-on-and-off"></a>
## Radar ligado e desligado

`radar` (padrão `true`) desenha o radar redondo. Desligue se você quiser só o menu de pausa:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar=false
```

Você não vê radar; o menu de pausa não é afetado. Durante o jogo você também pode ocultar e mostrar o radar sem reiniciar com a operação `radar.toggle` do módulo ([operações e eventos](../reference/operations-and-events.md#radartoggle)), mas ela não consegue mostrar um radar que a configuração desligou.

<a id="radar-size"></a>
## Tamanho do radar

`radar_size_percent` (50 a 200, padrão 100) escala o radar. Em 100 o círculo tem raio de 160 unidades no canvas de 1920 x 1080 (cerca de 30 % da altura da tela de ponta a ponta). Em 150 o raio é 240; em 50 é 80:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_size_percent=150
```

O tamanho também acompanha `ui_scale_percent`, e o raio nunca fica abaixo de 40 nem acima de 520 unidades. Um radar maior mostra a mesma distância (quem decide isso é o zoom) em mais pixels, então ruas e nomes ficam mais fáceis de ler.

<a id="radar-corner"></a>
## Canto do radar

`radar_corner` (`top-left`, `top-right`, `bottom-left`, `bottom-right`, padrão `bottom-left`) escolhe onde o radar fica:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=top-right
```

O radar mantém 24 unidades das bordas; nos cantos inferiores ele também deixa espaço para a faixa do relógio. Escolha o canto que não cubra os seus instrumentos nem o seu overlay de mapa.

<a id="radar-opacity"></a>
## Opacidade do radar

`radar_opacity_percent` (20 a 100, padrão 90) deixa o radar mais ou menos transparente. Tudo o que ele desenha é multiplicado por esse valor: mapa, anel, seta e faixa.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_opacity_percent=60
```

Em 60 a estrada à frente aparece através do radar. Em 100 ele é opaco. O valor não pode ficar abaixo de 20, então o radar nunca some por acidente.

<a id="radar-zoom"></a>
## Zoom do radar

`radar_zoom` (`near`, `medium`, `far`, padrão `medium`) é a distância que o radar mostra quando a sessão começa:

| Valor | Raio do círculo |
| --- | ---: |
| `near` | 200 m |
| `medium` | 400 m |
| `far` | 800 m |

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=far
```

A escala na borda inferior do círculo indica o raio. Durante a sessão, `radar.zoom` percorre seis passos, de 100 m a 3,2 km:

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=out
```

<a id="units"></a>
## Unidades

`units` (`metric` ou `imperial`, padrão `metric`) é o sistema de unidades para velocidade e distância no HUD. A faixa sob o radar mostra a sua velocidade à esquerda, e a escala do radar e da página Mapa segue o mesmo sistema:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.units=imperial
```

| `units` | Velocidade | Escala do radar | Barra de escala da página Mapa |
| --- | --- | --- | --- |
| `metric` | `47 km/h` | `100 m` ... `3.2 km` | metros e quilômetros |
| `imperial` | `29 mph` | `330 ft` ... `2 mi` | pés e milhas |

As palavras das unidades vêm do idioma da interface (`UI_UNIT_KMH`, `UI_UNIT_MPH`, `UI_UNIT_M`, `UI_UNIT_KM`, `UI_UNIT_FT`, `UI_UNIT_MI`). A velocidade é o `ground_speed` do jogo lido como metros por segundo; essa unidade é inferida e será confirmada na rodada em tempo de execução, então, se o número na faixa estiver errado por um fator constante, é o primeiro lugar para olhar ([limitações conhecidas](../reference/known-limitations.md)). O valor também é informado por `status` (`units`).

<a id="language"></a>
## Idioma

`language` (até 8 caracteres, padrão `auto`) escolhe o idioma da interface. Um código como `en`, `ENG`, `pt-BR` ou `de` o seleciona; `auto` acompanha o idioma do OMSI onde o módulo consegue lê-lo ([internacionalização](../concepts/internationalization.md)).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.language=pt-BR
```

Você vê o menu de pausa e os rótulos do radar em português do Brasil. Em `modules.ini`:

```ini
[openhud]
language=de
```

Um valor que não é um código (um número, uma palavra com espaços) é informado como problema de configuração (evento `module.openhud.settings-issue`) e `auto` é usado. Um código para o qual a interface não tem tradução (`es`) usa inglês na interface, enquanto o título e a legenda do próprio mapa ainda usam as variantes em espanhol do pacote dele.

<a id="ui-scale"></a>
## Escala da interface

`ui_scale_percent` (50 a 200, padrão 100) escala a interface do openHUD: o raio do radar e o menu de pausa.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.ui_scale_percent=130
```

Para o radar, 130 deixa o círculo 30 % maior além de `radar_size_percent`. Para o menu de pausa, o menu cresce até 130 % do tamanho normal, até onde a janela permitir (ele sempre mantém uma margem), e nunca fica menor que o normal: valores abaixo de 100 reduzem apenas o radar. Acima de 150, o menu usa a versão `@2x` do símbolo do mapa quando o pacote tem uma.

<a id="the-pause-menu"></a>
## O menu de pausa

`pause_menu` (padrão `true`) permite que o módulo substitua o menu de pausa nativo do OMSI onde a build permitir. Defina como `false` para manter o menu nativo em todos os casos:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.pause_menu=false
```

O radar continua funcionando. `module.openhud.armed` então informa `pause_menu: off: turned off in the settings`.

<a id="a-combination-in-a-file"></a>
## Uma combinação, em um arquivo

Para um jogador que quer um radar maior e mais discreto no canto superior direito, em português, com o menu de pausa ligado:

```ini
[openhud]
enabled=true
radar_corner=top-right
radar_size_percent=140
radar_opacity_percent=70
radar_zoom=medium
language=pt-BR
ui_scale_percent=110
pause_menu=true
```

Relacionadas: [referência de configurações](../reference/settings.md) · [ativando o módulo](enabling-the-module.md) · [perfis de sessão](session-profiles.md) · [o radar](../concepts/the-radar.md)
