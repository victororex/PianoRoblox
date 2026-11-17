# PianoRoblox V3 — 3.0.0

Player desktop Windows x64 em C# / .NET 8 / WPF, atualizado sobre a V2 existente. MIDI, sheets, JSON, Smart Optimize, teclado virtual, timeline, playlists, fila e atalhos são preservados.

## Executar

Abra **PianoRoblox.exe** no pacote extraído; no workspace, ele fica em **artifacts/publish-v3/PianoRoblox.exe**. A publicação é autocontida e em arquivo único: não exige instalar .NET. A publicação da V2 não é sobrescrita. O executável não possui instalador ou assinatura digital.

1. Abra uma experiência Roblox com piano controlado por teclado, ative o piano e feche o chat do jogo.
2. Importe um MIDI, importe JSON ou analise uma sheet.
3. Confira as trilhas, Smart Optimize e **Compatibilidade com o piano**.
4. Para MIDI novo, Hybrid é selecionado. Para uma música antiga da biblioteca, o modo Legacy permanece até você alterá-lo e salvar os ajustes.
5. Clique Play e volte ao jogo durante a contagem; ou use F6 já no Roblox. F7 pausa/retoma e F8 para.
6. Na Biblioteca, selecione uma playlist e clique **Reproduzir tudo**, sem escolher uma faixa. Em **Todas as músicas**, use **Reproduzir biblioteca inteira**.

A proteção de foco reconhece RobloxPlayerBeta.exe, independentemente da experiência. Ela verifica o processo em foco, não se o piano/chat está selecionado. O perfil continua **Legacy C2–C7, 61 Keys**, com as teclas originais: [MAPPING.md](MAPPING.md). ScanCode e VirtualKey permanecem disponíveis.

## Duração das notas

| Modo | Comportamento |
|---|---|
| LegacyTap | Mesmo mecanismo de acordes da V2, com Key Hold fixo de 1–100 ms. |
| MidiSustain | Note Down no início e Note Up no término musical de cada nota. Sustentações compatíveis se sobrepõem. |
| Hybrid | Usa as durações explícitas, com mínimo/máximo configuráveis. É o padrão de importações MIDI novas. |

Padrões de sustain: mínimo 8 ms, máximo **0 (sem limite)**, Release Gap 2 ms, overlap e retrigger ativados; hold estimado 8 ms. O mínimo/máximo só modifica durações em Hybrid. Um máximo configurado gera aviso e contagem das notas limitadas. Desligar Sustain Enabled volta ao Legacy.

Sheets e JSON V2 não possuem duração individual: o aplicativo informa que o hold é estimado, sem apresentá-lo como sustain original. MIDI sem Note Off também recebe essa indicação. MIDI Sustain não simula pedal CC64, dinâmica acústica nem instrumentos; envia entrada normal do Windows.

O scheduler usa um único worker, Stopwatch/PlaybackClock e uma timeline de ataques/liberações. A velocidade de 50–150% afeta os dois eventos, inclusive quando muda durante uma nota. Não existe Task ou timer por nota. Liberações não passam pelo limitador de densidade nem pela política de descarte de ataques atrasados.

Pause, Stop, Next, Previous, seek, troca, perda de foco, falha e fechamento liberam teclas e modificadores. Pausa congela o tempo. **Retomada e seek reatacam notas que atravessam a posição, somente pelo tempo restante**; notas já terminadas ficam silenciosas. No Legacy, seek conserva a regra V2 de iniciar nos ataques posteriores.

Repetições do mesmo pitch liberam a nota anterior antes do próximo ataque; IDs distintos impedem que o Note Off antigo solte a nova nota. Release Gap também é respeitado como mínimo físico em tempo real, sem bloquear outras liberações. Com retrigger desativado, intervalos sobrepostos do mesmo pitch são unidos até o maior término.

### Limites físicos e Roblox

