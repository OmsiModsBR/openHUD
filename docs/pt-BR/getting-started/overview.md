# O que é o openHUD

<!-- l10n: source=getting-started/overview.md source-sha256=64e36046e386f83928e58553511a57471f12832c33ec1909eaf272187941459b -->
> Tradução da [página original em inglês](../../getting-started/overview.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD (id do módulo `openhud`, versão `0.1.0`) é um HUD em jogo e um menu de pausa para o OMSI 2, empacotado como um Runtime Module do OmsiLaunch. Ele desenha um radar redondo (um minimapa) sobre a vista do jogo e, onde a build do OMSI permite, substitui o menu de pausa nativo do OMSI por um próprio, com uma página de menu, uma página de mapa em tela cheia e uma página com as linhas do mapa. Foi feito para mapas que trazem os pacotes opcionais do openOMSI (um mapa real para o radar e uma identidade para o menu de pausa), e funciona sem eles. Esta página diz o que ele faz, o que desenha, o que substitui, o que deixa em paz e de quais partes é feito. Requisitos e instalação estão em [instalação](installation.md); a primeira sessão está em [primeira sessão](first-session.md).

Páginas relacionadas: [instalação](installation.md), [primeira sessão](first-session.md), [as três partes](../concepts/the-three-parts.md), [o radar](../concepts/the-radar.md), [o menu de pausa](../concepts/the-pause-menu.md), [configurações](../reference/settings.md), [limitações conhecidas](../reference/known-limitations.md), [FAQ](../reference/faq.md).

<a id="what-it-draws"></a>
## O que ele desenha

| Elemento | Quando | O que você vê |
| --- | --- | --- |
| O radar | Enquanto você dirige e o jogo está rodando | Um mapa redondo em um canto da tela, centrado no seu veículo e girando com ele ("heading up"), com uma marca de norte, os outros veículos ao seu redor, a escala do círculo, e a sua velocidade e o relógio do jogo embaixo. Com um pacote realmap, mostra também água, parques, áreas construídas, relevo, nomes de municípios e os ícones do mapa ([o radar](../concepts/the-radar.md)). |
| O menu de pausa | Enquanto o OMSI está pausado e o menu de pausa nativo seria mostrado | Um painel sobre uma cópia escurecida da imagem do jogo, com três páginas: **Menu** (voltar ao jogo, novo jogo, salvar jogo, opções, sair do OMSI e a legenda do mapa), **Mapa** (o mapa inteiro, que você move e amplia) e **Linhas** (as linhas do mapa para a data da sessão) ([o menu de pausa](../concepts/the-pause-menu.md)). |
| Um balão na bandeja | Raramente | Quando uma parte precisou ser desligada (a camada de desenho, o radar, o menu de pausa ou um pacote de mapa), a bandeja do OmsiLaunch mostra um aviso curto. |

Tudo é desenhado pelo próprio módulo na vista Direct3D 9 do OMSI. Nada é adicionado às janelas do próprio OMSI.

<a id="what-it-replaces-and-what-it-leaves-alone"></a>
## O que ele substitui e o que deixa em paz

- Ele substitui o menu de pausa nativo do OMSI **somente** quando a build do OMSI em uso permite que o OmsiLaunch o entregue (a capacidade opcional `ui.pause-menu.replace`), quando a captura de entrada funciona e quando a camada de desenho está disponível. Em todos os outros casos o menu de pausa nativo continua exatamente como está, e o radar continua funcionando ([o menu de pausa](../concepts/the-pause-menu.md#when-the-native-menu-stays)).
- Ele não toca nas outras janelas do OMSI, nas configurações, nos arquivos nem nos add-ons. Nunca grava na instalação do OMSI ([segurança](../concepts/safety.md)).
- As ações do menu (novo jogo, salvar, opções, sair) são do próprio OMSI: o módulo pede ao OMSI que execute seu tratador nativo e o OMSI mostra seus diálogos nativos. O módulo não desenha nada enquanto um diálogo nativo está à frente.

<a id="what-it-needs-and-what-is-optional"></a>
## O que é necessário e o que é opcional

| | Necessário? | Observações |
| --- | --- | --- |
| OmsiLaunch com bridge protocol 3 | Sim | O módulo é um Runtime Module do OmsiLaunch ([instalação](installation.md#requirements)). |
| Uma vista Direct3D 9 no OMSI | Sim | A camada de desenho intercepta `Present` e `Reset` do dispositivo do jogo. |
| Captura de teclado e mouse (`bridge.input`) | Sim | Necessária para a entrada própria do menu de pausa. |
| `ui.pause-menu.replace` | Opcional | Sem ela o menu de pausa nativo permanece; o radar funciona. |
| Um pacote realmap para o mapa | Opcional | Sem ele o radar desenha apenas você, os veículos ao seu redor e a escala ([pacotes por mapa](../concepts/per-map-packages.md)). |
| Um pacote de menu de pausa para o mapa | Opcional | Sem ele o menu é o menu genérico do OMSI 2. |
| Fontes | Opcional | Sem uma fonte de pacote, a camada de desenho usa a fonte de interface do sistema. |

<a id="the-three-parts"></a>
## As três partes

| Parte | Projeto | O que faz |
| --- | --- | --- |
| O núcleo | `OpenHud.Core` (C#) | Lê o jogo pelas operações do OmsiLaunch, mantém o modelo do jogo, lê as configurações e os dois pacotes de mapa, e monta o radar e o mapa completo como uma cena de formas simples. |
| O menu de pausa | `OpenHud.Pause` (C#) | A máquina de estados que assume o menu de pausa nativo do OMSI e o devolve, e o modelo do próprio menu (páginas, foco, regras de teclado e mouse, leiaute). |
| A camada de desenho | `openhud-draw.dll` (C++, x86) | Desenha a cena na vista Direct3D 9 do jogo a cada `Present`. Não sabe nada sobre o jogo, o menu ou o mapa. |

Uma entrada fina, `OpenHud.dll` (o projeto do módulo), conecta as três ao OmsiLaunch: é o componente gerenciado que o bridge carrega, e é dona do tick. O repositório também guarda um espaço reservado para um programa de sessão opcional, `OpenHud.Host`; o manifesto não declara nenhum componente host, então nenhum é iniciado ([limitações conhecidas](../reference/known-limitations.md)). [As três partes](../concepts/the-three-parts.md) explica por que o trabalho é dividido assim.

<a id="what-it-does-not-do"></a>
## O que ele não faz

- Não é um recurso de desempenho. O desenho é limitado (uma cena tem no máximo 8.192 comandos e 1 MiB de JSON) e o trabalho do próprio módulo por tick é medido e informado, mas é trabalho somado ao do jogo, não no lugar dele ([desempenho](../concepts/performance.md)).
- Não lê a memória do OMSI por endereço, não simula entrada de teclado ou mouse, não automatiza as janelas do OMSI nem grava nos arquivos do OMSI. Tudo o que ele sabe sobre o jogo vem das operações públicas do OmsiLaunch ([segurança](../concepts/safety.md)).
- Não altera add-ons, licenças nem a propriedade de nenhum conteúdo.
- Não tem janela de configurações. Suas configurações são configurações de módulo do OmsiLaunch: `/module-setting` na linha de comando, `modules.ini` e presets de perfil de sessão ([como ativá-lo](../guides/enabling-the-module.md)).

<a id="status"></a>
## Status

O openHUD `0.1.0` está construído e coberto por testes offline que nunca iniciam o OMSI ([cobertura de testes](../status/test-coverage.md)). Suas rodadas em um OMSI em execução são agendadas pelo responsável e ainda não foram feitas; a [situação da validação](../status/validation.md) lista o que elas vão confirmar. O recurso de Runtime Modules do OmsiLaunch é `EXPERIMENTAL`.

O módulo não faz parte do OmsiLaunch e não é distribuído com ele. É software livre sob a Licença Pública Geral GNU, versão 3; usa o OmsiLaunch, que é licenciado sob a Licença Pública Geral Menor GNU, somente versão 3 (veja `NOTICE.txt`).

<a id="next"></a>
## Próximos passos

[Instalação](installation.md) · [Primeira sessão](first-session.md) · [O radar](../concepts/the-radar.md) · [O menu de pausa](../concepts/the-pause-menu.md)
