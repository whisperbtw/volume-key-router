# Volume Key Router v0.4.0

## Seguir o dispositivo padrao e detectar saidas novas (Bluetooth)

- Nova opcao `Seguir o dispositivo padrao do Windows` na aba principal.
  Quando marcada, o app acompanha automaticamente a saida ativa no Windows:
  se voce trocar o padrao (fone Bluetooth, cabo, alto-falante), o alvo troca
  junto, sem precisar abrir o app.
- A lista de dispositivos agora atualiza sozinha quando uma saida entra ou sai:
  conectar um fone Bluetooth faz a nova saida aparecer na hora, sem clicar em
  `Atualizar`.
- Ao seguir o padrao, o app tenta manter o app selecionado: se a sessao existir
  na nova saida, ela e restaurada. Em modo `Linha/dispositivo selecionado`, o
  alvo passa a ser a nova saida padrao.
- Desconectar o fone Bluetooth volta o alvo para a saida padrao restante.
- A opcao pode ser salva por perfil e fica desligada por padrao, preservando o
  comportamento atual de alvo fixo.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.4.0.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
