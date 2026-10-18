# Ativando o módulo, por sessão e de forma permanente

<!-- l10n: source=guides/enabling-the-module.md source-sha256=cfc280b53d5d3c66c6d2b00508946d291470319d57405e625f6a7144ef9f879a -->
> Tradução da [página original em inglês](../../guides/enabling-the-module.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

A ativação do openHUD é `requested`: instalá-lo não o liga. Uma sessão só o carrega quando algo o pede. Esta página mostra as formas de pedir, para uma sessão, para todas as sessões e a partir de um perfil de sessão, como mantê-lo fora de uma sessão, em que ordem o OmsiLaunch combina os pedidos e como conferir o que uma sessão faria antes de iniciar o OMSI. As regras são do OmsiLaunch; estão descritas na referência de Runtime Modules do OmsiLaunch (`docs\reference\runtime-modules.md`, offline em `<OMSI root>\.omsilaunch\docs\reference\runtime-modules.md`) e repetidas aqui apenas no que o openHUD precisa.

Páginas relacionadas: [instalação](../getting-started/installation.md), [configurações do radar e da interface](radar-and-interface-settings.md), [perfis de sessão](session-profiles.md), [desinstalação](uninstalling.md), [referência de configurações](../reference/settings.md).

<a id="for-one-session"></a>
## Para uma sessão

Adicione `/module:openhud` à linha de comando:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud
```

Adicione configurações para esta sessão com `/module-setting:openhud.<key>=<value>`, quantas quiser:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_zoom=far /module-setting:openhud.language=pt-BR
```

O openHUD não declara flags próprias; `/module-setting` é o caminho. Um valor fora dos limites do módulo, ou uma chave que ele não declara, deixa o plano não executável com `OL_E_MODULE_SETTING_INVALID`. Coloque o argumento entre aspas se ele contiver um caractere que o seu shell trata de forma especial.

<a id="for-every-session"></a>
## Para todas as sessões

Crie ou edite `<OMSI root>\.omsilaunch\modules.ini`. Uma seção por id de módulo; `enabled=true` carrega o módulo em todas as sessões, e as linhas `key=value` definem as configurações dele:

```ini
; one section per module id
[openhud]
enabled=true
radar_corner=top-right
radar_size_percent=120
language=auto
```

Linhas que começam com `;` ou `#` são comentários, e um comentário não pode vir depois de um valor. O OmsiLaunch apenas lê este arquivo. Uma entrada que ele não consegue usar é ignorada com o aviso `OL_W_MODULE_SETTING_IGNORED`.

Com `enabled=true` no arquivo, a ativação do módulo é informada como `settings-file`. Se o módulo então não puder ser carregado (um arquivo alterado, uma capacidade ausente), a sessão ainda inicia e o plano traz o aviso `OL_W_MODULE_NOT_LOADED`; quando você *pede* o módulo na linha de comando, um módulo que não pode ser carregado deixa o plano não executável.

Para manter configurações sem ligar o módulo para todos, deixe `enabled` de fora e escreva só as configurações: elas valem sempre que uma sessão carregar o módulo por outro caminho.

<a id="from-a-session-profile"></a>
## A partir de um perfil de sessão

Um preset de um perfil de sessão do OmsiLaunch pode pedir o módulo e trazer as configurações dele, de modo que cada mapa ou forma de jogar comece com o seu próprio HUD. Veja [perfis de sessão](session-profiles.md).

<a id="keeping-it-out-of-a-session"></a>
## Mantendo-o fora de uma sessão

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /no-module:openhud
```

`/no-module:openhud` mantém o módulo fora desta sessão mesmo que `modules.ini` ou um preset o peça. Ativar e desativar no mesmo comando é `OL_E_MODULE_SETTING_INVALID`. Um preset com `enabled: true` ou `false` conflita com a flag oposta (`OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`).

<a id="what-wins"></a>
## O que prevalece

Quanto a o módulo ser carregado ou não: um pedido explícito na linha de comando ou em um preset (`/module`, `/no-module`, `enabled:` em um preset) prevalece sobre o arquivo de configurações. Quanto ao valor de uma configuração, em ordem:

1. o pedido: `/module-setting:openhud.<key>=<value>` ou `modules.openhud.settings` do preset selecionado (uma chave que o preset define não pode também ser definida na linha de comando: `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT`);
2. a seção `[openhud]` de `modules.ini`;
3. o padrão do manifesto.

| Pedido | `modules.ini` | Padrão | `radar_zoom` efetivo |
| --- | --- | --- | --- |
| `/module-setting:openhud.radar_zoom=far` | `radar_zoom=near` | `medium` | `far` |
| (nenhum) | `radar_zoom=near` | `medium` | `near` |
| (nenhum) | (nenhum) | `medium` | `medium` |

<a id="checking-before-you-start"></a>
## Conferindo antes de iniciar

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan --json
```

O plano lista os módulos planejados. Para o openHUD você vê a ativação (`requested`, `settings-file`), as nove configurações com o valor efetivo e a origem dele (`request`, `settings-file` ou `default`), as operações que ele declarou e qualquer aviso. `/list:RuntimeModules` lista o que está instalado:

```text
OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules
```

<a id="common-messages"></a>
## Mensagens comuns

| Mensagem | Significado | O que fazer |
| --- | --- | --- |
| `OL_E_MODULE_NOT_FOUND` | Nenhuma pasta `openhud` válida foi encontrada | [Instalação](../getting-started/installation.md) |
| `OL_E_MODULE_INTEGRITY` | Um arquivo do módulo foi alterado ou é alcançado por um link | Reinstale a pasta |
| `OL_E_MODULE_SETTING_INVALID` | Um valor pedido está fora dos limites, ou a chave não existe | Corrija; a [referência de configurações](../reference/settings.md) traz os limites |
| `OL_W_MODULE_SETTING_IGNORED` | Uma entrada de `modules.ini` não pôde ser usada | Corrija ou remova a linha |
| `OL_W_MODULE_NOT_LOADED` | O módulo entrou por `modules.ini` mas não pôde ser carregado | Veja o motivo no plano ([solução de problemas](troubleshooting.md#the-module-is-not-listed-or-not-loaded)) |

Relacionadas: [configurações do radar e da interface](radar-and-interface-settings.md) · [perfis de sessão](session-profiles.md) · [solução de problemas](troubleshooting.md)
