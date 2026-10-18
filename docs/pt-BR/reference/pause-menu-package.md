# Formato do pacote de menu de pausa

<!-- l10n: source=reference/pause-menu-package.md source-sha256=98b05f1baef3ff485604d7126d04cffda648834faca78010ee770c57ae3fb6f9 -->
> Tradução da [página original em inglês](../../reference/pause-menu-package.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página é a referência normativa do pacote `openomsi-pausemenu`: a pasta que o autor de um mapa distribui junto ao mapa para dar ao menu de pausa a sua identidade (título, símbolos, cores, legenda, fontes e textos). Ela descreve o que `src/OpenHud.Core/Packages/PauseMenu/PauseMenuPackageReader.cs` lê, chave por chave, com cada tipo, padrão, limite e a linha de log de cada entrada rejeitada. Um guia passo a passo com um exemplo completo está em [criando um pacote de menu de pausa](../guides/making-a-pause-menu-package.md); o que acontece quando não há pacote está em [pacotes por mapa](../concepts/per-map-packages.md).

Versão do formato: `1`. Páginas relacionadas: [pacote realmap](realmap-package.md), [chaves de idioma da interface](ui-language-keys.md), [configurações](settings.md), [erros e degradação](errors-and-degradation.md), [solução de problemas](../guides/troubleshooting.md).

<a id="where-the-package-lives"></a>
## Onde o pacote fica

```text
<OMSI root>\maps\<Map folder>\
  global.cfg
  openomsi-pausemenu\             the package (fixed name, lower case)
    manifest.cfg                  required: its presence makes the package count
    identity.cfg                  optional: title, symbols, colors, legend
    symbols\                      optional: the pictures the identity names
    fonts\                        optional: font files, fonts.cfg and FONTS-LICENSE.txt
    strings\                      optional: translations of the package's own texts
    LICENSE.txt                   optional: the licence of the package's assets (for people)
    CREDITS.txt                   optional: credits (for people)
```

- O OMSI não lê esta pasta; o openHUD lê, somente leitura. Um mapa distribui, portanto, um único pacote para ambos e não perde nada.
- Os nomes acima são fixos. Um arquivo ausente significa que o recurso está desligado; nunca é um erro. Sem `manifest.cfg`, o pacote não existe para o openHUD.
- Os caminhos escritos dentro do pacote são relativos à pasta `openomsi-pausemenu`, usam barras normais e não podem sair dela (veja as [regras de caminho](#path-rules)).
- O openHUD nunca escreve no pacote.
- `LICENSE.txt` e `CREDITS.txt` não são lidos pelo leitor; são para as pessoas que recebem o pacote.

<a id="how-the-files-are-read"></a>
## Como os arquivos são lidos

Todos os arquivos `.cfg` compartilham um único leitor.

| Regra | Comportamento |
| --- | --- |
| Codificação | UTF-8, com ou sem marca de ordem de bytes. Qualquer outra (UTF-8 inválido) ou um arquivo com um caractere NUL é recusado por inteiro: `not valid UTF-8`, `contains a NUL character`. |
| Fins de linha | CRLF, LF ou CR. |
| Tamanho | Um arquivo `.cfg` maior que 65.536 bytes (64 KiB) é recusado: `larger than 65536 bytes`. No máximo 20.000 linhas são lidas e uma linha com mais de 8.192 caracteres é descartada (`line longer than 8192`). |
| Linhas | `key = value`. Linhas em branco e linhas cujo primeiro caractere não em branco é `#` são comentários. Uma linha sem `=` é descartada: `not a key = value line`. |
| Comentários na mesma linha | Um `#` que vem depois de espaço em branco encerra o valor (`stop_icon = symbols/stop.svg  # note`). Um valor que começa com `#` é uma cor, não um comentário. |
| Chaves | Sem distinção entre maiúsculas e minúsculas (`Title` é `title`). Uma chave repetida é descartada, vale a primeira: `duplicate key 'title' (the first one counts)`. |
| Caracteres de controle | Um caractere de controle (exceto tabulação) em um valor descarta a linha: `control character in the value of '<key>'`. |

Chaves desconhecidas, arquivos desconhecidos e nomes de `<kind>` ou `<mode>` desconhecidos são ignorados sem mensagem, de modo que um pacote escrito para uma versão posterior do formato ainda carrega.

<a id="path-rules"></a>
### Regras de caminho

Todo caminho no pacote (uma imagem, um arquivo de fonte) deve passar por estas verificações antes de o arquivo ser aberto. Um caminho que falha invalida apenas aquela entrada, nunca o pacote.

| Verificação | Mensagem (`unsafe path: ...`) |
| --- | --- |
| Não vazio, no máximo 240 caracteres | `empty path`, `path too long` |
| Relativo, somente barras normais | `absolute path`, `backslash (use forward slashes)` |
| Sem letra de unidade nem fluxo | `drive letter or stream` |
| Sem caracteres de controle nem `* ? " < > |` | `invalid character` |
| Sem segmento vazio, `.` ou `..` | `empty path segment`, `'.' or '..' segment` |
| Sem segmento terminado em ponto ou espaço | `segment ends with a dot or space` |
| Sem nomes de dispositivo do Windows (`CON`, `PRN`, `AUX`, `NUL`, `COM1` a `COM9`, `LPT1` a `LPT9`, com ou sem extensão) | `reserved device name` |

Depois da verificação textual, verifica-se que nenhum componente abaixo da raiz do pacote é um link (junction ou link simbólico); a própria pasta também não pode ser um link. Um arquivo que é um link ou passa por um link é recusado (`is or goes through a link`), assim como um caminho que resolve para fora do pacote (`resolves outside the package`).

<a id="manifestcfg"></a>
## `manifest.cfg`

| Chave | Tipo | Obrigatória | Padrão | Significado |
| --- | --- | --- | --- | --- |
| `format` | inteiro | sim | nenhum | Deve ser `1`. Qualquer outro valor (ou nenhum) faz o pacote inteiro ser ignorado. |
| `name` | texto | sim | nenhum | O nome do pacote, para o log. Um nome em branco faz o pacote ser ignorado. |
| `author` | texto | não | nenhum | Quem fez o pacote. |
| `credit` | texto | não | nenhum | Uma linha curta de crédito. |

`author`, `credit` e `name` são cortados em 512 caracteres. Os arquivos têm nomes fixos, então o manifesto não os lista.

| Situação | Linha de log |
| --- | --- |
| Sem pasta `openomsi-pausemenu`, ou sem `manifest.cfg` nela | nada (o pacote está ausente) |
| `manifest.cfg` maior que 64 KiB, ilegível ou um link | `Ignored: manifest.cfg: <reason>` |
| `manifest.cfg` não é texto válido | `Dropped: manifest.cfg: not valid UTF-8`, depois `Ignored: openomsi-pausemenu: manifest.cfg is unusable` |
| `format` ausente ou diferente de `1` | `Ignored: openomsi-pausemenu: unknown package format '<value>'` |
| `name` ausente ou em branco | `Ignored: openomsi-pausemenu: manifest has no name` |

<a id="identitycfg"></a>
## `identity.cfg`

Todas as chaves são opcionais. Uma chave omitida assume o seu padrão; uma chave que não pode ser usada é descartada com uma linha de log e o seu padrão é usado. Se o `identity.cfg` traz o seu próprio `format` e ele não é `1`, o arquivo inteiro é pulado: `Dropped: identity.cfg: unknown identity format '<value>'; defaults used`. As chaves são escritas em minúsculas no log, com o número da linha: `identity.cfg:7 symbol.green`.

<a id="texts"></a>
### Textos

| Chave | Tipo | Padrão | Significado |
| --- | --- | --- | --- |
| `title` | texto, de 1 a 512 caracteres | o nome da pasta do mapa | O nome mostrado no cabeçalho do menu. |
| `title.<LANG>` | texto | `title` | O mesmo em um idioma. `<LANG>` é o código de idioma de três letras do OMSI (`ENG`, `DEU`, `ESP`, `PTB` ...), em qualquer caixa. |
| `legend.<kind>` | texto | o texto embutido desse tipo ([chaves de idioma da interface](ui-language-keys.md)) | O texto da legenda de um tipo de marcador. `<kind>` tem de 1 a 32 caracteres de `a-z 0-9 _ -`. |
| `legend.<kind>.<LANG>` | texto | `legend.<kind>` | O mesmo em um idioma. |

Erros: `malformed text key` (uma chave que não é uma das quatro formas, ou um código de idioma que não tem três letras); `empty or longer than 512 characters`.

Os tipos de legenda embutidos são `station`, `terminal`, `garage`, `stop` e `landmark` (chaves `UI_LEGEND_*` em [chaves de idioma da interface](ui-language-keys.md)). O menu lista esses cinco tipos e todo outro tipo que o pacote adicionar com uma chave `legend.<kind>`; a amostra de cor de uma entrada tem a cor de `color.group.<kind>` quando o pacote a define, e a cor de destaque caso contrário.

<a id="symbols-and-icons"></a>
### Símbolos e ícones

Um valor de símbolo é um destes:

- `@omsi2`, `@simple` ou `@none`: uma marca que o próprio openHUD desenha, sem arquivo. `@omsi2` é a marca simplificada do OMSI 2, `@simple` um ponto simples, `@none` nada. Qualquer outro `@name` é descartado: `unknown internal symbol '@name'`.
- `symbols/<file>.png` ou `symbols/<file>.svg`: uma imagem diretamente dentro da pasta `symbols` (sem subpastas). Veja [imagens](#pictures).

| Chave | Padrão | Significado |
| --- | --- | --- |
| `symbol` | nenhum | O símbolo do mapa no cabeçalho do menu. |
| `symbol.<theme>` | `symbol` | O símbolo para um tema (`green`, `dark` ...). `<theme>` tem de 1 a 32 caracteres de `a-z 0-9 _ -`; qualquer outra coisa é descartada: `invalid theme name`. |
| `symbol.openomsi` | `symbol` | O símbolo para o leiaute do openHUD. |
| `stop_icon` | `@simple` | O ícone de uma parada no mapa. |
| `marker_icon.<kind>` | o ícone embutido | O ícone de um tipo de marcador (`station`, `terminal` ...). Um tipo inválido: `invalid marker kind`. |
| `mode_icon.<mode>` | o ícone embutido | O ícone de uma aba de modo de veículo. `<mode>` é `bus`, `tram`, `metro` ou `rail`; outro nome é ignorado sem mensagem. |
| `theme` | `blue` | O nome do tema padrão (`a-z 0-9 _ -`, até 32). Um nome inválido: `invalid theme name; default used`. |
| `layout` | `classic` | `classic` ou `openhud`. Outro valor: `layout must be classic or openhud; default used`. |

<a id="colors"></a>
### Cores

Uma cor é `#rrggbb` ou `#rrggbbaa` (alfa direto), em qualquer caixa. Um valor inválido é descartado: `not a #rrggbb colour; default used`.

| Chave | Padrão | Significado |
| --- | --- | --- |
| `color.line` | a do tema | A cor das linhas do mapa. |
| `color.brt` | a do tema | A cor do grupo de ônibus de trânsito rápido (BRT). |
| `color.accent` | a do tema | A cor de destaque do menu. |
| `color.group.<id>` | a do tema | A cor de um grupo de linhas. `<id>` tem de 1 a 32 caracteres de `a-z 0-9 _ -`; qualquer outra coisa: `invalid group id`. |

<a id="pictures"></a>
### Imagens

Uma imagem é validada apenas pelo seu cabeçalho e pelo seu tamanho; ela é decodificada depois, na camada de desenho. Uma imagem que falha é descartada, com o motivo, e a chave pela qual ela foi nomeada mantém o seu padrão.

| Formato | Limites e verificações |
| --- | --- |
| PNG | No máximo 2 MiB; no máximo 2048 x 2048 pixels; uma assinatura e um chunk de cabeçalho válidos (a soma de verificação do cabeçalho é conferida), uma combinação válida de tipo de cor e profundidade de bits, métodos de compressão, filtro e entrelaçamento padrão. Mensagens: `larger than 2 MiB`, `too short to be a PNG`, `not a PNG (signature)`, `PNG header chunk missing`, `PNG header checksum mismatch`, `PNG is 3000x10, the limit is 2048x2048`, `PNG colour type/bit depth combination is invalid`, `PNG compression/filter/interlace method unknown`. |
| SVG | No máximo 256 KiB, UTF-8 válido, um elemento raiz `<svg` (só uma declaração XML e comentários podem vir antes dele, então um arquivo que começa com um `DOCTYPE` resulta em `no <svg> root element`). O subconjunto estático: sem `<script`, `<foreignobject`, `<!doctype`, `<!entity`, `<iframe`, `<image`, `<embed`, `<object`, `javascript:`, `@import` ou `<?xml-stylesheet`; todo `href` e todo `url(...)` deve apontar para um `#id` interno; sem atributo de evento `on...=`. Mensagem: `contains '<script', which the static subset does not allow`, `external reference (href)`, `external reference (url())`, `event handler attribute`, `no <svg> root element`. |
| Qualquer outra coisa | `only PNG and SVG pictures are read` |

Outras mensagens para um valor de símbolo: `pictures live directly in symbols/` (o caminho não é `symbols/<file>`), `file not found in symbols/ (or beyond the folder limit)`, `unsafe path: <reason>`.

**Limite da pasta.** `symbols/` admite os seus primeiros 64 arquivos e 16 MiB, em ordem de nome (com distinção de caixa, ordinal). Cada arquivo seguinte é descartado com `Dropped: symbols/<file>: beyond the folder limit of 64 files / 16 MiB` e não pode ser usado. Arquivos em subpastas de `symbols` não são vistos.

**Pares de alta resolução.** Para uma imagem `symbols/name.png`, o leitor procura também `symbols/name@2x.png` (a mesma extensão) e, se ele for válido, o mantém junto à imagem para uma escala de interface alta. Um par que falha é descartado sob o seu próprio nome (`symbols/name@2x.png`) e a imagem principal permanece.

<a id="the-two-marks"></a>
### As duas marcas

| Arquivo | Finalidade | Alternativa quando ausente ou inválido |
| --- | --- | --- |
| `symbols/omsi2-logo.png` | O logotipo do OMSI 2. O openHUD não inclui nenhuma cópia do logotipo de marca registrada. | o `symbol` do pacote, depois a marca genérica `@omsi2`, depois o texto "OMSI 2" |
| `symbols/openomsi.png` | A marca do openOMSI. | nenhuma |

Ambas seguem as regras de imagem (somente PNG para esses nomes). O autor do pacote é responsável pelo direito de usar o logotipo no pacote ([fontes, símbolos e licenciamento](../concepts/fonts-symbols-and-licensing.md)).

<a id="fonts"></a>
## `fonts/`

O openHUD não inclui nenhum arquivo de fonte. Um pacote pode levar as fontes de que o seu visual precisa; sem elas, a camada de desenho usa a fonte de interface do sistema.

| Arquivo | Obrigatório | Significado |
| --- | --- | --- |
| `fonts/*.ttf`, `fonts/*.otf` | não | Os arquivos de fonte, diretamente em `fonts` (sem subpastas). Somente TrueType ou OpenType estáticos. No máximo 12 arquivos, em ordem de nome; os seguintes: `beyond the limit of 12 font files`. |
| `fonts/FONTS-LICENSE.txt` | **sim, quando houver qualquer arquivo de fonte** | A licença de cada arquivo de fonte. Pelo menos um byte. O leitor não o interpreta. |
| `fonts/fonts.cfg` | **sim, quando houver qualquer arquivo de fonte** | Qual arquivo serve a qual papel. |

Sem `FONTS-LICENSE.txt` (ou com um vazio) ou sem `fonts.cfg`, nenhuma fonte do pacote é usada e uma linha diz por quê, por exemplo `Dropped: fonts/: fonts/FONTS-LICENSE.txt is missing or empty; the system font is used`.

`fonts/fonts.cfg`:

| Chave | Valor | Peso padrão | Significado |
| --- | --- | ---: | --- |
| `format` | `1` | | Opcional; outro valor recusa as fontes: `unknown fonts format '<value>'`. |
| `regular` | `<file> [ \| <weight> [ \| <note> ] ]` | 400 | Texto do corpo. |
| `medium` | igual | 500 | Abas e legendas. |
| `bold` | igual | 700 | Títulos, botões. |
| `black` | igual | 900 | Números em destaque. |
| `condensed` | igual | 700 | Texto estreito (dicas, horários, códigos). |

- `<file>` é um nome de arquivo diretamente em `fonts` (`unsafe path: ...`, `font files live directly in fonts/`, `font file not found (or beyond the limit)`).
- `<weight>` é um número inteiro de 100 a 900 (`weight must be 100..900; the role's default is used`). Ele decide qual face substitui um papel que você omitiu; não muda o arquivo.
- `<note>` é texto livre para as pessoas.
- Um arquivo de fonte deve ser um arquivo TrueType ou OpenType estático de no máximo 4 MiB com um diretório de tabelas válido: `larger than 4 MiB`, `not a TrueType/OpenType font (collections and web fonts are not read)`, `variable font (instance it offline)`, `table directory truncated`, `no head table or outlines`.
- Um papel omitido assume o peso mais próximo entre os papéis presentes (um empate vai para o mais leve). Nenhum papel utilizável: `no usable font in fonts/fonts.cfg; the system font is used`.
- Nomes de papel desconhecidos são ignorados.

As fontes são registradas de forma privada para a sessão pela camada de desenho (nunca instaladas no Windows). Um caractere que a fonte não tem é desenhado com a fonte de interface do sistema, nunca como `?`.

<a id="strings"></a>
## `strings/`

`strings/<LANG>.cfg` (`ENG.cfg`, `PTB.cfg`, `DEU.cfg` ...; `<LANG>` tem exatamente três letras, em qualquer caixa) contém os textos próprios do pacote para um idioma, como `key = value`.

| Regra | Valor |
| --- | --- |
| Arquivos | no máximo 32; os seguintes: `beyond the limit of 32 files` |
| Tamanho | 64 KiB por arquivo |
| Chaves | `a-z 0-9 _ . -`, de 1 a 64 caracteres, convertidas para minúsculas: `malformed key` |
| Valores | de 1 a 512 caracteres: `empty or longer than 512` |
| Textos por arquivo | no máximo 512: `more than 512 strings; the rest is not read` |
| `format` | aceito e ignorado |

O menu pede a chave `title` e `legend.<kind>`. Para cada uma, o texto vem de, em ordem:

1. `strings/<LANG>.cfg` do idioma em uso, chave `title` ou `legend.<kind>`;
2. `identity.cfg`, `title.<LANG>` ou `legend.<kind>.<LANG>`;
3. `identity.cfg`, `title` ou `legend.<kind>`;
4. o texto embutido do openHUD (para um título: o nome da pasta do mapa).

`<LANG>` é o código da configuração de idioma quando `language` é um código explícito; caso contrário, é o código de idioma do próprio OMSI ([internacionalização](../concepts/internationalization.md)). Outras chaves de um arquivo de `strings` são lidas e mantidas.

<a id="limits-at-a-glance"></a>
## Limites de relance

| O quê | Limite |
| --- | --- |
| Arquivo `.cfg` | 64 KiB, 20.000 linhas, 8.192 caracteres por linha |
| Qualquer valor de texto | 512 caracteres |
| `symbols/` | 64 arquivos, 16 MiB; uma imagem 2 MiB e 2048 x 2048 (PNG), 256 KiB (SVG) |
| `fonts/` | 12 arquivos de fonte, 4 MiB cada |
| `strings/` | 32 arquivos, 512 textos cada |
| O log do pacote | 200 linhas; além disso `N more entries not listed` |

<a id="template"></a>
## Modelo

Copie estes arquivos para `openomsi-pausemenu` e mude os valores. Eles nomeiam imagens e fontes que você fornece: os nomes de arquivo são os do [exemplo completo](../guides/making-a-pause-menu-package.md#the-binary-files) e cada linha é aceita pelo leitor. Mantenha apenas as chaves de que precisa.

<a id="templatemanifestcfg"></a>
#### `template/manifest.cfg`

```ini
# openomsi-pausemenu/manifest.cfg
format = 1
name = Example City pause menu
author = Example Maps Studio
credit = Map by Example Maps Studio
```

<a id="templateidentitycfg"></a>
#### `template/identity.cfg`

```ini
# openomsi-pausemenu/identity.cfg - every key is optional
format = 1

# Header
title = Example City
title.ENG = Example City
title.PTB = Cidade Exemplo
title.DEU = Beispielstadt

# Symbols: a picture under symbols/, or @omsi2, @simple, @none
symbol = symbols/map-symbol.png
symbol.green = symbols/map-symbol-green.png
symbol.openomsi = symbols/map-symbol-wide.png

# Look
theme = green
layout = classic
color.line = #3a7bd5
color.brt = #1e7b34
color.accent = #24589a
color.group.express = #c0392b

# Map icons
stop_icon = symbols/stop.svg
marker_icon.station = symbols/station.png
marker_icon.terminal = symbols/terminal.png
marker_icon.garage = @simple
mode_icon.bus = symbols/mode-bus.svg
mode_icon.tram = symbols/mode-tram.svg

# Legend texts
legend.station = Station
legend.station.PTB = Estação
legend.station.DEU = Bahnhof
legend.terminal = Terminal
legend.garage = Depot
```

<a id="templatefontsfontscfg"></a>
#### `template/fonts/fonts.cfg`

```ini
# openomsi-pausemenu/fonts/fonts.cfg
# role = file | weight | note
format = 1
regular = ExampleSans-Regular.ttf | 400
medium = ExampleSans-Regular.ttf | 500 | tabs and captions
bold = ExampleSans-Bold.ttf | 700
black = ExampleSans-Bold.ttf | 900
condensed = ExampleSans-Regular.ttf | 400 | narrow text
```

<a id="templatestringsptbcfg"></a>
#### `template/strings/PTB.cfg`

```ini
# openomsi-pausemenu/strings/PTB.cfg
title = Cidade Exemplo
legend.terminal = Terminal
```

Junto a eles vão `fonts/FONTS-LICENSE.txt` (a licença dos seus arquivos de fonte; obrigatório sempre que `fonts` contiver uma fonte), as imagens nomeadas acima e, opcionalmente, `LICENSE.txt` e `CREDITS.txt`.

Relacionado: [criando um pacote de menu de pausa](../guides/making-a-pause-menu-package.md) · [pacotes por mapa](../concepts/per-map-packages.md) · [pacote realmap](realmap-package.md) · [chaves de idioma da interface](ui-language-keys.md) · [erros e degradação](errors-and-degradation.md)
