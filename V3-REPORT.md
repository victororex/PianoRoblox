# PianoRoblox V3 — relatório de entrega

Atualização incremental do repositório real victororex/PianoRoblox, em 10/10/2026. Branch local **feature/v3**, baseada na master **c71faa820b4c4ccd61e14c93b1c5ea2be74b85e3**. A cópia local foi comparada ao GitHub antes de modificar: não havia diferenças. Os fontes V2 estão preservados em artifacts/source-v2-before-v3.zip. Nenhum push/release remoto foi realizado.

**Resultado: versão 3.0.0, build Release sem avisos/erros, 108 testes aprovados e executável Windows x64 autocontido produzido.** A validação acústica dentro do Roblox permanece manual.

## Correções e interface

- Smart Optimize recebe o título de apresentação de Song.Title ao carregar a biblioteca. Importação nova utiliza o nome original. O GUID físico é mantido. Renomear/salvar atualiza os painéis; cálculos antigos são invalidados por versão/cancelamento.
- Biblioteca/playlist possuem Reproduzir tudo, Reproduzir aleatoriamente e Adicionar todas à fila. Todas as músicas possui Reproduzir biblioteca inteira. Nenhum desses comandos exige selecionar uma faixa.
- O menu de contexto Reproduzir a partir daqui continua a coleção da posição escolhida.
- Há pesquisa por título/artista, filtro MIDI/Sheet/JSON, ordenação por título/artista/duração/data, seleção múltipla para playlist, destaque da faixa atual e botão para abrir Songs/.
- A barra persistente mostra título com tooltip, artista, contexto, faixa, próxima música, tempo, Shuffle, Repeat e modo de duração. A fila tem entradas numeradas. Pausa pelo usuário e por perda de foco são distinguidas; carregamento, contagem, conclusão e erro têm estados próprios no playback.

## Arquitetura e MIDI Sustain

MusicEvent mantém int[] Notes para compatibilidade e acrescenta MusicNote[] NoteDetails com duração nullable, velocity, trilha e prioridade. O início vem do evento; NoteSchedule materializa pitch, início, duração, término, identidade e tecla física. Null conserva a distinção entre duração original e estimada.

PlaybackService permanece o único proprietário do worker e PlaybackClock. Legacy usa o mecanismo V2. MidiSustain/Hybrid percorrem uma timeline de Note On/Off ordenada, sem Task/timer por nota. O tempo absoluto usa Stopwatch; velocidade muda ataques e términos proporcionalmente sem saltar a posição. Liberações têm prioridade e não podem ser descartadas pelo limitador de densidade/atraso.

Padrões para MIDI novo: Hybrid, mínimo 8 ms, máximo 0/sem limite, Release Gap 2 ms, overlap/retrigger ativados e hold estimado 8 ms. Músicas/configurações V2 mantêm Legacy por default. MIDI Sustain conserva o Note Off; Hybrid aplica os limites explicitamente e avisa quantas notas foram limitadas. Sustain Enabled desligado usa Legacy.

O scheduler preserva polifonia entre teclas compatíveis. Mesmo pitch com retrigger libera/reaperta e usa IDs diferentes, evitando Note Off antigo sobre a nova nota. O gap é também mínimo físico em tempo real, inclusive a 150%; uma fila de prioridade adia o ataque sem atrasar outras liberações. Sem retrigger, sobreposições do mesmo pitch são unidas até o maior término. Sem overlap, o acorde anterior termina antes do novo ataque.

Pause, Stop, Next, Previous, seek, foco, troca, falha/cancelamento e fechamento liberam teclas e modificadores. Pause congela o relógio. Retomada/seek no sustain reatacam somente intervalos que atravessam a posição, pelo tempo restante. No Legacy, o seek V2 continua nos ataques posteriores. Ajustes da sessão são copiados: editar a tela não altera silenciosamente o playback em execução.

## Limitações físicas e do jogo

Shift é global, e pitches como t/Shift+t compartilham tecla física. Sustentações incompatíveis são liberadas antes do novo ataque e contabilizadas; não se promete sustain perfeito nesses casos. GroupSafe agrupa entradas compatíveis; Sequential e Chord Spread permanecem. Ataques impossíveis com o gap configurado são ignorados com aviso.

