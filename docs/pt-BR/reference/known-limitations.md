# Limitações conhecidas

<!-- l10n: source=reference/known-limitations.md source-sha256=2e31a862d07f186d81ef1c48296037ac41905ed7764cfe46a326ff81b1e4ba10 -->
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
| A faixa mostra apenas o relógio | Nenhuma próxima parada, linha, velocidade ou atraso é desenhada, embora a tabela da interface tenha os textos para eles. |
| `units` não tem efeito visível | A configuração é validada e informada, mas nenhum texto que o módulo desenha depende dela; o rótulo de escala está em metros e quilômetros. |
| Apenas com a frente para cima | O radar não tem configuração de modo norte para cima; a página Mapa é norte para cima. |
| Precisa de um tile do jogador resolvido | Sem o tile do jogador na tabela de tiles do mapa, o radar não desenha nada. |
| Os veículos têm uma só forma | Todo veículo ao seu redor é desenhado com o mesmo tipo (sem distinção entre ônibus, bonde ou caminhão). |
| Sem rota | A rota da viagem ativa não é desenhada. |

<a id="packages"></a>
## Pacotes

| Limitação | Detalhe |
| --- | --- |
| Partes dos formatos são lidas mas não desenhadas | Menu de pausa: `stop_icon`, `marker_icon.*`, `mode_icon.*`, `color.brt`, `symbols/omsi2-logo.png`, `symbols/openomsi.png`, `author`, `credit`. Realmap: as cores do manifesto, as chaves do marcador do jogador (`player*`), `brt_lines`, as listas `*_vehicles`, nomes de ruas e limites de velocidade, a linha do município ([pacotes por mapa](../concepts/per-map-packages.md#what-the-module-uses)). |
| Símbolos SVG são validados, não desenhados | A camada de desenho recebe pixels brutos; o módulo decodifica apenas PNG. Um símbolo de mapa em SVG é substituído pela marca genérica (o evento `packages` registra isso). |
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
| O `credit` de um pacote não é mostrado | O rodapé do menu mostra apenas a dica. |

<a id="differences-found-between-the-pieces"></a>
## Diferenças encontradas entre as peças

| Onde | Diferença | Qual vale |
| --- | --- | --- |
| `module.template.json` diz que `language` `auto` "follows OMSI's language" | O módulo não passa nenhum idioma do OMSI ao resolvedor, então `auto` é inglês | O código |
| `module.template.json` diz que `units` serve para "speed and distance on the radar" | Nada do que é desenhado a usa | O código |
| `NOTICE.txt` diz que `OpenHud.Host.exe` "is started by OmsiLaunch as a session program" | O manifesto não tem componente host, e a compilação não o empacota | O manifesto |
| O rascunho do pacote de menu de pausa diz que a pasta pode estar em outra raiz de conteúdo e que `credit` é mostrado no rodapé | O módulo procura apenas na pasta do mapa, e o rodapé não mostra crédito | O código |
| O rascunho diz que a fonte de reserva é Segoe UI | A camada de desenho usa a fonte que o Windows usa nas caixas de mensagem (Segoe UI no Windows atual) | O código |
| O rascunho diz que arquivos de pacote desconhecidos são ignorados e que `@2x` é usado acima da escala 1,5 | O módulo usa a imagem `@2x` acima de `ui_scale_percent` 150 | O código |
| `PauseStrings` (o catálogo do menu) e a tabela da interface definem algumas chaves `UI_PAUSE_*` com textos diferentes ("Back to the game" e "Resume") | O menu mostra os do catálogo | O código ([chaves de idioma da interface](ui-language-keys.md)) |

Relacionado: [status de validação](../status/validation.md) · [pacotes por mapa](../concepts/per-map-packages.md) · [perguntas frequentes](faq.md)
