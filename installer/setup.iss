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
; The app itself runs asInvoker (unprivileged) - this is for the installer's own needs:
; writing to Program Files and registering the elevated GPU-toggle helper tasks below.
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
; Registers the elevated GPU-toggle helper tasks the app triggers on demand (see
; WindowsGpuController) - created here, while Setup itself is already elevated, so the app
; never needs its own UAC prompt for this. WindowsGpuController.EnsureHelperTasksRegistered
; is the fallback for installs that skip this (e.g. running from source).
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""PowerHelperGpuEnable"" /TR ""\""{app}\PowerHelper.GpuHelper.exe\"" enable"" /RL HIGHEST /F"; Flags: runhidden
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /TN ""PowerHelperGpuDisable"" /TR ""\""{app}\PowerHelper.GpuHelper.exe\"" disable"" /RL HIGHEST /F"; Flags: runhidden
; shellexec (not plain CreateProcess) launches this as the interactive desktop user rather
; than inheriting Setup.exe's own elevated token - the app now runs asInvoker and should not
; start elevated just because the installer that launched it was.
Filename: "{app}\PowerHelper.exe"; Description: "Launch Power Helper"; Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
; Best-effort cleanup of the scheduled tasks if they were ever registered - ignores failure
; since any of them may never have been created.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PowerHelper"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PowerHelperGpuEnable"" /F"; Flags: runhidden; RunOnceId: "RemoveGpuEnableTask"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""PowerHelperGpuDisable"" /F"; Flags: runhidden; RunOnceId: "RemoveGpuDisableTask"
