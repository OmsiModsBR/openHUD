# Status de validação

<!-- l10n: source=status/validation.md source-sha256=06216be956b283c757b7f06bcfdd9f10b325cc9b13c584126827a14568efe4bf -->
> Tradução da [página original em inglês](../../status/validation.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD `0.1.0` está construído e coberto por testes offline que nunca iniciam o OMSI ([cobertura de testes](test-coverage.md)). As rodadas dele em um OMSI em execução são agendadas pelo responsável e não foram feitas. Esta página diz isso com clareza e lista o que essas rodadas vão confirmar. Nada abaixo é uma falha conhecida; é uma lista de comportamentos que só um jogo em execução pode confirmar.

Páginas relacionadas: [cobertura de testes](test-coverage.md), [limitações conhecidas](../reference/known-limitations.md), [compatibilidade](../reference/compatibility.md).

<a id="what-exists-today"></a>
## O que existe hoje

- O núcleo gerenciado, o menu de pausa e o módulo estão construídos e cobertos por gates, incluindo sessões executadas contra um simulado do bridge do OmsiLaunch.
- A camada de desenho nativa está construída e coberta por testes nativos, incluindo um backend Direct3D 9 testado contra um dispositivo quando é possível criar um e comparado com um rasterizador por software.
- Os dois leitores de pacote são cobertos por fixtures, incluindo entradas hostis e corrompidas aleatoriamente.
- O próprio OmsiLaunch documenta que o ato de assumir o menu de pausa e a captura de entrada foram validados apenas offline.

<a id="what-the-rounds-in-a-running-omsi-will-confirm"></a>
## O que as rodadas em um OMSI em execução vão confirmar

| Comportamento | O que será confirmado |
| --- | --- |
| Assumir o menu de pausa nativo | Que `ui.pause.take-over` oculta o menu nativo, `ui.pause.action` executa os handlers nativos (continuar, novo jogo, salvar, opções, sair) e `ui.pause.restore-native` o mostra de novo, nas builds do OMSI em uso; que o jogo permanece pausado o tempo todo |
| Captura de entrada na janela real | Que os eventos de teclado e mouse da janela real do jogo são capturados enquanto o menu está aberto e liberados antes dos diálogos nativos, e que nenhuma tecla vaza para o jogo; a espera pela liberação das teclas antes de continuar |
| Os hooks de Present e Reset no dispositivo real | Que os hooks da camada de desenho se instalam no dispositivo Direct3D 9 real do jogo, que o desenho aparece a cada frame, que uma mudança de resolução (`Reset`) é superada e que o segundo dispositivo do formulário de seleção de veículo é ignorado |
| A imagem de fundo | Que copiar o back buffer dá a imagem certa para o fundo do menu |
| As convenções de unidades e posição que o modelo infere | Que `ground_speed` está em metros por segundo; que a posição de um tile na grade vezes o tamanho do tile mais a posição local dá os metros do mapa; que o eixo frontal do quaternion de orientação dá o rumo no sentido horário a partir do norte; que a tabela de tiles sempre contém o tile do jogador; que o `grid` de um pacote coincide com o tamanho de tile do jogo |
| Qualidade do desenho | Que o texto, o símbolo e o mapa são legíveis em resoluções e escalas reais |
| Custo | Que o tick e o desenho ficam dentro do orçamento em uma sessão real em um mapa grande (`tick_max_ms`, `slow_ticks`) |
| Convivência | Que os hooks se empilham com os hooks Direct3D do próprio produto e com os de outros módulos |

<a id="how-results-will-be-recorded"></a>
## Como os resultados serão registrados

Os resultados serão adicionados a esta página com a data, a build do OMSI, o mapa e o que foi observado. Até lá, toda afirmação desta documentação sobre o comportamento do jogo é uma afirmação sobre o que o código pede ao OmsiLaunch e o que o OmsiLaunch documenta, não uma observação do jogo.

Relacionadas: [cobertura de testes](test-coverage.md) · [limitações conhecidas](../reference/known-limitations.md)
