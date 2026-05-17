# Volume Key Router v0.1.5

## Correcao

- `F1` comum nao e mais interceptado pelo app.
- O atalho de mostrar midia fica restrito a tecla especial de abrir app de
  midia, que alguns teclados enviam ao apertar `Fn+F1`.
- README atualizado para explicar que o Windows nao diferencia `Fn+F1` de `F1`
  quando o teclado envia apenas `F1` puro.

## Mantido da v0.1.4

- Overlay por teclas manuais de midia.
- Titulo/artista aparecem antes da capa, sem segurar a abertura do overlay.
- Espaco da capa fica reservado enquanto ela carrega para evitar deslocamento
  do texto.
- Instalador com opcoes `Atualizar` e `Reparar`.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.1.5.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
