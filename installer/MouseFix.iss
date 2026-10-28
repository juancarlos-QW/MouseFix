; Inno Setup script for the MouseFix installer.
; Build after publishing the app (see README):
;   ISCC.exe installer\MouseFix.iss
; Output: installer\Output\MouseFix-Setup-<version>.exe

#define AppName "MouseFix"
#define AppExe "MouseFix.exe"
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
; Version comes from the published exe (<Version> in MouseFix.App.csproj), as major.minor.patch.
#define Major
#define Minor
#define Patch
#define Build
#expr GetVersionComponents(PublishDir + "\" + AppExe, Major, Minor, Patch, Build)
#define AppVersion Str(Major) + "." + Str(Minor) + "." + Str(Patch)

[Setup]
; Picks Portuguese or English from the Windows language; asks only when neither matches.
ShowLanguageDialog=auto
; Never change AppId: Windows uses it to recognise upgrades of the same app.
AppId={{C1089291-C759-41EF-9319-17C37C031F5C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=juancarlos-QW
AppPublisherURL=https://github.com/juancarlos-QW/MouseFix
AppSupportURL=https://github.com/juancarlos-QW/MouseFix/issues
AppUpdatesURL=https://github.com/juancarlos-QW/MouseFix/releases
; Per-user install: no admin prompt. {autopf} becomes %LocalAppData%\Programs.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\MouseFix.App\Assets\mousefix.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=Output
OutputBaseFilename=MouseFix-Setup-{#AppVersion}
; The exe is already compressed, so heavier settings only slow the build down.
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Close a running MouseFix before replacing or removing it.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Checked by default: the desktop shortcut is created unless the user unticks it.
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; The app itself writes this value when "Iniciar com o Windows" is on; remove it on uninstall
; so Windows doesn't try to start a program that no longer exists.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MouseFix"; Flags: dontcreatekey uninsdeletevalue

[UninstallRun]
; The uninstaller can't close apps by itself; ask a running MouseFix to exit (and wait) so its files can be removed.
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: runhidden waituntilterminated; RunOnceId: "CloseMouseFix"
; Leave game mode and delete the "MouseFix Jogo" power plan, so Windows isn't left on a plan from an uninstalled app.
Filename: "{app}\{#AppExe}"; Parameters: "--remove-power-plan"; Flags: runhidden waituntilterminated; RunOnceId: "RemovePowerPlan"

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; Silent installs come from "Atualizar agora" inside MouseFix: reopen it when done.
Filename: "{app}\{#AppExe}"; Parameters: "--updated"; Flags: nowait skipifnotsilent
