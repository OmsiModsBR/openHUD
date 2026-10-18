# Segurança

<!-- l10n: source=concepts/safety.md source-sha256=b452a919b1fc1ea351805ab39c0e2ca356823783b47217438dd18df6115f730a -->
> Tradução da [página original em inglês](../../concepts/safety.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD roda dentro do OMSI com os direitos do usuário que iniciou a sessão, como qualquer plugin do OMSI. Esta página lista o que o módulo nunca faz, no que ele toca e como, como ele falha e o que acontece quando ele para. Essas são propriedades do código, garantidas pela sua estrutura e verificadas pelos testes offline quando um teste consegue verificá-las; não são promessas sobre o OMSI em si.

Páginas relacionadas: [as três partes](the-three-parts.md), [o menu de pausa](the-pause-menu.md), [pacotes por mapa](per-map-packages.md), [erros e degradação](../reference/errors-and-degradation.md), [convivência](../guides/coexisting.md), [desinstalação](../guides/uninstalling.md).

<a id="what-the-module-never-does"></a>
## O que o módulo nunca faz

| Ele nunca | Como isso é verdade |
| --- | --- |
| Contém um endereço de memória, um deslocamento, uma assinatura ou um ponteiro do OMSI | Tudo o que ele sabe sobre o jogo vem das operações públicas do OmsiLaunch (`time.read`, `map.read`, `player-vehicle.read`, `ui.pause.*` e as demais do manifesto). Os endereços da build exata pertencem aos perfis de build do OmsiLaunch, não aos módulos. O código gerenciado recebe texto, nunca um ponteiro; o código nativo recebe um ponteiro de dispositivo apenas para compará-lo com o do jogo e desenhar nele. |
| Envia uma tecla ou um clique do mouse | Não há chamada para enviar entrada no código. O menu de pausa lê os eventos capturados e pede ao OMSI que execute seus handlers por meio de `ui.pause.action` do OmsiLaunch, que os chama diretamente. |
| Automatiza as janelas do OMSI | Nenhuma janela é encontrada, clicada, movida ou lida. O menu de pausa nativo é ocultado e mostrado de novo pelas operações do OmsiLaunch. |
| Grava em um arquivo da instalação do OMSI | O módulo não tem caminho de escrita: lê pacotes e não grava nada. Nem sequer usa o seu diretório de dados. |
| Grava em um pacote | Os leitores de pacote abrem os arquivos somente para leitura e recusam links. |
| Instala qualquer coisa no OMSI quando é compilado | `tools\Build-Module.ps1` grava apenas na pasta `artifacts` do repositório. Instalar é uma cópia que você mesmo faz ([instalação](../getting-started/installation.md)). |
| Altera um add-on, uma licença ou um direito de uso | Ele não lê nenhum deles. |
| Abre uma conexão de rede ou um programa | Não tem código de rede e não inicia nenhum processo. |

<a id="what-it-does-touch"></a>
## No que ele toca

| O quê | Como | Por quanto tempo |
| --- | --- | --- |
| O dispositivo Direct3D 9 do jogo | Faz hook de `Present` e `Reset` por meio do registro de hooks do OmsiLaunch (empilhado, nunca sobrescrevendo um hook que não é dele) e desenha apenas no dispositivo do jogo | Até o módulo parar; os hooks são removidos pela bridge, e a DLL continua carregada, inerte, até o OMSI sair |
| O back buffer | Enquanto o menu de pausa está aberto, a camada de desenho copia a última imagem do jogo para uma textura que serve de fundo do menu | Liberada quando o menu fecha e na parada |
| As mensagens de teclado e mouse da janela do jogo | Enquanto o menu de pausa está aberto (`bridge.input`): o OmsiLaunch registra os eventos e os remove da fila para que o OMSI não aja sobre eles | Liberadas antes de qualquer diálogo nativo abrir, quando o menu fecha e na parada |
| O menu de pausa nativo do OMSI | Ocultado e mostrado de novo por `ui.pause.take-over` e `ui.pause.restore-native`; suas ações são executadas por `ui.pause.action` | Devolvido em qualquer falha e na parada, enquanto o jogo ainda estiver pausado |
| Fontes | Registradas apenas para este processo | Removidas na parada |
| Memória | Imagens (no máximo 32 MiB), atlas de glifos e os dados já analisados dos pacotes | Liberada na parada |

<a id="package-safety"></a>
## Segurança dos pacotes

Os dois leitores de pacote tratam um pacote como dado hostil. Cada caminho é verificado antes de um arquivo ser aberto (nenhum caminho absoluto, nenhum `..`, nenhuma unidade, nenhum nome de dispositivo, nenhum link no caminho, nada fora da pasta), cada tamanho e contagem é limitado, cada número é verificado como finito e plausível, e um arquivo que falha é descartado sozinho. Um bug no leitor não pode derrubar o menu de pausa: uma exceção inesperada termina no menu genérico. Veja [pacote de menu de pausa](../reference/pause-menu-package.md#how-the-files-are-read) e [pacote realmap](../reference/realmap-package.md).

O texto que vem de um pacote ou do jogo é dado. Ele é limitado, tem os caracteres de controle removidos e é desenhado; nada nele é executado, aberto ou seguido.

<a id="how-it-fails"></a>
## Como ele falha

A política de falha no manifesto é `degrade`: uma falha custa a parte que falhou e o módulo permanece. Cada parte é envolvida de modo que nada escape de um callback:

- uma leitura do jogo que falha torna aquela parte do snapshot "indisponível" e não afeta as outras;
- uma falha no radar é contada e dita uma vez; cinco seguidas desligam o radar com um aviso;
- uma falha no menu de pausa devolve o menu nativo e deixa apenas o mapa atrás dele ([o menu de pausa](the-pause-menu.md#when-something-fails));
- uma falha na camada de desenho é reportada, repetida duas vezes e depois desliga o radar e devolve o menu nativo;
- um problema em um pacote ignora o pacote.

A bridge aplica os próprios limites por cima: um tick ou uma operação que retorna tarde é uma falha daquela chamada, reportada uma vez por código, e um módulo que nunca retorna não pode ser interrompido pela bridge ([erros e degradação](../reference/errors-and-degradation.md)).

<a id="stopping"></a>
## Parada

Quando a sessão termina (ou o módulo falha), a bridge chama `Stop()`. O módulo:

1. diz à camada de desenho para parar de desenhar;
2. para o menu de pausa: libera a captura, libera a imagem de fundo e, se ainda oculta o menu nativo do OMSI e o jogo ainda está pausado, o restaura; qualquer parte que não pôde ser feita de imediato (por exemplo, enquanto um diálogo nativo está aberto) é repetida a cada 250 ms;
3. substitui a cena por uma vazia e libera o fundo;
4. emite `module.openhud.stopping`.

Depois que o módulo parou, toda chamada à camada de desenho é recusada, seus detours repassam para as funções originais e suas fontes e imagens somem.

<a id="recovery"></a>
## Recuperação

O módulo não mantém nenhum arquivo de instalação, então não há nada a recuperar depois de uma falha: sua memória, seus hooks e a entrada capturada terminam com o processo do OMSI. Uma falha no meio de uma pausa deixa o OMSI como o OMSI está; a próxima sessão começa limpa.

Relacionado: [o menu de pausa](the-pause-menu.md) · [pacotes por mapa](per-map-packages.md) · [convivência](../guides/coexisting.md)
