# SideDock

SideDock is a lightweight Windows 11 edge drawer for applications, files, folders, websites, and shortcuts. It is built with C#/.NET 8, WPF, and native Windows APIs.

## Features

- Opens from a right-edge handle or `Ctrl+Alt+Space`.
- Displays shortcuts as tiles or per-tab full-width rows.
- Uses crisp vector icons for folders and PDFs, including PDF shortcuts.
- Mirrors linked folders without modifying their contents.
- Browses folders through cascading menus.
- Supports drag-and-drop, editing, pinning, and startup with Windows.
- Watches shortcut folders and refreshes automatically.

## Install

1. Open the repository's **Releases** page.
2. Download `SideDock.exe` from the latest release.
3. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) if it is not already installed.
4. Place the executable in a permanent folder and run it.

The repository and its release downloads are private. A GitHub account with repository access is required.

## Use

- Hover over or click the handle in the middle of the right screen edge.
- Press `Ctrl+Alt+Space` to open or close the drawer.
- Select **PIN** to keep the drawer open.
- Select **EDIT** to reorder or remove tiles, or double-click a tab name to rename it.
- Open the `...` menu for startup, layout, folder, help, and exit options.
- Select **How to use** for the built-in step-by-step guide.

To add a linked folder tab, create a Windows shortcut to the folder and move the `.lnk` file into `C:\tools\SideDock shortcuts`. SideDock detects it automatically and displays the folder's direct contents as a read-only tab.

For long filenames, select the tab and enable **Full-width rows for this tab** in the `...` menu. The setting is saved separately for each tab.

Configuration is stored in `%LOCALAPPDATA%\SideDock\config.json`, with an automatic backup at `config.backup.json`.

## Development

Requirements: Windows 10/11 and the .NET 8 SDK.

```powershell
dotnet test .\SideDock.sln --configuration Release
dotnet publish .\src\SideDock\SideDock.csproj --configuration Release --runtime win-x64 --self-contained false --output .\publish\win-x64 -p:PublishSingleFile=true
```

Generated output under `bin`, `obj`, and `publish` is intentionally excluded from version control. Release executables belong in GitHub Releases, not in the repository.

## License

SideDock is licensed under the GNU General Public License v3.0. See `LICENSE` for the complete terms.
