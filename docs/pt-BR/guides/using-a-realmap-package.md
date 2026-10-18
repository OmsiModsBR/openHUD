# Usando um pacote realmap com o radar

<!-- l10n: source=guides/using-a-realmap-package.md source-sha256=aa3de477d84c9cbed65d7600e3ec4e6d13eaf35bd47a07900b119a183b0ba13d -->
> Tradução da [página original em inglês](../../guides/using-a-realmap-package.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Um pacote realmap dá ao radar um mapa real para desenhar: água, parques, áreas urbanizadas, relevo, o nome e o limite de velocidade da rua em que você está, os ícones de estações e garagens, o município em que você está e as linhas do mapa. Este guia mostra como o autor de um mapa organiza um pacote, com um pequeno exemplo completo cujos arquivos de texto são impressos por inteiro e verificados pela suíte de testes, e como o jogador percebe a diferença. O formato está na [referência do pacote realmap](../reference/realmap-package.md).

Páginas relacionadas: [o radar](../concepts/the-radar.md), [pacotes por mapa](../concepts/per-map-packages.md), [configurações do radar e da interface](radar-and-interface-settings.md), [solução de problemas](troubleshooting.md), [criando um pacote de menu de pausa](making-a-pause-menu-package.md).

O exemplo é de um mapa inventado, "Example City", com uma "Line 12" inventada.

<a id="for-players-what-you-do"></a>
## Para jogadores: o que você faz

Nada. Se o mapa que você joga traz uma pasta `openomsi-realmap` ao lado do seu `global.cfg`, o módulo a encontra por meio do OmsiLaunch e o radar a usa; se não, o radar desenha sem ela. Para conferir se o módulo encontrou o pacote, leia os eventos do módulo no início da sessão ([solução de problemas](troubleshooting.md#reading-the-modules-events-and-notices)).

<a id="for-map-authors-step-by-step"></a>
## Para autores de mapas: passo a passo

<a id="step-1-create-the-folder"></a>
### Passo 1. Crie a pasta

```text
<OMSI root>\maps\Example City\
  global.cfg
  openomsi-realmap\
    manifest.cfg
    surface.oms
    markers.cfg
    regions.cfg
    lines.lin
    icons\
      terminal.png
      depot.png
```

A pasta se chama `openomsi-realmap`, em minúsculas. Só `manifest.cfg` é obrigatório. Os arquivos são feitos de antemão pelas suas próprias ferramentas, a partir dos dados que você tiver; o módulo apenas os lê.

<a id="step-2-write-manifestcfg"></a>
### Passo 2. Escreva o `manifest.cfg`

<a id="openomsi-realmapmanifestcfg"></a>
#### `openomsi-realmap/manifest.cfg`

```ini
# Realmap package of the Example City map
format = 1
name = Example City
grid = 300
credit = Map data by Example Maps Studio. Fitted to the map's own roads.
north = 0
alignment = fitted
terrain_offset = 0

# Colors of the dark look; the light_ twin is the light look
water = #7fb2e5
river = #7fb2e5
green = #9ccf8f
built = #2a2a2a
light_water = #5a98d1

# The player's marker
player = triangle
player_free = #ffffff
player_line = #3a7bd5
player_brt = #1e7b34
brt_lines = 12, 15

# Which vehicles are drawn as trams, metro trains and trains
tram_vehicles = vehicles/tram_example/
metro_vehicles = vehicles/metro_example/
rail_vehicles = vehicles/train_example/
```

`format` e `grid` são obrigatórios. `grid` é o tamanho, em metros, dos tiles nos números que você usa para posições: 300 para um mapa comum, outro número para um mapa organizado de outra forma. Um `grid` errado coloca o jogador no lugar errado no radar, porque o módulo converte os números de tile do jogo para os seus metros com ele.

<a id="step-3-build-surfaceoms"></a>
### Passo 3. Monte o `surface.oms`

`surface.oms` é um arquivo binário escrito pelas suas ferramentas ([o leiaute do arquivo](../reference/realmap-package.md#surfaceoms)). O pacote de exemplo tem um chunk, `(0, 0)`, com origem `(0, 0)` e chunks de 2000 m:

| Conteúdo | Valor |
| --- | --- |
| Água (classe de preenchimento 0) | um triângulo com vértices `(100, 100)`, `(400, 100)`, `(100, 400)` |
| Parque (classe de preenchimento 1) | um triângulo com vértices `(1500, 1500)`, `(1700, 1500)`, `(1500, 1700)` |
| Rio (classe de linha 0) | uma linha de 6 m de largura passando por `(0, 500)`, `(900, 520)`, `(1900, 500)`, chamada "Example River" |
| Rua (classe de linha 3, rank 2) | `(200, 200)` a `(800, 200)`, chamada "Main Street" |
| Rua com limites (classe de linha 4) | `(200, 300)` a `(800, 300)`, limite 50, limite para veículos pesados 40, chamada "River Avenue" |
| Rótulo (tipo 0, distrito, rank 200) | em `(500, 500)`, "Old Town" |
| Rótulo (tipo 1, terminal, rank 255) | em `(1200, 840)`, "Central Terminal" |

<a id="step-4-add-icons-and-markerscfg"></a>
### Passo 4. Adicione ícones e `markers.cfg`

Dois arquivos PNG em `icons`, de até 1024 x 1024 pixels: `icons/terminal.png` (64 x 64) e `icons/depot.png` (96 x 96) no exemplo.

<a id="openomsi-realmapmarkerscfg"></a>
#### `openomsi-realmap/markers.cfg`

```ini
# icon | width in pixels | x | y | name
marker = icons/terminal.png | 32 | 1200.5 | 840.0 | Central Terminal
marker = icons/depot.png | 48 | 650.0 | 410.5 | East Depot
```

<a id="step-5-add-regionscfg"></a>
### Passo 5. Adicione `regions.cfg`

<a id="openomsi-realmapregionscfg"></a>
#### `openomsi-realmap/regions.cfg`

```ini
# region = name | label x | label y | box width m | box height m | polygon
region = Old Town | 750 | 750 | 600 | 120 | 0,0 1500,0 1500,1500 0,1500
region = Harbor District | 1700 | 400 | 0 | 0 | 1500,0 2000,0 2000,800 1500,800
```

Uma largura ou altura de caixa igual a 0 significa que o nome não cabe em nenhum lugar legível e não é desenhado; o distrito continua conhecido para a linha "você está em".

<a id="step-6-add-lineslin"></a>
### Passo 6. Adicione `lines.lin`

Os campos são separados por um caractere TAB; as posições são relativas ao registro `ORIGEM`. O bloco abaixo usa caracteres de tabulação reais: copie-o com eles.

<a id="openomsi-realmaplineslin"></a>
#### `openomsi-realmap/lines.lin`

```text
# lines of Example City
RLIN	1
ORIGEM	0.0	0.0
L	12	EXP	Old Town - East Depot	bus	0	0
R	4.2	9	14	0	Every day	Articulated	Central Terminal - East Depot
P	1200.5 840.0 900.0 700.0 650.0 410.5
S	1200.5 840.0 650.0 410.5
R	4.3	9	15	0	Every day	Articulated	East Depot - Central Terminal
P	650.0 410.5 900.0 700.0 1200.5 840.0
S	650.0 410.5 1200.5 840.0
L	12	EXP	Old Town - East Depot (until 2019)	bus	0	20191231
R	3.8	8	12	0	Mon-Fri	Standard	Old Town - East Depot
P	1200.5 840.0 650.0 410.5
S	1200.5 840.0 650.0 410.5
L	T1	TRM	Harbor Tram	tram	19900101	0
R	2.0	5	8	1	Every day	Tram	Harbor loop
P	1700.0 400.0 1900.0 400.0 1900.0 600.0 1700.0 600.0
S	1700.0 400.0 1900.0 600.0
```

Uma forma de linha tem uma ou duas rotas (uma rota é um sentido). O painel mostra as formas válidas na data da sessão: com `from` e `to` como `yyyymmdd` (`0` = aberto), a primeira forma da Line 12 vale em todos os anos e a segunda só até o fim de 2019.

<a id="step-7-add-credit-and-licence"></a>
### Passo 7. Adicione crédito e licença

Coloque a licença dos seus dados em `openomsi-realmap/LICENSE.txt`. O leitor não a abre; ela é para as pessoas que recebem o pacote.

<a id="what-the-example-looks-like-to-the-reader"></a>
## Como o exemplo aparece para o leitor

Quando o exemplo acima é lido, o pacote é carregado com todas as camadas (superfície, marcadores, regiões e linhas de transporte), e o leitor não informa nenhuma entrada rejeitada. No exemplo:

- o manifesto tem `grid = 300`, `alignment = fitted`, um marcador de jogador em triângulo e as cores acima;
- o arquivo de marcadores dá dois marcadores com seus dois ícones;
- o arquivo de regiões dá dois municípios, o primeiro com uma caixa de rótulo legível e o segundo sem;
- o arquivo de linhas dá três formas de linha: duas da Line 12 (uma linha de ônibus) e uma linha de bonde; em uma data de 2021 as formas válidas são a Line 12 atual e o bonde, e em uma data de 2019 as duas formas da Line 12 e o bonde.

<a id="mistakes-the-reader-reports"></a>
## Erros que o leitor informa

Cada um destes descarta uma entrada; o resto do pacote permanece. As mensagens são as do leitor (a linha é `Dropped: <file>:<line>: <reason>`):

| O que você escreveu | O log diz |
| --- | --- |
| `marker = icons/terminal.png \| 32 \| 1200` (campos faltando) | `markers.cfg:<line>: expected icon \| width \| x \| y \| name` |
| `marker = elsewhere/x.png \| 30 \| 1 \| 1` | `icons are PNG files under icons/` |
| `marker = icons/missing.png \| 30 \| 1 \| 1` | `icon icons/missing.png: not found` |
| um ícone maior que 1024 x 1024 | `icon icons/huge.png: larger than 1024x1024` |
| `region = Broken \| 1 \| 1 \| 1 \| 1 \| 1,1 2,2` | `polygon needs 3 to 20000 points` |
| `L` com um tipo de veículo que não existe (`hovercraft`) | `unknown kind of vehicle 'hovercraft'` |
| `grid = 0` no manifesto | `Ignored: openomsi-realmap: manifest grid is missing or not a plausible tile size` |

Relacionadas: [referência do pacote realmap](../reference/realmap-package.md) · [o radar](../concepts/the-radar.md) · [solução de problemas](troubleshooting.md)
