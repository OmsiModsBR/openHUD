# O protocolo de cena em palavras simples

<!-- l10n: source=concepts/the-scene-protocol.md source-sha256=8f84cae4292816e8f52045b23a0d1d27862349bd80e3ef378a81f2589627a233 -->
> Tradução da [página original em inglês](../../concepts/the-scene-protocol.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O protocolo de cena é a única interface entre a parte do openHUD que decide o que desenhar e a parte que desenha. Esta página o explica sem o detalhe em nível de byte: o que é uma cena, o que pode haver em uma, como ela chega à tela, quais são os limites e o que acontece quando algo está errado. O formato exato, cada comando e cada limite estão na [referência do protocolo de cena (em inglês)](../../scene-protocol.md), que é o texto normativo.

Páginas relacionadas: [as três partes](the-three-parts.md), [o radar](the-radar.md), [o menu de pausa](the-pause-menu.md), [desempenho](performance.md), [fontes, símbolos e licenciamento](fonts-symbols-and-licensing.md).

<a id="a-scene-is-a-drawing-not-a-program"></a>
## Uma cena é um desenho, não um programa

Uma cena é uma lista de comandos simples que são desenhados em ordem, em uma tela que é sempre de 1920 por 1080 unidades:

| Comando | Desenha |
| --- | --- |
| `rect`, `circle` | Um retângulo (com cantos arredondados, se pedido) ou um círculo, preenchido e/ou com contorno |
| `line`, `arc` | Um segmento ou um arco, com largura e pontas redondas ou retas |
| `poly` | Um polígono ou uma polilinha |
| `text` | Uma linha de texto, com tamanho, papel de fonte, cor, alinhamento e largura máxima opcional |
| `image` | Uma imagem registrada, com opacidade, tonalidade e rotação |
| `clip`, `unclip` | Restringir o desenho a um retângulo ou a um círculo, e suspender a restrição |
| `push`, `pop` | Mover, girar ou escalar tudo o que for desenhado até o `pop` correspondente |

As cores são texto como `#14171cd9` (vermelho, verde, azul e, opcionalmente, opacidade). Os ângulos são em graus, com 0 apontando para cima, no sentido horário. Não há laço, condição nem script: uma cena é dado, e a mesma cena é sempre desenhada da mesma forma.

Por exemplo, um painel com um título:

```json
{"v":1,"w":1920,"h":1080,"background":false,"cmds":[
 {"t":"rect","x":10,"y":20,"w":300,"h":120,"fill":"#14171cd9","r":12},
 {"t":"text","x":24,"y":40,"s":"openHUD","size":22,"font":"bold","color":"#ffffff","base":"mid"}
]}
```

<a id="from-a-scene-to-the-screen"></a>
## Da cena à tela

1. O núcleo monta uma cena (o radar, ou o menu de pausa com o mapa dentro) e o módulo a envia à camada de desenho em uma única chamada.
2. A camada de desenho verifica o documento. Um bom substitui a cena anterior por inteiro; um ruim é recusado com um código e a cena anterior permanece.
3. A cada quadro, quando o jogo apresenta sua imagem, a camada de desenho desenha a cena atual sobre ela, escalada ao tamanho real da vista do jogo (`min(width/1920, height/1080)`, centralizada).

Como a tela é virtual, a mesma cena tem a mesma aparência em qualquer resolução, e o menu de pausa pode mapear o mouse de volta dos pixels reais para as unidades da tela ([o menu de pausa](the-pause-menu.md#size-and-the-mouse)).

<a id="pictures-and-fonts"></a>
## Imagens e fontes

Uma imagem é registrada uma vez, por um id, como pixels brutos; as cenas então a referenciam pelo nome. O símbolo de um pacote de mapa e os ícones do pacote realmap viajam assim, depois de decodificados fora do tick. As fontes são registradas por papel (`regular`, `medium`, `bold`, `black`, `condensed`) a partir de um arquivo que o módulo já validou; um papel sem fonte usa a fonte de interface do sistema. Um caractere que uma fonte não tem é desenhado com a fonte de interface do sistema, nunca como um ponto de interrogação.

<a id="limits-and-why-they-exist"></a>
## Limites, e por que existem

| Limite | Valor |
| --- | --- |
| Comandos em uma cena | 8.192 |
| Tamanho de uma cena | 1 MiB de JSON |
| Aninhamento de clips / transformações | 8 / 16 |
| Imagens registradas | 128, no máximo 4 MiB de pixels cada |
| Texto em um comando | 4.096 bytes |

O núcleo nunca produz uma cena acima de um limite: quando um mapa tem mais elementos do que o orçamento, o radar mantém os mais visíveis (grandes, de camadas importantes) e descarta o resto ([desempenho](performance.md)). A camada de desenho aplica os mesmos limites a tudo o que recebe, porque ela também não confia em quem a chama.

<a id="what-happens-when-it-goes-wrong"></a>
## O que acontece quando algo dá errado

| Situação | O que acontece |
| --- | --- |
| Uma cena está malformada ou acima de um limite | A camada de desenho responde com um código (por exemplo `too_large`, `json_syntax`) e continua desenhando a cena anterior. O módulo o conta e, após cinco cenas de radar com falha seguidas, desliga o radar com um aviso. |
| Um comando é desconhecido ou falta um membro obrigatório | Esse comando é pulado e contado; o resto é desenhado. Isso permite que um núcleo mais novo converse com uma camada mais antiga. |
| A camada de desenho falha ao desenhar (uma exceção dentro do quadro do jogo) | Ela para de desenhar, diz o motivo em um evento (`draw_failed`) e espera ser avisada para tentar de novo. O módulo tenta duas vezes; se continuar falhando, desliga o radar e devolve o menu de pausa nativo. |
| O dispositivo do jogo é reiniciado (uma mudança de resolução) | A camada libera seus objetos de dispositivo antes do reset e os recria depois; nada se perde. |
| O módulo para | A camada é avisada para parar de desenhar, a cena é esvaziada e, daí em diante, toda chamada é recusada. |

Relacionado: [referência do protocolo de cena (em inglês)](../../scene-protocol.md) · [as três partes](the-three-parts.md) · [erros e degradação](../reference/errors-and-degradation.md)
