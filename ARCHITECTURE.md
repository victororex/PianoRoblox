# Arquitetura PianoRoblox V3

Evolução incremental da V2 no mesmo executável WPF, Core independente de UI e projeto xUnit. Backup dos fontes anteriores: artifacts/source-v2-before-v3.zip. Branch local feature/v3 baseada em origin/master c71faa8.

## Pipeline musical

MidiService → MidiImportResult (pitch, ataque, duração/Note Off, velocity, canal, trilha, programa/percussão) → MidiOptimizationService → MusicSequence/MusicEvent/MusicNote → SequenceTransformer → PlaybackService/PlaybackClock → KeyboardPlayer → KeyboardService → SendInput.

MusicEvent conserva o contrato V2 `TimeMs + int[] Notes` e acrescenta NoteDetails. MusicNote usa duração nullable: null indica ausência de informação, nunca um sustain original inventado. O evento fornece início; NoteSchedule materializa StartTimeMs, DurationMs, EndTimeMs, velocity, trilha, prioridade, identidade e tecla física por nota.

A otimização escolhe a proveniência/velocity deterministicamente e conserva o maior término das duplicatas. Antecipar ataques em merge aumenta a duração correspondente, sem antecipar seu Note Off. Transposição/dobramento e JSON mantêm NoteDetails. A avaliação de carga contabiliza ataques e liberações; os limites musicais de ataques V2 são preservados.

## Playback

PlaybackService continua sendo o único proprietário do worker, cancelamento e relógio absoluto. Legacy chama PlayChord como na V2. MidiSustain/Hybrid usam INoteKeyboardPlayer e NoteSchedule: ordenação O(n log n), duas transições por intervalo retido, liberações antes de ataques no mesmo instante.

Uma fila de prioridade local adia ataques que precisam de gap físico, sem bloquear liberações. Não há Task/timer por nota ou espera ocupada. O worker aguarda alvos absolutos em intervalos limitados, verificando foco/cancelamento. LateEventPolicy e FpsFriendly podem descartar ataques, nunca Note Off. Mudanças de velocidade alteram o relógio musical, incluindo liberações pendentes.

Pause/Stop e despacho compartilham sincronização. Pause libera imediatamente, congela relógio e invalida o estado físico. Resume reataca só notas ainda válidas, pelo tempo restante. Seek constrói posição na mesma timeline e reataca intervalos que a atravessam; notas terminadas não são reenviadas. Stop/troca aguardam finalização/liberação do worker antes de qualquer outra sequência. MainViewModel mantém a geração de reprodução que invalida avanços automáticos obsoletos.

KeyboardPlayer rastreia IDs e teclas físicas. Um Off obsoleto não libera um ataque novo. GroupSafe permite batching de notas compatíveis; Spread altera os ataques. Como Shift é global, intervalos incompatíveis são truncados explicitamente e contabilizados. KeyboardService mantém a proteção V2 contra envio parcial e limpeza de notas e dos dois lados de Shift/Ctrl/Alt, inclusive após falha.

## Apresentação e navegação

MidiImportViewModel recebe título de apresentação explicitamente. Renomear invalida cálculos que carregam o título antigo; os tickets impedem callbacks obsoletos. O GUID do arquivo permanece restrito ao caminho/logs.

LibraryViewModel usa PlaybackQueueService para reproduzir coleções, shuffle, repeat, histórico e fila. Comandos de reprodução integral não dependem de SelectedSong. Busca/filtro/ordenação afetam a lista visível; Play All usa a coleção inteira. IDs de entrada distinguem duplicatas manuais. A próxima permutação de repeat é calculada antes da transição e coincide com o preview.

MainViewModel coordena arquivos, preparação, troca e estados. Views contêm bindings, seleção múltipla/contexto e eventos visuais. TimelineViewModel reutiliza 16 linhas. PlaybackVisualBuffer coalesce ataques; o teclado sustentado consulta as notas físicas ativas em 25 FPS. Diagnósticos são snapshots em até 2 Hz, sem notificar WPF por evento musical.

## Persistência

SongLibraryService e PlaylistService usam JsonDataStore. O mesmo arquivo compartilha um semáforo entre instâncias. Leitura inválida tenta .bak; falha de ambos não retorna coleções vazias. Antes da primeira escrita V3, há snapshot dos índices em Backups/before-v3. Escritas usam temporário/substituição; originais corrompidos são arquivados em .corrupt-… e backups válidos são mantidos.

Não há migração destrutiva: campos adicionais possuem defaults e Songs/ não é regravada. DataMigrationService mantém a cópia V1 original. PianoProfile permanece a abstração de layout e alcance; a UI de calibração de perfis maiores não integra esta versão.
