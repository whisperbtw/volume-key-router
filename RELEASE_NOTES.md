# Volume Key Router v0.3.1

## Correcao: perfis e atalhos nao resetam mais

- Corrigido o reset total de perfis e atalhos quando o arquivo de
  configuracoes (`%AppData%\volume-key-router\settings.json`) falhava ao ser
  lido.
- A gravacao agora e atomica: primeiro escreve num arquivo temporario e depois
  substitui o arquivo real, entao uma gravacao interrompida nao corrompe mais
  as configuracoes.
- Se o arquivo principal estiver corrompido, o app tenta recuperar do backup
  (`settings.json.bak`), que e atualizado a cada salvamento.
- Se nao houver recuperacao possivel, o arquivo corrompido e preservado como
  `settings.json.corrupt-<data>.json` para recuperacao manual, em vez de ser
  apagado em silencio.
- Atalhos duplicados nao sao mais desativados silenciosamente ao salvar.
  A interface ja bloqueia duplicados; agora a configuracao existente e
  preservada.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.3.1.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