Alguns pianos Roblox utilizam amostras de duração fixa, mesmo mantendo a tecla pressionada. O teste de sustain/polifonia na aba Teste permite conferir a experiência. Não há pedal CC64, síntese de áudio, dinâmica acústica ou integração interna do Roblox. Só são usados os mecanismos normais de entrada do Windows. O retorno de SendInput não confirma o recebimento pelo jogo. Não se garante precisão de 1 ms.

## Smart Optimize, transposição e JSON

Notas preservadas mantêm suas durações. Duplicatas conservam o maior término, com velocity/proveniência determinística. Ao antecipar uma nota em merge, a duração é compensada para manter seu término. Transposição e dobramento por oitava mantêm os metadados; colisões de pitches conservam o maior término e são informadas.

A análise de carga acrescenta o pico conjunto de ataques/liberações. Os limites musicais de ataques da V2 permanecem, sem descarte de liberações obrigatórias. BPM e tempo original não são reduzidos para disfarçar densidade.

JSON V2 com keys importa normalmente. V3 aceita notes com midi, durationMs opcional, velocity, trackIndex e priority. Exporta notes e keys, preservando leitores antigos. Sheets/JSON sem duração usam Legacy ou hold explicitamente estimado, nunca apresentado como Note Off original.

Compatibilidade mostra total/abaixo/dentro/acima e percentual das trilhas selecionadas antes da simplificação. A recomendação testa todos os deslocamentos −24…+24, maximiza cobertura e prefere o menor deslocamento em empates. Só é aplicada pelo botão do usuário. Ignore/Octave/Ask e transposição manual continuam disponíveis.

## Playlists e segurança de navegação

PlaybackQueueService/LibraryViewModel foram reutilizados. Play All usa a ordem salva; a biblioteca inteira usa a ordenação escolhida, sem restringir a reprodução pela pesquisa visível. Shuffle visita todas uma vez por ciclo. Repeat One repete a atual ao concluir; Next manual ignora esse repeat. Repeat Playlist cria outro ciclo; o preview coincide com a próxima faixa, inclusive no ciclo aleatório seguinte.

A fila manual conserva a prioridade V2 sobre o restante da playlist. A contagem entre músicas é respeitada. Next aguarda Stop/liberação antes de carregar/iniciar. Stop invalida gerações de avanço/carregamento para impedir reinício tardio. Playlists vazias/arquivos ausentes exibem mensagem e não entram em loop infinito. ↑/↓ foi mantido como fallback estável de ordenação; não foi acrescentado drag-and-drop.

## Persistência e compatibilidade V2

Mantidos %AppData%/PianoRoblox/, songs.json, settings.json, playlists.json e Songs/. Arquivos GUID não são renomeados ou apagados. A atualização não acessou/modificou a biblioteca pessoal em AppData durante os testes: toda persistência de teste usa diretórios temporários isolados.

Novos campos possuem defaults. Configuração por música inclui duração, mínimo/máximo, gap, overlap, retrigger, modo de acorde/Shift e alcance, além dos ajustes existentes. Antes da primeira gravação V3, JsonDataStore copia os índices e .bak existentes para Backups/before-v3/. Não há migração destrutiva dos arquivos musicais.

Escritas são atômicas. Índice inválido tenta .bak e mostra aviso; original é preservado. Na gravação após recuperação, o inválido é arquivado como .corrupt-… e o backup válido não é substituído por corrupção. Sem backup válido, gravação fica bloqueada: nenhuma lista vazia substitui a biblioteca por falha de leitura. Também foram testados documentos null, entradas nulas e índice principal desaparecido com backup inválido. A migração V1 original permanece.

## Diagnósticos e desempenho

Diagnósticos opcionais: notas executadas, Down/Up, notas puladas, eventos atrasados, maior atraso, teclas ativas e conflitos físicos identificados. São snapshots em até 2 Hz. Timeline/teclado usam atualização visual em 25 FPS, com linhas reutilizadas e buffer de ataques; as teclas sustentadas consultam o estado físico ativo. Legacy também contabiliza entradas.

Preparação da timeline O(n log n); estruturas ativas limitadas pelo perfil físico; um único worker. A fixture de densidade possui 2000 ataques em aproximadamente meio segundo e confirma finalização sem exceção/tecla presa, com Down/Up equilibrados. Não foi medido FPS real ou consumo de CPU no Roblox.

## Testes e músicas reais

