# Documentação do openHUD

<!-- l10n: source=README.md source-sha256=ce5045b9dab2ae4f7ec68bd5ea343b3f8601ccc2c4755c6e1e66867f125c1ce3 -->
> Tradução da [página original em inglês](../README.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

[English](../README.md) | Português (Brasil)

Esta é a documentação em português do Brasil do openHUD `0.1.0` (id do módulo `openhud`), o módulo de HUD no jogo e de menu de pausa para o OMSI 2, um Runtime Module do OmsiLaunch. Cada página descreve o que o código atual faz; quando uma página e o código divergem, vale o código e a página contém um bug. As páginas espelham a estrutura da documentação do OmsiLaunch: primeiros passos, conceitos, guias, referência e status.

O módulo é um Runtime Module do OmsiLaunch com o protocolo de bridge 3. A documentação do próprio OmsiLaunch (sua referência de Runtime Modules, a referência da CLI e a referência de perfis de sessão) é a fonte normativa de tudo o que o módulo usa do OmsiLaunch: onde os módulos ficam, integridade, ativação, `/module-setting`, `modules.ini`, presets. Estas páginas apontam para ela pelo nome e repetem apenas o que um usuário deste módulo precisa.

Vocabulário usado em toda a documentação: o módulo é construído e coberto por testes offline; as suas rodadas em um OMSI em execução são agendadas pelo responsável ([status de validação](status/validation.md)); o recurso de Runtime Modules do OmsiLaunch é `EXPERIMENTAL`.

<a id="who-reads-what"></a>
## Quem lê o quê

| Público | Comece aqui | Depois |
| --- | --- | --- |
| Jogadores | [Visão geral](getting-started/overview.md), [Instalação](getting-started/installation.md), [Primeira sessão](getting-started/first-session.md) | [Configurações do radar e da interface](guides/radar-and-interface-settings.md), [Controles](reference/controls.md), [FAQ](reference/faq.md) |
| Jogadores que ajustam ou usam perfis | [Configurações](reference/settings.md), [Ativando o módulo](guides/enabling-the-module.md) | [Perfis de sessão](guides/session-profiles.md), [Desinstalação](guides/uninstalling.md) |
| Jogadores que têm um problema | [Solução de problemas](guides/troubleshooting.md) | [Operações e eventos](reference/operations-and-events.md), [Erros e degradação](reference/errors-and-degradation.md), [Convivência](guides/coexisting.md) |
| Autores de mapas e de conteúdo | [Pacotes por mapa](concepts/per-map-packages.md), [Criando um pacote de menu de pausa](guides/making-a-pause-menu-package.md) | [Formato do pacote de menu de pausa](reference/pause-menu-package.md), [Usando um pacote realmap](guides/using-a-realmap-package.md), [Pacote realmap](reference/realmap-package.md), [Fontes, símbolos e licenciamento](concepts/fonts-symbols-and-licensing.md) |
| Integradores e mantenedores | [As três partes](concepts/the-three-parts.md), [Arquitetura](architecture.md), [Protocolo de cena (em inglês)](../scene-protocol.md) | [Compatibilidade](reference/compatibility.md), [Segurança](concepts/safety.md), [Limitações conhecidas](reference/known-limitations.md), [Cobertura de testes](status/test-coverage.md), [Changelog](../../CHANGELOG.md) |

<a id="navigation"></a>
## Navegação

<a id="getting-started"></a>
### Primeiros passos

| Página | Finalidade |
| --- | --- |
| [Visão geral](getting-started/overview.md) | O que o openHUD é, o que desenha, o que substitui e o que deixa em paz, do que precisa e as suas três partes. |
| [Instalação](getting-started/installation.md) | Requisitos, construção da pasta do módulo, instalação em uma instalação do OmsiLaunch, verificação e atualização. |
| [Primeira sessão](getting-started/first-session.md) | Uma primeira sessão passo a passo com as linhas de comando exatas, o que você deve ver, as teclas e como ler o que o módulo fez. |

<a id="concepts"></a>
### Conceitos

| Página | Finalidade |
| --- | --- |
| [As três partes e por quê](concepts/the-three-parts.md) | O núcleo, o menu de pausa e a camada de desenho, o que cada um conhece e um tick. |
| [O protocolo de cena em palavras simples](concepts/the-scene-protocol.md) | O que é uma cena, como ela chega à tela, os seus limites e o comportamento em caso de falha. |
| [O radar](concepts/the-radar.md) | O que ele mostra, zoom, orientação, posições, tamanho e quando está vazio. |
| [O menu de pausa](concepts/the-pause-menu.md) | Como ele assume o menu nativo do OMSI, os seus estados, o que acontece quando algo falha e as suas páginas. |
| [Pacotes por mapa](concepts/per-map-packages.md) | Os pacotes realmap e de menu de pausa: onde ficam, o que é usado, o que acontece quando faltam. |
| [Fontes, símbolos e licenciamento](concepts/fonts-symbols-and-licensing.md) | O que o módulo distribui (nada), quem é responsável e como o texto é desenhado. |
| [Internacionalização](concepts/internationalization.md) | Idiomas da interface, como o idioma é escolhido e de onde vem cada texto. |
| [Desempenho e a divisão tick/desenho](concepts/performance.md) | O tick, o desenho, os limites e como ver os números. |
| [Segurança](concepts/safety.md) | O que o módulo nunca faz, no que ele mexe, como falha e como para. |

<a id="guides"></a>
### Guias

| Página | Finalidade |
| --- | --- |
| [Ativando o módulo](guides/enabling-the-module.md) | Por sessão, permanentemente, a partir de um perfil; precedência; conferência com um plano. |
| [Configurações do radar e da interface](guides/radar-and-interface-settings.md) | Cada configuração com um exemplo completo. |
| [Usando com perfis de sessão](guides/session-profiles.md) | Presets que levam as configurações do módulo ou o desligam. |
| [Criando um pacote de menu de pausa](guides/making-a-pause-menu-package.md) | Passo a passo com um pacote de exemplo completo e as linhas de log das entradas rejeitadas. |
| [Usando um pacote realmap](guides/using-a-realmap-package.md) | Organizando um pacote realmap com um exemplo completo. |
| [Solução de problemas](guides/troubleshooting.md) | O radar, o menu de pausa, texto, idioma, CPU, pacotes; leitura de eventos e avisos. |
| [Convivendo com outros módulos e plugins](guides/coexisting.md) | Hooks do Direct3D, captura de entrada, o menu de pausa, overlays. |
| [Desinstalação](guides/uninstalling.md) | Para uma sessão, um perfil, todas as sessões e em definitivo. |

<a id="reference"></a>
### Referência

| Página | Finalidade |
| --- | --- |
| [Configurações](reference/settings.md) | Cada chave com tipo, padrão, limites e um exemplo. |
| [Operações e eventos](reference/operations-and-events.md) | As quatro operações e cada evento e aviso, com os dados. |
| [Formato do pacote de menu de pausa](reference/pause-menu-package.md) | Cada arquivo, chave, tipo, padrão, limite e erro, com um modelo. |
| [Pacote realmap](reference/realmap-package.md) | O que o radar lê de um pacote realmap. |
| [Chaves de idioma da interface](reference/ui-language-keys.md) | Cada chave `UI_` em três idiomas. |
| [Controles](reference/controls.md) | Teclado e mouse do menu de pausa. |
| [Compatibilidade e capacidades](reference/compatibility.md) | Recursos do bridge, capacidades, operações e o que acontece sem elas. |
| [Erros e degradação](reference/errors-and-degradation.md) | O que pode dar errado e o que o módulo faz. |
| [Limitações conhecidas](reference/known-limitations.md) | O que ele não faz e as diferenças encontradas. |
| [FAQ](reference/faq.md) | Respostas curtas. |
| [Protocolo de cena (em inglês)](../scene-protocol.md) | O formato normativo de cena entre o núcleo e a camada de desenho. |
| [Arquitetura](architecture.md) | O projeto, para desenvolvedores. |

<a id="status"></a>
### Status

| Página | Finalidade |
| --- | --- |
| [Cobertura de testes](status/test-coverage.md) | Os gates por grupo e o que provam. |
| [Status de validação](status/validation.md) | O que é coberto offline e o que as rodadas em um OMSI em execução vão confirmar. |
| [Changelog](../../CHANGELOG.md) | O que o `0.1.0` contém. |

<a id="how-this-documentation-is-kept-in-sync"></a>
## Como esta documentação é mantida em sincronia

O `tools\Check-Docs.ps1` roda a partir da raiz do repositório e verifica que todo link relativo e âncora em `docs\**\*.md`, `README.md` e `CHANGELOG.md` resolve, que toda configuração de `module.template.json` está na [referência de configurações](reference/settings.md) com seu tipo, padrão e limites e tem a sua própria seção, que toda página está listada acima e que a árvore `docs\pt-BR\` espelha a árvore em inglês (mesmas páginas, mesmos títulos, mesmos blocos de código). Os gates `docs.*` dos testes reconstroem os pacotes de exemplo impressos nos guias a partir do texto da página e os leem com os leitores reais, e passam cada exemplo de configuração pelo interpretador real de configurações ([cobertura de testes](status/test-coverage.md)). Eles não verificam a prosa, então uma página ainda pode estar errada sobre o comportamento; reporte isso como um bug contra a página.

<a id="license"></a>
## Licença

Copyright © 2026 LMonteiro - github.com/lmonteirotech and OmsiModsBR Team - github.com/OmsiModsBR. O openHUD é software livre sob a GNU General Public License, versão 3; usa o OmsiLaunch, que é licenciado sob a GNU Lesser General Public License, somente versão 3. Veja o `LICENSE` e o `NOTICE.txt` do repositório.
