# Pacote realmap

<!-- l10n: source=reference/realmap-package.md source-sha256=330bbe09c12cdb644ff762f42d53878bf0e7cc0b078472123d03af0693b3826a -->
> Tradução da [página original em inglês](../../reference/realmap-package.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O pacote `openomsi-realmap` é uma imagem opcional e pré-construída de um mapa, que fica junto ao mapa: a água, os parques, as áreas construídas, o relevo, as ruas com nome e os limites de velocidade, os ícones das estações, os municípios e as linhas. O radar do openHUD o usa para desenhar um mapa real em volta do jogador, em vez de um círculo vazio. Esta página lista o que `src/OpenHud.Core/Packages/Realmap/` lê e como trata o que lê. O formato é produzido pelas ferramentas do autor do mapa; a página é para quem faz os arquivos e para quem quer saber no que o radar pode confiar. Um exemplo completo está em [usando um pacote realmap com o radar](../guides/using-a-realmap-package.md).

Versão do formato: `1`. Páginas relacionadas: [o radar](../concepts/the-radar.md), [pacotes por mapa](../concepts/per-map-packages.md), [pacote de menu de pausa](pause-menu-package.md), [compatibilidade e capacidades](compatibility.md), [erros e degradação](errors-and-degradation.md).

<a id="where-it-is-and-who-finds-it"></a>
## Onde fica e quem o encontra

```text
<OMSI root>\maps\<Map folder>\openomsi-realmap\
  manifest.cfg      required: its presence makes the package count
  surface.oms       the pre-built surface, streets and relief (binary)
  markers.cfg       optional: icons on the map
  regions.cfg       optional: municipalities
  lines.lin         optional: the lines (bus, tram, metro, rail)
  icons\            optional: the PNG pictures that markers.cfg names
  LICENSE.txt       optional: the licence of the data, for people (not read)
```

O módulo não procura a pasta por conta própria. Ele declara a entrada de conteúdo `realmap-package` (`required: false`) e o bridge do OmsiLaunch localiza a pasta do mapa atual, verifica que a leitura é segura e entrega ao módulo o seu diretório e uma impressão digital. Valem as regras do OmsiLaunch: o pacote deve estar dentro da instalação, nenhuma parte dele pode ser um link, e ele é limitado a 50.000 arquivos e 4 GiB; caso contrário, o módulo recebe `OL_E_MODULE_CONTENT_INPUT_INVALID` e ignora o pacote. Um mapa sem pacote não é um erro: o radar desenha sem ele ([o radar](../concepts/the-radar.md#without-a-realmap-package)).

O módulo pede o pacote do mapa que o jogo informa (`map.read`, nome de arquivo no formato `maps\<folder>\global.cfg`). Se o jogo não informa um nome assim, nenhum pacote é procurado.

Todos os dados do pacote não são confiáveis. Cada valor é verificado, uma entrada que falha é descartada com uma linha no log do pacote (`Dropped: <file>:<line>: <reason>`), e uma camada cujo arquivo está ausente ou corrompido é desligada sozinha: as outras camadas permanecem. Nada no leitor lança exceção para o radar.

Os arquivos `.cfg` seguem as mesmas regras de texto do pacote de menu de pausa (UTF-8, `key = value`, comentários com `#`, chaves duplicadas: vale a primeira), veja [como os arquivos são lidos](pause-menu-package.md#how-the-files-are-read). Os caminhos usam as mesmas [regras de caminho](pause-menu-package.md#path-rules).

<a id="manifestcfg"></a>
## `manifest.cfg`

Até 256 KiB.

| Chave | Tipo | Obrigatória | Padrão | Significado |
| --- | --- | --- | --- | --- |
| `format` | inteiro | sim | nenhum | Deve ser `1`. Caso contrário, o pacote é ignorado: `Ignored: openomsi-realmap: unknown package format '<value>'`. |
| `grid` | número | sim | nenhum | O tamanho do tile do autor, em metros, maior que 1 e no máximo 100.000. Um valor ausente ou implausível faz o pacote ser ignorado: `Ignored: openomsi-realmap: manifest grid is missing or not a plausible tile size`. |
| `name` | texto | não | nenhum | O nome do pacote. |
| `credit` | texto | não | nenhum | A linha de crédito (no máximo 400 caracteres). |
| `north` | número | não | `0` | Graus no sentido horário do "para cima" próprio do mapa (+Y) até o norte real, no máximo 3600 em valor absoluto; armazenado módulo 360. Inválido: `Dropped: manifest.cfg north: not a number of degrees; 0 used`. |
| `alignment` | `exact`, `fitted` | não | não informado | `exact`: as camadas do mundo real ficam onde estão na realidade; `fitted`: foram curvadas para se ajustar às estradas do próprio mapa. Informativo. Outro valor: `must be exact or fitted`. |
| `terrain_offset` | número | não | `0` | Metros somados à altura de cada curva de nível; no máximo 20.000 em valor absoluto. |
| `player` | `triangle` | não | a seta padrão | `triangle` desenha o jogador como um triângulo. |
| `player_free` | cor | não | o padrão | Cor do triângulo sem horário. |
| `player_line` | cor | não | o padrão | Cor do triângulo em uma linha normal. |
| `player_brt` | cor | não | o padrão | Cor do triângulo em uma linha citada em `brt_lines`. |
| `brt_lines` | lista separada por vírgulas | não | nenhum | Números de linha desenhados como o tipo BRT (no máximo 64, cada um com até 120 caracteres). |
| `tram_vehicles`, `metro_vehicles`, `rail_vehicles` | lista separada por vírgulas | não | nenhum | Partes de caminhos de arquivo de veículo (minúsculas, `/`), até 64 de 120 caracteres. Um veículo ferroviário cujo caminho contém uma delas é desenhado como bonde, trem de metrô ou trem. |
| `land`, `water`, `river`, `green`, `grass`, `forest`, `sport`, `farm`, `built`, `rail`, `rail_edge`, `border`, `contour` | cor | não | as do próprio radar | As cores do visual escuro. |
| `light_land` ... `light_contour` | cor | não | as do próprio radar | O mesmo para o visual claro (`light_` mais a chave). |

Uma cor é `#rrggbb` ou `#rrggbbaa`. Uma cor inválida é descartada: `Dropped: manifest.cfg <key>: not a #rrggbb colour; the game's own is used`. Uma posição nunca é armazenada com latitude ou longitude: um mapa pode ser curvado, encurtado ou fictício.

<a id="coordinates"></a>
## Coordenadas

As posições no pacote são metros da grade de tiles do autor: `x = tile_column * grid + position_in_tile`, `y = tile_row * grid + position_in_tile`, X para leste, Y para norte. São os mesmos números que o OMSI mostra para os objetos do mapa. O módulo posiciona o jogador na mesma grade: ele lê a tabela de tiles do jogo (`map.tiles.list`), multiplica a coluna e a linha de cada tile pelo `grid` do pacote e soma a posição dentro do tile ([o radar](../concepts/the-radar.md#positions-and-headings)). É por isso que `grid` é obrigatório: um mapa cujos tiles não têm 300 m (por exemplo 585,795 m) ficaria, de outro modo, deslocado por quilômetros longe da origem.

<a id="surfaceoms"></a>
## `surface.oms`

Um arquivo binário, little-endian, escrito pelas ferramentas do autor. O radar o lê por chunks, apenas os que estão perto do jogador.

| Parte | Conteúdo |
| --- | --- |
| Cabeçalho | magic `OMSF`; `u32` versão (deve ser `1`); `f64` origem X e Y; quatro `f32` de limites (informativos, não confiáveis); `f32` tamanho do chunk em metros (1 a 1.000.000) |
| Tabela de textos | `u32` contagem (no máximo 4.000.000), depois cada nome como `u16` comprimento e bytes UTF-8 |
| Índice de chunks | `u32` contagem (no máximo 1.000.000), depois por chunk `i32` cx, `i32` cy, `u64` deslocamento, `u32` comprimento |
| Carga do chunk | preenchimentos: `u32` contagem, cada um `u8` classe, `u32` contagem de triângulos, `f32[6 * n]`; linhas: `u32` contagem, cada uma `u8` classe, `u8` flags, `f32` largura, `u32` nome, `u32` contagem de pontos, `f32[2 * n]`; rótulos: `u32` contagem, cada um `u8` tipo, `u8` rank, `u32` nome, `f32` x, `f32` y |

As coordenadas em um chunk são deslocamentos a partir da origem; o leitor soma a origem. Um chunk `(cx, cy)` cobre `[cx * size, (cx + 1) * size)` a partir da origem em cada eixo. Um nome é um índice na tabela de textos (`u32` máximo = nenhum). As camadas do mapa inteiro (fronteiras, rótulos) são repetidas em todo chunk que tocam.

Classes:

| Classe de preenchimento | Significado | | Classe de linha | Significado | `width` | `flags` |
| ---: | --- | --- | ---: | --- | --- | --- |
| 0 | água | | 0 | rio | largura em metros | 0 |
| 1 | verde (parques) | | 1 | ferrovia | | 0 |
| 2 | área construída | | 2 | fronteira | | 0 |
| 3 | gramado | | 3 | rua (não desenhada; consultada para o nome e o rank) | | rank 0 a 5 |
| 4 | floresta | | 4 | rua com limites | limite de velocidade, km/h (0 = nenhum) | limite de veículos pesados, km/h (0 = o mesmo) |
| 5 | esporte | | 5 | curva de nível | altura acima do nível do mar, m | 0 |
| 6 | lavoura | | 6 | curva de nível mestra (a cada quinta) | altura | 0 |

Tipos de rótulo: `0` bairro, `1` terminal, `2` cidade, `3` ponto de referência, `4` água; `rank` de 0 a 255 é a importância do rótulo.

Regras do leitor:

- Classes e tipos desconhecidos são pulados, o resto do chunk é mantido.
- Uma coordenada que não é finita ou que excede 10.000.000 descarta aquele triângulo, linha ou rótulo.
- Um chunk tem no máximo 64 MiB. Um chunk que não pode ser interpretado (uma contagem que mente, uma carga truncada) é ilegível: é deixado de fora da imagem e contado, e os outros chunks são desenhados. Se o cabeçalho, a tabela de textos ou o índice forem inutilizáveis, a camada de superfície inteira é desligada: `Dropped: surface.oms: <reason>` (`too short`, `bad magic`, `implausible origin`, `implausible chunk size`, `string table larger than the file`, `chunk index larger than the file`, `header unreadable: ...`); uma versão desconhecida é `Ignored: surface.oms: unknown version <n>`. Entradas de índice fora do arquivo ou repetidas são contadas: `N chunk index entries are outside the file or repeated`.
- No máximo 256 chunks respondem a uma consulta; se a janela é maior, usam-se os chunks mais próximos do seu centro e a imagem é marcada como truncada. O leitor mantém os 24 chunks interpretados usados mais recentemente.
- As curvas de nível recebem `terrain_offset` somado à sua altura.

<a id="level-of-detail"></a>
### Nível de detalhe

O radar pede um nível de detalhe a cada consulta.

| Nível | Quando | Retornado |
| --- | --- | --- |
| Visão geral | zoom distante | preenchimentos, água, ferrovias, fronteiras, rótulos de rank 160 ou mais, marcadores; linhas simplificadas com tolerância de 6 m; sem curvas de nível |
| Normal | zoom normal | acrescenta curvas de nível mestras e rótulos de rank 64 ou mais; tolerância de 1,5 m |
| Próximo | zoom próximo | tudo: ruas com nomes e limites, todas as curvas de nível, todos os rótulos; sem simplificação |

<a id="markerscfg-and-icons"></a>
## `markers.cfg` e `icons/`

Um marcador por linha, qualquer número de linhas `marker` (a chave pode se repetir):

```text
marker = icons/terminal.png | 32 | 1200.5 | 840.0 | Central Terminal
```

| Campo | Regra |
| --- | --- |
| `icon` | um PNG diretamente em `icons/` ou abaixo dela, até 1024 x 1024 pixels e 2 MiB (alfa direto); inseguro, ausente, fora de `icons/` ou não PNG: descartado |
| `width` | largura em pixels no tamanho de interface 1, de 1 a 1024 |
| `x`, `y` | metros da grade do autor, no máximo 10.000.000 em valor absoluto |
| `name` | opcional, para as pessoas |

No máximo 20.000 marcadores; o arquivo tem no máximo 4 MiB. Linhas com menos de quatro campos, ou com qualquer campo ruim, são descartadas uma a uma: `Dropped: markers.cfg:<line>: expected icon | width | x | y | name`, `unsafe icon path: <reason>`, `icons are PNG files under icons/`, `icon <path>: <reason>` (o erro da própria imagem, por exemplo `larger than 1024x1024`), `icon width must be 1..1024 pixels`, `position is not a plausible pair of metres`. Um ícone que falha uma vez não é tentado de novo nas linhas seguintes que o nomeiam (`icon unusable (see above)`).

<a id="regionscfg"></a>
## `regions.cfg`

```text
region = Old Town | 750.0 | 750.0 | 600.0 | 120.0 | 0,0 1500,0 1500,1500 0,1500
```

Campos: `name | label x | label y | box width | box height | polygon`, em que o polígono são pares `x,y` separados por espaços (de 3 a 20.000 pontos). A região diz em qual município o jogador está (vence o menor polígono que contém o ponto quando eles se aninham); a caixa é onde o nome cabe como uma marca d'água discreta; uma largura ou altura de caixa igual a 0 significa que não há espaço legível e o nome não é desenhado. No máximo 2.000 regiões, arquivo de no máximo 4 MiB. Erros: `expected name | label x | label y | width | height | polygon`, `label position or box is not plausible`, `polygon needs 3 to 20000 points`, `polygon has a point that is not an x,y pair of plausible metres`.

<a id="lineslin"></a>
## `lines.lin`

Um arquivo de texto separado por tabulações de no máximo 32 MiB; `#` inicia uma linha de comentário. As posições são relativas ao registro `ORIGEM`. O arquivo deve começar com `RLIN`, tabulação, `1`.

| Registro | Campos (separados por tabulação) | Significado |
| --- | --- | --- |
| `RLIN` | `1` | A versão. Qualquer outra coisa: `Ignored: lines.lin: unknown version`. |
| `ORIGEM` | x, y | A origem das posições que seguem (padrão 0, 0). |
| `L` | number, group, name, [kind], [from], [to] | Uma forma de linha. `kind` é `bus` (padrão), `tram`, `metro` ou `rail`; `from` e `to` são datas `yyyymmdd`, `0` = aberto. |
| `R` | km, stops, minutes, circular (`0`/`1`), days, model, itinerary | Uma rota (um sentido) da forma; no máximo duas por forma. |
| `P` | x y x y ... | O trajeto da rota, relativo a `ORIGEM`. |
| `S` | x y x y ... | As paradas da rota. |

No máximo 5.000 formas de linha. Uma forma que é válida em uma data é mostrada nessa data (`from <= date <= to`). Registros desconhecidos são ignorados, de modo que uma versão posterior possa acrescentar registros. Erros: `line record needs number, group, name`, `unknown kind of vehicle '<kind>'`, `from/to must be yyyymmdd or 0`, `route record needs km, stops, minutes, circular, days, model, itinerary`, `a line form has at most two routes; extra ones dropped`, `coordinates are not pairs of plausible metres`.

<a id="what-each-layer-gives-the-radar"></a>
## O que cada camada dá ao radar

| Camada | Sem ela |
| --- | --- |
| `surface.oms` | sem imagem do mapa: apenas o jogador, os veículos próximos e os marcadores |
| ruas em `surface.oms` | sem nome de rua e sem limite de velocidade |
| `markers.cfg` | sem ícones |
| `regions.cfg` | sem linha de município nem marca d'água |
| `lines.lin` | sem lista de linhas |
| cores do `manifest.cfg` e `player*` | as cores e a seta do próprio radar |

Quais dessas camadas o módulo desenha na versão 0.1.0 está listado em [pacotes por mapa](../concepts/per-map-packages.md#what-the-module-uses).

Relacionado: [usando um pacote realmap](../guides/using-a-realmap-package.md) · [o radar](../concepts/the-radar.md) · [pacote de menu de pausa](pause-menu-package.md)
