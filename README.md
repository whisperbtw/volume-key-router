# Volume Key Router

O **Volume Key Router** e um utilitario para Windows que intercepta as teclas de
volume do teclado e redireciona o ajuste para um alvo especifico: um aplicativo
ou uma saida de audio.

Em vez de diminuir o volume do sistema inteiro, voce pode diminuir so o Spotify,
so o navegador, ou so uma linha como o `Voicemeeter AUX Input`.

Este projeto e open source e foi feito em **vibe coding**. Isso inclui o codigo,
a interface, este README e os textos do projeto.

## Funcionalidades

- Interface WPF com tema escuro.
- Intercepta `Volume Up`, `Volume Down` e `Volume Mute` por padrao.
- Permite escolher quais teclas ou combinacoes acionam volume, mute e overlay
  de midia.
- Funciona com `Fn+F2/F3/F4`, quando o notebook envia essas teclas como volume.
- Controla o volume de um aplicativo/processo especifico.
- Controla o volume de uma saida inteira, como `Voicemeeter AUX Input`.
- Mute (`Fn+F4`) afeta apenas o app ou a linha selecionada.
- Se o alvo estiver mutado, `Volume Up` ou `Volume Down` desmuta o alvo antes
  de ajustar o volume.
- Permite escolher o alvo pela interface grafica.
- Mostra o icone do processo na lista de apps/sessoes quando o Windows permite.
- Salva a ultima escolha e tenta restaura-la ao abrir novamente.
- Se o ultimo app ou dispositivo ainda nao estiver disponivel, procura em
  segundo plano sem ficar piscando a interface.
- Tem perfis para guardar alvo, dispositivo, passo e atalhos diferentes.
- Fica no tray, com opcoes para abrir, mostrar overlay, pausar roteamento,
  recarregar dispositivos, trocar perfil e sair.
- Pode iniciar junto com o Windows.
- Pode iniciar minimizado no tray quando for aberto pelo Windows.
- Permite verificar manualmente se existe uma nova release na aba `Sistema`.
- Impede duas instancias abertas ao mesmo tempo.
- Mostra um overlay proprio quando o volume muda.
- Permite ajustar posicao, tamanho por preset, duracao, tema e exibicao da capa
  no overlay.
- O overlay pode mostrar titulo, artista e capa da musica atual quando o player
  entrega essas informacoes ao Windows.
- O overlay aparece quando o usuario troca faixa, pausa ou volta a tocar usando
  teclas de midia.
- `Fn+F1` mostra o overlay com a musica atual, sem precisar mudar o volume.
- Mantem a ultima capa em cache durante ajustes rapidos para evitar piscadas no
  overlay.
- Reserva o espaco da capa enquanto ela carrega, evitando que texto e barra
  mudem de posicao no meio da exibicao.
- Nao usa a API do Spotify; as informacoes de musica vem dos controles de midia
  do Windows.
- Atalho global para curtir no Spotify desktop, sem chave de API ou token.
  Aciona a janela em segundo plano sem trocar o foco ou mover o mouse.

## Download

Baixe a versao mais recente na aba **Releases**:

```text
VolumeKeyRouterSetup-x.y.z.exe
```

O instalador inclui todos os arquivos necessarios para Windows x64. Voce nao
precisa instalar o .NET para usar a versao da release.

Ao instalar uma versao nova por cima da antiga, o instalador fecha o app aberto
de forma graciosa, atualiza os arquivos e preserva suas configuracoes.
Quando uma instalacao existente e detectada, o instalador mostra as opcoes
`Atualizar` e `Reparar` antes de continuar.

Se o Windows bloquear o arquivo por ele ter vindo da internet:

1. Clique com o botao direito no instalador.
2. Abra **Propriedades**.
3. Marque **Desbloquear**.
4. Clique em **OK**.

## Como Usar

1. Instale e abra o Volume Key Router.
2. Escolha o dispositivo de saida.
3. Escolha um modo:
   - `App selecionado`: controla apenas o app selecionado na lista.
   - `Linha/dispositivo selecionado`: controla a saida de audio inteira.
4. Clique em `Ativar captura`.
5. Use as teclas de volume do teclado.
6. Use `Fn+F1` para mostrar a musica atual no overlay sem mudar o volume.

As abas `Overlay`, `Atalhos`, `Perfis` e `Sistema` concentram as preferencias
visuais, teclas capturadas, perfis de uso, opcoes de inicializacao e verificacao
manual de atualizacao. Na aba `Atalhos`, clique no botao da acao e pressione a
tecla ou combinacao que deve acionar aquela funcao, como `Ctrl+Alt+F2`.

## Overlay de Midia e Fn+F1

Com a captura ativa, `Fn+F1` mostra o overlay com a musica atual. Ele serve
como um atalho rapido para ver o que esta tocando, incluindo titulo, artista e
capa quando o player entrega essas informacoes ao Windows.

O Volume Key Router intercepta a tecla especial de abrir app de midia que alguns
teclados enviam ao apertar `Fn+F1`. O `F1` comum continua livre para funcionar
normalmente nos outros aplicativos.

