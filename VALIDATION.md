# Validação PianoRoblox V2

Windows x64, 28/09/2026. SDK .NET 9.0.305, destino .NET 8.

- Restore concluído.
- Build Release: **zero avisos, zero erros**.
- Testes Release: **68 aprovados, zero falhas, zero ignorados**.
- Preservados os 34 casos existentes e adicionados 34 casos V2. O teste de interface existente foi ampliado.

Cobertura nova: metadados MIDI, percussão, dedupe, merge ancorado, seleção musical de acordes, todas as janelas de densidade, presets/Auto, BPM/duração, cancelamento, migração V1, defaults e configurações por música, playlists CRUD/ordem/backup, fila, shuffle/histórico/repeat, Previous, builder de input, envio parcial, Shift agrupado, foco, backlog, FPS Friendly, buffer visual e reutilização da timeline.

Integração WPF: sete abas renderizadas em memória, com MIDI e playlist preenchidos, sem erros de binding. Recalcular rapidamente conserva o preset mais recente. A playlist avança após liberar teclas; Stop durante leitura MIDI impede reinício; configurações por música são restauradas e salvas. O teclado falso confirma no máximo um acorde em execução.

## Benchmark calculado

MIDI sintético: 200 BPM, 7 trilhas incluindo tempo, 1152 notas MIDI, 192 de bateria. Todas as trilhas melódicas selecionadas, bateria excluída, sem transposição.

| Preset | Eventos | Notas | Pico eventos/s | Pico notas/s | Maior acorde | SendInput estimado |
|---|---:|---:|---:|---:|---:|---:|
| Original | 64 | 960 | 14 | 210 | 15 | 1554 |
| Balanced | 64 | 130 | 14 | 30 | 3 | 325 |
| Performance | 64 | 82 | 14 | 20 | 2 | 208 |

Todos preservam **200 BPM e a duração original**. Neste cenário, a redução está nas notas por evento; continuam 64 eventos. Outros arquivos podem reduzir eventos por merge ou remoção completa de grupos. Chamadas são estimativas Auto/GroupSafe, não medições de FPS. O exemplo não é uma transcrição de Black Rover.

## Evidências

- artifacts/build-v2.log
- artifacts/test-results-v2/v2-release.trx
- artifacts/ui-v2/tab-0.png até tab-6.png
- artifacts/ui-v2/benchmark.txt
- artifacts/ui-v2/complex-synthetic.mid
- artifacts/publish-v2.log
- artifacts/publish-v2/PianoRoblox.exe

~~~powershell
$env:PIANOROBLOX_UI_TEST_OUTPUT = Join-Path $PWD 'artifacts/ui-v2'
dotnet test PianoRoblox.sln -c Release --logger 'trx;LogFileName=v2-release.trx' --results-directory artifacts/test-results-v2
~~~

O relatório V1 foi preservado em artifacts/VALIDATION-v1-historical.md; snapshot de fontes em artifacts/source-v1-before-v2.zip. ZIPs fornecidos pelo usuário foram preservados.

Não foi executado teste musical dentro do Roblox. Recebimento de SendInput, FPS, layout físico e preservação auditiva ainda dependem de teste no jogo. Note Off é metadado; Key Hold controla o envio. Não há garantia de timing de 1 ms.

