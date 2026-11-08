# graveyard-keeper

Mods pessoais de qualidade de vida para **Graveyard Keeper 2** e o ambiente blue-green para desenvolvê-los.
Guia completo (arquitetura, CLI `tools/gk2`, regras de design nativo): [AGENTS.md](AGENTS.md).

| Projeto | O quê |
| --- | --- |
| `GK2Insight/` | informações na tela e dump de dados (F7 / Shift+F7 / Ctrl+F7). v0.1 ainda com painel IMGUI provisório |
| `GK2DevBridge/` | ponte HTTP de desenvolvimento, só na instância green |
| `tools/` | `gk2` (CLI), `green.ps1` (instância green), `winshot.ps1`, host de cenários |
| `docs/cenarios/` | cenários C# executados dentro do jogo (`gk2 run`) |
| `docs/referencia/` | prints das telas nativas usados como referência visual |
