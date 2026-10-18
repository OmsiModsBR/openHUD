# Usando com perfis de sessão predefinidos

<!-- l10n: source=guides/session-profiles.md source-sha256=375b2f5a4cdc2511acf59c35123a7acecce20a4861483d022d9d7cb9b2a42fcd -->
> Tradução da [página original em inglês](../../guides/session-profiles.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Um perfil de sessão do OmsiLaunch é um pacote YAML que fixa um mapa, um entrypoint e até 99 presets. Um preset pode pedir o openHUD e trazer as configurações dele, de modo que cada mapa ou forma de jogar comece com o HUD que lhe convém: um radar pequeno para o trajeto diário, um grande para passear, só o menu de pausa, ou nenhum HUD. Esta página mostra como escrevê-los, como eles se combinam com a linha de comando e com `modules.ini`, e quais erros o OmsiLaunch rejeita. O formato do perfil em si está na referência de perfis de sessão do OmsiLaunch (`docs\reference\session-profiles.md` do OmsiLaunch).

Páginas relacionadas: [ativando o módulo](enabling-the-module.md), [configurações do radar e da interface](radar-and-interface-settings.md), [referência de configurações](../reference/settings.md), [desinstalação](uninstalling.md).

<a id="the-modules-block-of-a-preset"></a>
## O bloco `modules` de um preset

Um preset tem um mapeamento `modules` opcional, com uma entrada por id de módulo. Para este módulo a entrada é `openhud`, com duas chaves:

| Chave | Significado |
| --- | --- |
| `enabled` | `true` pede o módulo para a sessão; `false` o mantém fora. Não definida: o módulo só é carregado se a linha de comando ou `modules.ini` o pedir (a ativação dele é `requested`). |
| `settings` | As configurações do módulo por chave, com os valores da [referência de configurações](../reference/settings.md). Verificadas contra o manifesto quando a sessão é planejada. |

```yaml
modules:
  openhud:
    enabled: true
    settings:
      radar_corner: top-right
      radar_zoom: far
```

Os valores podem ser números, booleanos ou strings YAML; o OmsiLaunch os converte como na linha de comando e recusa um valor fora dos limites do manifesto quando a sessão é planejada (`OL_E_MODULE_SETTING_INVALID`). Uma chave de configuração deve casar com `[a-z][a-z0-9_]{0,31}` e o id do módulo com `[a-z0-9][a-z0-9-]{1,47}`, ou o perfil é inválido (`OL_E_SESSION_PROFILE_INVALID`). Se o módulo não estiver instalado, `enabled: true` é informado no planejamento como `OL_E_MODULE_NOT_FOUND`, e configurações sozinhas como `OL_W_MODULE_SETTING_IGNORED`.

<a id="a-complete-example"></a>
## Um exemplo completo

`<OMSI root>\.omsilaunch\session-profiles\example-city-hud\profile.yaml`: quatro presets do mesmo mapa, cada um com um HUD diferente.

```yaml
schema: omsilaunch.session-profile/v1
id: example-city-hud
name: Example City with openHUD
author: You
version: "1.0"
compatibility:
  maps:
    - maps\Example City\global.cfg
new:
  map: maps\Example City\global.cfg
  entrypoint-index: 1
presets:
  - index: 1
    id: commuter
    name: Commuter (small radar)
    modules:
      openhud:
        enabled: true
        settings:
          radar_size_percent: 80
          radar_opacity_percent: 70
          radar_zoom: near
  - index: 2
    id: sightseeing
    name: Sightseeing (large radar, far zoom)
    modules:
      openhud:
        enabled: true
        settings:
          radar_corner: top-right
          radar_size_percent: 150
          radar_zoom: far
          language: pt-BR
  - index: 3
    id: pause-menu-only
    name: Pause menu only
    modules:
      openhud:
        enabled: true
        settings:
          radar: false
  - index: 4
    id: plain
    name: OMSI as it is
    modules:
      openhud:
        enabled: false
```

Inicie um deles com:

```text
OmsiLaunch.exe /predefined-profile:example-city-hud /predefined-profile-index:2 /new
```

O `id` deve ser igual ao nome da pasta. As chaves fora do bloco `modules` (`compatibility`, `new`, `settings`, ...) são definidas pela referência do OmsiLaunch; o exemplo apenas mostra onde o bloco entra. No exemplo, o mapa e o entrypoint são fixados pelo perfil, então a linha de comando não leva mais nada.

<a id="how-a-preset-combines-with-the-rest"></a>
## Como um preset se combina com o resto

- **Uma chave que o preset define não pode também ser definida na linha de comando.** `/predefined-profile-index:2` junto com `/module-setting:openhud.radar_zoom=near` é `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`. Defina a chave no preset ou na linha de comando, não nos dois. Chaves que o preset não define ainda podem ser dadas na linha de comando.
- **`enabled` em um preset conflita com a flag oposta.** Um preset com `enabled: false` e `/module:openhud` é rejeitado; o mesmo vale para `enabled: true` com `/no-module:openhud`.
- **Os valores do preset prevalecem sobre `modules.ini`**, que prevalece sobre o padrão do manifesto.

| O preset define | Linha de comando | `modules.ini` | Resultado para `radar_zoom` |
| --- | --- | --- | --- |
| `far` | (nada a respeito) | `near` | `far` |
| (nada) | `/module-setting:openhud.radar_zoom=far` | `near` | `far` |
| `far` | `/module-setting:openhud.radar_zoom=near` | | rejeitado: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT` |

<a id="keeping-the-module-out-of-a-preset"></a>
## Mantendo o módulo fora de um preset

Um preset que deve ser sempre simples pode dizer isso:

```yaml
    modules:
      openhud:
        enabled: false
```

Isso mantém o módulo fora mesmo quando `modules.ini` tem `enabled=true`.

<a id="listing-and-checking"></a>
## Listando e conferindo

```text
OmsiLaunch.exe "C:\OMSI 2" /list:SessionProfiles
OmsiLaunch.exe /predefined-profile:example-city-hud /predefined-profile-index:1 /new /plan
```

A segunda linha monta o plano do primeiro preset sem iniciar o OMSI.

Relacionadas: [ativando o módulo](enabling-the-module.md) · [configurações do radar e da interface](radar-and-interface-settings.md) · [referência de configurações](../reference/settings.md)
