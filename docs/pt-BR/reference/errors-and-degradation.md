# Erros e degradação

<!-- l10n: source=reference/errors-and-degradation.md source-sha256=f9fb3d3047f730d684d482f67c29f3c09f199978de63af7387c9bf469584c78f -->
> Tradução da [página original em inglês](../../reference/errors-and-degradation.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD foi feito para falhar pouco: sua política de falha é `degrade`, e cada parte continua funcionando quando outra não consegue. Esta página lista o que pode dar errado, o que indica o problema (um código do OmsiLaunch, um evento ou uma linha de log), o que o módulo faz a respeito e o que você pode fazer. Os códigos são do OmsiLaunch; a página diz o que significam para este módulo.

Páginas relacionadas: [operações e eventos](operations-and-events.md), [solução de problemas](../guides/troubleshooting.md), [o menu de pausa](../concepts/the-pause-menu.md#when-something-fails), [compatibilidade](compatibility.md), [segurança](../concepts/safety.md).

<a id="the-policy"></a>
## A política

Sob `degrade`, uma falha deixa o módulo na sessão: a chamada que falhou devolve o seu código, o bridge informa `module.degraded` uma vez por código, e o módulo continua ativo. O módulo acrescenta a sua própria contenção: todo callback é protegido, então nada escapa para o bridge, e o que continua falhando é desligado, nunca repetido em laço. A sessão nunca falha por causa do openHUD.

<a id="at-planning-omsilaunch"></a>
## No planejamento (OmsiLaunch)

| Situação | Código | O que acontece | O que fazer |
| --- | --- | --- | --- |
| Um arquivo da pasta do módulo mudou, está faltando ou é alcançado por um link | `OL_E_MODULE_INTEGRITY` | Não é carregado; com `/module:openhud` o plano não é executável | Reinstale ([instalação](../getting-started/installation.md)) |
| `module.json` ausente ou inválido, ou a pasta não se chama `openhud` | `OL_E_MODULE_MANIFEST_INVALID`, `OL_E_MODULE_NOT_FOUND` | Não é carregado | Reinstale |
| O OmsiLaunch não serve o protocolo de bridge 3 | `OL_E_MODULE_PROTOCOL_UNSUPPORTED` | Não é carregado | Use um OmsiLaunch mais novo |
| A build do OMSI não pode fornecer um recurso obrigatório (`bridge.hooks.d3d9`, `bridge.input` ...) | `OL_E_MODULE_CAPABILITY_UNRESOLVED` | Não é carregado | Veja [compatibilidade](compatibility.md) |
| O valor de uma configuração é inválido ou a chave não existe | `OL_E_MODULE_SETTING_INVALID` | O plano não é executável | Corrija o valor |
| Uma entrada de `modules.ini` não pode ser usada | `OL_W_MODULE_SETTING_IGNORED` | Usa-se o padrão | Corrija a linha |
| Um preset e a linha de comando definem a mesma chave | `OL_E_SESSION_PROFILE_OVERRIDE_CONFLICT` | Não executável | Defina em um só lugar |

<a id="inside-omsi"></a>
## Dentro do OMSI

| Situação | O que o módulo faz | Indicado por |
| --- | --- | --- |
| O componente nativo não pode ser usado | O módulo roda sem desenhar; nada é mostrado | Aviso "The draw layer is not available, so openHUD shows nothing."; `armed` `draw: unavailable` |
| Os hooks Direct3D não podem ser instalados | O mesmo: nenhuma cena chega à tela | Nenhum evento `hooks`; evento de recusa de hook do OmsiLaunch |
| Uma configuração é inutilizável | Usa-se o padrão | `settings-issue` |
| Uma parte do jogo não pode ser lida | Essa parte do snapshot fica indisponível; as outras não são afetadas | `read-problem` (uma vez por tipo) |
| O veículo ou o tile do jogador é desconhecido | O radar espera | `radar_state` em `status` |
| Uma cena é recusada pela camada de desenho | A cena anterior permanece; cinco recusas seguidas desligam o radar | `scene_failures`; aviso "The radar was turned off after repeated errors." |
| A camada de desenho falha ao desenhar | Ela para de desenhar; o módulo tenta duas vezes, depois desliga o radar e devolve o menu de pausa nativo | `draw-failed`, `draw_failed`; aviso |
| Uma falha no radar | Contada e informada uma vez; cinco seguidas desligam o radar | `fault`; aviso |
| Uma chamada do menu de pausa falha | Tudo é devolvido; o menu nativo é mostrado apenas com o mapa atrás | `pause_state` `Provisional`, `pause_last_error` |
| O menu de pausa continua falhando | Ele desiste pelo resto da sessão | `pause-degraded`; aviso "openHUD's pause menu is not available; OMSI's own menu stays." |
| `OL_E_CAPABILITY_UNRESOLVED` de uma chamada `ui.pause.*` | O módulo para de perguntar | `pause_unavailable` `true` |
| Um pacote não pode ser usado | O pacote é ignorado; vale o comportamento genérico | diagnósticos de `packages`; aviso "A map package could not be used and was ignored." |
| Uma entrada ruim em um pacote | A entrada é descartada | Linha `Dropped:` em `packages` |
| O carregamento dos pacotes lança exceção | Os pacotes são ignorados | `package-problem` |
| Um tick lança exceção ou retorna tarde | Informado pelo bridge uma vez por código; ticks pulados não entram em fila | `module.degraded` |
| O módulo para | Tudo é liberado ([segurança](../concepts/safety.md#stopping)) | `stopping` |

<a id="package-log-lines"></a>
## Linhas de log de pacotes

Um problema de pacote é uma linha `Severity: entry: reason`. `Dropped` é uma entrada deixada de fora, `Ignored` um pacote inteiro ou recurso não usado, `Info` uma observação (como `N more entries not listed` depois de 200 linhas). Cada motivo está listado com o seu arquivo nas referências do [pacote de menu de pausa](pause-menu-package.md) e do [pacote realmap](realmap-package.md).

<a id="codes-of-the-pause-operations"></a>
## Códigos das operações de pausa

| Código | Significado para o openHUD |
| --- | --- |
| `OL_E_UI_NOT_PAUSED` | O jogo não está em pausa: nada a assumir; volta a Running |
| `OL_E_UI_BUSY` | Um diálogo nativo está aberto, ou o OMSI está dentro do seu frame; o módulo tenta assumir de novo por um segundo |
| `OL_E_UI_ALREADY_TAKEN_OVER` | Algo já mantém o controle; o módulo o devolve |
| `OL_E_CAPABILITY_UNRESOLVED` | Esta build não consegue fazer isso; fixado |
| `OL_E_RUNTIME_OPERATION_FAILED` | A operação falhou; limpeza após falha |

Relacionado: [solução de problemas](../guides/troubleshooting.md) · [operações e eventos](operations-and-events.md) · [compatibilidade](compatibility.md)
