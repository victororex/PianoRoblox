# Validação PianoRoblox V3

10/10/2026, Windows x64, SDK .NET 8.0.423, alvo .NET 8. A V2 foi compilada/testada antes das alterações: **68 aprovados**, sem avisos/erros. Histórico preservado em artifacts/VALIDATION-v2-historical.md e V2-REPORT.md.

- Restore V3 concluído.
- Build Release final: **0 avisos, 0 erros**.
- Suíte V3: **108 aprovados, 0 falhas, 0 ignorados** (~12 s).
- Integração WPF final adicional: **1 aprovado**, sete abas renderizadas e nenhum erro de binding.
- Publicação Windows x64 autocontida/arquivo único concluída: **3.0.0.0**, 162884480 bytes.
- SHA-256: A025772EAB4E28292407F4FF8E28981154745B0F3098224C8179711901F7C525.

## Evidências

| Verificação | Evidência |
|---|---|
| Referência V2 | artifacts/test-results-v2-baseline/v2-baseline.trx |
| Restore / build / testes / publish V3 | artifacts/restore-v3.log; artifacts/build-v3.log; artifacts/test-v3.log; artifacts/publish-v3.log |
| 108 casos | artifacts/test-results-v3/v3-delivery.trx |
| WPF final | artifacts/test-results-v3/v3-ui-final.trx |
| Sete abas | artifacts/ui-v3/tab-0.png…tab-6.png |
| Título real no Smart Optimize e barra inferior | artifacts/ui-v3/v3-library-title.png |
| MIDIs reais | artifacts/ui-v3/real-midi-v3.txt |
| Executável | artifacts/publish-v3/PianoRoblox.exe |

Os testes usam teclados/transportes simulados. Não enviam notas ao desktop nem confirmam recebimento pelo Roblox. O smoke test WPF usa biblioteca temporária, sem alterar AppData pessoal. A análise dos três MIDIs existentes verifica dados e intervalos, sem transcrição/download ou avaliação auditiva.

## Casos de aceitação automatizados

1. Song.Title separado de GUID; rename e otimizações/leitura concorrentes; título consistente nos dados e bindings WPF.
2. Play All sem seleção: A→B→C automático; contagem/fila/Previous preservados.
3. Shuffle percorre o ciclo sem repetição e preview coincide com Next, inclusive no repeat seguinte.
4. C4 Down em ~1000 ms, Up em ~2500 ms; não é um tap de 8 ms. Teste real de relógio com tolerância operacional, sem prometer precisão de 1 ms.
5. Polifonia C4/E4: E4 termina e C4 continua pressionada.
6. Mesmo pitch reatacado; Off antigo não libera ataque novo; gap físico mínimo também a 150%.
7. Pause libera tudo/congela relógio; Resume usa duração restante. Foco perdido tem motivo próprio e impede retomada sem foco.
8. Next/Stop durante sustain liberam A antes de B e impedem avanço tardio.
9. Conflitos Shift/tecla física identificados; limpeza e falhas parciais V2 preservadas.
10. MIDI existente e JSON V2 importam; JSON V3 round-trip com duração e metadados.
11. Balanced conserva términos após merge/dedupe; transposição/dobramento conserva duração e maior término em colisões.
12. Coleção vazia/arquivos ausentes/corrupção não provocam reset ou loop infinito. Backup/original preservados.
13. Note Off respeita 50/100/150% e alteração ao vivo sem salto; ajustes da sessão são copiados.
14. 2000 ataques densos: conclusão sem erro/tecla presa, contagens Down/Up equilibradas; um worker e nenhuma Task por nota.

## Protocolo manual pendente no Roblox

1. Use uma cópia da biblioteca se desejar comparar V2/V3 lado a lado. Abra o jogo, ative o piano e mantenha a proteção de foco ligada.
2. Em Teste, execute **Testar sustain e polifonia**: C4 dura 1,5 s; E4 entra após 0,5 s e dura 0,5 s, enquanto C4 continua. Compare visual/sonoramente com Legacy.
3. Confirme o segundo ataque de C4 e a nota Shift+t. Teste GroupSafe/Sequential e diferentes layouts ScanCode/VirtualKey. Verifique se a experiência sustenta áudio por tecla ou usa amostra fixa.
4. Pause durante C4, perca o foco, retome com F7, faça seek no meio, use Stop/Next/Previous e feche o app. Nenhuma tecla/modificador deve permanecer pressionado; a pausa por foco deve explicar o motivo.
5. Importe Never Meant to Belong, Treachery e Black Rover; compare Original/Balanced e 50/100/150%, inclusive mudando a velocidade durante nota longa. Confira avisos de conflito, cobertura e transposição antes de aplicar.
6. Crie uma playlist das três, clique Reproduzir tudo, verifique a ordem/contagem/próxima faixa e conclusão automática. Confira Shuffle, Repeat One, Repeat Playlist, fila manual e arquivo removido temporariamente numa cópia de teste.
7. Se avaliar FPS/CPU, use ferramentas confiáveis externas com o mesmo jogo, cena e trecho, anotando medições V2/Legacy/Hybrid em condições iguais. O aplicativo não mede nem estima FPS real.

Pendências opcionais: editor visual de perfis/calibração e drag-and-drop. A versão mantém o perfil Legacy61 e reordenação ↑/↓. Limitações completas e detalhes de entrega em V3-REPORT.md.
