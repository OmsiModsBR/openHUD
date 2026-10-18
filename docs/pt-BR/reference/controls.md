# Controles

<!-- l10n: source=reference/controls.md source-sha256=d0e1cced53596936c88c0f3d2f3fe9ea2277577c9f9114f427ccc6ff5ac02f37 -->
> Tradução da [página original em inglês](../../reference/controls.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O radar não tem teclas. O menu de pausa é controlado pelo teclado e pelo mouse enquanto está aberto; o módulo captura esses eventos (para que o OMSI não aja sobre eles) apenas enquanto o menu está aberto, e os devolve antes de qualquer diálogo nativo abrir. Esta página lista cada tecla e ação do mouse por página. Ela foi extraída de `src/OpenHud.Pause/PauseMenu.cs` e conferida pelos gates `pause.menu.*`.

Páginas relacionadas: [primeira sessão](../getting-started/first-session.md), [o menu de pausa](../concepts/the-pause-menu.md), [operações e eventos](operations-and-events.md).

<a id="everywhere-in-the-menu"></a>
## Em todo o menu

| Entrada | Faz |
| --- | --- |
| Esc | Volta ao jogo (espera as teclas serem soltas, até 750 ms) |
| Tab | Próxima página (Menu, Mapa, Linhas, depois Menu de novo) |
| Shift+Tab | Página anterior |
| Clique em uma aba | Essa página |

Outras teclas são ignoradas; nenhuma chega ao OMSI enquanto o menu está aberto.

<a id="menu-page"></a>
## Página Menu

Os cinco botões são, em ordem: voltar ao jogo, novo jogo, salvar jogo, opções, sair do OMSI.

| Entrada | Faz |
| --- | --- |
| Baixo / Cima | Botão seguinte / anterior (dá a volta) |
| Home / End | Primeiro / último botão |
| Enter, Espaço | Pressiona o botão em foco |
| Mover o mouse | Foca o botão sob o ponteiro |
| Clique esquerdo | Pressionar e soltar no mesmo botão o ativa; soltar em outro lugar cancela |
| Clique duplo em um botão | Conta como um pressionamento; o soltar ativa |

Novo jogo, salvar jogo, opções e sair do OMSI executam os handlers do próprio OMSI. O menu se oculta e devolve o teclado enquanto o diálogo do OMSI está aberto, e volta quando ele fecha e o jogo continua em pausa.

<a id="map-page"></a>
## Página Mapa

| Entrada | Faz |
| --- | --- |
| Setas | Deslocam em 40 unidades de menu |
| `+`, `+` numérico | Aumenta o zoom um passo |
| `-`, `-` numérico | Diminui o zoom um passo |
| Home | Centraliza em você e redefine o zoom |
| Roda do mouse sobre o mapa | Zoom (um passo por clique da roda) |
| Botão esquerdo, arrastar | Desloca o mapa |
| Clique duplo no mapa | Centraliza em você e redefine o zoom |

O zoom tem 21 passos (de 8 para fora a 12 para dentro) em torno da escala padrão; cada passo muda a escala por um fator de 1,25. O mapa abre centralizado no seu veículo toda vez que o menu abre.

<a id="lines-page"></a>
## Página Linhas

| Entrada | Faz |
| --- | --- |
| Baixo / Cima | Seleciona a linha seguinte / anterior (a primeira, se nenhuma estiver selecionada) |
| Page Down / Page Up | Move uma página (9 linhas) |
| Home / End | Primeira / última linha |
| Enter | Ativa a linha selecionada (sem outro efeito na 0.1.0) |
| Roda do mouse sobre a lista | Rola, 3 linhas por clique da roda |
| Clique esquerdo em uma linha | Seleciona a linha |
| Clique duplo em uma linha | Seleciona e ativa (sem outro efeito na 0.1.0) |

<a id="notes"></a>
## Observações

- Os botões direito e do meio são ignorados.
- Uma tecla ainda pressionada quando você retoma seria vista pelo OMSI como um novo pressionamento, então o menu espera a liberação (no máximo 750 ms; depois retoma de qualquer forma).
- As teclas agem no key-down; key-downs repetidos repetem a ação.

Relacionado: [o menu de pausa](../concepts/the-pause-menu.md) · [primeira sessão](../getting-started/first-session.md)
