# Internacionalização

<!-- l10n: source=concepts/internationalization.md source-sha256=f9dec842c8f19d6be5448079c2e79ab99a613fbfbc4bb8d05b15a590dcc0df2a -->
> Tradução da [página original em inglês](../../concepts/internationalization.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O texto próprio do openHUD (os botões do menu de pausa, os rótulos do radar e do mapa, os avisos) existe em três idiomas: inglês, português (Brasil) e alemão. O pacote de menu de pausa de um mapa pode acrescentar o título e a legenda do mapa em qualquer idioma para o qual o OMSI tenha um código. Esta página explica como o idioma é escolhido, de onde vem cada texto, o que uma tradução ausente causa e o que está fora do alcance do módulo. As chaves e seus textos estão em [chaves de idioma da interface](../reference/ui-language-keys.md); a configuração `language` está na [referência de configurações](../reference/settings.md#language).

Páginas relacionadas: [pacote de menu de pausa](../reference/pause-menu-package.md), [fontes, símbolos e licenciamento](fonts-symbols-and-licensing.md), [configurações do radar e da interface](../guides/radar-and-interface-settings.md#language), [solução de problemas](../guides/troubleshooting.md#the-wrong-language).

<a id="two-kinds-of-text"></a>
## Dois tipos de texto

| Tipo | Onde fica | Idiomas |
| --- | --- | --- |
| A interface | Incorporada ao módulo (as chaves `UI_`); nada é lido do disco | Inglês, português (Brasil), alemão |
| O texto próprio do mapa (título, legenda, strings por idioma) | O pacote de menu de pausa do mapa | Qualquer idioma com um código OMSI de três letras (`ENG`, `DEU`, `ESP`, `PTB`, `FRA` ...) |

Os textos são UTF-8 em todo lugar. O título de um mapa escrito em polonês ou francês aparece em uma fonte latina; caracteres que a fonte escolhida não tem recorrem à fonte de interface do sistema ([fontes](fonts-symbols-and-licensing.md#how-text-is-drawn)).

<a id="how-the-language-is-chosen"></a>
## Como o idioma é escolhido

A configuração `language` é `auto` (o padrão) ou um código. Ela aceita até oito caracteres: letras, `-` ou `_` (`en`, `ENG`, `pt-BR`, `de`). É armazenada em maiúsculas, com `_` trocado por `-`. Qualquer outra coisa é informada como um problema de configuração e `auto` é usado.

**O idioma da interface** é resolvido a partir do código: `ENG` ou `EN` é inglês; `PTB` ou `PT` (qualquer `pt-...`) é português; `DEU`, `DE` ou `GER` é alemão. Um código sem tradução (`es`, `FRA`) recorre ao que o OMSI informa e, depois, ao inglês. Na versão 0.1.0, `auto` resulta em inglês: o módulo ainda não lê o idioma do OMSI, então defina `language` explicitamente para obter outro (`auto` deve seguir o idioma do próprio OMSI, e seguirá quando o módulo passar a lê-lo).

**O código de idioma do pacote** é o que as chaves com sufixo do pacote usam (`title.PTB`). Uma configuração explícita é mapeada para o código do OMSI (`en` para `ENG`, `pt` para `PTB`, `de` para `DEU`, `es` para `ESP`, `fr` para `FRA`, `it` para `ITA`, `nl` para `NLD`, `pl` para `POL`, `cs` para `CZE`, `ru` para `RUS`; um código de três letras é usado como está). Com `auto`, deve-se usar o código do próprio OMSI (na 0.1.0 o módulo não tem nenhum para usar e pede aos pacotes `ENG`), mesmo que a interface não tenha tradução para ele, de modo que um pacote ainda possa mostrar seu título em espanhol a um jogador cujo OMSI está em espanhol enquanto o texto da interface fica em inglês.

<a id="where-each-text-comes-from"></a>
## De onde vem cada texto

| Texto | Ordem de busca |
| --- | --- |
| Um botão, um rótulo, um aviso | A tabela da interface no idioma escolhido; se esse idioma não tem texto para a chave, o texto em inglês; uma chave desconhecida mostra a própria chave (assim uma tradução ausente fica visível, nunca em branco). |
| Os rótulos próprios do menu de pausa | O catálogo de 13 chaves do menu de pausa no idioma escolhido primeiro, depois a tabela da interface, depois o inglês. |
| O título do mapa | `strings/<LANG>.cfg` (`title`), depois `title.<LANG>` de `identity.cfg`, depois `title`, depois o nome da pasta do mapa. |
| Uma entrada da legenda | `strings/<LANG>.cfg` (`legend.<kind>`), depois `legend.<kind>.<LANG>`, depois `legend.<kind>`, depois o texto embutido do tipo (`UI_LEGEND_STATION` ...). |

Os marcadores `{0}`, `{1}` em um texto são preenchidos pelo módulo (`UI_RADAR_EARLY`: "{0} early"). Um marcador sem valor é deixado como está. As unidades são chaves separadas (`UI_UNIT_KMH`, `UI_UNIT_MI`) para que um idioma possa ordená-las como precisar.

<a id="what-the-tests-guarantee"></a>
## O que os testes garantem

Um gate verifica que toda chave tem um texto nos três idiomas e que um texto tem os mesmos marcadores nos três ([cobertura de testes](../status/test-coverage.md)). Ele não consegue verificar se uma tradução é boa; correções são bem-vindas.

<a id="limits"></a>
## Limites

- O texto da interface existe em três idiomas. Outros idiomas precisam de uma tradução acrescentada ao módulo; um pacote de mapa não pode traduzir a interface (ele pode traduzir o próprio título e a própria legenda).
- O texto é disposto da esquerda para a direita, um glifo após o outro. Idiomas da direita para a esquerda e escritas que precisam de shaping não são suportados.
- O módulo não altera o idioma do OMSI nem seus diálogos nativos: os diálogos nativos que as ações do menu abrem (salvar, opções) são do OMSI e mantêm o idioma do OMSI.

Relacionado: [chaves de idioma da interface](../reference/ui-language-keys.md) · [pacote de menu de pausa](../reference/pause-menu-package.md) · [configurações](../reference/settings.md)
