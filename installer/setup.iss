; Inno Setup script for Power Helper. Build with:
;   iscc /DAppVersion=0.1.0 /DSourceExe=C:\full\path\to\publish\PowerHelper.exe installer\setup.iss
; SourceExe should point at the self-contained single-file exe from `dotnet publish`
; (see .github/workflows/release.yml for the exact publish command CI uses).

#ifndef AppVersion
#define AppVersion "0.0.0"
#endif
#ifndef SourceExe
#define SourceExe "..\publish\PowerHelper.exe"
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
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "PowerHelper.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\Power Helper"; Filename: "{app}\PowerHelper.exe"
Name: "{group}\Uninstall Power Helper"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Power Helper"; Filename: "{app}\PowerHelper.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PowerHelper.exe"; Description: "Launch Power Helper"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Best-effort cleanup of the logon scheduled task if "Start with Windows" was ever enabled -
; ignores failure since the task may never have been registered.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PowerHelper"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
