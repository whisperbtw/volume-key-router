#ifndef AppVersion
#define AppVersion "0.1.3"
#endif

#define AppName "Volume Key Router"
#define AppExeName "volume-key-router.exe"
#define AppPublisher "whisper"
#define AppUninstallKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\{B8EC1FE0-6836-43DE-B465-C9C3A94DF713}_is1"

[Setup]
AppId={{B8EC1FE0-6836-43DE-B465-C9C3A94DF713}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\Volume Key Router
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=VolumeKeyRouterSetup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\volume-key-router.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.pdb"
Type: files; Name: "{app}\*.ico"
Type: files; Name: "{app}\*.png"

[Files]
Source: "..\publish\win-x64\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Abrir {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\{#AppExeName}"; Parameters: "--shutdown-existing"; RunOnceId: "ShutdownVolumeKeyRouter"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  ExistingInstall: Boolean;
  ExistingVersion: string;
  ExistingInstallDir: string;
  MaintenancePage: TInputOptionWizardPage;

function DetectExistingInstall(): Boolean;
var
  InstallLocation: string;
begin
  ExistingVersion := '';
  ExistingInstallDir := ExpandConstant('{localappdata}\Programs\Volume Key Router');

  RegQueryStringValue(HKCU, '{#AppUninstallKey}', 'DisplayVersion', ExistingVersion);
  if RegQueryStringValue(HKCU, '{#AppUninstallKey}', 'InstallLocation', InstallLocation) then
  begin
    if InstallLocation <> '' then
    begin
      ExistingInstallDir := RemoveBackslashUnlessRoot(InstallLocation);
    end;
  end;

  Result :=
    (ExistingVersion <> '') or
    FileExists(AddBackslash(ExistingInstallDir) + '{#AppExeName}');
end;

function MaintenanceSubtitle(): string;
begin
  if ExistingVersion <> '' then
  begin
    Result :=
      'Versao instalada: ' + ExistingVersion + #13#10 +
      'Versao deste instalador: {#AppVersion}';
  end
  else
  begin
    Result :=
      'Ja existe uma instalacao em:' + #13#10 +
      ExistingInstallDir;
  end;
end;

procedure InitializeWizard();
begin
  ExistingInstall := DetectExistingInstall();
  if ExistingInstall then
  begin
    WizardForm.DirEdit.Text := ExistingInstallDir;

    MaintenancePage := CreateInputOptionPage(
      wpWelcome,
      'Instalacao existente encontrada',
      'Escolha como continuar',
      MaintenanceSubtitle(),
      True,
      False);
    MaintenancePage.Add('Atualizar para a versao {#AppVersion}');
    MaintenancePage.Add('Reparar a instalacao existente');

    if ExistingVersion = '{#AppVersion}' then
    begin
      MaintenancePage.Values[1] := True;
    end
    else
    begin
      MaintenancePage.Values[0] := True;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;

  if ExistingInstall and (PageID = wpSelectDir) then
  begin
    Result := True;
  end;

  if ExistingInstall and (MaintenancePage <> nil) and (PageID = MaintenancePage.ID) then
  begin
    Result := False;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if ExistingInstall and (CurPageID = wpReady) and (MaintenancePage <> nil) then
  begin
    if MaintenancePage.Values[1] then
    begin
      WizardForm.NextButton.Caption := '&Reparar';
    end
    else
    begin
      WizardForm.NextButton.Caption := '&Atualizar';
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    if FileExists(ExpandConstant('{app}\{#AppExeName}')) then
    begin
      Exec(
        ExpandConstant('{app}\{#AppExeName}'),
        '--shutdown-existing',
        '',
        SW_HIDE,
        ewWaitUntilTerminated,
        ResultCode);
      Sleep(1000);
    end;
  end;
end;
