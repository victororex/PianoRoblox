# PianoRoblox V2

Player desktop para Windows em C# / .NET 8 / WPF. Evolução incremental da V1: MIDI, sheets, JSON, SendInput, Play/Pause/Stop, velocidade, transposição, alcance, teclado virtual, timeline, biblioteca e atalhos continuam disponíveis.

## Executar

No outro PC, entre na sua conta GitHub e baixe **PianoRoblox-v2-win-x64.zip** na [página de releases](https://github.com/victororex/PianoRoblox/releases/latest). Extraia o ZIP e abra **PianoRoblox.exe**. O repositório é privado, portanto o download exige acesso à conta.

Para levar também músicas, playlists e configurações, siga [TRANSFERIR.md](TRANSFERIR.md).

Abra **artifacts/publish-v2/PianoRoblox.exe**. A publicação Windows x64 é autocontida e não exige instalar .NET no computador de destino. Pode distribuir apenas o executável. Não há instalador ou assinatura digital.

1. Abra uma experiência Roblox com piano controlável por teclado, ative o piano e feche o chat do jogo.
2. Importe MIDI/JSON ou analise uma sheet.
3. Em MIDI, confira as trilhas e o resultado do Smart Optimize. Auto é o padrão.
4. Mantenha a proteção de foco ativada. Clique Play e volte ao Roblox durante a contagem de 3 segundos, ou use F6 já no jogo.
5. F7 pausa/retoma; F8 para. Ao perder o foco, o player pausa.

A detecção usa RobloxPlayerBeta.exe, independentemente da experiência. Ela confirma o processo em foco, não se o piano ou o chat está selecionado. O perfil inicial é **Legacy C2–C7 61 Keys**, com o mapeamento exato da V1: [tabela completa](MAPPING.md). ScanCode é padrão; VirtualKey continua disponível. Teste as teclas pretas no seu layout físico.

## MIDI e Smart Optimize

O pipeline conserva instante, pitch, velocity, canal, trilha/nome, programa/instrumento, duração e percussão antes de criar a sequência. Aceita MIDI formato 0/1 e respeita mudanças de tempo; formato 2, com sequências independentes, é rejeitado. Note On com velocity zero atua como Note Off.

O canal 10 de General MIDI (índice 9) é percussão. **Ignorar bateria/percussão** começa ativado; trilhas exclusivamente de bateria começam desmarcadas. Trilhas mistas preservam suas notas melódicas. Para incluir bateria, selecione sua trilha e desligue o filtro.

A tabela mostra nome, instrumento, canais de 1 a 16, quantidade, faixa e notas fora de C2–C7. Cada checkbox recalcula em segundo plano. Resultados antigos não substituem cálculos mais recentes. Play, salvar e exportar aguardam a otimização. Mudanças durante uma reprodução valem no próximo início.

| Preset | Merge | Acorde máximo | Velocity mínima | Notas / 250 ms | Notas / 1 s |
|---|---:|---:|---:|---:|---:|
| Original | 0 ms | sem simplificação | 0 | sem limitador | sem limitador |
| Balanced | 6 ms | 6 | 15 | 10 | 32 |
| Performance | 8 ms | 4 | 25 | 7 | 22 |
| Custom | ajustável | ajustável | ajustável | ajustável | ajustável |

**Auto** analisa as trilhas escolhidas após filtro de percussão e dedupe exato: acorde máximo ≤4 e pico ≤25 notas/s → Original; ≤8 e ≤45 → Balanced; demais → Performance. O usuário pode escolher outro preset. Campos numéricos são editáveis em Custom.

Original aplica somente seleção de trilhas, filtro opcional de percussão e dedupe de pitch no mesmo instante. Preserva notas fracas. Balanced/Performance removem notas abaixo da velocity mínima e usam:

- **Merge ancorado:** 1000, 1002 e 1004 ms viram um acorde em 1000 ms com janela de 6 ms. A janela não se prolonga por encadeamento.
- **Dedupe:** conserva uma nota por pitch, favorecendo velocity/duração, e guarda as trilhas de origem.
- **Acordes:** heurística determinística prioriza melodia aguda, baixo, velocity, provável trilha principal e distribuição entre registros. Melody Priority é opcional.
- **Density Limiter:** janelas deslizantes de 250 e 1000 ms removem as notas menos importantes. Uma nota melódica posterior pode substituir acompanhamento anterior. Janelas são semiabertas: dois ataques separados por exatamente 1000 ms não ocupam a mesma janela de um segundo.

**BPM e duração são preservados.** O limitador descarta notas sem mudar timestamps. Somente o merge antecipa ataques dentro de sua janela de poucos milissegundos. Velocidade manual continua independente, de 50% a 150%.

“Antes” representa o MIDI completo; “Depois” inclui todos os filtros das trilhas escolhidas. A redução inclui bateria e trilhas desativadas. Nenhum MIDI original é regravado.

**Simular Performance** compara Original, Balanced e Performance sem enviar teclas. As chamadas SendInput são estimativas para Auto/GroupSafe antes da transposição, incluindo a liberação final dos acordes. Spread, falhas, pausas e limpeza extra podem alterar esse número. Não mede FPS.

## Playback e envio de teclas

O relógio continua baseado em Stopwatch/PlaybackClock e tempos absolutos. O custo de um acorde não é somado ao próximo timestamp. Pausa congela o relógio e velocidade muda sem saltar a posição. A resolução solicitada de 1 ms é liberada ao terminar. Não há spin loop.

- **Auto / Simultaneous:** naturais em lote. GroupSafe agrupa notas contíguas com o mesmo estado de Shift e teclas físicas distintas. Transições de Shift são serializadas, preservando a ordem.
- **Spread:** sequência por nota. Spread de 0–50 ms; Key Hold de 1–100 ms. O custo de pressionar/soltar e preparar Shift limita o intervalo mínimo real.
- **Normal:** esperas assíncronas limitadas, recalculadas pelo próximo alvo absoluto.
- **LowLatency:** esperas menores, de até 2 ms, sem espera ocupada.
- **FpsFriendly:** limita despachos a 8 notas/50 ms e 32 notas/s de tempo real; descarta eventos quando necessário. Não altera BPM.

LateEventPolicy preserva eventos recentes; acima de 80 ms descarta baixa prioridade e acima de 160 ms descarta inclusive os antigos de alta prioridade. Recuperação de eventos atrasados mais de 40 ms fica limitada a 8 notas/50 ms.

Um acorde natural usa uma chamada SendInput para os downs e uma para a liberação. Teclas previstas são rastreadas antes da chamada nativa. Envios parciais provocam limpeza também das teclas incertas. Stop, Pause, perda de foco, seek, troca de música e saída liberam notas e os dois lados de Shift/Ctrl/Alt. Falhas de liberação conservam o estado incerto para outra tentativa.

A duração original das notas é metadado MIDI; o envio usa **Key Hold**, sem sustain/pedal/dinâmica. Ao retomar um acorde interrompido, ele pode ser reenviado. O retorno de SendInput não confirma que o jogo aceitou a nota.

## Biblioteca, playlists e fila

Na Biblioteca, expanda **Salvar música carregada / atualizar ajustes**, edite título/artista/origem e adicione a música. O arquivo é copiado para a pasta local. **Salvar ajustes da carregada** atualiza uma entrada existente: transposição, velocidade, spread, preset, parâmetros Custom, trilhas, percussão, prioridade melódica e performance mode.

Digite um nome e use **+ Nova playlist**. Em **Todas as músicas**, selecione a música e a playlist de destino para adicionar. Dentro da playlist, use ↑/↓, remover, renomear e excluir. Excluir playlist não exclui músicas. Remover da biblioteca preserva o arquivo local; referências ausentes são ignoradas na navegação.

Duplo clique carrega e toca; dentro de playlist, inicia naquele ponto. O player inferior fica disponível em todas as abas.

- **Fila:** entradas adicionadas têm prioridade sobre o restante da playlist e não a alteram. IDs próprios permitem remover uma duplicata específica. Limpar próximas remove a ordem pendente, mantendo a música atual.
- **Shuffle:** pré-calcula uma permutação Fisher–Yates; visita todas uma vez por ciclo e mantém histórico. Evita repetir imediatamente a última música ao iniciar outro ciclo quando possível.
- **Next:** cancela e libera antes de iniciar a próxima. Avança automaticamente no término. Contagem entre músicas padrão: 0, configurável.
- **Previous:** antes de 3 segundos musicais, volta no histórico; depois disso reinicia a atual. Sem histórico, reinicia a atual.
- **Repeat Off:** para ao fim. **Repeat Playlist:** novo ciclo. **Repeat One:** repete ao concluir; Next manual avança.

Um único PlaybackService atende todas as músicas. Stop durante carregamento invalida a intenção de tocar.

## Sheets e JSON

**Esta sheet não contém timing preciso. O ritmo será estimado.**

| Sintaxe | Interpretação |
|---|---|
| a | nota |
| [6rtu] | acorde |
| {abcd} | sequência rápida, 1/16 beat por nota |
| - / -- | uma / duas pausas |
| espaço, tab, quebra de linha | separação temporal; CRLF conta uma vez |
| tuo | três notas com passo padrão |

Padrões: 120 BPM, espaço 1/4 beat, hífen 1/2 beat. Separadores explícitos substituem o passo implícito entre tokens; espaços/hífens consecutivos somam tempo. Em {abcd}, o próximo separador começa na última nota rápida. Pausas iniciais/finais são preservadas. [6rtu]--[rtu]--[etu]--[etu] começa em 0, 500, 1000 e 1500 ms nos padrões indicados.

Maiúsculas exigem Shift. Corrija caracteres desconhecidos e delimitadores incompletos antes de tocar. #, & e ) não são notas do perfil atual. Após editar uma sheet, analise novamente antes de salvar.

JSON compatível com V1:

~~~json
{
  "title": "Teste",
  "bpm": 120,
  "durationMs": 1100,
  "events": [
    { "timeMs": 0, "keys": ["t", "u", "o"] },
    { "timeMs": 1000, "keys": ["T"] }
  ]
}
~~~

BPM e duração são opcionais ao importar. Exportação aplica otimização MIDI, transposição e alcance, preservando o tempo original; velocidade manual não é embutida. Fora de C2–C7: Ignore descarta, Octave desloca por oitavas, Ask pergunta. Seek inicia nos ataques posteriores à posição, sem reconstruir sustentações.

## Dados e migração

Dados em **%AppData%/PianoRoblox/**: settings.json, songs.json, playlists.json e pasta Songs.

Se a pasta nova não existir e **%AppData%/GakuranAutoPiano/** existir, copia os dados para uma pasta temporária irmã e publica a cópia completa ao terminar. Preserva a pasta antiga, não sobrescreve a nova e registra **“Dados da versão anterior migrados para PianoRoblox.”** Links/junctions não são seguidos. Uma cópia interrompida não se torna a biblioteca ativa.

Novos campos têm defaults para JSON antigo. Índices/configurações são escritos em temporário, substituídos atomicamente e mantêm backup .bak. Playlists podem recuperar o backup quando o JSON atual está inválido.

Atalhos: **F4** anterior, **F5** próxima, **F6** play, **F7** pausa, **F8** stop, **F9** +5%, **F10** −5%, **F11** 100%. Conflitos são registrados no Log. F4/F5 só são adicionados à configuração antiga se não conflitam com um código salvo. Hotkeys podem ser editadas no settings.json com o app fechado.

## Desenvolvimento e publicação

Windows x64, SDK .NET 8 ou mais recente com destino .NET 8. Produção usa DryWetMidi 8.0.3; testes usam xUnit 2.9.2, runner 2.8.2 e Microsoft.NET.Test.Sdk 17.11.1.

~~~powershell
dotnet restore PianoRoblox.sln
dotnet build PianoRoblox.sln -c Release
dotnet test PianoRoblox.sln -c Release
dotnet run --project PianoRoblox.csproj
dotnet publish PianoRoblox.csproj -p:PublishProfile=Windows
~~~

Equivalente explícito de publicação:

~~~powershell
dotnet publish PianoRoblox.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish-v2
~~~

WPF não usa trimming. Bibliotecas nativas podem ser extraídas para pasta temporária pelo runtime. Nesta máquina foi usado cache NuGet curto em pasta temporária para evitar limites de caminho do Windows.

[Arquitetura](ARCHITECTURE.md) · [Validação](VALIDATION.md) · [Relatório dos 16 itens da V2](V2-REPORT.md)

## Limites

Os testes usam transportes simulados e não enviam notas ao desktop. Renderizam WPF e verificam bindings. O benchmark sintético valida contagens e BPM; **FPS real e reconhecimento musical dentro do Roblox dependem de avaliação no jogo**. Não foi fornecido MIDI real de Black Rover para comparação auditiva.

Cache persistente, áudio local, sustain por Note Off, perfis além de Legacy61 e exportação AutoHotkey ficaram fora desta entrega. O app não modifica arquivos ou memória do Roblox.

