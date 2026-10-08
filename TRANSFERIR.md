# Usar PianoRoblox em outro PC

## Aplicativo pronto

1. No outro PC Windows x64, entre na conta GitHub que tem acesso ao repositório privado.
2. Abra https://github.com/victororex/PianoRoblox/releases/latest.
3. Em Assets, baixe **PianoRoblox-v2-win-x64.zip**.
4. Extraia o ZIP para uma pasta e abra **PianoRoblox.exe**.

Não é necessário instalar .NET, Visual Studio ou compilar os fontes. O arquivo “Source code” do GitHub contém o projeto para desenvolvimento; o ZIP com “win-x64” contém o aplicativo pronto.

## Suas músicas e configurações

Os 38 arquivos MIDI fornecidos estão salvos na pasta [musicas pianoblox](musicas%20pianoblox/) deste repositório privado. No outro PC, baixe o repositório pelo botão **Code → Download ZIP** (ou faça `git clone`), extraia os arquivos e use **Importar MIDI** no PianoRoblox para escolher as músicas dessa pasta.

A biblioteca do aplicativo, suas playlists e configurações continuam na pasta local do Windows. Para levar também esses dados:

1. Feche o PianoRoblox nos dois PCs.
2. No PC atual, pressione Win+R e abra **%AppData%\PianoRoblox**.
3. Copie a pasta inteira para um pendrive, OneDrive ou outro meio de sua escolha. Ela inclui Songs, songs.json, playlists.json e settings.json quando existentes.
4. No outro PC, pressione Win+R, abra **%AppData%** e coloque a pasta **PianoRoblox** ali antes de abrir o programa. Se já existir uma biblioteca nesse PC, preserve uma cópia antes de substituir.
5. Abra PianoRoblox.exe. As músicas, playlists e configurações serão carregadas.

Se ainda estiver usando os dados da V1, abra primeiro a V2 no PC atual para concluir a migração automática e então copie a pasta nova.

## Código-fonte

Para continuar o desenvolvimento no outro computador:

```powershell
git clone https://github.com/victororex/PianoRoblox.git
cd PianoRoblox
dotnet restore PianoRoblox.sln
dotnet build PianoRoblox.sln -c Release
dotnet test PianoRoblox.sln -c Release
```

Desenvolvimento exige SDK .NET com suporte ao destino .NET 8 e WPF no Windows.
