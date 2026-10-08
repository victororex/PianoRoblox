# PianoRoblox V2 — relatório de entrega

Atualização incremental concluída em 28/09/2026. Snapshot da V1 preservado antes das alterações; ZIPs originais fornecidos pelo usuário permanecem no projeto.

## 1. Arquivos modificados

Comparação com artifacts/source-v1-before-v2.zip: 39 arquivos modificados ou renomeados. Parte deles mudou somente namespaces/referências. A solution e os três projetos passaram a usar PianoRoblox. Lista completa:
- app.manifest
- App.xaml
- App.xaml.cs
- Core/Models/AppSettings.cs
- Core/Models/MusicEvent.cs
- Core/Models/PianoNote.cs
- Core/Models/Song.cs
- Core/PianoRoblox.Core.csproj
- Core/Services/MidiService.cs
- Core/Services/PianoMappingService.cs
- Core/Services/PlaybackClock.cs
- Core/Services/PlaybackService.cs
- Core/Services/SequenceJsonService.cs
- Core/Services/SequenceTransformer.cs
- Core/Services/SheetParser.cs
- Core/Services/SongLibraryService.cs
- Core/Services/TimerResolution.cs
- MAPPING.md
- PianoRoblox.csproj
- PianoRoblox.sln
- Properties/PublishProfiles/Windows.pubxml
- README.md
- Services/HotkeyService.cs
- Services/KeyboardPlayer.cs
- Services/KeyboardService.cs
- Services/RobloxWindowService.cs
- Tests/MappingAndSheetTests.cs
- Tests/MidiAndPersistenceTests.cs
- Tests/PianoRoblox.Tests.csproj
- Tests/PlaybackTests.cs
- Tests/UiSmokeTests.cs
- Utilities/AsyncCommand.cs
- Utilities/ObservableObject.cs
- VALIDATION.md
- ViewModels/MainViewModel.cs
- Views/MainWindow.xaml
- Views/MainWindow.xaml.cs
- Views/PianoKeyboard.cs
- Views/Theme.xaml

## 2. Arquivos novos

Modelos e serviços V2 separados do MainViewModel, novos ViewModels, testes e documentação:
- ARCHITECTURE.md
- Core/Models/MidiImportResult.cs
- Core/Models/OptimizationSettings.cs
- Core/Models/PianoProfile.cs
- Core/Models/Playlist.cs
- Core/Models/QueueItem.cs
- Core/Services/DataMigrationService.cs
- Core/Services/LateEventPolicy.cs
- Core/Services/MidiComplexityAnalyzer.cs
- Core/Services/MidiOptimizationService.cs
- Core/Services/PlaybackQueueService.cs
- Core/Services/PlaylistService.cs
- Services/KeyboardInputBuilder.cs
- Tests/InputV2Tests.cs
- Tests/LibraryV2Tests.cs
- Tests/OptimizationV2Tests.cs
- Tests/PresentationV2Tests.cs
- ViewModels/LibraryViewModel.cs
- ViewModels/MidiImportViewModel.cs
- ViewModels/TimelineViewModel.cs
- V2-REPORT.md

## 3. Smart Optimize

Pipeline: metadados MIDI → trilhas selecionadas → percussão → recomendação Auto → velocity → merge/dedupe → acordes → densidade → MusicSequence → transposição/alcance → playback.

Auto escolhe Original quando acorde máximo ≤4 e pico ≤25 notas/s; Balanced até 8 e 45; senão Performance. A análise para Auto ignora duplicações exatas e bateria filtrada. Original não descarta notas fracas nem impõe limites musicais. Balanced usa 6 ms / 6 notas por acorde / velocity 15 / 10 notas em 250 ms / 32 em 1 s; Performance usa 8 / 4 / 25 / 7 / 22. Custom permite editar esses valores.

BPM e duração não são reduzidos. A análise e a simulação usam eventos reais. Alterações de trilhas/presets recalculam fora da thread visual e resultados antigos são descartados. Alterações durante playback entram no próximo início.

## 4. Filtro de bateria

Canal MIDI 9, exibido como canal 10, é percussão. O filtro começa ligado. Trilhas só de percussão começam desmarcadas. Trilhas mistas mantêm notas melódicas. A tela mostra quantidade encontrada/removida. Para incluir bateria é necessário selecionar a trilha e desligar o filtro.

## 5. Seleção de tracks

Checkbox por trilha, com nome, instrumento, canal, notas, faixa e quantidade fora do alcance. Seleção salva em Song.SelectedTracks. Canal/programa/velocity/duração permanecem no modelo intermediário. O arquivo MIDI original nunca é sobrescrito.

## 6. Limitador de acordes

Dedupe escolhe um pitch por grupo e preserva proveniência. O grupo é ancorado no primeiro ataque; notas próximas não prolongam a janela por encadeamento. Acordes grandes conservam melodia aguda e baixo quando possível, e completam o limite com velocity, provável trilha principal e cobertura de registros. A escolha é determinística, não as primeiras N notas. Melody Priority é configurável.

## 7. Density Limiter

Janelas deslizantes semiabertas de 250/1000 ms. Se o limite é excedido, remove a nota de menor importância, incluindo acompanhamento anterior quando uma melodia posterior é mais importante. Não desloca timestamps nem altera BPM. O merge é a única etapa que pode antecipar ataques dentro de sua janela curta.

## 8. Batch SendInput

