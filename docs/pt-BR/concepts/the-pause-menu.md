# O menu de pausa

<!-- l10n: source=concepts/the-pause-menu.md source-sha256=a5df1dfdf707663dfa1335660bbb107ec3e9af732b7939c2c70dc2eb70904731 -->
> Tradução da [página original em inglês](../../concepts/the-pause-menu.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Quando o OMSI pausa, ele mostra seu menu de pausa nativo. O openHUD pode assumir esse menu: ele pede ao OMSI, por meio do OmsiLaunch, que oculte o menu nativo, mostra o seu e, quando você escolhe algo, pede ao OMSI que execute a ação nativa. Esta página explica como isso funciona, os estados por que o módulo passa, o que acontece quando algo falha (e por que o menu nativo é então deixado em paz), as páginas do menu e como ele é dimensionado e lido com o mouse. As teclas estão em [controles](../reference/controls.md); a identidade que um mapa pode dar ao menu está em [pacotes por mapa](per-map-packages.md).

Páginas relacionadas: [primeira sessão](../getting-started/first-session.md), [pacotes por mapa](per-map-packages.md), [segurança](safety.md), [solução de problemas](../guides/troubleshooting.md#the-pause-menu-is-the-native-one), [erros e degradação](../reference/errors-and-degradation.md), [status de validação](../status/validation.md).

<a id="what-the-module-asks-omsi-for"></a>
## O que o módulo pede ao OMSI

Tudo passa por quatro operações do catálogo público do OmsiLaunch, que o manifesto declara (capacidade `ui.pause-menu.replace`, opcional):

| Operação | Significado |
| --- | --- |
| `ui.pause.state` | O jogo está pausado? O menu nativo está visível? Há um diálogo nativo ocupado? Qual foi o resultado da última ação (`resumed`, `game-started`, `returned`, `quit-requested`)? |
| `ui.pause.take-over` | Ocultar o menu nativo (o OMSI continua pausado, a simulação continua retida). |
| `ui.pause.action` | Executar uma ação nativa: `resume`, `new-game`, `save`, `options` ou `quit`. O handler do próprio OMSI é executado; seus diálogos são os nativos. |
| `ui.pause.restore-native` | Mostrar o menu nativo de novo. |

O módulo não clica no menu nativo, não envia mensagens a ele nem move sua janela: o OmsiLaunch chama os handlers do OMSI diretamente. O módulo também precisa do teclado e do mouse do jogo enquanto seu menu está aberto (`bridge.input`); o OmsiLaunch registra esses eventos e impede que cheguem ao OMSI, e o módulo os lê como eventos simples (tecla virtual, posição, botão, roda).

<a id="when-the-native-menu-stays"></a>
## Quando o menu nativo permanece

O módulo só assume o menu de pausa quando todas estas condições são verdadeiras; caso contrário, o menu nativo é mostrado exatamente como sem o openHUD, e o radar não é afetado.

| Condição | Onde é decidida | O que `pause.state` diz quando ela falha |
| --- | --- | --- |
| A configuração `pause_menu` é true | Configurações | `turned off in the settings` |
| A camada de desenho está disponível | Arm | `no draw layer` |
| A capacidade `ui.pause-menu.replace` está disponível nesta build | Arm | `ui.pause-menu.replace is not available in this build` |
| A captura de entrada está disponível | Arm | `input capture is not available` |

Elas são verificadas uma vez, quando o módulo é armado. Durante a sessão o menu de pausa ainda pode falhar e desistir, veja [quando algo falha](#when-something-fails).

<a id="the-states"></a>
## Os estados

O menu de pausa é uma máquina de estados conduzida pelo tick do módulo:

| Estado | Significado |
| --- | --- |
| `Running` | O jogo não está pausado (ou o menu não é nosso). A cada tick pergunta a `ui.pause.state`. |
| `TakeOverPending` | O jogo está pausado e o menu nativo está visível: pedindo ao OMSI que o oculte. Uma resposta "ocupado" é repetida por até um segundo. |
| `Open` | O menu é mostrado sobre uma cópia da última imagem do jogo; o teclado e o mouse estão capturados. |
| `Resuming` | Você escolheu "voltar ao jogo" (ou pressionou Esc): esperando as teclas serem soltas (no máximo 750 ms); depois a captura é devolvida e a ação de retomar é enviada. |
| `Modal` | Você escolheu novo jogo, salvar, opções ou sair: a captura é devolvida e o OMSI mostra seu diálogo nativo; o módulo não desenha nada e espera. |
| `Ending` | O OMSI informou que está saindo. |
| `Provisional` | Algo falhou: o menu nativo está visível e só o mapa é desenhado atrás dele. |
| `Stopped` | O módulo parou. |

Transições, em resumo:

```text
Running --paused and native menu visible--> TakeOverPending --taken over--> Open
Open --Esc or "back to the game"--> Resuming --keys released, resume sent, game running--> Running
Open --new game / save / options / quit--> Modal
Modal --back, still paused--> Open (the keyboard is taken again)
Modal --a new game started, or the game is running--> Running
Modal --OMSI is quitting--> Ending
any state --a failed call--> Provisional
Stop --from any state--> everything given back
```

<a id="rules-the-machine-keeps"></a>
## Regras que a máquina mantém

- **Nada é desenhado enquanto um diálogo nativo está na frente.** Quando o OMSI informa um diálogo ocupado (um diálogo de salvar, suas opções), o módulo oculta sua cena e libera o teclado.
- **A captura é liberada antes de qualquer ação que possa abrir um diálogo nativo.** Diálogos nativos precisam de entrada real.
- **O menu nativo só é restaurado enquanto o jogo ainda está pausado.** Restaurá-lo em um jogo em execução o pausaria de novo. Se o jogo foi retomado por outra parte, o módulo apenas solta.
- **Uma tecla ainda pressionada ao retomar dispararia como um novo pressionamento no jogo** (o OMSI também lê as teclas diretamente), então o módulo espera as teclas serem soltas, por até 750 ms, e retoma mesmo assim. Os tempos esgotados são contados (`key_release_timeouts` em `pause.state`).
- **Uma thread.** A máquina pertence à thread da sua primeira chamada; uma chamada de qualquer outra thread é recusada.
- **A limpeza é idempotente.** Parar devolve a captura, a imagem de fundo e o menu nativo, e repete a cada 250 ms o que não pôde ser feito (por exemplo, uma restauração recusada enquanto um diálogo estava aberto).

<a id="when-something-fails"></a>
## Quando algo falha

Qualquer chamada com falha a uma operação `ui.pause.*`, uma captura que não consegue começar, uma imagem de fundo que não pode ser obtida, um estado que não pode ser lido ou uma porta que lança exceção levam ao mesmo lugar:

1. tudo o que o módulo mantém é devolvido (captura, fundo e o menu nativo, se estava oculto e o jogo ainda está pausado);
2. se o jogo ainda está pausado, o estado passa a `Provisional`: **o menu nativo do OMSI está visível e funciona como sempre; o módulo desenha apenas o mapa atrás dele**. Assim você nunca fica sem menu algum;
3. a falha é guardada como `pause_last_error` (`OL_E_UI_BUSY`, `OL_E_UI_NOT_PAUSED`, `OL_E_UI_ALREADY_TAKEN_OVER`, `OL_E_CAPABILITY_UNRESOLVED`, `OL_E_RUNTIME_OPERATION_FAILED`, ou o código bruto) e a próxima pausa recomeça de `Running`.

Casos especiais:

| Resposta | O que a máquina faz |
| --- | --- |
| `OL_E_UI_BUSY` ao assumir | Repete por até um segundo (o OMSI pode estar dentro do seu quadro), depois falha como acima. |
| `OL_E_UI_NOT_PAUSED` | O jogo está em execução: nada a assumir, volta a `Running`. |
| `OL_E_UI_ALREADY_TAKEN_OVER` | O jogo mantém um estado de menu assumido que o módulo desconhece: o módulo o devolve com a limpeza de falha. |
| `OL_E_CAPABILITY_UNRESOLVED` | Travado: o módulo deixa de perguntar ao jogo sobre o menu de pausa pelo resto da sessão. |

Se o menu de pausa continua falhando (cinco falhas seguidas, cinco cenas que não podem ser publicadas, ou a camada de desenho falhando), o módulo desiste dele na sessão: devolve tudo, emite `pause-degraded` e mostra um aviso ("openHUD's pause menu is not available; OMSI's own menu stays."). O radar continua ([erros e degradação](../reference/errors-and-degradation.md)).

<a id="the-pages"></a>
## As páginas

| Página | Conteúdo |
| --- | --- |
| Menu | Cinco botões nesta ordem: voltar ao jogo, novo jogo, salvar jogo, opções, sair do OMSI; e a legenda dos marcadores do mapa à direita. |
| Mapa | O mapa inteiro, norte para cima, centralizado em você; você o move e dá zoom. Usa o pacote realmap quando há um. |
| Linhas | As linhas do mapa que circulam na data da sessão, uma por linha da tabela (número, nome, cor). Selecionar uma linha não tem outro efeito na 0.1.0. |

O cabeçalho mostra o título e o símbolo do mapa; o rodapé mostra uma dica com as teclas. O texto do menu vem do idioma da interface ([internacionalização](internationalization.md)); o título, o símbolo, a cor de destaque, a cor das linhas e a legenda vêm do pacote de menu de pausa do mapa, ou são os genéricos.

<a id="size-and-the-mouse"></a>
## Tamanho e o mouse

O menu é projetado como um bloco de 904 x 600 unidades. Ele é desenhado em uma escala que nunca fica abaixo da escala da própria janela e cresce com `ui_scale_percent` até onde a janela permite (mantém uma margem de cerca de 2 % nas laterais e 4 % acima e abaixo). A tela da cena é a virtual de 1920 x 1080, então o menu mantém a proporção em qualquer resolução.

O mouse chega em pixels de cliente da vista do jogo. O módulo pede à camada de desenho o tamanho real da vista em que desenha e mapeia os pixels de volta para a tela e depois para as unidades do menu, de modo que um clique cai sobre o que está desenhado. O movimento e o zoom da página Mapa usam o mesmo mapeamento.

Relacionado: [controles](../reference/controls.md) · [pacotes por mapa](per-map-packages.md) · [erros e degradação](../reference/errors-and-degradation.md) · [status de validação](../status/validation.md)
