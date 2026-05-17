# Volume Key Router v0.1.4

## Destaques

- Overlay agora aparece quando o usuario troca faixa usando teclas de midia.
- Overlay tambem aparece quando o usuario pausa ou volta a tocar usando tecla de
  midia.
- `Fn+F1` passa a mostrar o overlay com a musica atual, sem alterar volume.
- Enquanto a captura estiver ativa, `F1` e a tecla de abrir app de midia sao
  interceptadas para esse atalho.
- A leitura de midia foi otimizada: titulo/artista aparecem primeiro e a capa
  entra depois, sem segurar a abertura do overlay.
- O overlay reserva o espaco da capa enquanto ela carrega, evitando o texto
  pular de posicao quando a imagem aparece.
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
