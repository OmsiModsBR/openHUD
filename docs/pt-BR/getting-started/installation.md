# Instalação

<!-- l10n: source=getting-started/installation.md source-sha256=453aad9a1eaba57b7a1d55f68867ae11d706ffde70dc5028ddc4eccec0b5c527 -->
> Tradução da [página original em inglês](../../getting-started/installation.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD é instalado como uma pasta dentro de uma instalação do OmsiLaunch. Esta página lista os requisitos, diz como construir a pasta do módulo a partir do repositório, onde colocá-la, como verificar se o OmsiLaunch a aceita e como atualizá-la e removê-la. Nada no processo copia coisa alguma para a sua instalação do OMSI sozinho: a build grava na pasta `artifacts` do repositório, e você mesmo copia a pasta pronta.

Páginas relacionadas: [visão geral](overview.md), [primeira sessão](first-session.md), [como ativar o módulo](../guides/enabling-the-module.md), [desinstalação](../guides/uninstalling.md), [compatibilidade](../reference/compatibility.md), [solução de problemas](../guides/troubleshooting.md).

<a id="requirements"></a>
## Requisitos

| Requisito | Detalhe |
| --- | --- |
| Windows | As plataformas que o OmsiLaunch suporta (veja a referência de compatibilidade do OmsiLaunch). As ferramentas do próprio módulo são para Windows x64; os componentes que rodam dentro do OMSI são de 32 bits. |
| OMSI 2 | Uma instalação que o OmsiLaunch suporta, com o plugin permanente do OmsiLaunch instalado (guia de instalação do OmsiLaunch). |
| OmsiLaunch | Uma build que atenda o **bridge protocol 3** de Runtime Modules: o módulo declara `bridge_protocol` de 3 a 3, porque usa o tick, as operações em processo, a captura de entrada e as entradas de conteúdo da comunidade. |
| Capacidades | O módulo não pode ser carregado sem os recursos do bridge `bridge.events`, `bridge.runtime`, `bridge.tick`, `bridge.hooks.d3d9` e `bridge.input`. `ui.pause-menu.replace`, `camera.read`, `timetable.read` e `weather.read` são opcionais ([compatibilidade](../reference/compatibility.md)). |

Opcionais, por mapa: um pacote realmap e um pacote de menu de pausa ([pacotes por mapa](../concepts/per-map-packages.md)). Nenhum dos dois é necessário para instalar o módulo.

<a id="building-the-module-folder"></a>
## Construindo a pasta do módulo

Construir é só para quem trabalha a partir do repositório; uma pasta que você recebeu já construída precisa apenas da próxima seção.

Ferramentas: o .NET SDK, as ferramentas de build C++ do Visual Studio (MSBuild com o toolset x86) e um checkout do OmsiLaunch ao lado deste repositório (ou o caminho dele informado com `-OmsiLaunchRoot`).

```text
powershell -File tools\Build-Module.ps1 -OmsiLaunchRoot C:\src\OmsiLaunch
```

O script:

1. constrói a camada de desenho `openhud-draw.dll` (C++, x86) e seus testes nativos, e os executa;
2. constrói o componente gerenciado (`OpenHud.dll`, com `OpenHud.Core.dll` e `OpenHud.Pause.dll`);
3. monta `artifacts\modules\openhud\` com os arquivos abaixo e um `module.json` gerado a partir de `module.template.json`, no qual cada componente é fixado pelo seu SHA-256;
4. a menos que você passe `-SkipTests`, constrói e executa os testes gerenciados.

| Arquivo em `artifacts\modules\openhud\` | O que é |
| --- | --- |
| `module.json` | O manifesto, com os pins dos componentes. |
| `OpenHud.dll`, `OpenHud.Core.dll`, `OpenHud.Pause.dll` | O componente gerenciado e as duas bibliotecas que ele usa. |
| `openhud-draw.dll` | A camada de desenho nativa. |
| `NOTICE.txt`, `LGPL-3.0.txt`, `GPL-3.0.txt` | O aviso de licença e os textos das licenças. |

A build nunca grava em uma instalação do OMSI e nunca inicia o OMSI.

<a id="installing"></a>
## Instalando

Copie a pasta `openhud` inteira para a pasta de módulos do plugin permanente:

```text
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\module.json
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.Core.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\OpenHud.Pause.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\openhud-draw.dll
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\NOTICE.txt
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\LGPL-3.0.txt
<OMSI root>\plugins\OmsiLaunch.Modules\openhud\GPL-3.0.txt
```

O nome da pasta deve ser exatamente `openhud`, o id do módulo. Não edite os arquivos: o `module.json` fixa os componentes pelo SHA-256, e um arquivo alterado faz o OmsiLaunch recusar o módulo (`OL_E_MODULE_INTEGRITY`).

A ativação do módulo é `requested`: instalá-lo não o liga. Uma sessão só o carrega quando você pede ([como ativar o módulo](../guides/enabling-the-module.md)).

<a id="verifying"></a>
## Verificando

Verifique a pasta sem tocar em nenhuma instalação:

```text
OmsiLaunch.exe /verify-module:"C:\OMSI 2\plugins\OmsiLaunch.Modules\openhud"
```

O código de saída `0` significa que o manifesto é lido e cada arquivo fixado confere com seu pin; `7` significa que um arquivo fixado não confere; `2` significa que o manifesto não é lido ou que falta um arquivo ([erros e degradação](../reference/errors-and-degradation.md)).

Verifique se o OmsiLaunch o lista na sua instalação:

```text
OmsiLaunch.exe "C:\OMSI 2" /list:RuntimeModules
```

A entrada de `openhud` mostra a versão `0.1.0`, a ativação `requested`, suas quatro operações (`status`, `radar.zoom`, `radar.toggle`, `pause.state`), suas nove configurações com tipo e padrão, e se a pasta é válida. Se a entrada estiver ausente ou inválida, veja a [solução de problemas](../guides/troubleshooting.md#the-module-is-not-listed-or-not-loaded).

Verifique o que uma sessão faria, sem iniciar o OMSI:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan
```

Um plano executável imprime `Plan: READY`; com `--json` o plano lista o módulo entre os módulos planejados, com suas configurações e a origem de cada valor. Um módulo que não pode ser carregado torna o plano não executável, com o código `OL_E_MODULE_*` do módulo.

<a id="upgrading"></a>
## Atualização

Feche o OMSI e as sessões do OmsiLaunch, apague a pasta `openhud` antiga e copie a nova no lugar dela. As configurações ficam nos próprios arquivos do OmsiLaunch (`modules.ini`, perfis de sessão), não na pasta do módulo, então são mantidas. O módulo não guarda dados próprios entre sessões.

<a id="removing"></a>
## Remoção

A [desinstalação](../guides/uninstalling.md) cobre como remover o módulo para uma sessão, para um perfil e de vez.

Relacionado: [visão geral](overview.md) · [primeira sessão](first-session.md) · [como ativar o módulo](../guides/enabling-the-module.md)
