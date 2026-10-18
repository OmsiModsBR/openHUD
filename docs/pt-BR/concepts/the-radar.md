# O radar

<!-- l10n: source=concepts/the-radar.md source-sha256=83d783fb2e194f763842eefcbf3e55ea619e539a379bf2b1a19836961e073313 -->
> Tradução da [página original em inglês](../../concepts/the-radar.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O radar é o minimapa redondo em um canto da tela. Esta página explica o que ele mostra, como decide onde as coisas estão, seus passos de zoom e suas duas orientações, qual é seu tamanho e sua transparência, o que muda com um pacote realmap e quando ele não desenha nada. As configurações que o controlam estão na [referência de configurações](../reference/settings.md) e no [guia de configurações do radar e da interface](../guides/radar-and-interface-settings.md).

Páginas relacionadas: [pacote realmap](../reference/realmap-package.md), [pacotes por mapa](per-map-packages.md), [o protocolo de cena em palavras simples](the-scene-protocol.md), [desempenho](performance.md), [solução de problemas](../guides/troubleshooting.md#the-radar-does-not-appear).

<a id="what-it-shows"></a>
## O que ele mostra

Do centro para fora:

| Elemento | Origem | Observações |
| --- | --- | --- |
| Camadas do mapa | O pacote realmap | Água, áreas verdes, áreas construídas, relevo, vias, trilhos, fronteiras. Sem pacote: nenhuma. |
| Nomes de municípios e lugares | O pacote realmap | Um nome discreto onde uma região se encontra (até 12 no radar) e os rótulos do pacote. |
| Marcadores | O pacote realmap | Os ícones do pacote (estações, terminais, garagens); um marcador sem ícone utilizável é um ponto. |
| Veículos ao seu redor | `road-vehicles.list` e `road-vehicle.read` | Até 48 no radar, os mais próximos primeiro. Só veículos a até 800 m de você são lidos. |
| Você | `player-vehicle.read` | Uma seta no meio. |
| O anel | | Um anel fino com quatro marcas (a vermelha é o norte) e um pequeno triângulo e uma letra vermelhos mostrando onde fica o norte. |
| A escala | | O raio do círculo em metros ou quilômetros, na borda inferior. |
| A faixa | `time.read` | Sob o radar (acima dele quando não há espaço embaixo): o relógio do jogo. |

O radar não mostra rotas, próximas paradas, velocidade nem tabela de horários nesta versão: a faixa traz apenas o relógio ([limitações conhecidas](../reference/known-limitations.md)).

<a id="zoom"></a>
## Zoom

O raio visível é um de seis passos: 100, 200, 400, 800, 1600 e 3200 metros. A configuração `radar_zoom` escolhe o passo inicial:

| `radar_zoom` | Passo | Raio mostrado |
| --- | ---: | ---: |
| `near` | 1 | 200 m |
| `medium` | 2 | 400 m |
| `far` | 3 | 800 m |

Enquanto você joga, a operação `radar.zoom` move um passo para dentro ou para fora, entre os seis passos ([operações e eventos](../reference/operations-and-events.md#radarzoom)). O nível de detalhe acompanha o raio: até 800 m tudo é desenhado (ruas, todos os rótulos); até 1600 m o nível normal; além disso o nível de visão geral (sem curvas de nível secundárias, menos rótulos, linhas simplificadas) ([pacote realmap](../reference/realmap-package.md#level-of-detail)).

<a id="orientation"></a>
## Orientação

Um radar pode manter o norte para cima ou manter a sua direção para cima. O radar do openHUD mantém **a sua direção para cima**: o mapa gira conforme você gira, então o que está à sua frente fica sempre no topo, e a marca de norte do anel se move para mostrar onde fica o norte. O mapa completo da página Mapa do menu de pausa tem o norte para cima.

Se o pacote realmap declara um `north` (o ângulo entre o "para cima" do próprio mapa e o norte real), a imagem é girada por ele, de modo que o norte real fique para cima quando o mapa está com o norte para cima; os veículos e o jogador passam pela mesma rotação e sempre concordam com o mapa.

<a id="positions-and-headings"></a>
## Posições e direções

O OMSI informa a posição de um veículo como um número de tile e uma posição dentro do tile. O módulo a converte para os metros da grade do mapa:

1. ele lê a tabela de tiles do mapa (`map.tiles.list`) e aprende a coluna e a linha de cada tile;
2. uma posição é `coluna x tamanho do tile + posição no tile`, e o mesmo para a linha; o tamanho do tile é o `grid` do pacote (300 m quando não há pacote);
3. uma direção é lida da orientação do veículo (um quaternion): a direção do seu eixo frontal vista de cima, em graus no sentido horário a partir do norte.

Duas coisas decorrem disso. Primeiro, o radar só desenha quando o tile do seu veículo está na tabela; quando não está (um tile que não pode ser lido, ou a tabela ainda não lida), o radar espera e tenta de novo em breve. Segundo, o tamanho da grade importa: um pacote cujo `grid` está errado desloca o mapa em relação aos veículos, tanto mais quanto mais longe da origem ([pacote realmap](../reference/realmap-package.md#coordinates)).

As unidades e convenções das posições do jogo são as que o modelo infere das operações do OmsiLaunch; ainda não foram confirmadas em um jogo em execução ([status de validação](../status/validation.md)).

<a id="size-corner-opacity"></a>
## Tamanho, canto, opacidade

| Configuração | Efeito |
| --- | --- |
| `radar_corner` | Um dos quatro cantos da tela de 1920 x 1080, a 24 unidades das bordas (os cantos inferiores deixam mais 44 para a faixa). |
| `radar_size_percent` | O raio é 160 unidades da tela x tamanho / 100 x `ui_scale_percent` / 100, no mínimo 40 e no máximo 520. Com 100 % e escala 100 %, o círculo tem 320 unidades de diâmetro, cerca de 30 % da altura de uma vista de 1080 pixels. |
| `radar_opacity_percent` | Multiplica a opacidade de tudo o que o radar desenha (mapa, anel, seta, faixa). 100 o desenha opaco. |
| `ui_scale_percent` | Escala o radar junto com o menu de pausa. |

Tudo é posicionado na tela, então o radar mantém seu lugar e sua proporção quando a janela ou a resolução muda.

<a id="when-the-radar-shows-nothing"></a>
## Quando o radar não mostra nada

O radar fica vazio (e a cena é limpa) nestes casos. `status` informa qual em `radar_state` ([operações e eventos](../reference/operations-and-events.md#status)).

| `radar_state` | Significado |
| --- | --- |
| `off` | A configuração `radar` é false, ou `radar.toggle` o ocultou. |
| `disabled` | Foi desligado após erros repetidos (cinco seguidos); `radar.toggle` o liga de novo. |
| `no-frame` | O jogo não pôde ser lido de forma alguma neste tick. |
| `no player vehicle` | Você não está em um veículo (ou o jogo não tem nenhum). |
| `player unreadable` | O veículo do jogador existe, mas sua posição não pôde ser lida. |
| `the player's tile is not in the map's tile table` | Veja [posições e direções](#positions-and-headings). |
| `on` | Está desenhando. |

Enquanto o menu de pausa está aberto, o radar não é desenhado; o menu tem o mapa ([o menu de pausa](the-pause-menu.md)).

<a id="without-a-realmap-package"></a>
## Sem um pacote realmap

O radar continua funcionando. Ele mostra o anel, a sua seta, os veículos ao seu redor e o relógio, sobre um disco escuro liso. Tudo o que vem do mapa fica faltando: sem água, sem ruas, sem nomes, sem ícones. O autor de um mapa pode adicionar o pacote a qualquer momento ([usando um pacote realmap](../guides/using-a-realmap-package.md)).

<a id="how-often-it-is-drawn"></a>
## Com que frequência é desenhado

O radar é reconstruído no máximo dez vezes por segundo e só quando algo visível mudou: a sua posição a um quarto de metro, a sua direção a um grau, os veículos ao seu redor, o minuto do relógio, o zoom ou novos dados do mapa. Os dados do mapa são lidos em uma thread de trabalho, uma janela por vez ao seu redor, e a janela anterior permanece na tela até a nova ficar pronta ([desempenho](performance.md)).

Relacionado: [pacote realmap](../reference/realmap-package.md) · [configurações do radar e da interface](../guides/radar-and-interface-settings.md) · [operações e eventos](../reference/operations-and-events.md)
