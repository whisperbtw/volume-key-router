# Volume Key Router v0.3.0

## Novidades

- Aba `Sistema` agora tem o botao `Verificar atualizacao`.
- A verificacao manual compara a versao instalada com a ultima release do
  GitHub e pode abrir a pagina da release quando houver versao nova.
- O tamanho do overlay agora usa presets de tamanho total.
- Novo preset `Muito pequeno` para quem quer um overlay bem compacto.
- Presets disponiveis: `Muito pequeno`, `Pequeno`, `Padrao` e `Grande`.

## Ajustes do overlay e da midia

- O overlay aplica largura e altura pelos presets, em vez de ajustar so a
  largura.
- O preset `Muito pequeno` usa padding, capa, textos e barra reduzidos para nao
  ficar apenas espremido.
- Ao avancar ou voltar faixa manualmente, o overlay espera metadados atualizados
  antes de mostrar a musica, reduzindo o caso de aparecer a faixa anterior.
- Enquanto a proxima faixa ainda nao chegou pelos controles de midia do Windows,
  o overlay mostra `Trocando midia`.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.3.0.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
