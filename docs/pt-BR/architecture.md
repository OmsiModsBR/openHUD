# Arquitetura

<!-- l10n: source=architecture.md source-sha256=42b90556c67e216e3333bc79de0039a8c2e534dd08f15b9e7f0a758c78c38af2 -->
> Tradução da [página original em inglês](../architecture.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD é um Runtime Module do OmsiLaunch (bridge protocol 3). Ele lê o jogo apenas pelas operações de catálogo do OmsiLaunch, decide em código gerenciado o que desenhar e entrega o resultado a uma pequena camada nativa que o rasteriza no dispositivo Direct3D 9 do jogo. Status: construído e testado offline contra um bridge falso; ainda não executado no OMSI.

<a id="the-three-parts"></a>
## As três partes

| Parte | Onde | Faz |
| --- | --- | --- |
| Módulo gerenciado | `src/OpenHud.Module` (+ `OpenHud.Core`, `OpenHud.Pause`) | Entrada `OpenHud.HudModule`. Lê o jogo, monta o radar, o mapa completo e o menu de pausa como cenas, executa a máquina de estados da pausa, publica cenas. Carrega dentro do OMSI (x86, .NET 6). |
| Camada de desenho nativa | `src/OpenHud.Draw` (C++, x86) | Interpreta as cenas, rasteriza (fontes, imagens, recortes, transformações) e desenha no dispositivo do jogo a partir do hook de `Present`; guarda o instantâneo do fundo da pausa. Não sabe nada sobre o jogo. |
| Programa de sessão (opcional) | `src/OpenHud.Host` | Reservado para trabalho que não deve rodar dentro do OMSI. Não é usado pelo módulo atual. |

O `OpenHud.Core` não tem dependência de OMSI, Direct3D nem OmsiLaunch: modelo, pacotes, construtores de cena, i18n, configurações. O `OpenHud.Pause` é o modelo e a máquina de estados do menu de pausa por trás de pequenas portas. Apenas o `OpenHud.Module` referencia `OmsiLaunch.Runtime.Modules`; seus adaptadores transformam o bridge nessas portas.

<a id="the-scene-protocol"></a>
## O protocolo de cena

O lado gerenciado e o nativo se encontram em um único documento JSON, a cena (um canvas virtual de 1920x1080 e uma lista de primitivas), mais algumas operações (`scene`, `image.put`, `font.put`, `measure`, `background.capture`, `background.release`, `visible`, `stats`). A camada nativa desenha a última cena recebida a cada `Present`. Contrato completo: [scene-protocol.md](../scene-protocol.md) (em inglês). A camada nativa também é a fonte das métricas de texto (`measure`) e do tamanho da vista do jogo (`stats`).

<a id="data-flow-of-a-tick-every-50-ms-on-omsis-ui-thread"></a>
## Fluxo de dados de um tick (a cada 50 ms, na thread de UI do OMSI)

1. Adotar os pacotes que um worker terminou (uma troca imutável) e enviar à camada nativa algumas fontes e imagens enfileiradas, em alguns milissegundos.
2. A cada segundo, pedir `stats` à camada: o tamanho da vista, para mapear o mouse, e se ela falhou.
3. `FrameSource` lê o catálogo (`time`, `map`, `camera`, veículos do jogador e da via, tabela de tiles) em um `FrameSnapshot` imutável. Uma leitura que falha torna essa parte indisponível e nada mais.
4. Quando o `maps\<folder>\global.cfg` do mapa é visto pela primeira vez, um job em segundo plano localiza os pacotes do mapa pela API de conteúdo, lê-os e inicia um worker de consultas para o mapa.
5. A máquina de pausa dá um passo (veja abaixo).
6. A cena a mostrar é escolhida: enquanto a máquina não está em `Running`, é a que a máquina publicou (o menu, o mapa provisório ou nada); caso contrário, o radar.
7. O radar é reconstruído apenas se uma impressão digital do que ele mostra mudou (posição a 25 cm, direção a um grau, veículos, relógio, dados do mapa, zoom), no máximo a cada 100 ms, e enviado apenas se a cena codificada diferir da última. As cenas de pausa são enviadas de imediato quando mudam.

A geometria do mapa nunca bloqueia um tick: o worker responde a consultas de janela do pacote realmap (áreas, linhas, ruas, rótulos, marcadores) a partir do disco, descarta elementos pequenos demais para serem vistos na escala pedida, mantém os maiores até um orçamento e os transforma em elementos de radar no plano de exibição. O tick desenha o conjunto concluído mais recente e pede uma nova janela quando a vista sai da atual. O `north` do pacote gira o plano inteiro para que o norte real fique para cima; os veículos e o jogador passam pela mesma transformação.

<a id="pause-menu"></a>
## Menu de pausa

A `PauseMachine` (RUNNING, TAKE_OVER_PENDING, OPEN, RESUMING, MODAL, ENDING, PROVISIONAL, STOPPED) conduz as operações `ui.pause.*` e mostra o menu próprio do openHUD no lugar do menu do OMSI:

- Quando o jogo está pausado com o menu nativo visível, a máquina assume o menu, tira um instantâneo do fundo, inicia a captura de entrada e publica o menu. O módulo não captura o Esc por conta própria; o OMSI pausa sozinho.
- Os eventos de teclado e mouse capturados (coordenadas de cliente da vista) conduzem o menu. O Esc ou o item de retomar espera as teclas serem soltas, libera a captura e pede `resume`.
- Novo jogo, salvar, opções e sair podem abrir um diálogo modal nativo: a captura é liberada primeiro, nada é desenhado enquanto o jogo está ocupado, e o menu volta quando o diálogo termina com o jogo ainda pausado.
- Qualquer falha de uma chamada `ui.pause.*` leva ao modo provisório: o menu nativo continua visível e apenas o mapa completo é desenhado por trás dele. Uma capacidade que não se resolve impede a máquina de pedir de novo.

O módulo preenche a região `map` do menu com o mapa completo (viewport a partir do movimento e do zoom do menu, desenhado por meio de um recorte e uma translação) e a página de linhas com as linhas de transporte do pacote. O módulo só cria a máquina quando a configuração está ligada, existe uma camada de desenho, `ui.pause-menu.replace` está disponível e a captura de entrada está disponível.

<a id="failure-and-degradation"></a>
## Falha e degradação

Nada lança exceção para fora de um callback. Cada parte de um tick roda sob uma proteção; uma falha é contada e informada uma vez como evento, e falhas repetidas desligam essa parte:

- O radar é desligado após falhas repetidas de montagem ou de publicação (um aviso na bandeja); `radar.toggle` com `visible=true` o traz de volta.
- Se a camada de desenho informa uma falha, a máquina de pausa é parada primeiro (o menu nativo é restaurado, a captura e o fundo são liberados), depois a camada é solicitada a desenhar de novo; se continuar falhando, o radar também fica desligado.
- Se as cenas não podem ser publicadas enquanto o menu está aberto, a máquina de pausa é parada do mesmo modo.
- Sem camada de desenho, sem captura ou com uma capacidade ausente, a parte afetada é desligada com um motivo (`pause.state`, `status`); o resto continua funcionando.
- `Stop` esconde a camada, devolve o menu nativo (também quando uma resposta perdida deixou o jogo pausado com o menu oculto), libera a captura e o fundo, e é idempotente. Um módulo parado não faz mais nenhuma chamada.
- Pacotes inutilizáveis são ignorados com um diagnóstico (um evento `packages`, um aviso); o radar então desenha sem eles. Um pacote de menu de pausa fornece o título, o símbolo, as cores, a legenda e as fontes; sem um, o menu é genérico.

<a id="what-each-package-provides"></a>
## O que cada pacote fornece

- `openomsi-realmap` (localizado pelo bridge, `content_inputs`): a cartografia (áreas, linhas, ruas com limites, rótulos), marcadores com ícones, municípios e linhas de transporte. Lido em código gerenciado como dado não confiável; a impressão digital é apenas uma chave de cache.
- `openomsi-pausemenu` (ao lado dele, na pasta do mapa): o título do menu por idioma, o símbolo, as cores de destaque e de linha, os textos da legenda, as strings e as fontes. Símbolos e ícones PNG são decodificados por um decodificador estrito (limites de tamanho, checksums, sem adivinhação) e enviados como RGBA; símbolos e ícones SVG são convertidos por um conversor de subconjunto endurecido em formas da cena (nada é rasterizado e a camada de desenho não precisa de nada novo), e um desenho que não cabe é substituído pela marca genérica, com o motivo registrado no log. As fontes são registradas por caminho absoluto validado; nenhuma fonte e nenhum logotipo de marca acompanham o módulo.

<a id="operations-of-the-module"></a>
## Operações do módulo

Declaradas em `module.template.json`: `status` (configurações em vigor, estados dos pacotes, contadores, tempos do tick), `radar.zoom` (`direction` in ou out), `radar.toggle` (`visible` opcional), `pause.state` (estado da máquina e último erro tipado). As operações de catálogo que o módulo chama estão listadas em `runtime_operations`.
