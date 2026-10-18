# Desinstalação

<!-- l10n: source=guides/uninstalling.md source-sha256=80f6150031524405e8e51dc54745c1ea30852fd9c90277b97418633d34c4f761 -->
> Tradução da [página original em inglês](../../guides/uninstalling.md) do openHUD 0.1.0. A página em inglês é a referência normativa: se as duas divergirem, valem a página em inglês e o código.

Esta página mostra como deixar de usar o openHUD em uma sessão, em um perfil, em todas as sessões, e como removê-lo por completo. Nada do que o módulo faz precisa ser desfeito: ele não grava nada na sua instalação do OMSI e não mantém dados próprios.

Páginas relacionadas: [ativando o módulo](enabling-the-module.md), [perfis de sessão](session-profiles.md), [instalação](../getting-started/installation.md), [segurança](../concepts/safety.md#recovery).

<a id="for-one-session"></a>
## Em uma sessão

```text
OmsiLaunch.exe /new /map:"maps\Example City\global.cfg" /entrypoint-index:1 /no-module:openhud
```

Ou simplesmente não o solicite: a ativação é `requested`.

<a id="for-a-profile"></a>
## Em um perfil

Defina `enabled: false` no bloco `modules` do preset, ou deixe o bloco de fora ([perfis de sessão](session-profiles.md#keeping-the-module-out-of-a-preset)).

<a id="for-every-session"></a>
## Em todas as sessões

Remova `enabled=true` (ou defina `enabled=false`) na seção `[openhud]` de `<OMSI root>\.omsilaunch\modules.ini`:

```ini
[openhud]
enabled=false
```

<a id="removing-the-module"></a>
## Removendo o módulo

1. Feche o OMSI e qualquer sessão do OmsiLaunch.
2. Apague a pasta `<OMSI root>\plugins\OmsiLaunch.Modules\openhud`.
3. Opcionalmente, apague a seção `[openhud]` de `modules.ini` e as entradas `openhud` dos seus perfis de sessão. Se deixadas no lugar, são inofensivas: um preset que diz `enabled: true` para um módulo que não está instalado torna seu plano não executável (`OL_E_MODULE_NOT_FOUND`), e as configurações sozinhas são ignoradas com `OL_W_MODULE_SETTING_IGNORED`.

Não há diretório de dados a limpar: `/clear-module-data:openhud` não tem nada a apagar, porque o módulo não grava ali.

<a id="what-stays"></a>
## O que permanece

Os pacotes de mapa (`openomsi-realmap`, `openomsi-pausemenu`) fazem parte dos seus mapas e permanecem; o OMSI os ignora. O menu de pausa do OMSI permanece inalterado o tempo todo: o openHUD apenas o oculta enquanto uma sessão está em andamento.

Relacionado: [ativando o módulo](enabling-the-module.md) · [instalação](../getting-started/installation.md)
