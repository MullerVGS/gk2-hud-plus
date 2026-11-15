# graveyard-keeper

Mods de qualidade de vida para **Graveyard Keeper 2** (o pacote **GK2 HUD+**) e o ambiente blue-green para desenvolvê-los.
Guia completo (arquitetura, CLI `tools/gk2`, regras de design nativo): [AGENTS.md](AGENTS.md).

| Projeto | O quê |
| --- | --- |
| `GK2HudPlus/` | **GK2 HUD+**: os mods de jogador num plugin só, com funcionalidades que ligam e desligam sem reabrir o jogo |
| `GK2Hud/` | interface: tooltips, números no HUD, medalhão, calendário, diário, árvore de quests (módulo + entrada avulsa) |
| `GK2Map/` | mapa: zoom, vista dos locais, inspeção (módulo + entrada avulsa) |
| `GK2Saves/` | Save e Load no menu de pausa, histórico (módulo + entrada avulsa) |
| `GK2ZombieRefund/` | devolver perk de zumbi (módulo + entrada avulsa) |
| `GK2Insight/` | ferramenta de debug: painéis F7 / Shift+F7 (uGUI), dump Ctrl+F7 e telemetria para o Grafana da VPS |
| `GK2DevBridge/` | ponte HTTP de desenvolvimento, só na instância green |
| `GK2HotLoader/` | aplica mods promovidos com o jogo aberto e avisa com a notificação nativa |
| `Shared/` | código comum: ModGuard (partes isoladas), ModHost (lista de funcionalidades), troca de plugin em execução |
| `tools/` | `gk2` (CLI), `green.ps1` (instância green), `winshot.ps1`, host de cenários |
| `docs/cenarios/` | cenários C# executados dentro do jogo (`gk2 run`) |
| `docs/referencia/` | prints das telas nativas usados como referência visual |
