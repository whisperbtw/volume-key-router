# Volume Key Router v0.1.4

## Destaques

- Overlay agora aparece quando a musica muda.
- Overlay tambem aparece quando o player pausa ou volta a tocar.
- `Fn+F1` passa a mostrar o overlay com a musica atual, sem alterar volume.
- Enquanto a captura estiver ativa, `F1` e interceptado para esse atalho.
- Mantem as melhorias anteriores: capa/titulo/artista no overlay, cache da capa
  para reduzir piscadas e opcoes `Atualizar`/`Reparar` no instalador.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.1.4.exe
```

## Notas

- As informacoes de musica vem dos controles de midia do Windows. Se o player
  nao publicar metadados, o overlay pode mostrar apenas o volume ou nada.
- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
