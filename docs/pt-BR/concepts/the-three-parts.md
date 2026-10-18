# As três partes e por quê

<!-- l10n: source=concepts/the-three-parts.md source-sha256=8d62405b43ae91c2637562f20d172fa92cb73b18d0fffde220687ea84da4ef6c -->
> Tradução da [página original em inglês](../../concepts/the-three-parts.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD é dividido em três partes que se encontram em uma pequena interface: um documento que diz o que desenhar. Esta página explica o que cada parte faz, o que ela não pode saber, como um tick do módulo move informação entre elas e por que o trabalho é dividido assim. O projeto é descrito com mais profundidade para desenvolvedores em [arquitetura](../architecture.md); o documento trocado entre as partes é definido no [protocolo de cena (em inglês)](../../scene-protocol.md).

Páginas relacionadas: [o protocolo de cena em palavras simples](the-scene-protocol.md), [o radar](the-radar.md), [o menu de pausa](the-pause-menu.md), [desempenho](performance.md), [segurança](safety.md).

<a id="the-parts"></a>
## As partes

| Parte | Linguagem, bitness | Fica em | Sabe | Não sabe |
| --- | --- | --- | --- | --- |
| O núcleo | C#, carregado pelo plugin de 32 bits | `OpenHud.Core` | O jogo como o OmsiLaunch o descreve (hora, mapa, câmera, o veículo do jogador, os veículos ao redor); as configurações; os textos da interface; os dois pacotes de mapa; como dispor um radar e um mapa como formas | Direct3D, janelas, entrada, qualquer endereço no OMSI |
| O menu de pausa | C#, igual | `OpenHud.Pause` | Os estados do menu de pausa, suas páginas, foco e regras de teclado e mouse, seu leiaute; como falar com a pausa do OMSI por quatro pequenas portas | O radar, os pacotes, a bridge |
| A camada de desenho | C++, x86 | `OpenHud.Draw` (`openhud-draw.dll`) | Triângulos, glifos, texturas, o dispositivo Direct3D 9 do jogo; o formato de cena | O jogo, o menu, o mapa |

Uma quarta peça, `OpenHud.Module` (`OpenHud.dll`), é a cola: o componente gerenciado que a bridge do OmsiLaunch carrega. Ela adapta o contexto do OmsiLaunch às portas das outras partes, é dona do tick e decide o que publicar. Não tem lógica própria sobre leiaute.

<a id="why-a-scene"></a>
## Por que uma cena

O núcleo decide **o que** é desenhado; a camada de desenho apenas o **rasteriza**. Eles se encontram em um documento JSON, a cena (uma lista de retângulos, círculos, linhas, polígonos, texto e imagens, em uma tela virtual de 1920 x 1080), e algumas pequenas operações para registrar imagens e fontes e para mostrar ou ocultar. Dividir o trabalho assim tem quatro consequências:

- **Todo o leiaute é código gerenciado simples.** O radar e o menu são montados sem um dispositivo Direct3D, então os testes rodam em qualquer lugar e um leiaute é comparado com documentos de referência ([cobertura de testes](../status/test-coverage.md)).
- **A camada nativa permanece pequena e verificável.** Ela analisa um documento, o desenha e responde `ok` ou um erro. Não guarda estado sobre o jogo, então não pode discordar dele.
- **Uma nova cena substitui a antiga atomicamente.** Publicar é uma única chamada; a camada mantém apenas a última cena e a desenha a cada quadro, então um tick atrasado ou lento nunca mostra uma imagem pela metade.
- **As falhas ficam contidas.** Uma cena que a camada recusa deixa a anterior; uma camada que falha não desenha nada e avisa, e o jogo continua.

<a id="one-tick"></a>
## Um tick

A bridge chama o módulo a cada 50 ms na thread de interface do OMSI. Em cada tick o módulo:

1. lê o jogo pelas operações do OmsiLaunch (`time.read`, `map.read`, `player-vehicle.read`, `road-vehicles.list` e as demais que o manifesto declara). Uma parte que não pode ser lida torna-se "indisponível" sem afetar as outras;
2. percebe uma troca de mapa e começa a carregar os pacotes do mapa em uma thread de trabalho (nunca dentro do tick);
3. dá um passo na máquina de estados do menu de pausa: pergunta ao OMSI se o jogo está pausado, assume o menu nativo ou o devolve, e lê as teclas e o mouse;
4. monta uma cena, o radar enquanto o jogo roda, ou o menu (com o mapa completo dentro) enquanto está pausado;
5. envia a cena à camada de desenho somente quando algo visível mudou.

A camada de desenho, enquanto isso, não está presa ao tick. Ela desenha a última cena recebida a cada `Present` do dispositivo do jogo, qualquer que seja a thread que apresenta.

<a id="what-each-part-may-never-do"></a>
## O que cada parte nunca pode fazer

- O núcleo nunca envia ponteiros, handles ou endereços à camada, e a camada nunca lê o jogo.
- Nenhuma das partes gerenciadas abre uma janela, envia uma tecla ou um clique, ou grava em um arquivo do OMSI.
- Nenhuma parte contém um endereço, um deslocamento ou uma assinatura do OMSI. O que o jogo expõe vem do catálogo do OmsiLaunch; o OmsiLaunch o resolve por build do OMSI e responde `OL_E_CAPABILITY_UNRESOLVED` onde não consegue ([compatibilidade](../reference/compatibility.md)).

<a id="where-the-code-is"></a>
## Onde está o código

| Pasta | Conteúdo |
| --- | --- |
| `src/OpenHud.Core/Settings`, `I18n` | O analisador de configurações e os textos da interface. |
| `src/OpenHud.Core/Model` | O modelo do jogo: `FrameSource` e os tipos de snapshot. |
| `src/OpenHud.Core/Packages` | Os dois leitores de pacote. |
| `src/OpenHud.Core/Scene`, `Radar` | Os tipos e o codec de cena; os construtores do radar e do mapa completo. |
| `src/OpenHud.Pause` | A máquina de estados e o menu. |
| `src/OpenHud.Draw` | A camada de desenho. |
| `src/OpenHud.Module` | A cola. |

Relacionado: [o protocolo de cena em palavras simples](the-scene-protocol.md) · [arquitetura](../architecture.md) · [desempenho](performance.md)
