# Volume Key Router v0.1.1

## Destaques

- Interface migrada para WPF.
- Novo instalador Inno Setup (`VolumeKeyRouterSetup-0.1.1.exe`).
- Instalador atualiza por cima da versao anterior usando o mesmo `AppId`.
- Instalador fecha o app aberto de forma graciosa antes de substituir arquivos.
- Configuracoes continuam em `%AppData%\volume-key-router\settings.json`.
- Mute (`Fn+F4` / `Volume Mute`) agora afeta apenas o app ou linha selecionada.
- Overlay de volume refeito em WPF e ajustado para DPI.
- Salvamento de configuracoes corrigido para nao sobrescrever preferencias ao abrir.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.1.1.exe
```

## Notas

- O app agora depende de arquivos nativos WPF na pasta instalada. Por isso, a
  distribuicao recomendada e o instalador, nao apenas o `volume-key-router.exe`.
- O instalador nao usa assinatura/certificacao.
