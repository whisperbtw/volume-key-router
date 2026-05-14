# Volume Key Router

Controle as teclas de volume do Windows para mudar o volume de um app ou de
uma saida especifica, em vez de mexer no volume geral do sistema.

Projeto open source, feito em vibe coding. O codigo, a interface, este README e
os textos do projeto tambem fazem parte desse processo.

## O que ele faz

- Intercepta `Volume Up` e `Volume Down` do teclado.
- Funciona com `Fn+F2/F3` quando o notebook envia essas teclas como volume.
- Controla o volume de um app/processo especifico.
- Controla o volume de uma saida inteira, como `Voicemeeter AUX Input`.
- Permite escolher o alvo por interface grafica.
- Salva a ultima escolha e restaura quando abrir de novo.
- Se o ultimo app/linha ainda nao existir, procura em segundo plano sem ficar
  piscando ou atualizando a lista toda hora.
- Fica no tray, com menu para abrir, ativar/pausar captura, atualizar e sair.
- Tem opcao de iniciar com o Windows.
- Tem opcao de iniciar minimizado com o Windows.
- Evita abrir duas instancias: se ja estiver rodando, abre a janela existente.
- Mostra overlay de volume proprio quando o volume muda.
- Nao usa API do Spotify.

## Para quem serve

Serve para casos como:

- baixar/aumentar so o Spotify;
- controlar so o navegador tocando musica;
- controlar so uma linha do Voicemeeter;
- deixar as teclas de volume afetando um alvo fixo enquanto o resto do sistema
  fica no mesmo volume.

## Como baixar

Baixe o `.exe` na aba **Releases** do GitHub.

Arquivo principal:

```text
volume-key-router.exe
```

O executavel publicado e self-contained, ou seja, ja leva o runtime .NET junto.
Nao precisa instalar .NET no computador para usar a versao da release.

Se o Windows bloquear por ter vindo da internet:

1. Clique com o botao direito no `.exe`.
2. Abra **Propriedades**.
3. Marque **Desbloquear**.
4. Clique em **OK**.

## Como usar

1. Abra `volume-key-router.exe`.
2. Escolha o dispositivo de saida.
3. Escolha o modo:
   - `App selecionado`: controla so o app selecionado na lista.
   - `Linha/dispositivo selecionado`: controla a saida inteira.
4. Clique em `Ativar captura`.
5. Use as teclas de volume do teclado.

## Voicemeeter AUX

Para controlar a linha auxiliar do Voicemeeter:

1. Em `Dispositivo de saida`, escolha `Voicemeeter AUX Input`.
2. Marque `Linha/dispositivo selecionado`.
3. Use as teclas de volume.

## Inicializacao com Windows

- `Iniciar com Windows`: coloca o app na inicializacao do usuario atual.
- `Iniciar minimizado com Windows`: quando o Windows abrir o app sozinho, ele
  nasce direto no tray.
- Abrir manualmente pelo `.exe` sempre mostra a janela normal.

## Onde ficam as configuracoes

```text
%AppData%\volume-key-router\settings.json
```

## Como compilar

Requisitos:

- Windows 10/11
- .NET SDK 10

Build:

```powershell
dotnet build .\VolumeKeyRouter.csproj -c Release
```

Rodar pelo SDK:

```powershell
dotnet run --project .\VolumeKeyRouter.csproj -c Release
```

Gerar o `.exe` self-contained:

```powershell
.\publish-win-x64.ps1
```

Saida:

```text
publish\win-x64\volume-key-router.exe
```

## Estrutura do projeto

```text
App\        entrada, CLI e configuracoes
Audio\      integracao com audio do Windows via NAudio
Core\       modelos compartilhados
Interop\    chamadas nativas do Windows
Keyboard\   hook global das teclas de volume
UI\         WinForms, tray, tema e restauracao do alvo salvo
```

## CLI opcional

A interface grafica e o uso principal, mas existe uma CLI simples para
diagnostico:

```powershell
.\publish\win-x64\volume-key-router.exe --cli --devices
.\publish\win-x64\volume-key-router.exe --cli --list
.\publish\win-x64\volume-key-router.exe --cli --process Spotify --step 3
```

## Observacoes

- A tecla `Fn` em si normalmente nao chega ao Windows. O app captura o evento
  de volume que o driver/firmware gera.
- Um app so aparece na lista quando o Windows cria uma sessao de audio para ele.
- Se usar Spotify Web, o processo pode ser `chrome`, `msedge` ou `firefox`, nao
  `Spotify`.
- Se um app roda como administrador e o hook nao pega as teclas nesse contexto,
  rode o Volume Key Router como administrador tambem.

## Licenca

MIT. Pode usar, modificar e distribuir.
