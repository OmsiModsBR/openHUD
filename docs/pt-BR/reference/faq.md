# Perguntas frequentes

<!-- l10n: source=reference/faq.md source-sha256=c30d9b80e1c6e69049ee4bcc394a097dfa195919a9a55d8e1f8d3cefefdfc3d3 -->
> Tradução da [página original em inglês](../../reference/faq.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Respostas curtas às perguntas mais comuns sobre o openHUD. Cada resposta aponta para a página com os detalhes.

Páginas relacionadas: [visão geral](../getting-started/overview.md), [limitações conhecidas](known-limitations.md), [solução de problemas](../guides/troubleshooting.md), [configurações](settings.md).

<a id="does-it-change-my-omsi-files"></a>
## Ele altera meus arquivos do OMSI?

Não. Ele lê pacotes e não grava nada, e a compilação nunca copia nada para dentro do OMSI ([segurança](../concepts/safety.md)).

<a id="do-i-have-to-turn-it-on-every-time"></a>
## Preciso ligá-lo toda vez?

A ativação dele é `requested`. Peça por sessão com `/module:openhud`, para todas as sessões com `enabled=true` em `[openhud]` no `modules.ini`, ou em um preset de perfil de sessão ([ativando o módulo](../guides/enabling-the-module.md)).

<a id="does-it-work-on-any-map"></a>
## Funciona em qualquer mapa?

Sim. Sem os pacotes opcionais do mapa, o radar mostra você e os veículos ao seu redor, e o menu de pausa é o genérico ([pacotes por mapa](../concepts/per-map-packages.md)).

<a id="why-is-my-radar-empty"></a>
## Por que meu radar está vazio?

Provavelmente porque o mapa não tem pacote realmap, ou porque o jogo ainda não informou ao módulo onde está o seu veículo. O `status` diz qual dos dois ([solução de problemas](../guides/troubleshooting.md#the-radar-does-not-appear)).

<a id="where-is-the-speed-the-next-stop-the-timetable"></a>
## Onde estão a velocidade, a próxima parada, a tabela horária?

A velocidade está na faixa sob o radar, ao lado do relógio, na unidade da configuração `units`. A próxima parada, a linha e o atraso não são desenhados: o catálogo não oferece fonte para eles ([limitações conhecidas](known-limitations.md)).

<a id="does-it-replace-omsis-pause-menu-on-every-build"></a>
## Ele substitui o menu de pausa do OMSI em todas as builds?

Apenas onde o OmsiLaunch consegue repassá-lo (`ui.pause-menu.replace`) e a captura de entrada e a camada de desenho funcionam. Caso contrário, o menu do OMSI é mostrado como sempre ([o menu de pausa](../concepts/the-pause-menu.md#when-the-native-menu-stays)).

<a id="what-happens-if-the-menu-breaks-while-i-am-paused"></a>
## O que acontece se o menu quebrar enquanto estou em pausa?

O módulo devolve o menu nativo do OMSI e desenha apenas o mapa atrás dele, então você nunca fica sem menu ([o menu de pausa](../concepts/the-pause-menu.md#when-something-fails)).

<a id="does-it-click-omsis-buttons-for-me"></a>
## Ele clica nos botões do OMSI por mim?

Não. Ele pede ao OMSI, por meio do OmsiLaunch, que execute o handler nativo da ação. Nenhum clique, tecla ou mensagem é enviado.

<a id="can-i-change-the-radar-while-i-play"></a>
## Posso alterar o radar enquanto jogo?

Sim: `OmsiLaunch.exe /runtime:module.openhud.radar.zoom /runtime-arg:direction=in` e `radar.toggle` ([operações e eventos](operations-and-events.md)).

<a id="how-do-i-move-or-resize-the-radar"></a>
## Como movo ou redimensiono o radar?

`radar_corner`, `radar_size_percent`, `radar_opacity_percent` e `ui_scale_percent` ([configurações do radar e da interface](../guides/radar-and-interface-settings.md)).

<a id="why-is-the-interface-in-english"></a>
## Por que a interface está em inglês?

`language=auto` resolve para inglês na 0.1.0. Defina `language=pt-BR` ou `language=de` ([internacionalização](../concepts/internationalization.md)).

<a id="can-i-use-my-own-language"></a>
## Posso usar meu próprio idioma?

A interface existe em inglês, português (Brasil) e alemão. Um pacote de mapa pode acrescentar o seu título e a legenda em qualquer código de idioma do OMSI.

<a id="how-do-i-give-my-map-its-own-look"></a>
## Como dou ao meu mapa uma aparência própria?

Adicione uma pasta `openomsi-pausemenu` ao mapa ([criando um pacote de menu de pausa](../guides/making-a-pause-menu-package.md)) e, para o radar, uma pasta `openomsi-realmap` ([usando um pacote realmap](../guides/using-a-realmap-package.md)).

<a id="does-it-include-fonts-or-logos"></a>
## Ele inclui fontes ou logotipos?

Não. Fontes e logotipos vêm do pacote do mapa, e o autor é responsável pelo direito de distribuí-los ([fontes, símbolos e licenciamento](../concepts/fonts-symbols-and-licensing.md)).

<a id="why-was-my-svg-symbol-not-drawn"></a>
## Por que meu símbolo SVG não foi desenhado?

Ele é desenhado quando cabe no subconjunto estático e no seu orçamento ([o subconjunto de SVG](pause-menu-package.md#the-svg-subset)). Quando não cabe, a marca genérica é desenhada no lugar e o motivo está no evento `packages` (`diagnostics`) e no log do pacote: um `DOCTYPE` (remova essa linha), um script, uma folha de estilo, uma referência externa, ou um desenho com mais formas do que o espaço permite.

<a id="why-does-a-package-entry-get-dropped"></a>
## Por que uma entrada de pacote é descartada?

A linha de log diz qual e por quê; todas as mensagens estão nas referências ([pacote de menu de pausa](pause-menu-package.md), [pacote realmap](realmap-package.md)).

<a id="does-the-units-setting-do-anything"></a>
## A configuração `units` faz alguma coisa?

Sim. Ela escolhe km/h ou mph para a velocidade na faixa e metros ou pés para as escalas ([configurações](settings.md#units)).

<a id="does-it-slow-the-game"></a>
## Ele deixa o jogo mais lento?

Ele acrescenta trabalho limitado: um tick a cada 50 ms e um desenho a cada frame. O custo dele em um jogo em execução ainda não foi medido ([desempenho](../concepts/performance.md), [status de validação](../status/validation.md)).

<a id="can-it-run-with-other-overlays"></a>
## Ele pode rodar com outros overlays?

Normalmente, se eles interceptam o dispositivo Direct3D pelo OmsiLaunch ou não o interceptam. Um método do dispositivo alterado por algo fora do registro do OmsiLaunch é recusado ([convivência](../guides/coexisting.md)).

<a id="how-do-i-remove-it"></a>
## Como o removo?

Exclua a pasta `openhud` ([desinstalação](../guides/uninstalling.md)).

<a id="is-it-tested"></a>
## Ele é testado?

Por testes offline que nunca iniciam o OMSI ([cobertura de testes](../status/test-coverage.md)). As rodadas em um OMSI em execução são agendadas pelo dono do projeto ([status de validação](../status/validation.md)).

Relacionado: [limitações conhecidas](known-limitations.md) · [solução de problemas](../guides/troubleshooting.md)
