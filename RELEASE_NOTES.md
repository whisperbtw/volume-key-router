# Volume Key Router v0.1.2

## Destaques

- Overlay agora tenta mostrar titulo, artista e capa da musica atual.
- A capa da musica fica em cache durante ajustes rapidos para reduzir piscadas.
- Se o alvo estiver mutado, `Volume Up` ou `Volume Down` desmuta o app/linha
  selecionado antes de ajustar o volume.
- Mute (`Fn+F4` / `Volume Mute`) continua afetando apenas o app ou linha
  selecionada.
- A leitura de musica/capa e opcional: quando o player nao publica metadados no
  Windows, o overlay segue mostrando apenas o volume.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.1.2.exe
```

## Notas

- As informacoes de musica vem dos controles de midia do Windows. O app nao usa
  API do Spotify.
- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
