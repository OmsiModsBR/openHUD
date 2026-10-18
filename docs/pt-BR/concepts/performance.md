# Desempenho e a divisão entre tick e desenho

<!-- l10n: source=concepts/performance.md source-sha256=9942601277bbeaeda54bb5990953fa2431b91f926467bd0c5d6ccdcba4e87065 -->
> Tradução da [página original em inglês](../../concepts/performance.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD roda em dois ritmos: um **tick** a cada 50 ms, que lê o jogo e monta uma cena, e um **desenho** a cada quadro, que coloca a última cena na tela. Esta página explica quanto cada um custa, o que é limitado e como, como o módulo protege o jogo do próprio trabalho e como ver os números. Ela descreve o que o código limita; não afirma um custo em taxa de quadros, porque nenhum foi medido ainda em um jogo em execução ([status de validação](../status/validation.md)).

Páginas relacionadas: [as três partes](the-three-parts.md), [o protocolo de cena em palavras simples](the-scene-protocol.md), [o radar](the-radar.md), [solução de problemas](../guides/troubleshooting.md#high-cpu-or-a-slow-game), [limitações conhecidas](../reference/known-limitations.md).

<a id="the-tick"></a>
## O tick

A bridge chama o módulo cerca de a cada 50 ms (`tick_ms` é 50) na thread de interface do OMSI. Um tick que lança exceção, retorna tarde (acima do seu intervalo) ou nunca retorna é uma falha do módulo segundo as regras da bridge; a política de falha do módulo é `degrade`, então um tick atrasado é reportado uma vez e o módulo continua rodando (ticks perdidos são ignorados, nunca enfileirados). O módulo mantém o próprio trabalho bem abaixo de 50 ms e o mede.

O que um tick faz, e os limites:

| Trabalho | Limite |
| --- | --- |
| Ler o jogo | O relógio e a câmera a cada tick; o veículo do jogador a cada tick; o mapa, o clima e a tabela de horários a cada 20º tick; a tabela de tiles a cada 60º tick (e de imediato quando o tile do jogador é desconhecido, no máximo a cada 10º tick); a lista de veículos a cada 5º tick |
| Ler os veículos ao seu redor | No máximo 24 chamadas `road-vehicle.read` por tick (primeiro os nunca lidos, depois os mais próximos de você cuja leitura é mais antiga); só são mantidos veículos a até 800 m; no máximo 128 são reportados; um veículo não lido por 40 ticks é descartado |
| Avançar o menu de pausa | Uma chamada `ui.pause.state` por tick enquanto o jogo não está pausado |
| Montar o radar | No máximo a cada 100 ms, e só se algo visível mudou (posição a 0,25 m, direção a 1 grau, os veículos, o minuto do relógio, o zoom, novos dados do mapa) |
| Montar o menu de pausa e o mapa | Só quando o estado do menu ou os dados do mapa mudaram |
| Enviar imagens e fontes à camada de desenho | Um orçamento de 6 ms por tick; o restante espera o próximo tick |
| Carregar um pacote | Em uma thread de trabalho, nunca no tick |
| Ler dados do mapa | Em uma thread de trabalho: o tick pede uma janela e continua desenhando a anterior até a nova ficar pronta |

Todas as leituras do jogo são chamadas em processo às operações do OmsiLaunch, executadas na thread de interface como o próprio código do OMSI; cada uma é pequena e limitada.

<a id="what-a-scene-costs"></a>
## Quanto custa uma cena

O núcleo nunca produz mais do que a camada de desenho aceita:

| | Radar | Mapa completo |
| --- | --- | --- |
| Comandos | 6.000 (uma parte é reservada para veículos, marcadores e o anel) | 7.000 a 7.200 |
| Pontos de polilinha/polígono | 24.000 | 30.000 |
| Veículos / marcadores | 48 / 48 | 120 a 200 |
| Elementos do mapa oferecidos por uma janela | 40.000 triângulos e linhas | o mesmo |

Quando uma janela contém mais do que o orçamento, o construtor mantém os elementos mais visíveis (grandes na tela, de camadas importantes: primeiro vias, depois água, áreas construídas, áreas verdes, relevo) e descarta o resto; a escolha é determinística, então a mesma entrada desenha a mesma imagem. A geometria é recortada para a janela visível e os pontos que não acrescentam detalhe visível são descartados antes de serem contados no orçamento.

O módulo só envia uma cena se ela for diferente da última enviada. Um radar estático não custa nada além da verificação.

<a id="the-draw"></a>
## O desenho

A camada de desenho faz hook de `Present` e `Reset` do dispositivo do jogo por meio do registro de hooks da bridge, empilhada sobre os hooks do próprio produto. A cada `Present` ela desenha a cena atual sobre a imagem do jogo, apenas no dispositivo do próprio jogo (o formulário de seleção de veículo cria um segundo dispositivo com os mesmos métodos; a camada o ignora).

- **Nunca espera.** Se um `Reset` ou uma parada detém o bloqueio quando `Present` chega, aquele quadro é pulado.
- **Agrupa.** Os triângulos são reunidos em poucas chamadas de desenho; os glifos vêm de um atlas de textura com substituição do menos recentemente usado, então o atlas permanece limitado.
- **Restaura o que altera.** O estado do Direct3D que ela define é salvo e restaurado; também funciona quando chamada dentro da própria cena do jogo.
- **Backends de software e de hardware.** A mesma cena é rasterizada por um backend de software (usado pelos testes) e pelo backend Direct3D 9, e um teste os compara.
- **Memória.** As imagens são limitadas a 128 e a 4 MiB cada uma, 32 MiB no total.
- **Falha.** Uma falha durante o desenho (uma exceção capturada na fronteira do quadro) desliga o desenho e é reportada; a camada não tenta desenhar de novo até o módulo pedir.

<a id="seeing-the-numbers"></a>
## Ver os números

`status` ([operações e eventos](../reference/operations-and-events.md#status)) reporta as medidas do próprio módulo:

| Chave | Significado |
| --- | --- |
| `ticks` | Ticks desde o início da sessão |
| `tick_last_ms`, `tick_max_ms` | Duração do último tick e do mais longo, em ms |
| `slow_ticks` | Ticks que levaram mais de 40 ms |
| `scenes_published`, `scenes_skipped`, `scene_failures` | Cenas enviadas, puladas porque nada mudou e recusadas pela camada de desenho |
| `native_calls`, `native_failures` | Chamadas à camada de desenho e quantas falharam |
| `uploads_pending`, `fonts_registered`, `images_registered` | Imagens e fontes em espera e registradas |
| `frames_with_problems` | Ticks em que parte do jogo não pôde ser lida |

Os contadores da própria camada de desenho (cenas, comandos, triângulos, chamadas de desenho, quadros pulados, resets do dispositivo, capturas) são a operação `stats` do componente nativo; o módulo os lê a cada 20º tick e age diante de uma falha.

<a id="what-costs-the-most-and-what-to-do"></a>
## O que custa mais, e o que fazer

| Se você quer menos trabalho | Faça |
| --- | --- |
| Menos por tick | Desligue o radar (`radar=false`) quando quiser só o menu de pausa; um `radar_size_percent` menor desenha menos formas |
| Menos desenho do mapa | `radar_zoom=near` mostra uma área pequena com todo o detalhe; `far` mostra mais com menos detalhe |
| Nada | Não solicite o módulo para essa sessão ([ativando o módulo](../guides/enabling-the-module.md)) |

Um pacote realmap pesado custa tempo de leitura na thread de trabalho, uma vez por janela; depois disso a imagem é desenhada a partir da memória.

Relacionado: [as três partes](the-three-parts.md) · [operações e eventos](../reference/operations-and-events.md) · [solução de problemas](../guides/troubleshooting.md)