Referência V2: **68/68 aprovados**, build sem avisos/erros. V3: **108/108 aprovados**, zero falhas/ignorados; 40 casos adicionais e integração WPF ampliada. A renderização das sete abas não abriu janela de desktop nem enviou teclas reais; não houve erro de binding.

Cobertura: título GUID/apresentação, rename/recalcular concorrente, leitura MIDI obsoleta, playlist A→B→C sem seleção, shuffle/repeat/preview, fila, vazio/ausentes, busca/filtros/ordenação/seleção múltipla, sustentações 1000→2500 ms, polifonia, retrigger e gap a 150%, pausa/foco, Stop/Next durante sustain, seek, velocidades 50/100/150 e mudança ao vivo, Shift, falha de transporte, densidade, JSON V2/V3, merge/dedupe/transposição, duração estimada/limites, defaults/backups/corrupção.

MIDIs já existentes no workspace foram analisados sem copiá-los/adicioná-los novamente ao repositório:

| Música | Notas no MIDI completo | Notas Balanced | Cobertura original | Sugestão de cobertura | Conflitos físicos identificados no plano Balanced |
|---|---:|---:|---:|---:|---:|
| Black Rover | 1966 | 1203 | 83,1% | +9 semitons | 452 |
| Never Meant to Belong | 1543 | 1512 | 99,7% | +3 semitons | 352 |
| Treachery | 2010 | 2003 | 89,4% | +12 semitons | 85 |

A tabela de músicas usa o MIDI completo para cobertura; o painel do app utiliza trilhas/filtro de percussão selecionados. As sugestões maximizam cobertura, não avaliam qualidade harmônica. Em todos os três arquivos, os términos das notas retidas no Balanced foram verificados contra o MIDI original, e cada intervalo do plano tem ataque/liberação. Máximo de hold default não cortou notas. Esses testes são de dados/scheduler, não uma avaliação acústica.

Evidências: artifacts/test-results-v2-baseline/v2-baseline.trx; artifacts/test-results-v3/v3-delivery.trx; artifacts/test-results-v3/v3-ui-final.trx; artifacts/ui-v3/tab-0.png…tab-6.png; artifacts/ui-v3/v3-library-title.png; artifacts/ui-v3/real-midi-v3.txt. O teste WPF final confirmou títulos, A→B→C, Next durante sustain e Stop impedindo avanço.

## Build e publicação

SDK portátil oficial **.NET 8.0.423**, Windows x64, destino .NET 8. Restore concluído. Build Release: **0 avisos, 0 erros**. Publicação final: sucesso, sem avisos/erros.

```powershell
dotnet restore PianoRoblox.sln
dotnet build PianoRoblox.sln -c Release
dotnet test PianoRoblox.sln -c Release
dotnet publish PianoRoblox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o artifacts/publish-v3
```

Nesta máquina os comandos usaram artifacts/tooling/dotnet/dotnet.exe, com cache/CLI dentro de artifacts/tooling/. Logs: artifacts/restore-v3.log, artifacts/build-v3.log, artifacts/test-v3.log e artifacts/publish-v3.log.

**Executável: artifacts/publish-v3/PianoRoblox.exe**

Caminho completo: C:\Users\User\Documents\Codex\2026-10-07\consegue-x20\outputs\PianoRoblox\artifacts\publish-v3\PianoRoblox.exe

FileVersion: **3.0.0.0**. Tamanho: **162884480 bytes** (~155,34 MiB).

SHA-256: **A025772EAB4E28292407F4FF8E28981154745B0F3098224C8179711901F7C525**.

O .dylib de outra plataforma que DryWetMidi deixa na saída não é necessário no Windows e não integra o ZIP de distribuição. O pacote contém o executável autocontido e documentação, sem o SDK/cache e sem músicas adicionais. A pasta publish-v2 não foi sobrescrita.

## Pendências e limites restantes

1. Validação dentro do Roblox: comportamento acústico, layout físico, recepção efetiva de teclas e desempenho/FPS. Protocolo em VALIDATION.md; não foram declarados resultados no jogo.
2. Sustentações com estados de Shift/teclas incompatíveis usam fallback informado. Pianos com amostras fixas podem não sustentar som.
3. Editor visual/calibração de novos perfis e drag-and-drop não foram incluídos; PianoProfile está preparado para layouts adicionais e ↑/↓ permanece funcional.
4. Não há falhas conhecidas nos 108 testes. Isso não substitui validação em todas as experiências Roblox, teclados ou PCs.
