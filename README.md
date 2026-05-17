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
- Intercepta `Volume Up`, `Volume Down` e `Volume Mute`.
- Funciona com `Fn+F2/F3/F4`, quando o notebook envia essas teclas como volume.
- Controla o volume de um aplicativo/processo especifico.
- Controla o volume de uma saida inteira, como `Voicemeeter AUX Input`.
- Mute (`Fn+F4`) afeta apenas o app ou a linha selecionada.
- Se o alvo estiver mutado, `Volume Up` ou `Volume Down` desmuta o alvo antes
  de ajustar o volume.
- Permite escolher o alvo pela interface grafica.
- Salva a ultima escolha e tenta restaura-la ao abrir novamente.
- Se o ultimo app ou dispositivo ainda nao estiver disponivel, procura em
  segundo plano sem ficar piscando a interface.
- Fica no tray, com opcoes para abrir, ativar/pausar captura, atualizar e sair.
- Pode iniciar junto com o Windows.
- Pode iniciar minimizado no tray quando for aberto pelo Windows.
- Impede duas instancias abertas ao mesmo tempo.
- Mostra um overlay proprio quando o volume muda.
- O overlay pode mostrar titulo, artista e capa da musica atual quando o player
  entrega essas informacoes ao Windows.
- Mantem a ultima capa em cache durante ajustes rapidos para evitar piscadas no
  overlay.
- Nao usa a API do Spotify; as informacoes de musica vem dos controles de midia
  do Windows.

## Download

Baixe a versao mais recente na aba **Releases**:

```text
VolumeKeyRouterSetup-x.y.z.exe
```

O instalador inclui todos os arquivos necessarios para Windows x64. Voce nao
precisa instalar o .NET para usar a versao da release.

Ao instalar uma versao nova por cima da antiga, o instalador fecha o app aberto
de forma graciosa, atualiza os arquivos e preserva suas configuracoes.

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
