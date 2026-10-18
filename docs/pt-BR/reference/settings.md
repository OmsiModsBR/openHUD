# Referência de configurações

<!-- l10n: source=reference/settings.md source-sha256=cbc056f37fc587b56a8009557916d8ddae5d19da11109c03a92b4dc3a5ad3530 -->
> Tradução da [página original em inglês](../../reference/settings.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD declara 9 configurações no seu manifesto. Esta página lista cada chave com o seu tipo, padrão e limites, depois descreve o que cada uma faz e mostra um exemplo. A tabela de resumo é conferida com `module.template.json` (a fonte do `module.json` empacotado) pela suíte de testes e por `tools\Check-Docs.ps1`, e o comportamento descrito foi conferido com `src/OpenHud.Core/Settings/HudSettings.cs` e com o módulo (`src/OpenHud.Module/HudModule.cs`).

Páginas relacionadas: [configurações do radar e da interface](../guides/radar-and-interface-settings.md), [ativando o módulo](../guides/enabling-the-module.md), [perfis de sessão](../guides/session-profiles.md), [o radar](../concepts/the-radar.md), [internacionalização](../concepts/internationalization.md), a referência de Runtime Modules do OmsiLaunch (configurações, o arquivo de configurações, precedência).

<a id="how-settings-reach-the-module"></a>
## Como as configurações chegam ao módulo

O módulo não tem arquivo de configurações próprio e não declara flags de linha de comando. As configurações chegam a ele por meio do OmsiLaunch, que as valida contra o manifesto e recusa um valor fora dos limites. O valor efetivo de uma configuração é, em ordem:

1. a requisição: `/module-setting:openhud.<key>=<value>`, `LaunchSpec.Modules.Settings` (`"openhud.<key>": "<value>"`) ou `modules.openhud.settings` do preset de perfil de sessão selecionado (uma chave que o preset define não pode também ser definida na linha de comando: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`);
2. a seção `[openhud]` de `<OMSI root>\.omsilaunch\modules.ini` (`key=value`; linhas de comentário começam com `;` ou `#`);
3. o padrão do manifesto.

O OmsiLaunch normaliza os valores: booleanos aceitam `true`/`false`, `yes`/`no`, `on`/`off`, `1`/`0`; escolhas são comparadas sem diferenciar maiúsculas de minúsculas; inteiros são conferidos contra os seus limites; texto não tem caracteres de controle e tem um limite de comprimento. Um valor pedido inválido, ou uma chave que o módulo não declara, torna o plano não executável (`OL_E_MODULE_SETTING_INVALID`); uma entrada inutilizável de `modules.ini` é ignorada com `OL_W_MODULE_SETTING_IGNORED`. `/list:RuntimeModules` mostra cada configuração com o seu tipo e padrão.

O módulo confere os valores uma segunda vez com as mesmas regras. A leitura nunca lança exceção: uma chave ausente vira o padrão, e um valor que ele não pode usar vira o padrão mais um evento `settings-issue`. `enabled` não é uma configuração do módulo: o OmsiLaunch liga e desliga os módulos da mesma forma para todos eles ([ativando o módulo](../guides/enabling-the-module.md)).

<a id="summary"></a>
## Resumo

| Chave | Tipo | Padrão | Intervalo ou escolhas |
| --- | --- | --- | --- |
| `radar` | booleano | `true` | `true` ou `false` |
| `pause_menu` | booleano | `true` | `true` ou `false` |
| `ui_scale_percent` | inteiro | `100` | 50 a 200 |
| `language` | texto | `auto` | até 8 caracteres |
| `units` | escolha | `metric` | `metric`, `imperial` |
| `radar_zoom` | escolha | `medium` | `near`, `medium`, `far` |
| `radar_corner` | escolha | `bottom-left` | `top-left`, `top-right`, `bottom-left`, `bottom-right` |
| `radar_size_percent` | inteiro | `100` | 50 a 200 |
| `radar_opacity_percent` | inteiro | `90` | 20 a 100 |

Nos exemplos, a forma de linha de comando vale para uma sessão; a mesma chave e valor funcionam em `modules.ini` (`key=value` sob `[openhud]`) e em um preset (`modules.openhud.settings`). Acrescente `/module:openhud` para solicitar o módulo quando nada mais o fizer.

<a id="the-radar"></a>
## O radar

<a id="radar"></a>
### `radar`

Desenha o radar redondo sobre a view do jogo. Com `false`, o radar nunca é desenhado e `radar.toggle` não consegue mostrá-lo; o menu de pausa é independente. `status` informa `radar_setting` e `radar_state` (`off`).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar=false
```

<a id="radar_zoom"></a>
### `radar_zoom`

A distância que o radar mostra no início: `near` é um raio de 200 m, `medium` 400 m, `far` 800 m. A operação `radar.zoom` percorre os seis passos 100, 200, 400, 800, 1600 e 3200 m durante a sessão. O nível de detalhe do mapa acompanha o raio.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=near
```

<a id="radar_corner"></a>
### `radar_corner`

O canto da tela onde o radar fica, a 24 unidades de canvas das bordas (os cantos inferiores deixam espaço para a faixa do relógio).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=bottom-right
```

<a id="radar_size_percent"></a>
### `radar_size_percent`

O tamanho do radar: o seu raio é 160 unidades de canvas x `radar_size_percent` / 100 x `ui_scale_percent` / 100, no mínimo 40 e no máximo 520.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_size_percent=120
```

<a id="radar_opacity_percent"></a>
### `radar_opacity_percent`

A opacidade de tudo o que o radar desenha, de 20 a 100 por cento. Aplicada apenas ao radar, não ao menu de pausa.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_opacity_percent=75
```

<a id="the-interface"></a>
## A interface

<a id="pause_menu"></a>
### `pause_menu`

Substitui o menu de pausa do OMSI pelo do openHUD onde a build permite. Mesmo com `true`, o menu nativo permanece quando a camada de desenho, a capacidade `ui.pause-menu.replace` ou a captura de entrada não está disponível ([o menu de pausa](../concepts/the-pause-menu.md#when-the-native-menu-stays)); `false` o mantém em todos os casos.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.pause_menu=false
```

<a id="ui_scale_percent"></a>
### `ui_scale_percent`

Escala da interface: o raio do radar e o menu de pausa. O menu nunca é desenhado menor que o seu tamanho normal e cresce até onde a janela permitir, então valores abaixo de 100 afetam apenas o radar. Acima de 150, o menu usa a versão `@2x` do símbolo de um mapa quando existe.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.ui_scale_percent=125
```

<a id="language"></a>
### `language`

O idioma da interface. `auto` (ou um valor vazio) é o padrão; caso contrário, um código de letras, `-` ou `_`, de no máximo 8 caracteres (`en`, `ENG`, `pt-BR`, `de`), guardado em maiúsculas. Existem inglês, português (Brasil) e alemão; outros códigos usam inglês para a interface e ainda selecionam o idioma dos textos de um pacote de mapa ([internacionalização](../concepts/internationalization.md)). Um valor com outros caracteres é um `settings-issue` e usa-se `auto`.

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.language=de
```

<a id="units"></a>
### `units`

`metric` ou `imperial`, para velocidade e distância no HUD. Na versão 0.1.0 nenhum texto desenhado depende dela ainda (a escala do radar está em metros e quilômetros); ela é validada e informada por `status` ([limitações conhecidas](known-limitations.md)).

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.units=imperial
```

<a id="a-file-with-every-setting"></a>
## Um arquivo com todas as configurações

```ini
[openhud]
radar=true
pause_menu=true
ui_scale_percent=100
language=auto
units=metric
radar_zoom=medium
radar_corner=bottom-left
radar_size_percent=100
radar_opacity_percent=90
```

Esses são os padrões; escrevê-los não muda nada.

Relacionado: [configurações do radar e da interface](../guides/radar-and-interface-settings.md) · [ativando o módulo](../guides/enabling-the-module.md) · [operações e eventos](operations-and-events.md)
