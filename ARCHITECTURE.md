# PianoRoblox — evolução da V1

O código da V1 foi lido antes da renomeação. O snapshot está em `artifacts/source-v1-before-v2.zip`; ZIPs fornecidos pelo usuário não foram alterados.

Fluxo preservado: `MidiService` ou `SheetParser` → `MusicSequence` → `SequenceTransformer` → `PlaybackService` / `PlaybackClock` → `KeyboardPlayer` → `KeyboardService` → SendInput. `PianoMappingService` é a fonte única das teclas; `RobloxWindowService` valida o processo em foco. `MainViewModel` coordena esses componentes e `MainWindow` contém somente ligação de eventos da interface. `SongLibraryService` persiste JSON e arquivos locais. A solution tem um executável WPF, um núcleo independente da interface e testes xUnit.

Na V2, MIDI passa por modelos intermediários com trilhas/canais/velocity/duração, análise e otimização antes da sequência tocável. O BPM e o relógio absoluto não são substituídos. A seleção de trilhas e os presets ficam em `MidiImportViewModel` e serviços do núcleo. Playlists e fila pertencem a `LibraryViewModel`, `PlaylistService` e `PlaybackQueueService`.

`TimelineViewModel` mantém uma janela pequena com linhas reutilizadas. `PlaybackVisualBuffer` desacopla eventos musicais de atualizações WPF, consumidas em 25 FPS. A troca de músicas aguarda cancelamento/liberação do único player antes de iniciar outra sequência. As continuações automáticas são invalidadas ao parar/trocar a música.

`PianoProfile` descreve o layout sem associar o aplicativo ao nome de uma experiência Roblox. O primeiro perfil conserva exatamente as 61 teclas C2–C7.

Migração usa cópia para a nova pasta AppData, preservando os dados V1. Campos JSON novos têm defaults; uma biblioteca antiga continua desserializável. A publicação da V2 usa nome e pasta próprios para não confundir executáveis históricos.