`t` e `Shift+t` compartilham uma tecla física. O teclado Windows também possui um único estado global de Shift. Quando sustentações exigem estados incompatíveis, a nota anterior precisa ser liberada; esse conflito é contabilizado. GroupSafe agrupa notas compatíveis; Sequential e Chord Spread continuam disponíveis. Ataques impossíveis com o gap/Shift configurado são ignorados com aviso.

Nem todo piano sustenta áudio enquanto a tecla está pressionada: algumas experiências usam amostras de duração fixa. Use **Teste → Testar sustain e polifonia** para conferir no jogo. O envio correto de Down/Up não garante som idêntico ao MIDI ou reconhecimento pelo Roblox.

## Smart Optimize e títulos

O título de apresentação é independente do arquivo físico. MIDIs da biblioteca usam Song.Title; importações novas usam o nome original. Renomear e salvar ajustes atualiza Player, Smart Optimize, biblioteca, timeline e barra inferior, mantendo o arquivo GUID. Cálculos obsoletos são cancelados e não podem restaurar um título anterior.

O pipeline mantém pitch, tempo, duração, velocity, trilha e prioridade até o scheduler. Duplicatas no mesmo grupo conservam o maior término; o merge compensa a antecipação do início. Transposição, dobramento por oitava e JSON mantêm as durações. A análise exibe a carga conjunta de ataques e liberações.

| Preset | Merge | Máximo/acorde | Velocity mínima | Notas/250 ms | Notas/s |
|---|---:|---:|---:|---:|---:|
| Original | 0 ms | 128 | 0 | 10000 | 40000 |
| Balanced | 6 ms | 6 | 15 | 10 | 32 |
| Performance | 8 ms | 4 | 25 | 7 | 22 |
| Custom | editável | editável | editável | editável | editável |

Auto escolhe Original para acorde ≤4 e pico ≤25 notas/s; Balanced até 8/45; demais usam Performance. Seleção de trilhas, percussão (canal MIDI 10), prioridade melódica e simulação de performance permanecem. Original não simplifica o conteúdo além de seleção/filtro/dedupe. BPM não é alterado para reduzir carga. A simulação SendInput é uma estimativa de Legacy/GroupSafe, sem medir FPS.

## Compatibilidade com o piano

O painel mostra total, abaixo, dentro, acima e percentual de cobertura das trilhas escolhidas antes da simplificação. Analisa deslocamentos de **−24 a +24 semitons** e sugere a maior cobertura, preferindo o menor deslocamento em empates. A sugestão só é aplicada quando você clica no botão.

Ignore descarta notas fora do alcance; Octave dobra por oitavas; Ask pergunta antes de tocar. A transposição manual continua disponível. Quando o dobramento junta pitches, o maior término é conservado e a colisão aparece no Log. A abstração PianoProfile aceita outros layouts, mas esta entrega não inventa mapeamentos de experiências nem inclui editor visual de perfis.

## Biblioteca, playlists e fila

- Reproduzir tudo segue a ordem salva da playlist; em Todas as músicas, segue a ordenação escolhida, incluindo a biblioteca inteira mesmo com pesquisa/filtro visível.
- Reproduzir aleatoriamente escolhe uma primeira faixa e uma permutação sem repetição no ciclo.
- Adicionar todas à fila mantém a prioridade da fila manual sobre o restante da playlist, conforme a regra V2; Repeat One tem prioridade na conclusão automática.
- O menu de contexto **Reproduzir a partir daqui** inicia na faixa selecionada e continua pelas demais.
- Playlist vazia e arquivos ausentes apresentam mensagem; entradas ausentes são ignoradas. A faixa atual fica destacada, com título/artista, contexto, número, próxima música, shuffle/repeat e modo de duração na barra persistente.
- Pesquisa por título/artista, filtro MIDI/Sheet/JSON, ordenação por título/artista/duração/data e seleção múltipla (Ctrl/Shift) para adicionar à playlist.
- Criar, renomear, excluir e reordenar playlists; ↑/↓ permanecem como alternativa estável ao arrastar e soltar.
- O botão **Abrir localização dos arquivos** abre Songs/. Remover uma entrada ou playlist não apaga os arquivos.

