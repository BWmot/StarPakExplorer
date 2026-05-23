# StarPakExplorer

Windows-only WPF desktop MVP for temporary unpacking and searching Starbound / OpenStarbound PAK files.

## Features

- Uses the official `asset_unpacker.exe` as the unpack backend.
- Caches unpacked PAKs under `%LOCALAPPDATA%\StarPakExplorer\Cache`.
- Remembers the last selected `asset_unpacker.exe` and last PAK parent directory in `%LOCALAPPDATA%\StarPakExplorer\settings.json`.
- Reads `_metadata` / `.metadata` for mod name, author, and Workshop ID.
- Shows unpacked resource files.
- Previews text-like Starbound assets and pretty-prints valid JSON.
- Previews common image files such as `png`, `jpg`, `gif`, and `bmp`.
- Searches keywords across text assets and shows file, line number, and line text.
- Scans duplicate `itemName` values inside one loaded PAK for `.item`, `.activeitem`, `.object`, and `.matitem` files.
- Filters the file list by Starbound resource sections and by checked file extensions discovered in the current PAK.

## Build

```powershell
dotnet build .\StarPakExplorer.sln
```

## Run

```powershell
dotnet run --project .\StarPakExplorer.UI\StarPakExplorer.UI.csproj
```

In the app:

1. Click `选择 asset_unpacker.exe` and pick Starbound's official unpacker.
2. Click `选择 PAK` and pick a `.pak` or `contents.pak`.
3. Select files on the left to preview text content.
4. Use the search tab for keywords such as `extrachargebeam`.
5. Use the duplicate tab to scan repeated `itemName` values inside the current PAK.

Logs are written to `%LOCALAPPDATA%\StarPakExplorer\Logs\app.log`.
