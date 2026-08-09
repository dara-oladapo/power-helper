; Inno Setup script for Power Helper. Build with:
;   iscc /DAppVersion=0.2.0 /DSourceDir=C:\full\path\to\publish installer\setup.iss
;
; SourceDir points at the whole `dotnet publish` output directory, not a single exe. The app
; head is .NET MAUI on WinUI 3, which does not support PublishSingleFile, so the payload is a
; folder containing PowerHelper.exe alongside the self-contained .NET and Windows App SDK
; runtimes. See .github/workflows/release.yml for the exact publish command CI uses.

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\publish"
#endif

[Setup]
; Stable across versions so upgrades replace in place instead of side-by-side installing.
AppId={{F688EA3B-B031-4F19-8B17-EE831F630BA6}
AppName=Power Helper
AppVersion={#AppVersion}
AppPublisher=Dara Oladapo
AppPublisherURL=https://github.com/dara-oladapo/power-helper
AppUpdatesURL=https://github.com/dara-oladapo/power-helper/releases
DefaultDirName={autopf}\PowerHelper
DefaultGroupName=Power Helper
UninstallDisplayIcon={app}\PowerHelper.exe
OutputBaseFilename=PowerHelper-Setup-{#AppVersion}
OutputDir=..\installer-output
Compression=lzma2
SolidCompression=yes
; The app itself always runs elevated (requireAdministrator in its manifest) to
; enable/disable the GPU device and manage its startup task, so the installer matches.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
; recursesubdirs/createallsubdirs because a WinUI publish output is a tree, not a file:
; runtime assemblies, native WindowsAppSDK binaries, and the generated icon assets all have
; to arrive intact or the app fails to start with a missing-dependency error.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Power Helper"; Filename: "{app}\PowerHelper.exe"
Name: "{group}\Uninstall Power Helper"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Power Helper"; Filename: "{app}\PowerHelper.exe"; Tasks: desktopicon

[Run]
; shellexec (not plain CreateProcess) is required here: Setup.exe itself runs elevated, and
; a directly-elevated process launching another exe that also demands elevation via its own
; manifest fails with error 740 (ERROR_ELEVATION_REQUIRED) unless routed through the shell.
Filename: "{app}\PowerHelper.exe"; Description: "Launch Power Helper"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Best-effort cleanup of the logon scheduled task if "Start with Windows" was ever enabled -
; ignores failure since the task may never have been registered.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PowerHelper"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