KeyboardInputBuilder constrói pacotes ScanCode/VirtualKey separadamente do transporte user32. SendBatch faz uma chamada nativa para o lote. Um acorde natural usa dois lotes: downs e limpeza/ups. GroupSafe agrupa notas contíguas compatíveis com Shift, sem reordenar transições. As teclas previstas são rastreadas antes da chamada; envio parcial dispara limpeza. Teclas incertas continuam registradas para nova tentativa de liberação. Stop/Pause/foco/seek/troca/saída liberam notas, Shift, Ctrl e Alt. Testes usam transportes falsos.

LateEventPolicy evita esvaziar atrasos em rajadas: baixa prioridade pode ser ignorada acima de 80 ms; tudo acima de 160 ms; recuperação limitada a 8 notas/50 ms. FpsFriendly ainda limita envio real a 8 notas/50 ms e 32/s. Normal/LowLatency preservam o relógio absoluto. A UI consome o evento visual mais recente a cada 40 ms, reutiliza linhas de timeline e brushes do piano.

## 9. Playlists

CRUD, adicionar/remover músicas, ordem por ↑/↓ e persistência em playlists.json. Excluir playlist não exclui músicas. A fila é independente, com IDs por entrada, remoção individual e limpeza. Persistência usa temporário, backup e substituição. A barra de player fica em todas as abas. Ajustes por música são salvos explicitamente e restaurados ao carregar.

## 10. Shuffle

Fisher–Yates cria a ordem antes da navegação. Não repete uma música da playlist no mesmo ciclo e mantém histórico para Previous. Ao repetir a playlist, evita repetir imediatamente a última quando há alternativas. Entradas manuais duplicadas na fila continuam possíveis por intenção do usuário.

## 11. Next / Previous

Next cancela e aguarda liberação do único playback antes de carregar/iniciar. Ao término natural, a fila avança automaticamente. Contagem entre músicas é 0 por padrão. Previous antes de 3 segundos musicais navega pelo histórico; depois reinicia a atual. Stop durante leitura MIDI impede que o carregamento concluído volte a tocar. Duplo clique inicia a música ou a playlist daquela posição. F4/F5 foram adicionados sem substituir atalhos existentes conflitantes.

## 12. Repeat

Off encerra; Playlist cria novo ciclo; One repete a atual ao concluir. Next manual ignora Repeat One. A fila manual precede o restante da playlist. O estado aparece no botão persistente.

## 13. Migração V1

Se a pasta PianoRoblox em AppData ainda não existir, DataMigrationService copia a biblioteca antiga para uma pasta temporária irmã e publica por renomeação ao concluir. Inclui songs.json, settings.json e Songs. A pasta anterior é preservada; destino existente nunca é sobrescrito. Links/junctions são ignorados. Mensagem de sucesso: “Dados da versão anterior migrados para PianoRoblox.” Novos campos JSON têm defaults.

A revisão recursiva de nomes no código/documentação ativos encontrou o nome antigo somente no caminho de migração, testes e explicação dessa compatibilidade. Saídas de build e relatórios V1 históricos foram preservados separadamente.

## 14. Resultados dos testes

**68 aprovados, zero falhas, zero ignorados em Release.** Mantidos os 34 casos anteriores e adicionados 34 casos novos. A integração WPF renderiza sete abas e confere os bindings, recalcula presets concorrentes, avança playlist sem sobreposição, interrompe carga MIDI por Stop e restaura/salva ajustes.

No MIDI sintético de 200 BPM, 1152 notas e 192 notas de bateria: Original gerou 960 notas, pico 210/s e acorde máximo 15; Balanced gerou 130, pico 30/s e acorde máximo 3; Performance gerou 82, pico 20/s e acorde máximo 2. Os três mantiveram duração e BPM. Nesse exemplo permanecem 64 eventos, com menos notas por evento. Estimativa de chamadas SendInput: 1554 / 325 / 208. Valores e fixture: artifacts/ui-v2/benchmark.txt e complex-synthetic.mid.

Não foi realizado teste auditivo ou medição de FPS dentro do Roblox. Não havia um MIDI real de Black Rover fornecido. O exemplo sintético comprova regras/contagens, não reconhecimento da música. Cache persistente, áudio local, sustain por Note Off, novos perfis e AutoHotkey não foram incluídos; eram extensões opcionais.

Relatório detalhado: VALIDATION.md. TRX: artifacts/test-results-v2/v2-release.trx. Renderizações: artifacts/ui-v2/.

## 15. Build e publicação

Restore concluído. Build Release: **zero avisos, zero erros**. Publicação Windows x64 autocontida em arquivo único: **zero avisos, zero erros**. Um aviso inicial de consulta ao NuGet foi resolvido com nova restauração; o log final está limpo.

Comando de publicação:

~~~powershell
dotnet publish PianoRoblox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o artifacts/publish-v2
~~~

Logs: artifacts/build-v2.log e artifacts/publish-v2.log. SDK 9.0.305 com destino .NET 8. Perfil Windows.pubxml atualizado para publicar em publish-v2.

## 16. Executável

**artifacts/publish-v2/PianoRoblox.exe** — versão **2.0.0.0**, **163088841 bytes** (aproximadamente 155,5 MiB).

Caminho completo:

~~~text
C:\Users\Victor Alexandre\OneDrive - Uniube - Sociedade Educacional Uberabense\Documentos\ChatGPT\Pianoroblox\artifacts\publish-v2\PianoRoblox.exe
~~~

SHA-256: 69C933F6E98DCEA77A9B55E6076B2E5687C6300CA03A3BA164226E6C3D6DF25A

O executável é autocontido. A dependência pode deixar um arquivo .dylib de outra plataforma ao publicar; ele não é usado pelo leitor MIDI gerenciado no Windows e não integra o ZIP de distribuição. O pacote Windows contém o exe e documentação.
