# Volume Key Router v0.1.3

## Destaques

- Instalador agora detecta quando o Volume Key Router ja esta instalado.
- Ao detectar uma instalacao existente, o instalador mostra as opcoes
  `Atualizar` e `Reparar`.
- Em atualizacao/reparo, o instalador usa a pasta da instalacao existente e
  evita passar pela escolha de pasta como se fosse uma instalacao nova.
- Mantem as melhorias da v0.1.2: overlay com titulo/artista/capa, cache da capa
  para reduzir piscadas e desmute automatico ao usar `Volume Up` ou
  `Volume Down`.

## Arquivo da Release

```text
VolumeKeyRouterSetup-0.1.3.exe
```

## Notas

- O instalador atualiza por cima da versao anterior e preserva as configuracoes
  em `%AppData%\volume-key-router\settings.json`.
- O instalador nao usa assinatura/certificacao.
