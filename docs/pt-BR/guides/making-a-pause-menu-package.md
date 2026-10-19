# Criando um pacote de menu de pausa para o seu mapa

<!-- l10n: source=guides/making-a-pause-menu-package.md source-sha256=99c4a0b1ef56dae2eb4d55cedbdf9fe36802a1b03010855b4e42951498a43136 -->
> Tradução da [página original em inglês](../../guides/making-a-pause-menu-package.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Este guia leva o autor de um mapa de uma pasta vazia até um pacote `openomsi-pausemenu` pronto, passo a passo, com um exemplo completo em que todo arquivo de texto é mostrado aqui por inteiro. O exemplo é verificado pela suíte de testes: os arquivos abaixo são lidos com o mesmo leitor que o módulo usa e não devem gerar nenhuma entrada rejeitada ([cobertura de testes](../status/test-coverage.md)). O formato em si, chave por chave, está na [referência do pacote de menu de pausa](../reference/pause-menu-package.md).

Um pacote é opcional. Sem ele, o menu de pausa do openHUD é o menu genérico do OMSI 2: o título é o nome da pasta do mapa, não há símbolo do mapa, a fonte é a fonte de interface do sistema e as cores são o tema azul padrão ([pacotes por mapa](../concepts/per-map-packages.md)). Um pacote muda esse menu para que ele tenha a cara do seu mapa.

Páginas relacionadas: [referência: pacote de menu de pausa](../reference/pause-menu-package.md), [fontes, símbolos e licenciamento](../concepts/fonts-symbols-and-licensing.md), [internacionalização](../concepts/internationalization.md), [solução de problemas](troubleshooting.md), [pacotes realmap](using-a-realmap-package.md).

O exemplo é para um mapa inventado, "Example City", com uma linha inventada, "Line 12". Use os seus próprios nomes.

<a id="step-1-create-the-folder"></a>
## Passo 1. Crie a pasta

Dentro da pasta do seu mapa, crie `openomsi-pausemenu`, exatamente com esse nome, em minúsculas:

```text
<OMSI root>\maps\Example City\
  global.cfg
  openomsi-pausemenu\
```

Se você distribui o mapa como um mod, coloque a pasta dentro da pasta do mapa no arquivo compactado, ao lado de `global.cfg`, para que ela seja instalada junto com o mapa. O próprio OMSI ignora a pasta; nada muda no jogo.

<a id="step-2-write-manifestcfg"></a>
## Passo 2. Escreva o `manifest.cfg`

Este é o único arquivo obrigatório. Sem ele, o pacote não existe.

<a id="openomsi-pausemenumanifestcfg"></a>
#### `openomsi-pausemenu/manifest.cfg`

```ini
# Pause-menu package of the Example City map
format = 1
name = Example City pause menu
author = Example Maps Studio
credit = Map by Example Maps Studio. Symbols by Example Maps Studio.
```

`format = 1` e um `name` não vazio são obrigatórios. Todo o resto é opcional.

<a id="step-3-add-the-symbols"></a>
## Passo 3. Adicione os símbolos

Crie `symbols` e coloque suas imagens diretamente nela (sem subpastas). PNG e SVG são lidos. O exemplo usa estes arquivos:

<a id="the-binary-files"></a>
### Os arquivos binários

| Arquivo | Tipo | Tamanho |
| --- | --- | --- |
| `symbols/map-symbol.png` | PNG | 256 x 256 |
| `symbols/map-symbol@2x.png` | PNG | 512 x 512 |
| `symbols/map-symbol-green.png` | PNG | 256 x 256 |
| `symbols/map-symbol-wide.png` | PNG | 512 x 128 |
| `symbols/station.png` | PNG | 64 x 64 |
| `symbols/terminal.png` | PNG | 64 x 64 |
| `symbols/omsi2-logo.png` | PNG | 300 x 120 |
| `symbols/openomsi.png` | PNG | 200 x 60 |
| `fonts/ExampleSans-Regular.ttf` | TrueType | estática |
| `fonts/ExampleSans-Bold.ttf` | TrueType | estática |

Você mesmo cria essas imagens; os tamanhos são os do exemplo. Regras para ter em mente:

- Um PNG tem no máximo 2048 x 2048 pixels e 2 MiB. Use RGBA com alfa direto (não pré-multiplicado).
- Faça o símbolo quadrado ou dê a ele a proporção que quiser; o openHUD mantém a proporção e encaixa a imagem no seu espaço, nunca a estica.
- `map-symbol@2x.png` é o par opcional de alta resolução de `map-symbol.png`: mesmo nome mais `@2x`, mesma extensão, usado quando a escala da interface é alta.
- `omsi2-logo.png` e `openomsi.png` são opcionais. O openHUD não inclui nenhuma cópia do logotipo de marca registrada do OMSI 2; se você quiser que ele apareça no menu, você o fornece aqui e é responsável por ter o direito de fazê-lo ([licenciamento](../concepts/fonts-symbols-and-licensing.md)).
- As fontes são arquivos reais que você obtém com uma licença que permita redistribuição dentro do seu pacote. Nunca copie uma fonte comercial para um pacote.

Os cinco arquivos SVG abaixo são estáticos e desenham apenas formas, caminhos e cores lisas. O openHUD os transforma nas formas da cena, então permanecem nítidos em qualquer tamanho e dispensam um PNG. Um SVG não pode conter script, folha de estilo, `DOCTYPE`, imagem embutida nem qualquer referência externa; muitos editores adicionam um `DOCTYPE` ao exportar, então remova-o. O que é desenhado e o que é ignorado está em [o subconjunto de SVG](../reference/pause-menu-package.md#the-svg-subset). Os dois últimos arquivos mostram mais dele: curvas, arcos, comandos relativos, uma transformação de grupo e um atributo `style`.

<a id="openomsi-pausemenusymbolsstopsvg"></a>
#### `openomsi-pausemenu/symbols/stop.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <circle cx="12" cy="12" r="10" fill="#ffffff"/>
  <circle cx="12" cy="12" r="5" fill="#24589a"/>
</svg>
```

<a id="openomsi-pausemenusymbolsmode-bussvg"></a>
#### `openomsi-pausemenu/symbols/mode-bus.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <rect x="3" y="4" width="18" height="14" rx="3" fill="#ffffff"/>
  <rect x="5" y="7" width="14" height="5" fill="#24589a"/>
  <circle cx="8" cy="19" r="2" fill="#ffffff"/>
  <circle cx="16" cy="19" r="2" fill="#ffffff"/>
</svg>
```

<a id="openomsi-pausemenusymbolsmode-tramsvg"></a>
#### `openomsi-pausemenu/symbols/mode-tram.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <rect x="5" y="3" width="14" height="15" rx="3" fill="#ffffff"/>
  <rect x="7" y="6" width="10" height="5" fill="#24589a"/>
  <path d="M8 21 L12 18 L16 21" fill="none" stroke="#ffffff" stroke-width="2"/>
</svg>
```

<a id="openomsi-pausemenusymbolsmode-metrosvg"></a>
#### `openomsi-pausemenu/symbols/mode-metro.svg`

```xml
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <path d="M12 3 C7 3 4 6 4 10 V17 H6 L4 20 H8 L9.5 18 H14.5 L16 20 H20 L18 17 H20 V10 C20 6 17 3 12 3 Z" fill="#ffffff"/>
  <rect x="6" y="7" width="12" height="5" rx="1" fill="#24589a"/>
  <circle cx="8.5" cy="15" r="1" fill="#24589a"/>
  <circle cx="15.5" cy="15" r="1" fill="#24589a"/>
</svg>
```

<a id="openomsi-pausemenusymbolsmode-railsvg"></a>
#### `openomsi-pausemenu/symbols/mode-rail.svg`

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!-- A rail icon: arcs, relative commands, a group transform and a style attribute -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">
  <g transform="translate(12 12) scale(0.9)" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round">
    <path d="M-9 8 h18 M-6 8 v-5 a6 6 0 0 1 12 0 v5"/>
    <path d="M-9 12 q9 -4 18 0" style="stroke:#24589a"/>
  </g>
</svg>
```

<a id="step-4-write-identitycfg"></a>
## Passo 4. Escreva o `identity.cfg`

A identidade define o título, os símbolos, as cores e a legenda. Todas as chaves são opcionais; uma chave que você omite assume o padrão genérico.

<a id="openomsi-pausemenuidentitycfg"></a>
#### `openomsi-pausemenu/identity.cfg`

```ini
# Identity of the Example City pause menu
format = 1

# Header: the title in OMSI's language codes (ENG, PTB, DEU, ...)
title = Example City
title.ENG = Example City
title.PTB = Cidade Exemplo
title.DEU = Beispielstadt

# Symbols: pictures under symbols/ or internal marks (@omsi2, @simple, @none)
symbol = symbols/map-symbol.png
symbol.green = symbols/map-symbol-green.png
symbol.openomsi = symbols/map-symbol-wide.png

# Look
theme = green
layout = classic
color.line = #3a7bd5          # the map's lines
color.brt = #1e7b34           # the bus-rapid-transit group
color.accent = #24589a
color.group.express = #c0392b # Line 12 and the other express lines

# Map icons
stop_icon = symbols/stop.svg
marker_icon.station = symbols/station.png
marker_icon.terminal = symbols/terminal.png
marker_icon.garage = @simple
mode_icon.bus = symbols/mode-bus.svg
mode_icon.tram = symbols/mode-tram.svg
mode_icon.metro = symbols/mode-metro.svg
mode_icon.rail = symbols/mode-rail.svg

# Legend texts, with the language variants
legend.station = Station
legend.station.PTB = Estação
legend.station.DEU = Bahnhof
legend.terminal = Terminal
legend.garage = Depot
```

Coisas que vale a pena saber:

- Comentários começam com `#` no início de uma linha, ou depois de um espaço no fim de um valor (`color.line = #3a7bd5   # note`). Um valor que começa com `#` é uma cor.
- O sufixo de idioma é o código de três letras do OMSI: `title.ENG`, `title.PTB`, `title.DEU`, `title.ESP`. O menu usa a variante do idioma em uso e, na falta dela, `title` ([internacionalização](../concepts/internationalization.md)).
- O caminho de uma imagem sempre começa com `symbols/`, usa barras normais e nomeia um arquivo diretamente nessa pasta.
- `@simple`, `@omsi2` e `@none` são marcas que o próprio openHUD desenha: nenhum arquivo é necessário.

<a id="step-5-add-fonts-with-their-licence-optional"></a>
## Passo 5. Adicione fontes, com a licença delas (opcional)

Se o visual do seu mapa precisa de uma tipografia, coloque arquivos `.ttf` ou `.otf` estáticos em `fonts`, diga em `fonts.cfg` qual arquivo serve a qual papel e inclua a licença de cada fonte em `FONTS-LICENSE.txt`. Sem um arquivo de licença, as fontes não são usadas de forma alguma.

<a id="openomsi-pausemenufontsfontscfg"></a>
#### `openomsi-pausemenu/fonts/fonts.cfg`

```ini
# role = file | weight | note
format = 1
regular = ExampleSans-Regular.ttf | 400
medium = ExampleSans-Regular.ttf | 500 | tabs and captions
bold = ExampleSans-Bold.ttf | 700
black = ExampleSans-Bold.ttf | 900
condensed = ExampleSans-Regular.ttf | 400 | narrow text
```

<a id="openomsi-pausemenufontsfonts-licensetxt"></a>
#### `openomsi-pausemenu/fonts/FONTS-LICENSE.txt`

```text
ExampleSans-Regular.ttf and ExampleSans-Bold.ttf
Copyright (c) Example Type Foundry.
Distributed under the licence stated by the foundry, which allows
redistribution inside this package. Replace this text with the full
licence text of every font file you include.
```

Os cinco papéis são `regular`, `medium`, `bold`, `black` e `condensed`. Um papel que você omite assume a face de peso mais próximo entre as que você listou. Fontes variáveis e coleções de fontes (`.ttc`) não são lidas: crie instâncias estáticas antes, com uma ferramenta de fontes.

<a id="step-6-add-per-language-texts-optional"></a>
## Passo 6. Adicione textos por idioma (opcional)

Para idiomas em que um arquivo por idioma é mais prático do que chaves com sufixo, adicione `strings/<LANG>.cfg`. O menu pede `title` e `legend.<kind>`, e um arquivo de `strings` prevalece sobre o `identity.cfg` no idioma que ele nomeia.

<a id="openomsi-pausemenustringsengcfg"></a>
#### `openomsi-pausemenu/strings/ENG.cfg`

```ini
format = 1
title = Example City
legend.terminal = Terminal
```

<a id="openomsi-pausemenustringsptbcfg"></a>
#### `openomsi-pausemenu/strings/PTB.cfg`

```ini
format = 1
title = Cidade Exemplo
legend.terminal = Terminal da Linha 12
```

<a id="openomsi-pausemenustringsdeucfg"></a>
#### `openomsi-pausemenu/strings/DEU.cfg`

```ini
format = 1
title = Beispielstadt
legend.terminal = Endhaltestelle der Linie 12
```

<a id="step-7-add-licence-and-credits-for-people-optional"></a>
## Passo 7. Adicione licença e créditos para as pessoas (opcional)

O leitor nunca abre estes dois arquivos; eles são para quem recebe o pacote.

<a id="openomsi-pausemenulicensetxt"></a>
#### `openomsi-pausemenu/LICENSE.txt`

```text
The pictures in symbols/ are (c) Example Maps Studio and may be
redistributed with the Example City map. The fonts have their own
licence in fonts/FONTS-LICENSE.txt.
```

<a id="openomsi-pausemenucreditstxt"></a>
#### `openomsi-pausemenu/CREDITS.txt`

```text
Example City map: Example Maps Studio
Symbols: Example Maps Studio
```

<a id="the-finished-package"></a>
## O pacote pronto

```text
openomsi-pausemenu\
  manifest.cfg
  identity.cfg
  LICENSE.txt
  CREDITS.txt
  symbols\
    map-symbol.png
    map-symbol@2x.png
    map-symbol-green.png
    map-symbol-wide.png
    station.png
    terminal.png
    omsi2-logo.png
    openomsi.png
    stop.svg
    mode-bus.svg
    mode-tram.svg
    mode-metro.svg
    mode-rail.svg
  fonts\
    fonts.cfg
    FONTS-LICENSE.txt
    ExampleSans-Regular.ttf
    ExampleSans-Bold.ttf
  strings\
    ENG.cfg
    PTB.cfg
    DEU.cfg
```

<a id="step-8-check-it"></a>
## Passo 8. Verifique

Não há um programa verificador separado: o próprio leitor do módulo é a verificação, e cada problema que ele encontra vira uma linha do log do pacote ([solução de problemas](troubleshooting.md#reading-the-modules-events-and-notices)). Antes de publicar, percorra esta lista, que é o que o leitor verifica:

1. A pasta se chama `openomsi-pausemenu` e `manifest.cfg` existe, com `format = 1` e um `name`.
2. Todo arquivo `.cfg` é texto UTF-8 de no máximo 64 KiB, com linhas `key = value`.
3. Nenhuma chave aparece duas vezes em um arquivo (a segunda é descartada).
4. Todo caminho de imagem começa com `symbols/`, usa `/` e existe diretamente em `symbols`.
5. Toda imagem é um PNG de no máximo 2048 x 2048 e 2 MiB, ou um SVG de no máximo 256 KiB sem script, folha de estilo, `DOCTYPE`, imagem embutida nem referência externa, e dentro do seu orçamento de formas (160 para o símbolo e as duas marcas, 32 para um ícone).
6. `symbols` contém no máximo 64 arquivos e 16 MiB.
7. Toda cor é `#rrggbb` ou `#rrggbbaa`.
8. Se `fonts` contém um arquivo de fonte: `fonts.cfg` e um `FONTS-LICENSE.txt` não vazio existem; toda fonte é um arquivo TrueType ou OpenType estático de no máximo 4 MiB; no máximo 12 arquivos.
9. Todo código de idioma tem três letras.
10. Nada no pacote é um link (junction ou link simbólico) para outro lugar.

Depois inicie uma sessão com o módulo ([primeira sessão](../getting-started/first-session.md)) e leia o log: um pacote limpo não imprime nenhuma linha sobre si mesmo.

<a id="what-the-log-says-for-each-rejected-entry"></a>
## O que o log diz para cada entrada rejeitada

Uma entrada ruim é descartada sozinha e o resto do pacote permanece; a linha do log começa com `Dropped` (uma entrada foi deixada de fora), `Ignored` (o pacote inteiro ou um recurso inteiro não foi usado) ou `Info` (uma nota, como o log ser cortado em 200 linhas). A linha mostra o arquivo e o número da linha, depois o motivo.

| Você escreveu | O log diz |
| --- | --- |
| `symbol.green = symbols/missing.png` | `Dropped: identity.cfg:<line> symbol.green: file not found in symbols/ (or beyond the folder limit)` |
| `color.accent = blue` | `Dropped: identity.cfg:<line> color.accent: not a #rrggbb colour; default used` |
| `symbol = ../manifest.cfg` | `Dropped: identity.cfg:<line> symbol: unsafe path: '.' or '..' segment` |
| `symbol = C:/logo.png` | `Dropped: identity.cfg:<line> symbol: unsafe path: drive letter or stream` |
| `symbol = logo.png` | `Dropped: identity.cfg:<line> symbol: pictures live directly in symbols/` |
| `symbol = @brand` | `Dropped: identity.cfg:<line> symbol: unknown internal symbol '@brand'` |
| `title.EN = Hello` (um código de idioma de duas letras) | `Dropped: identity.cfg:<line> title.en: malformed text key` |
| `layout = fancy` | `Dropped: identity.cfg:<line> layout: layout must be classic or openhud; default used` |
| `title` duas vezes no arquivo | `Dropped: identity.cfg:<line>: duplicate key 'title' (the first one counts)` |
| uma linha sem `=` | `Dropped: identity.cfg:<line>: not a key = value line` |
| um SVG que começa com um `DOCTYPE` (só uma declaração XML e comentários podem vir antes de `<svg`) | `Dropped: identity.cfg:<line> stop_icon: a DOCTYPE declaration is refused (it can define entities and load external files); remove the <!DOCTYPE ...> line` |
| um PNG de 3000 x 10 pixels | `Dropped: identity.cfg:<line> symbol.green: PNG is 3000x10, the limit is 2048x2048` |
| um arquivo chamado `.png` que não é um PNG | `Dropped: identity.cfg:<line> symbol.green: not a PNG (signature)` |
| fontes sem `FONTS-LICENSE.txt` | `Dropped: fonts/: fonts/FONTS-LICENSE.txt is missing or empty; the system font is used` |
| um peso de fonte de 1000 | `Dropped: fonts/fonts.cfg:<line> bold: weight must be 100..900; the role's default is used` |
| uma fonte variável | `Dropped: fonts/fonts.cfg:<line> bold: variable font (instance it offline)` |
| um arquivo de fonte que não está em `fonts` | `Dropped: fonts/fonts.cfg:<line> bold: font file not found (or beyond the limit)` |
| uma chave de `strings` com um espaço | `Dropped: strings/ENG.cfg:<line>: malformed key` |
| `format = 2` em `manifest.cfg` | `Ignored: openomsi-pausemenu: unknown package format '2'` |
| nenhum `name` em `manifest.cfg` | `Ignored: openomsi-pausemenu: manifest has no name` |

Os motivos que não estão listados aqui estão na [referência](../reference/pause-menu-package.md): cada chave e cada arquivo tem suas mensagens ao lado.

Relacionado: [referência do pacote de menu de pausa](../reference/pause-menu-package.md) · [solução de problemas](troubleshooting.md) · [usando um pacote realmap](using-a-realmap-package.md)
