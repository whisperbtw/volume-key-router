# Volume Key Router

O **Volume Key Router** é um utilitário para Windows que intercepta as teclas de
volume do teclado e redireciona o ajuste para um alvo específico: um aplicativo
ou uma saída de áudio.

Em vez de diminuir o volume do sistema inteiro, você pode diminuir só o Spotify,
só o navegador, ou só uma linha como o `Voicemeeter AUX Input`.

Este projeto é open source e foi feito em **vibe coding**. Isso inclui o código,
a interface, este README e os textos do projeto.

## Funcionalidades

- Intercepta `Volume Up` e `Volume Down` do teclado.
- Funciona com `Fn+F2/F3`, quando o notebook envia essas teclas como volume.
- Controla o volume de um aplicativo/processo específico.
- Controla o volume de uma saída inteira, como `Voicemeeter AUX Input`.
- Permite escolher o alvo pela interface gráfica.
- Salva a última escolha e tenta restaurá-la ao abrir novamente.
- Se o último app ou dispositivo ainda não estiver disponível, procura em
  segundo plano sem ficar piscando a interface.
- Fica no tray, com opções para abrir, ativar/pausar captura, atualizar e sair.
- Pode iniciar junto com o Windows.
- Pode iniciar minimizado no tray quando for aberto pelo Windows.
- Impede duas instâncias abertas ao mesmo tempo.
- Mostra um overlay próprio quando o volume muda.
- Não usa a API do Spotify.

## Download

Baixe a versão mais recente na aba **Releases**:

```text
volume-key-router.exe
```

O executável da release é **self-contained**, então já inclui o runtime .NET.
Você não precisa instalar o .NET para usar essa versão.

Se o Windows bloquear o arquivo por ele ter vindo da internet:

1. Clique com o botão direito no `.exe`.
2. Abra **Propriedades**.
3. Marque **Desbloquear**.
4. Clique em **OK**.

## Como usar

1. Abra `volume-key-router.exe`.
2. Escolha o dispositivo de saída.
3. Escolha um modo:
   - `App selecionado`: controla apenas o app selecionado na lista.
   - `Linha/dispositivo selecionado`: controla a saída de áudio inteira.
4. Clique em `Ativar captura`.
5. Use as teclas de volume do teclado.

## Exemplo: Voicemeeter AUX

Para controlar só a linha auxiliar do Voicemeeter:

1. Em `Dispositivo de saída`, escolha `Voicemeeter AUX Input`.
2. Marque `Linha/dispositivo selecionado`.
3. Use as teclas de volume normalmente.

## Inicialização com Windows

- `Iniciar com Windows`: adiciona o app à inicialização do usuário atual.
- `Iniciar minimizado com Windows`: quando o Windows abrir o app, ele já nasce
  direto no tray.
- Se você abrir o `.exe` manualmente, a janela aparece normalmente.

## Configurações

As configurações ficam em:

```text
%AppData%\volume-key-router\settings.json
```

## Desenvolvimento

Requisitos:

- Windows 10/11
- .NET SDK 10

Compilar:

```powershell
dotnet build .\VolumeKeyRouter.csproj -c Release
```

Rodar pelo SDK:

```powershell
dotnet run --project .\VolumeKeyRouter.csproj -c Release
```

Gerar o executável self-contained:

```powershell
.\publish-win-x64.ps1
```

Saída:

```text
publish\win-x64\volume-key-router.exe
```

## Estrutura

```text
App\        entrada, CLI e configurações
Audio\      integração com áudio do Windows via NAudio
Core\       modelos compartilhados
Interop\    chamadas nativas do Windows
Keyboard\   hook global das teclas de volume
UI\         WinForms, tray, tema e restauração do alvo salvo
```

## CLI opcional

A interface gráfica é o uso principal, mas existe uma CLI simples para
diagnóstico:

```powershell
.\publish\win-x64\volume-key-router.exe --cli --devices
.\publish\win-x64\volume-key-router.exe --cli --list
.\publish\win-x64\volume-key-router.exe --cli --process Spotify --step 3
```

## Observações

- A tecla `Fn` normalmente não chega ao Windows. O app captura o evento de
  volume gerado pelo driver ou firmware do teclado.
- Um app só aparece na lista quando o Windows cria uma sessão de áudio para ele.
- Se você usa Spotify Web, o processo pode aparecer como `chrome`, `msedge` ou
  `firefox`, não como `Spotify`.
- Se um app roda como administrador e o hook não pega as teclas nesse contexto,
  rode o Volume Key Router como administrador também.

## Licença

MIT. Pode usar, modificar e distribuir.
