# Primeira sessão

<!-- l10n: source=getting-started/first-session.md source-sha256=8ccb6808024105dbae610255d8fb2807acf9ed9583775bf74e3c748abc1f8e3e -->
> Tradução da [página original em inglês](../../getting-started/first-session.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página percorre uma primeira sessão com o openHUD, passo a passo, com as linhas de comando exatas: iniciar o OMSI com o módulo, como o radar deve parecer, como abrir o menu de pausa e usá-lo, as teclas e como ler os próprios eventos do módulo para saber o que ele decidiu. Ela supõe que o módulo está instalado ([instalação](installation.md)). Nada aqui precisa de um pacote de mapa; o que muda com um deles é dito em cada passo.

Páginas relacionadas: [visão geral](overview.md), [como ativar o módulo](../guides/enabling-the-module.md), [controles](../reference/controls.md), [solução de problemas](../guides/troubleshooting.md), [operações e eventos](../reference/operations-and-events.md).

Os exemplos usam uma pasta de mapa inventada, `maps\Example City\global.cfg`. Use uma das suas; `OmsiLaunch.exe /list:Maps` lista as disponíveis.

<a id="step-1-plan-the-session"></a>
## Passo 1. Planeje a sessão

Um plano monta a sessão sem iniciar o OMSI. Execute-o com o módulo solicitado:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /plan
```

O que você quer ver é `Plan: READY`. Se o plano não for executável, a mensagem nomeia o código: `OL_E_MODULE_NOT_FOUND` (a pasta não está onde o OmsiLaunch procura), `OL_E_MODULE_INTEGRITY` (um arquivo do módulo mudou), `OL_E_MODULE_CAPABILITY_UNRESOLVED` (esta build do OMSI não consegue dar ao módulo algo de que ele precisa), `OL_E_MODULE_PROTOCOL_UNSUPPORTED` (o OmsiLaunch é antigo demais). Veja a [solução de problemas](../guides/troubleshooting.md#the-module-is-not-listed-or-not-loaded).

<a id="step-2-start-the-session"></a>
## Passo 2. Inicie a sessão

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud
```

O OmsiLaunch inicia o OMSI com o mundo que você escolheu. O módulo é armado antes de o mundo começar e fica ativo quando o jogo está pronto para jogar. O módulo não tem janela nem splash: até o jogo estar rodando você vê apenas o OMSI.

Para iniciar com configurações diferentes só nesta sessão, acrescente flags `/module-setting`; por exemplo, um radar maior no canto superior direito:

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /module:openhud /module-setting:openhud.radar_corner=top-right /module-setting:openhud.radar_size_percent=130
```

<a id="step-3-the-radar"></a>
## Passo 3. O radar

Assim que você está no banco do motorista, o radar aparece em seu canto (inferior esquerdo por padrão), com 90 % de opacidade. O que você deve ver:

- um mapa redondo centrado no seu veículo, com o seu veículo no meio como uma seta;
- o mapa gira conforme você gira: a sua direção fica para cima. Uma pequena marca vermelha e a letra no anel mostram onde fica o norte;
- os outros veículos ao seu redor como pequenas formas coloridas (até 48);
- a escala do círculo na borda inferior (para o zoom padrão, `400 m`: o raio do círculo);
- a sua velocidade e o relógio do jogo em uma faixa sob o radar;
- com um pacote realmap: água, parques, áreas construídas, ferrovia, relevo, ruas e nomes de municípios, e os ícones do pacote ([o radar](../concepts/the-radar.md)). Sem um, o círculo fica vazio, exceto pelo seu veículo e pelos veículos ao seu redor.

O radar só desenha quando o jogo sabe onde você está: você precisa de um veículo de jogador, e o tile em que você está deve constar na tabela de tiles do mapa. Se você está numa câmera livre longe de qualquer veículo, ou o mapa acabou de mudar, o radar espera e não desenha nada. Isso é normal; o motivo aparece em `status` (passo 6).

<a id="step-4-the-pause-menu"></a>
## Passo 4. O menu de pausa

Pause o OMSI como você sempre faz. Se o openHUD conseguir assumir o menu na sua build, você vê o menu do openHUD sobre uma imagem escurecida do jogo, no lugar do menu nativo do OMSI:

- o cabeçalho mostra o título do mapa (o menu genérico mostra o nome da pasta do mapa, aqui `Example City`) e o símbolo dele, se o mapa trouxer um pacote;
- três abas: **Menu**, **Mapa**, **Linhas**;
- na página Menu, cinco botões: voltar ao jogo, novo jogo, salvar jogo, opções, sair do OMSI; e, à direita, a legenda dos marcadores do mapa.

Se você vir o menu nativo do OMSI, o openHUD não o assumiu; o radar não é afetado. Qual das condições falhou aparece em `pause.state` (passo 6) e em [o menu de pausa](../concepts/the-pause-menu.md#when-the-native-menu-stays).

<a id="step-5-the-keys"></a>
## Passo 5. As teclas

O menu de pausa é controlado pelo teclado e pelo mouse; o radar não tem teclas. As mais usadas:

| Tecla ou mouse | Faz |
| --- | --- |
| Esc | Volta ao jogo (de qualquer página) |
| Tab / Shift+Tab | Próxima / página anterior |
| Cima, Baixo | Move entre os botões (página Menu); move o mapa (página Mapa); seleciona uma linha (página Linhas) |
| Enter, Espaço | Pressiona o botão em foco |
| Roda | Zoom (página Mapa); rolagem (página Linhas) |
| Arrastar com o botão esquerdo | Move o mapa |
| Clique duplo | Centraliza o mapa em você |

A tabela completa está em [controles](../reference/controls.md). Os botões que abrem um diálogo do OMSI (novo jogo, salvar jogo, opções, sair do OMSI) devolvem primeiro o teclado ao OMSI; o módulo não desenha nada enquanto o diálogo está à frente e retoma o teclado quando você volta ao jogo pausado.

<a id="step-6-ask-the-module-what-it-did"></a>
## Passo 6. Pergunte ao módulo o que ele fez

O módulo informa pelos eventos de runtime do OmsiLaunch e expõe quatro operações. Enquanto a sessão roda, de outro console:

```text
OmsiLaunch.exe events read
OmsiLaunch.exe /runtime:module.openhud.status
OmsiLaunch.exe /runtime:module.openhud.pause.state
```

`events read` lista os eventos de runtime, entre eles `module.openhud.armed` (com `draw` e `pause_menu` dizendo `available` ou `unavailable`, `on` ou `off: <reason>`), `module.openhud.packages` (quais pacotes foram encontrados para o mapa) e `module.openhud.gameplay-ready`. `status` devolve o estado do módulo: se o radar está ligado, o zoom, os pacotes, contadores e o tempo do tick. `pause.state` devolve o estado do menu de pausa e o último erro que ele tratou. Ambos estão descritos em [operações e eventos](../reference/operations-and-events.md).

Você também pode mudar o radar enquanto joga:

```text
OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in
OmsiLaunch.exe /runtime:module.openhud.radar.toggle
```

<a id="step-7-end-the-session"></a>
## Passo 7. Encerre a sessão

Saia do OMSI normalmente. O módulo para junto com a sessão: deixa de desenhar, devolve o teclado e a imagem de fundo e, se ainda mantinha o menu de pausa do OMSI, o devolve ([segurança](../concepts/safety.md#stopping)).

<a id="if-something-does-not-look-right"></a>
## Se algo não parecer certo

| Você vê | Leia |
| --- | --- |
| Nenhum radar | [O radar não aparece](../guides/troubleshooting.md#the-radar-does-not-appear) |
| O menu de pausa nativo do OMSI | [O menu de pausa é o nativo](../guides/troubleshooting.md#the-pause-menu-is-the-native-one) |
| Caracteres estranhos no texto | [O texto mostra caracteres errados](../guides/troubleshooting.md#text-shows-wrong-characters) |
| O idioma errado | [O idioma errado](../guides/troubleshooting.md#the-wrong-language) |

Relacionado: [visão geral](overview.md) · [controles](../reference/controls.md) · [como ativar o módulo](../guides/enabling-the-module.md)
