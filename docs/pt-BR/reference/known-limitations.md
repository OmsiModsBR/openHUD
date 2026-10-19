# Limitações conhecidas

<!-- l10n: source=reference/known-limitations.md source-sha256=d1fe43f2c615d23e2a2c8117cf0b2ce5e14b2d105cca098f39aac90160d2aa5a -->
> Tradução da [página original em inglês](../../reference/known-limitations.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página lista, a partir do código, o que o openHUD `0.1.0` não faz, faz só em parte ou faz com um risco aceito, para que você não crie expectativas sobre um comportamento que ele não oferece. Onde o manifesto, o aviso, os rascunhos de especificação e o código divergem, vale o código; as diferenças encontradas estão listadas no final.

Páginas relacionadas: [status de validação](../status/validation.md), [compatibilidade](compatibility.md), [erros e degradação](errors-and-degradation.md), [perguntas frequentes](faq.md).

<a id="validation"></a>
## Validação

| Limitação | Detalhe |
| --- | --- |
| Ainda não executado em um OMSI real | O módulo é compilado e coberto apenas por testes offline. Assumir o menu de pausa nativo, a captura de entrada na janela real, os hooks Direct3D no dispositivo real e as convenções de posição e unidade que o modelo infere são confirmados em rodadas que o dono do projeto agenda ([status de validação](../status/validation.md)). |
| Os Runtime Modules são `EXPERIMENTAL` | O recurso do OmsiLaunch que carrega este módulo é experimental. |

<a id="the-radar"></a>
## O radar

| Limitação | Detalhe |
| --- | --- |
| Sem próxima parada, linha nem atraso na faixa | A faixa mostra a velocidade e o relógio. O catálogo não oferece fonte para a próxima parada, a linha nem o atraso, então esses espaços ficam vazios (a tabela da interface tem os seus textos). |
| A unidade da velocidade é uma suposição | O `ground_speed` do catálogo é lido como metros por segundo, o que é inferido, não confirmado (uma constante nomeada, `FrameSource.GroundSpeedToMetresPerSecond`). A velocidade na faixa vale o que vale essa suposição até a rodada em tempo de execução confirmá-la ([status de validação](../status/validation.md)). |
| Apenas com a frente para cima | O radar não tem configuração de modo norte para cima; a página Mapa é norte para cima. |
| Precisa de um tile do jogador resolvido | Sem o tile do jogador na tabela de tiles do mapa, o radar não desenha nada. |
| Os veículos têm uma só forma | Todo veículo ao seu redor é desenhado com o mesmo tipo (sem distinção entre ônibus, bonde ou caminhão). |
| Sem rota | A rota da viagem ativa não é desenhada. |

<a id="packages"></a>
## Pacotes

| Limitação | Detalhe |
| --- | --- |
| Partes dos formatos são lidas mas não desenhadas | Menu de pausa: `author` (para as pessoas). Realmap: `player_line` e `player_brt` (precisam da linha ativa do jogador, que o catálogo não dá), as cores `light_*` (o radar é sempre escuro), as listas `*_vehicles`, nomes de ruas e limites de velocidade, a linha do município, `alignment`, o `credit` do manifesto ([pacotes por mapa](../concepts/per-map-packages.md#what-the-module-uses)). |
| SVG é um subconjunto estático | Retângulos, círculos, elipses, linhas, polígonos e caminhos com cores lisas são desenhados como formas da cena. Gradientes, padrões, filtros, máscaras, caminhos de recorte, texto, `use` e animação são ignorados com um motivo; furos `evenodd` num caminho são preenchidos; tracejados são desenhados contínuos; um desenho acima do seu orçamento de comandos é recusado e a marca genérica é desenhada ([o subconjunto de SVG](pause-menu-package.md#the-svg-subset)). |
| `theme` e `layout` só escolhem o símbolo | Eles não mudam o leiaute nem as cores do menu. |
| O pacote de menu de pausa é procurado apenas na pasta do próprio mapa | Um pacote colocado em outra raiz de conteúdo não é encontrado. |
| Um pacote é lido uma vez por mapa | As edições aparecem após reiniciar a sessão. No máximo oito mapas são carregados em uma sessão. |
| No máximo 48 ícones de marcador e um símbolo | Os ícones adicionais ficam de fora (uma observação no evento `packages`). |

<a id="the-pause-menu"></a>
## O menu de pausa

| Limitação | Detalhe |
| --- | --- |
| Precisa de três condições no início | A camada de desenho, `ui.pause-menu.replace` e a captura de entrada; caso contrário, o menu nativo permanece. |
| A página Linhas é uma lista | Selecionar ou ativar uma linha não faz mais nada. |
| O mapeamento do mouse precisa do tamanho da view | O tamanho vem da camada de desenho depois do seu primeiro frame; antes disso o menu assume 1920 x 1080. |
| Um menu por vez | Outro módulo que capture entrada ou substitua o menu de pausa impede isso ([convivência](../guides/coexisting.md)). |

<a id="text-and-language"></a>
## Texto e idioma

| Limitação | Detalhe |
| --- | --- |
| Interface em três idiomas | Inglês, português (Brasil) e alemão. |
| `language=auto` resolve para inglês | O módulo resolve o idioma apenas pela configuração; ainda não lê o idioma do OMSI. Defina `language` explicitamente ([internacionalização](../concepts/internationalization.md)). |
| Da esquerda para a direita, sem shaping, sem kerning | Escritas da direita para a esquerda e complexas não são suportadas; caracteres fora do Plano Multilíngue Básico são desenhados em branco. |
| Os glifos vêm do GDI | As fontes são registradas de forma privada ao processo; uma fonte que o Windows recusa é informada e a fonte do sistema é usada. |

<a id="scope"></a>
## Escopo

| Limitação | Detalhe |
| --- | --- |
| Sem programa de sessão | `OpenHud.Host` no repositório é um espaço reservado vazio; o manifesto não declara componente host. |
| Sem diretório de dados | O módulo não guarda nada entre sessões. |
| Sem janela de configurações nem configurações no jogo | As configurações são as configurações de módulo do OmsiLaunch. |

<a id="differences-found-between-the-pieces"></a>
## Diferenças encontradas entre as peças

| Onde | Diferença | Qual vale |
| --- | --- | --- |
| `module.template.json` diz que `language` `auto` "follows OMSI's language" | O módulo não passa nenhum idioma do OMSI ao resolvedor, então `auto` é inglês | O código |
| `NOTICE.txt` diz que `OpenHud.Host.exe` "is started by OmsiLaunch as a session program" | O manifesto não tem componente host, e a compilação não o empacota | O manifesto |
| O rascunho do pacote de menu de pausa diz que a pasta pode estar em outra raiz de conteúdo | O módulo procura apenas na pasta do mapa | O código |
| O rascunho diz que a alternativa da marca do OMSI 2 é o texto "OMSI 2" | A marca genérica é uma etiqueta neutra com contorno e um volante; o openHUD não escreve nenhum texto de marca | O código |
| O rascunho diz que o rodapé mostra apenas as versões e o crédito | O rodapé mostra também a dica com as teclas | O código |
| O rascunho diz que a fonte de reserva é Segoe UI | A camada de desenho usa a fonte que o Windows usa nas caixas de mensagem (Segoe UI no Windows atual) | O código |
| O rascunho diz que arquivos de pacote desconhecidos são ignorados e que `@2x` é usado acima da escala 1,5 | O módulo usa a imagem `@2x` acima de `ui_scale_percent` 150 | O código |
| `PauseStrings` (o catálogo do menu) e a tabela da interface definem algumas chaves `UI_PAUSE_*` com textos diferentes ("Back to the game" e "Resume") | O menu mostra os do catálogo | O código ([chaves de idioma da interface](ui-language-keys.md)) |

Relacionado: [status de validação](../status/validation.md) · [pacotes por mapa](../concepts/per-map-packages.md) · [perguntas frequentes](faq.md)
