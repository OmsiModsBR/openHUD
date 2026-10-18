# Convivência com outros módulos e plugins

<!-- l10n: source=guides/coexisting.md source-sha256=d75e29b38da1b2a7ebb4e50f48d7655adc4c297dd36663f65c592c5804c212e8 -->
> Tradução da [página original em inglês](../../guides/coexisting.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

O openHUD faz hook de dois métodos do dispositivo Direct3D 9 do jogo, captura o teclado e o mouse enquanto seu menu de pausa está aberto e pode ocultar o menu de pausa nativo do OMSI. Esta página diz o que isso significa ao lado de outros módulos do OmsiLaunch, plugins do OMSI, overlays e ferramentas. As regras do registro de hooks e de conflitos são as da referência de Runtime Modules do OmsiLaunch.

Páginas relacionadas: [segurança](../concepts/safety.md), [compatibilidade](../reference/compatibility.md), [erros e degradação](../reference/errors-and-degradation.md), [solução de problemas](troubleshooting.md).

<a id="declared-conflicts"></a>
## Conflitos declarados

O manifesto não declara nenhum: o openHUD não se recusa a rodar ao lado de nenhum plugin do OMSI.

<a id="other-modules-that-hook-the-direct3d-device"></a>
## Outros módulos que fazem hook do dispositivo Direct3D

Os hooks são registrados em um único registro com seu dono, e hooks de módulos diferentes no mesmo método do dispositivo são empilhados e removidos do último para o primeiro. O detour de `Present` de cada módulo roda em sequência, então dois overlays desenham ambos. Duas coisas podem dar errado:

- **O método do dispositivo já foi alterado por algo fora do registro** (outro injetor, um overlay de captura de tela). A bridge recusa o hook (resultado `4`) e nunca o sobrescreve. O openHUD então não tem evento `hooks`, não desenha nada, e a operação `status` mostra `draw: available` sem nenhuma cena chegando à tela. Leia os eventos do OmsiLaunch para ver a recusa; ela nomeia o dono.
- **Um componente grava de volta os métodos originais do dispositivo** (observado pelo OmsiLaunch em uma instalação logo depois do início do jogo). Um hook do dispositivo deixa então de ser chamado e não pode ser removido; a limpeza reporta `OL_E_MODULE_CLEANUP_INCOMPLETE`.

O openHUD desenha apenas no dispositivo do próprio jogo; o formulário de seleção de veículo cria um segundo dispositivo com os mesmos métodos e a camada de desenho o ignora.

<a id="other-modules-that-capture-input"></a>
## Outros módulos que capturam entrada

Um módulo captura por vez. Só o menu de pausa de um módulo pode estar aberto; se outro módulo detém a captura, a captura do openHUD falha, o menu de pausa vai para seu estado provisório e o menu nativo do OMSI permanece visível.

<a id="other-modules-that-replace-the-pause-menu"></a>
## Outros módulos que substituem o menu de pausa

Dois módulos não podem assumir ambos o menu nativo. O segundo a pedir recebe `OL_E_UI_ALREADY_TAKEN_OVER`; o openHUD então devolve o que mantém e deixa o menu nativo para o outro.

<a id="omsi-plugins-and-overlays"></a>
## Plugins e overlays do OMSI

Plugins que apenas leem ou gravam arquivos do OMSI não são afetados. Overlays que fazem hook dos mesmos métodos Direct3D se comportam como descrito acima. Overlays que desenham em um canto (contadores de quadros, chat) podem cobrir o radar: mova-o com `radar_corner`.

<a id="tools-that-edit-your-files"></a>
## Ferramentas que editam seus arquivos

O openHUD lê pacotes e não grava nada, então não interfere com editores. Ele lê um pacote uma vez por mapa; para ver uma edição, reinicie a sessão.

<a id="several-maps-and-mods"></a>
## Vários mapas e mods

Os pacotes são procurados por pasta de mapa. Um mod que adiciona um pacote de menu de pausa ou realmap a um mapa que você possui vai para a pasta desse mapa como qualquer mod; o openHUD usa o que estiver lá no início da sessão.

Relacionado: [segurança](../concepts/safety.md) · [compatibilidade](../reference/compatibility.md) · [solução de problemas](troubleshooting.md)
