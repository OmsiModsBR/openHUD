# Pacotes por mapa

<!-- l10n: source=concepts/per-map-packages.md source-sha256=1876e3070acf37d54f3a638f4035ed66757ede94a74b1d3c7fa8420c3182a3c3 -->
> Tradução da [página original em inglês](../../concepts/per-map-packages.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD funciona em qualquer mapa, e pode ficar com a cara do mapa quando este traz dois pacotes opcionais: um pacote **realmap** (uma imagem pré-construída do mapa para o radar e a página de mapa) e um pacote de **menu de pausa** (o título, o símbolo, as cores, a legenda e as fontes do menu de pausa). Esta página explica o que eles são, onde ficam, quem os lê, o que o módulo faz com cada parte e o que acontece quando estão ausentes, parcialmente errados ou hostis. Os formatos estão na [referência do pacote realmap](../reference/realmap-package.md) e na [referência do pacote de menu de pausa](../reference/pause-menu-package.md); como fazê-los está nos guias de [pacotes de menu de pausa](../guides/making-a-pause-menu-package.md) e de [pacotes realmap](../guides/using-a-realmap-package.md).

Páginas relacionadas: [o radar](the-radar.md), [o menu de pausa](the-pause-menu.md), [fontes, símbolos e licenciamento](fonts-symbols-and-licensing.md), [segurança](safety.md), [solução de problemas](../guides/troubleshooting.md).

<a id="the-two-packages"></a>
## Os dois pacotes

| | `openomsi-realmap` | `openomsi-pausemenu` |
| --- | --- | --- |
| O que carrega | Água, parques, áreas construídas, relevo, ruas nomeadas com seus limites de velocidade, nomes de lugares, ícones, municípios, linhas | O título do menu de pausa, símbolos, cores, textos da legenda, fontes e textos por idioma |
| Arquivo obrigatório | `manifest.cfg` | `manifest.cfg` |
| Feito por | As ferramentas do autor do mapa | O autor do mapa, à mão |
| Usado para | O radar e a página Mapa | O menu de pausa e as cores do radar |

Ambos ficam dentro da pasta do próprio mapa, ao lado do seu `global.cfg`:

```text
<OMSI root>\maps\<Map folder>\global.cfg
<OMSI root>\maps\<Map folder>\openomsi-realmap\...
<OMSI root>\maps\<Map folder>\openomsi-pausemenu\...
```

O próprio OMSI ignora as duas pastas, então um mapa feito para vários jogos distribui um único conjunto de arquivos e não perde nada. São dados de terceiros: o módulo os lê em somente leitura, trata todo valor como não confiável e nunca grava neles.

<a id="who-finds-them"></a>
## Quem os encontra

O pacote realmap é uma *entrada de conteúdo* do módulo (`realmap-package`, não obrigatória). O bridge do OmsiLaunch localiza a pasta do mapa atual, verifica que ela está dentro da instalação, livre de links e dentro dos seus limites (50.000 arquivos, 4 GiB), e entrega ao módulo o diretório e uma impressão digital. O módulo procura o pacote de menu de pausa na **mesma pasta do mapa** (a vizinha da pasta realmap; quando o mapa não tem pacote realmap, ele deduz a pasta do mapa a partir da localização do próprio módulo na instalação). A pasta `openomsi-pausemenu` não é procurada em nenhum outro lugar; um pacote para um mapa que você não possui vai dentro da pasta desse mapa, como um mod.

O módulo percebe o mapa por `map.read` (um nome de arquivo no formato `maps\<folder>\global.cfg`) e começa a carregar os pacotes em uma thread de worker, nunca dentro do tick. No máximo oito mapas são carregados em uma sessão; uma troca de mapa substitui os pacotes, e as imagens do mapa anterior são descartadas da camada de desenho.

<a id="what-happens-without-them"></a>
## O que acontece sem eles

| Situação | Resultado |
| --- | --- |
| Sem pacote realmap | Não é um erro. O radar desenha o anel, você, os veículos ao seu redor e o relógio; a página Mapa mostra o mesmo, e a página Linhas diz "No lines to show". |
| Sem pacote de menu de pausa | Não é um erro. O menu genérico do OMSI 2: o título é o nome da pasta do mapa, sem símbolo (um disco neutro com uma marca de pausa é desenhado), o destaque azul padrão, a fonte de interface do sistema, os textos de legenda embutidos. |
| Uma pasta de pacote sem `manifest.cfg` | O mesmo que ausente. |
| Um pacote com `format` desconhecido, ou com manifesto ilegível | O pacote inteiro é ignorado com uma linha de log; vale o comportamento genérico. Um aviso é mostrado ("A map package could not be used and was ignored."). |
| Uma entrada ruim dentro de um pacote (uma imagem grande demais, uma cor que não é cor, um caminho que sai da pasta) | Essa entrada é descartada com um motivo; o resto do pacote permanece. |
| Uma camada do pacote realmap está ausente ou corrompida | Essa camada fica desligada; as outras permanecem. Um bloco corrompido de `surface.oms` perde apenas esse bloco. |
| A leitura de um pacote falha inesperadamente | O pacote é ignorado e a sessão continua. |

Nada que um pacote contenha pode parar o módulo: todo leitor termina em "usar o padrão genérico".

<a id="what-the-module-uses"></a>
## O que o módulo usa

Os leitores leem tudo o que os formatos definem e validam. O módulo na versão `0.1.0` desenha uma parte; o resto é lido, verificado e guardado para versões futuras.

| De | Usado na 0.1.0 | Lido mas ainda não desenhado |
| --- | --- | --- |
| `identity.cfg` do menu de pausa | `title` (e `title.<LANG>`), `symbol` / `symbol.<theme>` / `symbol.openomsi` (imagens PNG; a escolha segue `theme` e `layout`), `color.accent`, `color.line`, `color.group.<id>`, `legend.<kind>` (e por idioma) | `stop_icon`, `marker_icon.<kind>`, `mode_icon.<mode>`, `color.brt`; um símbolo que é um SVG (é validado, mas a marca genérica é desenhada) |
| `symbols/` do menu de pausa | A imagem do símbolo do mapa | `omsi2-logo.png`, `openomsi.png` |
| `fonts/` do menu de pausa | Todos os cinco papéis, registrados na camada de desenho | |
| `strings/` do menu de pausa | As chaves `title` e `legend.<kind>` | Outras chaves |
| `manifest.cfg` do menu de pausa | `format`, `name` (para o log) | `author`, `credit` |
| `surface.oms` do realmap | Preenchimentos, rios, ferrovias, fronteiras, curvas de nível, ruas (em zoom próximo), rótulos de lugares | Nomes de ruas e limites de velocidade, que são lidos (`StreetAt`) mas ainda não mostrados |
| `markers.cfg` e `icons/` do realmap | Os marcadores com seus ícones (PNG, até 128 pixels, 48 imagens) | |
| `regions.cfg` do realmap | Os nomes com uma caixa de rótulo legível, como marcas d'água discretas | A consulta "você está em" |
| `lines.lin` do realmap | As linhas válidas na data da sessão, na página Linhas | Rotas e paradas no mapa |
| `manifest.cfg` do realmap | `grid`, `north` | Cores (`water`, `green`, ...), `player*`, `brt_lines`, `*_vehicles`, `alignment`, `credit` |

As cores do radar são do próprio módulo, exceto o destaque e a cor de linha que o pacote de menu de pausa pode definir.

<a id="caching-and-change"></a>
## Cache e mudança

O módulo lê cada pacote uma vez por mapa, fora do tick, e mantém um instantâneo imutável. A impressão digital que identifica um pacote é uma chave de cache (o caminho relativo, o tamanho e a data de gravação de cada arquivo): nunca é uma verificação de integridade nem uma decisão de confiança. Para ver uma mudança em um pacote, reinicie a sessão.

<a id="reports"></a>
## Relatórios

Quando os pacotes de um mapa são carregados, o módulo emite o evento `module.openhud.packages` com o mapa, o estado de cada pacote (`Absent`, `Ignored`, `Loaded`), o número de fontes e imagens enviadas à camada de desenho e, quando há algo a dizer, os primeiros diagnósticos (`Dropped: ...`, `Ignored: ...`), limitados a 700 caracteres. Um problema de pacote que não pode ser mostrado como estado é `module.openhud.package-problem` ([operações e eventos](../reference/operations-and-events.md#events)).

Relacionado: [pacote de menu de pausa](../reference/pause-menu-package.md) · [pacote realmap](../reference/realmap-package.md) · [fontes, símbolos e licenciamento](fonts-symbols-and-licensing.md)
