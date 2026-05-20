# Volume Key Router v0.2.0

## Novidades

- Aba `Overlay` com posicao, largura, duracao, tema claro/escuro e opcao de
  mostrar ou ocultar a capa.
- Aba `Atalhos` com gravacao de combinacoes de teclas.
- Atalhos aceitam modificadores como `Ctrl`, `Shift`, `Alt` e `Win`.
- Botao `↺` ao lado de cada atalho para restaurar somente aquela funcao.
- Botao `Redefinir todos` para voltar todos os atalhos ao padrao.
- Bloqueio de atalhos duplicados na interface.
- Perfis para guardar alvo, dispositivo, passo e atalhos diferentes.
- Menu da bandeja com abrir janela, mostrar overlay, pausar roteamento,
  recarregar dispositivos, trocar perfil e sair.

## Correcoes

- Teclas `Fn+F2/F3/F4` voltam a ser capturadas mesmo quando o driver do teclado
  envia eventos marcados como injetados.
- O app ignora apenas teclas injetadas por ele mesmo ao repassar comandos de
  midia para o Windows.
- Texto selecionado em `Perfil`, `Posicao` e `Tema` nao fica mais preto no tema
  escuro.
- O overlay reposiciona corretamente ao mudar largura ou posicao.
- Duplicatas vindas manualmente do `settings.json` sao desativadas ao carregar.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.2.0.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
