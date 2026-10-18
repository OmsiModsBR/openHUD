# Fontes, símbolos e licenciamento

<!-- l10n: source=concepts/fonts-symbols-and-licensing.md source-sha256=5b14129f8f1f1e37fba39f32cfac8b558c6e983ac6e4db23064ecc577f09ce8a -->
> Tradução da [página original em inglês](../../concepts/fonts-symbols-and-licensing.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD não traz nenhum arquivo de fonte nem logotipo de marca próprio. Qualquer tipografia, símbolo ou logotipo que o menu de um mapa mostre vem do pacote de menu de pausa do mapa, e quem faz o pacote é responsável pelo direito de distribuí-lo. Esta página explica o que o módulo faz com fontes e imagens, o que ele verifica e o que deixa para o autor, e como o texto é desenhado quando uma fonte está ausente ou não tem um caractere.

Páginas relacionadas: [pacote de menu de pausa](../reference/pause-menu-package.md#fonts), [como fazer um pacote de menu de pausa](../guides/making-a-pause-menu-package.md), [pacotes por mapa](per-map-packages.md), [internacionalização](internationalization.md), [solução de problemas](../guides/troubleshooting.md#text-shows-wrong-characters).

<a id="who-is-responsible-for-what"></a>
## Quem é responsável pelo quê

| | O módulo | O autor do pacote |
| --- | --- | --- |
| Arquivos de fonte | Não traz nenhum. Usa as fontes de um pacote somente quando `fonts/FONTS-LICENSE.txt` existe e não está vazio. Não lê nem interpreta o texto da licença. | Escolhe fontes cuja licença permite redistribuição dentro do pacote e escreve as licenças delas em `FONTS-LICENSE.txt`. |
| Símbolos e logotipos | Traz um disco neutro com uma marca de pausa e as marcas internas `@omsi2`, `@simple` e `@none`, desenhadas por código. Não traz símbolo de marca nem cópia do logotipo do OMSI 2. | Detém o direito sobre cada imagem em `symbols/`, inclusive o logotipo do OMSI 2 se o pacote incluir um (`symbols/omsi2-logo.png`), e sobre o símbolo de marca de um mapa. |
| Dados do mapa | Lê em somente leitura. | Declara a licença em `LICENSE.txt` (para pessoas) e o crédito no manifesto. |

Os arquivos `LICENSE.txt` e `CREDITS.txt` de um pacote são para pessoas. O módulo nunca os abre. A verificação de licença das fontes é uma verificação de presença, não jurídica: um pacote pode conter um arquivo de licença errado, e o módulo não tem como saber.

<a id="what-the-module-does-with-a-font"></a>
## O que o módulo faz com uma fonte

1. O leitor encontra os arquivos de fonte diretamente em `fonts/` (`.ttf`, `.otf`, no máximo 12) e exige `fonts.cfg` e um `FONTS-LICENSE.txt` não vazio. Sem eles, nenhuma fonte do pacote é usada.
2. Ele verifica cada arquivo citado por `fonts.cfg`: um arquivo TrueType ou OpenType estático, de no máximo 4 MiB, com um diretório de tabelas válido. Fontes variáveis e coleções são recusadas.
3. Ele atribui os cinco papéis (`regular`, `medium`, `bold`, `black`, `condensed`); um papel que você omite recebe a face de peso mais próximo.
4. Quando os pacotes do mapa são carregados, o módulo pede à camada de desenho que registre cada fonte para o seu papel. A camada de desenho verifica o arquivo de novo e o registra apenas para este processo: ele nunca é instalado no Windows, nunca é copiado, nunca é gravado em disco. Quando a sessão termina, a camada de desenho cancela o registro de todas as fontes.

<a id="how-text-is-drawn"></a>
## Como o texto é desenhado

A camada de desenho pede ao GDI do Windows que rasterize cada glifo uma vez, no tamanho necessário, em um atlas de glifos (uma textura), e desenha o texto como triângulos texturizados. Consequências:

- **Um papel sem fonte** é desenhado com a fonte de interface do sistema (a fonte que o Windows usa em caixas de mensagem, geralmente Segoe UI), no peso do papel. Se um pacote fornece apenas alguns papéis, os outros usam a fonte `regular` do pacote antes da fonte do sistema.
- **Um caractere que a fonte não tem** é desenhado com a fonte de interface do sistema; um caractere que essa fonte também não tem é desenhado com o glifo de caractere ausente da própria fonte. Nunca é substituído por um ponto de interrogação.
- **Caracteres fora do Plano Multilíngue Básico** (a maioria dos emojis) são desenhados como um espaço em branco de meio em: o GDI é consultado glifo a glifo em unidades UTF-16.
- **Apenas avanços.** O texto é disposto com o avanço de cada glifo; não há kerning nem shaping, então escritas da direita para a esquerda e escritas complexas não são suportadas ([limitações conhecidas](../reference/known-limitations.md)).
- O módulo pede à camada de desenho a largura de um texto (`measure`) ao dimensionar chips e faixas, de modo que os leiautes seguem as fontes realmente registradas; se a camada não puder responder, usa-se uma aproximação.

<a id="pictures"></a>
## Imagens

| Tipo | O que acontece |
| --- | --- |
| Símbolo PNG | Validado pelo cabeçalho e pelo tamanho, depois decodificado na thread do worker (no máximo 512 pixels no maior lado após a redução) e registrado na camada de desenho como pixels brutos. |
| Símbolo SVG | Validado contra o subconjunto estático (sem scripts, sem `DOCTYPE`, sem imagens incorporadas, sem referências externas). O módulo não rasteriza SVG nesta versão: desenha a marca genérica no lugar. |
| `@omsi2`, `@simple`, `@none` | Desenhados pelo próprio openHUD. |
| Ícones do realmap | PNG, até 1024 x 1024 no pacote; reduzidos a no máximo 128 pixels; no máximo 48 imagens distintas. |

Uma imagem que falha em qualquer verificação é descartada com um motivo no log e o padrão é usado.

<a id="practical-rules-for-authors"></a>
## Regras práticas para autores

- Coloque a licença de cada arquivo de fonte em `fonts/FONTS-LICENSE.txt`, nomeando os arquivos que ela cobre.
- Não inclua uma fonte comercial, nem uma fonte cuja licença proíba a redistribuição, em um pacote que você distribui.
- Não inclua o logotipo do OMSI 2 a menos que você tenha o direito; a marca genérica `@omsi2` não precisa de permissão.
- Nunca distribua um símbolo de marca que não seja seu.
- Mantenha um `CREDITS.txt` para as pessoas que fizeram as imagens.

Relacionado: [pacote de menu de pausa](../reference/pause-menu-package.md) · [como fazer um pacote de menu de pausa](../guides/making-a-pause-menu-package.md) · [segurança](safety.md)