Next cancela e aguarda a liberação do único player antes de iniciar a próxima faixa. A conclusão natural avança automaticamente usando a contagem entre músicas. Stop invalida continuações/carregamentos pendentes. Previous antes de 3 segundos volta no histórico; depois reinicia a atual. Repeat Playlist cria um novo ciclo; a próxima faixa é pré-calculada, inclusive no fim do ciclo aleatório.

Use **Salvar ajustes da carregada** para persistir título/artista, velocidade, transposição, alcance, spread, modo de acorde/Shift, performance, otimização/trilhas e configuração de duração por música.

## Sheets e JSON compatível

Sheets: `a` = nota, `[abc]` = acorde, `{abcd}` = sequência rápida, `-` = pausa; espaços/quebras de linha somam tempo. Padrões 120 BPM, espaço ¼ beat e hífen ½ beat. Maiúsculas exigem Shift. Corrija caracteres inválidos antes de tocar. Após editar, analise novamente.

JSON V2 com `keys` continua importando. Na V3, `notes` é opcional e permite durações individuais:

```json
{
  "title": "Exemplo",
  "bpm": 120,
  "events": [
    {
      "timeMs": 1000,
      "notes": [
        { "midi": 60, "durationMs": 1500, "velocity": 100, "trackIndex": 0 },
        { "midi": 64, "durationMs": 500 }
      ]
    }
  ]
}
```

A exportação inclui também `keys` para leitores antigos. BPM e duração total são opcionais ao importar. Duração individual ausente permanece ausente. Exportar aplica otimização/transposição/alcance, mas não embute a velocidade manual.

## Dados e backups

Mantido **%AppData%/PianoRoblox/**: songs.json, settings.json, playlists.json e Songs/. Arquivos físicos GUID não são renomeados ou apagados. Campos novos possuem defaults V2. Antes da primeira gravação V3, os índices e backups existentes são copiados para **Backups/before-v3/**; Songs/ permanece intacta.

Gravações são atômicas, com .bak. JSON inválido tenta backup válido e mostra aviso. O original é preservado; uma gravação após recuperação arquiva o conteúdo inválido em .corrupt-… sem substituir o backup válido. Se ambos forem inválidos, leitura/gravação falham explicitamente: não há reset silencioso nem salvamento de lista vazia por falha de leitura. A migração V1 continua preservando a pasta anterior. Para transferência: [TRANSFERIR.md](TRANSFERIR.md).

## Diagnósticos e atalhos

Diagnósticos opcionais exibem notas executadas/puladas, Down/Up, eventos atrasados, maior atraso, teclas ativas e conflitos. A UI atualiza esses números no máximo duas vezes por segundo; teclado/timeline continuam a 25 FPS com buffer visual. Os números descrevem o envio local, não recebimento de áudio ou FPS do jogo.

F4 anterior · F5 próxima · F6 iniciar · F7 pausar/retomar · F8 parar · F9 +5% · F10 −5% · F11 100%. Atalhos podem ser editados em settings.json com o app fechado; conflitos são registrados.

## Desenvolvimento

Windows x64, SDK .NET 8 ou mais recente, destino .NET 8. DryWetMidi 8.0.3; xUnit 2.9.2; runner 2.8.2; Microsoft.NET.Test.Sdk 17.11.1.

```powershell
dotnet restore PianoRoblox.sln
dotnet build PianoRoblox.sln -c Release
dotnet test PianoRoblox.sln -c Release
dotnet publish PianoRoblox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o artifacts/publish-v3
```

WPF não usa trimming. O SDK portátil usado nesta máquina fica em artifacts/tooling/dotnet/ e não integra o aplicativo distribuído.

[Arquitetura](ARCHITECTURE.md) · [Validação V3](VALIDATION.md) · [Relatório V3](V3-REPORT.md) · [Histórico V2](V2-REPORT.md)