O overlay tambem aparece quando o usuario troca faixa, pausa ou volta a tocar
usando teclas de midia. Trocas automaticas feitas pelo player nao abrem o
overlay sozinhas.
Ao avancar ou voltar faixa manualmente, ele espera metadados atualizados para
evitar mostrar a musica anterior.

Esse atalho pode ser alterado na aba `Atalhos`. Se voce escolher `F1`, o `F1`
comum passa a ser capturado porque essa foi uma escolha explicita.

### Curtir no Spotify sem mudar o foco

Pressione `Ctrl+Alt+L` para adicionar a musica atual do Spotify a Musicas
Curtidas. O atalho pode ser alterado ou desativado na aba `Atalhos`, em
`Curtir no Spotify`, e acompanha os perfis. A captura precisa estar ativa.
Uma musica ja curtida continua curtida; o atalho nao remove curtidas.

Depois da acao, o overlay mostra a confirmacao, a capa e o nome da musica/artista,
sem botao clicavel nem barra de volume. Se o Spotify nao confirmar a curtida,
o overlay informa a falha. Segurar a tecla nao repete a acao.

O app le o controle pela acessibilidade do Windows e envia mensagens de clique
somente para a janela do Spotify. Nao traz o Spotify para frente, nao envia
atalhos globais e nao move o cursor. A confirmacao aparece no overlay.

Deixe a janela do Spotify aberta atras das outras janelas, sem minimizar.
Janelas minimizadas nao sao restauradas automaticamente. Esta integracao
suporta os controles em portugues e ingles e depende da interface do Spotify;
se os controles estiverem indisponiveis, o overlay informa a falha. Spotify Web
nao e suportado pelo atalho de curtir. O foco foi verificado em um teste com
outra janela ativa; jogos em tela cheia exclusiva ainda nao foram verificados.

## Exemplo: Voicemeeter AUX

Para controlar so a linha auxiliar do Voicemeeter:

1. Em `Dispositivo de saida`, escolha `Voicemeeter AUX Input`.
2. Marque `Linha/dispositivo selecionado`.
3. Use as teclas de volume normalmente.

## Inicializacao Com Windows

- `Iniciar com Windows`: adiciona o app a inicializacao do usuario atual.
- `Iniciar minimizado com Windows`: quando o Windows abrir o app, ele ja nasce
  direto no tray.
- Se voce abrir o app manualmente, a janela aparece normalmente.

## Configuracoes

As configuracoes ficam em:

```text
%AppData%\volume-key-router\settings.json
```

Elas nao ficam dentro da pasta de instalacao. Por isso, atualizar ou reinstalar
o app nao apaga suas preferencias.

## Desenvolvimento

Requisitos:

- Windows 10/11
- .NET SDK 10
- Inno Setup 6, apenas para gerar o instalador

Compilar:

```powershell
dotnet build .\VolumeKeyRouter.csproj -c Release
```

Rodar pelo SDK:

```powershell
dotnet run --project .\VolumeKeyRouter.csproj -c Release
```

Gerar a pasta publicada self-contained:

```powershell
.\publish-win-x64.ps1
```

Saida:

```text
publish\win-x64\
```

Gerar o instalador Inno Setup:

```powershell
.\build-installer.ps1
```

Saida:

```text
dist\VolumeKeyRouterSetup-x.y.z.exe
```

## Estrutura

```text
App\        entrada, CLI, settings e inicializacao com Windows
Audio\      integracao com audio do Windows via NAudio
Core\       modelos compartilhados
Interop\    chamadas nativas do Windows
Keyboard\   hook global das teclas de volume
UI\         janelas WPF, tray e overlay
installer\  script do Inno Setup
```

## CLI Opcional

A interface grafica e o uso principal, mas existe uma CLI simples para
diagnostico:

```powershell
.\publish\win-x64\volume-key-router.exe --cli --devices
.\publish\win-x64\volume-key-router.exe --cli --list
.\publish\win-x64\volume-key-router.exe --cli --process Spotify --step 3
```

## Observacoes

- A tecla `Fn` normalmente nao chega ao Windows. O app captura o evento de
  volume gerado pelo driver ou firmware do teclado.
- Em alguns teclados, `Fn+F1` chega como tecla de abrir app de midia. Essa tecla
  e interceptada para mostrar o overlay em vez de abrir outro app.
- O `F1` comum nao e interceptado. Se o seu teclado enviar `Fn+F1` como `F1`
  puro, o Windows nao permite diferenciar `Fn+F1` de `F1`.
- Um app so aparece na lista quando o Windows cria uma sessao de audio para ele.
- Titulo, artista e capa no overlay dependem do player publicar metadados para
  os controles de midia do Windows. Se o player nao publicar, o overlay continua
  funcionando apenas com o volume.
- Se voce usa Spotify Web, o processo pode aparecer como `chrome`, `msedge` ou
  `firefox`, nao como `Spotify`.
- Se um app roda como administrador e o hook nao pega as teclas nesse contexto,
  rode o Volume Key Router como administrador tambem.

## Licenca

MIT. Pode usar, modificar e distribuir.
