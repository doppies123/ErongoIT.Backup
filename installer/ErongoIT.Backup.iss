; ErongoIT Backup - client installer (Inno Setup 6)
;
; Build: tools\build-installer.ps1  (publishes the apps into installer\stage, then compiles this).
;
; Wizard: Welcome > Location > Sign in > Customer & plan > Folders > Shortcuts > Ready > Install > Finish
;         (the three registration pages are skipped when upgrading an already-registered PC)
;
; Silent install for mass roll-out:
;   ErongoIT-Backup-Setup-x.y.z.exe /VERYSILENT /SUPPRESSMSGBOXES
;       /CUSTOMER="Steve's Take Away" /PLAN="15min" /FOLDERS="C:\Users\Bob\Documents;D:\Data"
;       /USER=admin /PASSWORD=...  [/SERVER=https://backup.erongoit.com] [/NAME=PC-NAME]

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

#define AppName        "ErongoIT Backup"
#define AppPublisher   "Erongo IT Consultants"
#define AppUrl         "https://erongoit.com"
#define DefaultServer  "https://backup.erongoit.com"
#define ServiceName    "ErongoITBackupAgent"
#define AgentExe       "ErongoIT.Backup.Agent.exe"
#define GuiExe         "ErongoIT.Backup.Agent.Gui.exe"
#define StageDir       "stage"

[Setup]
AppId={{6B7D3C1E-5A2F-4E8B-9C11-3F0E2A9D7B41}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\ErongoIT Backup
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=output
OutputBaseFilename=ErongoIT-Backup-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
WizardImageFile=branding\wizard-large.bmp,branding\wizard-large-2x.bmp
WizardSmallImageFile=branding\wizard-small.bmp,branding\wizard-small-2x.bmp
SetupIconFile=branding\setup.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\Gui\{#GuiExe}
CloseApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nErongoIT Backup protects the files on this PC by backing them up securely to the ErongoIT cloud, automatically and on schedule.%n%nYou will need your ErongoIT administrator login to register this computer.

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "{#StageDir}\Agent\*"; DestDir: "{app}\Agent"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#StageDir}\Gui\*";   DestDir: "{app}\Gui";   Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\Gui\{#GuiExe}"
Name: "{group}\Change backup setup";  Filename: "{app}\Gui\{#GuiExe}"; Parameters: "--setup"
Name: "{group}\Backup logs";          Filename: "{commonappdata}\ErongoIT Backup\logs"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\Gui\{#GuiExe}"; Tasks: desktopicon

[Registry]
; Start the tray app at every Windows sign-in (all users): it shows backup
; status and offers agent updates. "--tray" keeps the window hidden.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ErongoIT Backup"; ValueData: """{app}\Gui\{#GuiExe}"" --tray"; Flags: uninsdeletevalue

[Run]
; Offered on the Finish page; opened as the normal (non-admin) user.
Filename: "{app}\Gui\{#GuiExe}"; Description: "Open {#AppName}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
const
  ServiceName = '{#ServiceName}';

var
  SignInPage: TInputQueryWizardPage;
  CustomerPage: TWizardPage;
  FoldersPage: TWizardPage;

  CustomerCombo: TNewComboBox;
  PlanCombo: TNewComboBox;
  DeviceNameEdit: TNewEdit;
  FolderList: TNewListBox;

  AccessToken: String;
  CustomerIds, CustomerNames: TArrayOfString;
  PlanIds, PlanNames: TArrayOfString;

  EnrollFailed: Boolean;
  EnrollAttempted: Boolean;

{ ------------------------------------------------------------------ helpers }

function DataDir: String;
begin
  Result := ExpandConstant('{commonappdata}\ErongoIT Backup');
end;

function IsEnrolled: Boolean;
begin
  Result := FileExists(DataDir + '\agent.json');
end;

function GetParam(const ParamName, DefaultValue: String): String;
begin
  Result := ExpandConstant('{param:' + ParamName + '|' + DefaultValue + '}');
end;

function HasScriptedEnroll: Boolean;
begin
  Result := (GetParam('CUSTOMER', '') <> '') and (GetParam('PASSWORD', '') <> '') and (GetParam('FOLDERS', '') <> '');
end;

function ServerUrl: String;
begin
  Result := Trim(SignInPage.Values[0]);
  while (Length(Result) > 0) and (Result[Length(Result)] = '/') do
    Delete(Result, Length(Result), 1);
end;

procedure RunHidden(const FileName, Params: String);
var
  ResultCode: Integer;
begin
  Log('Running: ' + FileName + ' ' + Params);
  Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Log('  exit code ' + IntToStr(ResultCode));
end;

procedure AddString(var Arr: TArrayOfString; const Value: String);
begin
  SetArrayLength(Arr, GetArrayLength(Arr) + 1);
  Arr[GetArrayLength(Arr) - 1] := Value;
end;

{ Quote one command-line argument (Windows CommandLineToArgv rules). }
function QuoteArg(const S: String): String;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
  begin
    if S[I] = '"' then
      Result := Result + '\"'
    else
      Result := Result + S[I];
  end;
  if (Length(Result) > 0) and (Result[Length(Result)] = '\') then
    Result := Result + '\';
  Result := '"' + Result + '"';
end;

{ ------------------------------------------------------------------ JSON }

function JsonEscape(const S: String): String;
var
  I: Integer;
  C: Char;
begin
  { Note: no line may start with '#' in an .iss file (preprocessor), so
    control characters are written as Chr(n) here. }
  Result := '';
  for I := 1 to Length(S) do
  begin
    C := S[I];
    if C = '"' then
      Result := Result + '\"'
    else if C = '\' then
      Result := Result + '\\'
    else if C = Chr(8) then
      Result := Result + '\b'
    else if C = Chr(9) then
      Result := Result + '\t'
    else if C = Chr(10) then
      Result := Result + '\n'
    else if C = Chr(13) then
      Result := Result + '\r'
    else
      Result := Result + C;
  end;
end;

{ Reads a JSON string value starting just after its opening quote. }
function ReadJsonString(const Json: String; StartPos: Integer; var EndPos: Integer): String;
var
  I: Integer;
  C: Char;
begin
  Result := '';
  I := StartPos;
  while I <= Length(Json) do
  begin
    C := Json[I];
    if C = '"' then
      Break;
    if (C = '\') and (I < Length(Json)) then
    begin
      I := I + 1;
      case Json[I] of
        'n': Result := Result + #10;
        't': Result := Result + #9;
        'r': Result := Result + #13;
        'b': Result := Result + #8;
        'f': Result := Result + #12;
        'u':
          begin
            Result := Result + Chr(StrToIntDef('$' + Copy(Json, I + 1, 4), 63));
            I := I + 4;
          end;
      else
        Result := Result + Json[I];
      end;
    end
    else
      Result := Result + C;
    I := I + 1;
  end;
  EndPos := I;
end;

function JsonValue(const Json, Key: String): String;
var
  P, EndPos: Integer;
begin
  Result := '';
  P := Pos('"' + Key + '":"', Json);
  if P > 0 then
    Result := ReadJsonString(Json, P + Length(Key) + 4, EndPos);
end;

// Parses a JSON array of objects with "id" and "name" into two arrays.
procedure ParseIdNameList(Json: String; var Ids, Names: TArrayOfString);
var
  P, EndPos: Integer;
  ItemId, ItemName: String;
begin
  SetArrayLength(Ids, 0);
  SetArrayLength(Names, 0);

  while True do
  begin
    P := Pos('"id":"', Json);
    if P = 0 then
      Break;
    ItemId := ReadJsonString(Json, P + 6, EndPos);
    Delete(Json, 1, EndPos);

    P := Pos('"name":"', Json);
    if P = 0 then
      Break;
    ItemName := ReadJsonString(Json, P + 8, EndPos);
    Delete(Json, 1, EndPos);

    AddString(Ids, ItemId);
    AddString(Names, ItemName);
  end;
end;

{ ------------------------------------------------------------------ HTTP }

function Http(const Method, Url, Body: String; var Status: Integer; var Response: String): Boolean;
var
  Req: Variant;
begin
  Result := False;
  Status := 0;
  Response := '';
  try
    Req := CreateOleObject('WinHttp.WinHttpRequest.5.1');
    Req.Open(Method, Url, False);
    Req.SetTimeouts(15000, 15000, 30000, 30000);
    Req.SetRequestHeader('Content-Type', 'application/json');
    if AccessToken <> '' then
      Req.SetRequestHeader('Authorization', 'Bearer ' + AccessToken);
    Req.Send(Body);
    Status := Req.Status;
    Response := Req.ResponseText;
    Result := True;
  except
    Response := GetExceptionMessage;
    Log('HTTP error: ' + Response);
  end;
end;

function SignIn(var ErrorMessage: String): Boolean;
var
  Status: Integer;
  Response: String;
begin
  Result := False;
  AccessToken := '';

  if not Http('POST', ServerUrl + '/api/auth/login',
      '{"username":"' + JsonEscape(SignInPage.Values[1]) + '","password":"' + JsonEscape(SignInPage.Values[2]) + '"}',
      Status, Response) then
  begin
    ErrorMessage := 'Could not reach the backup server.' + #13#10#13#10 + Response;
    Exit;
  end;

  if Status = 401 then
  begin
    ErrorMessage := 'The username or password is incorrect.';
    Exit;
  end;

  if Status <> 200 then
  begin
    ErrorMessage := 'The server returned HTTP ' + IntToStr(Status) + '.';
    Exit;
  end;

  AccessToken := JsonValue(Response, 'accessToken');
  Result := AccessToken <> '';
  if not Result then
    ErrorMessage := 'The server returned an unexpected login response.';
end;

function LoadCustomers(var ErrorMessage: String): Boolean;
var
  Status, I: Integer;
  Response: String;
begin
  Result := False;
  if not Http('GET', ServerUrl + '/api/customers', '', Status, Response) or (Status <> 200) then
  begin
    ErrorMessage := 'Could not load customers (HTTP ' + IntToStr(Status) + ').';
    Exit;
  end;

  ParseIdNameList(Response, CustomerIds, CustomerNames);

  if GetArrayLength(CustomerIds) = 0 then
  begin
    ErrorMessage := 'There are no customers on the server yet. Create one in the portal first.';
    Exit;
  end;

  CustomerCombo.Items.Clear;
  for I := 0 to GetArrayLength(CustomerNames) - 1 do
    CustomerCombo.Items.Add(CustomerNames[I]);
  CustomerCombo.ItemIndex := 0;
  Result := True;
end;

procedure LoadPlans;
var
  Status, I: Integer;
  Response: String;
begin
  PlanCombo.Items.Clear;
  SetArrayLength(PlanIds, 0);
  SetArrayLength(PlanNames, 0);

  { Index 0 = no plan. }
  AddString(PlanIds, '');
  AddString(PlanNames, '(none - assign later in the portal)');

  if CustomerCombo.ItemIndex >= 0 then
    if Http('GET', ServerUrl + '/api/customers/' + CustomerIds[CustomerCombo.ItemIndex] + '/backup-plans', '', Status, Response)
       and (Status = 200) then
    begin
      ParseIdNameList(Response, PlanIds, PlanNames);
      { Re-insert the "none" choice in front. }
      SetArrayLength(PlanIds, GetArrayLength(PlanIds) + 1);
      SetArrayLength(PlanNames, GetArrayLength(PlanNames) + 1);
      for I := GetArrayLength(PlanIds) - 1 downto 1 do
      begin
        PlanIds[I] := PlanIds[I - 1];
        PlanNames[I] := PlanNames[I - 1];
      end;
      PlanIds[0] := '';
      PlanNames[0] := '(none - assign later in the portal)';
    end;

  for I := 0 to GetArrayLength(PlanNames) - 1 do
    PlanCombo.Items.Add(PlanNames[I]);

  if PlanCombo.Items.Count > 1 then
    PlanCombo.ItemIndex := 1
  else
    PlanCombo.ItemIndex := 0;
end;

procedure CustomerComboChange(Sender: TObject);
begin
  LoadPlans;
end;

{ ------------------------------------------------------------------ folder page buttons }

procedure AddFolderClick(Sender: TObject);
var
  Dir: String;
begin
  Dir := ExpandConstant('{userdocs}');
  if BrowseForFolder('Select a folder to back up:', Dir, False) then
    if FolderList.Items.IndexOf(Dir) < 0 then
      FolderList.Items.Add(Dir);
end;

procedure RemoveFolderClick(Sender: TObject);
begin
  if FolderList.ItemIndex >= 0 then
    FolderList.Items.Delete(FolderList.ItemIndex);
end;

{ ------------------------------------------------------------------ wizard pages }

function NewLabel(Page: TWizardPage; const Caption: String; Top: Integer): TNewStaticText;
begin
  Result := TNewStaticText.Create(Page);
  Result.Parent := Page.Surface;
  Result.Caption := Caption;
  Result.Top := Top;
  Result.Left := 0;
end;

procedure InitializeWizard;
var
  Btn: TNewButton;
begin
  { Sign in }
  SignInPage := CreateInputQueryPage(wpSelectDir,
    'Sign in',
    'Connect to the ErongoIT backup server.',
    'Enter your ErongoIT administrator login. It is used only to register this computer and is not stored on it.');
  SignInPage.Add('Backup server:', False);
  SignInPage.Add('Username:', False);
  SignInPage.Add('Password:', True);
  SignInPage.Values[0] := GetParam('SERVER', '{#DefaultServer}');
  SignInPage.Values[1] := 'admin';

  { Customer & plan }
  CustomerPage := CreateCustomPage(SignInPage.ID,
    'Customer and backup plan',
    'Choose who this computer belongs to and how often it is backed up.');

  NewLabel(CustomerPage, 'Customer:', 0);
  CustomerCombo := TNewComboBox.Create(CustomerPage);
  CustomerCombo.Parent := CustomerPage.Surface;
  CustomerCombo.Style := csDropDownList;
  CustomerCombo.Top := ScaleY(18);
  CustomerCombo.Width := CustomerPage.SurfaceWidth;
  CustomerCombo.OnChange := @CustomerComboChange;

  NewLabel(CustomerPage, 'Backup plan:', ScaleY(56));
  PlanCombo := TNewComboBox.Create(CustomerPage);
  PlanCombo.Parent := CustomerPage.Surface;
  PlanCombo.Style := csDropDownList;
  PlanCombo.Top := ScaleY(74);
  PlanCombo.Width := CustomerPage.SurfaceWidth;

  NewLabel(CustomerPage, 'Computer name (as shown in the portal):', ScaleY(112));
  DeviceNameEdit := TNewEdit.Create(CustomerPage);
  DeviceNameEdit.Parent := CustomerPage.Surface;
  DeviceNameEdit.Top := ScaleY(130);
  DeviceNameEdit.Width := CustomerPage.SurfaceWidth;
  DeviceNameEdit.Text := GetParam('NAME', GetComputerNameString);

  { Folders }
  FoldersPage := CreateCustomPage(CustomerPage.ID,
    'Folders to back up',
    'Choose the folders on this computer to protect.');

  NewLabel(FoldersPage, 'These folders (and everything inside them) will be backed up:', 0);

  FolderList := TNewListBox.Create(FoldersPage);
  FolderList.Parent := FoldersPage.Surface;
  FolderList.Top := ScaleY(20);
  FolderList.Width := FoldersPage.SurfaceWidth;
  FolderList.Height := FoldersPage.SurfaceHeight - ScaleY(58);
  if DirExists(ExpandConstant('{userdocs}')) then
    FolderList.Items.Add(ExpandConstant('{userdocs}'));
  if DirExists(ExpandConstant('{userdesktop}')) then
    FolderList.Items.Add(ExpandConstant('{userdesktop}'));

  Btn := TNewButton.Create(FoldersPage);
  Btn.Parent := FoldersPage.Surface;
  Btn.Caption := '&Add folder...';
  Btn.Top := FolderList.Top + FolderList.Height + ScaleY(8);
  Btn.Width := ScaleX(110);
  Btn.Height := ScaleY(26);
  Btn.OnClick := @AddFolderClick;

  Btn := TNewButton.Create(FoldersPage);
  Btn.Parent := FoldersPage.Surface;
  Btn.Caption := '&Remove';
  Btn.Top := FolderList.Top + FolderList.Height + ScaleY(8);
  Btn.Left := ScaleX(118);
  Btn.Width := ScaleX(90);
  Btn.Height := ScaleY(26);
  Btn.OnClick := @RemoveFolderClick;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if (PageID = SignInPage.ID) or (PageID = CustomerPage.ID) or (PageID = FoldersPage.ID) then
    Result := IsEnrolled or WizardSilent;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ErrorMessage: String;
begin
  Result := True;

  if CurPageID = SignInPage.ID then
  begin
    if (ServerUrl = '') or (Trim(SignInPage.Values[1]) = '') or (SignInPage.Values[2] = '') then
    begin
      MsgBox('Enter the server address, username and password.', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    WizardForm.NextButton.Enabled := False;
    try
      Result := SignIn(ErrorMessage);
      if Result then
        Result := LoadCustomers(ErrorMessage);
    finally
      WizardForm.NextButton.Enabled := True;
    end;

    if Result then
      LoadPlans
    else
      MsgBox(ErrorMessage, mbError, MB_OK);
  end
  else if CurPageID = CustomerPage.ID then
  begin
    if CustomerCombo.ItemIndex < 0 then
    begin
      MsgBox('Choose a customer.', mbError, MB_OK);
      Result := False;
    end
    else if Trim(DeviceNameEdit.Text) = '' then
    begin
      MsgBox('Enter a computer name.', mbError, MB_OK);
      Result := False;
    end;
  end
  else if CurPageID = FoldersPage.ID then
  begin
    if FolderList.Items.Count = 0 then
    begin
      MsgBox('Add at least one folder to back up.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
var
  I: Integer;
begin
  Result := MemoDirInfo + NewLine + NewLine;

  if IsEnrolled then
    Result := Result + 'Registration:' + NewLine + Space + 'This PC is already registered - its settings will be kept.'
  else
  begin
    Result := Result + 'Register this computer:' + NewLine +
      Space + 'Server: ' + ServerUrl + NewLine +
      Space + 'Customer: ' + CustomerCombo.Text + NewLine +
      Space + 'Backup plan: ' + PlanCombo.Text + NewLine +
      Space + 'Computer name: ' + Trim(DeviceNameEdit.Text) + NewLine + NewLine +
      'Folders to back up:';
    for I := 0 to FolderList.Items.Count - 1 do
      Result := Result + NewLine + Space + FolderList.Items[I];
  end;

  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
end;

{ ------------------------------------------------------------------ install }

procedure StopServiceAndApps;
begin
  RunHidden(ExpandConstant('{sys}\sc.exe'), 'stop ' + ServiceName);
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#GuiExe}');
  RunHidden(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AgentExe}');
  Sleep(3000);
end;

{ Upgrades: stop the running service/GUI before files are replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopServiceAndApps;
  Result := '';
end;

procedure InstallService;
var
  AgentPath: String;
begin
  AgentPath := ExpandConstant('{app}\Agent\{#AgentExe}');

  { create fails harmlessly if the service exists (upgrade); config then points it here. }
  RunHidden(ExpandConstant('{sys}\sc.exe'),
    'create ' + ServiceName + ' binPath= "\"' + AgentPath + '\"" start= auto DisplayName= "ErongoIT Backup Agent"');
  RunHidden(ExpandConstant('{sys}\sc.exe'),
    'config ' + ServiceName + ' binPath= "\"' + AgentPath + '\"" start= auto');
  RunHidden(ExpandConstant('{sys}\sc.exe'),
    'description ' + ServiceName + ' "Backs up this computer to the ErongoIT cloud backup server."');
  RunHidden(ExpandConstant('{sys}\sc.exe'),
    'failure ' + ServiceName + ' reset= 86400 actions= restart/60000/restart/60000/restart/300000');
end;

{ Runs "Agent.exe --enroll". The password goes through a private temp file,
  never the command line. Folders are separated by ';'. }
function Enroll(const Server, User, Password, Customer, DeviceName, Plan, Folders: String): Boolean;
var
  Params, Remaining, Folder, PasswordFile: String;
  P, ResultCode: Integer;
  Lines: TArrayOfString;
begin
  PasswordFile := ExpandConstant('{tmp}\enroll.dat');
  SetArrayLength(Lines, 1);
  Lines[0] := Password;
  SaveStringsToUTF8File(PasswordFile, Lines, False);

  Params := '--enroll --server ' + QuoteArg(Server) +
            ' --username ' + QuoteArg(User) +
            ' --password-file ' + QuoteArg(PasswordFile) +
            ' --customer ' + QuoteArg(Customer) +
            ' --name ' + QuoteArg(DeviceName);

  if Plan <> '' then
    Params := Params + ' --plan ' + QuoteArg(Plan);

  Remaining := Folders;
  while Remaining <> '' do
  begin
    P := Pos(';', Remaining);
    if P = 0 then
    begin
      Folder := Remaining;
      Remaining := '';
    end
    else
    begin
      Folder := Copy(Remaining, 1, P - 1);
      Delete(Remaining, 1, P);
    end;
    Folder := Trim(Folder);
    if Folder <> '' then
      Params := Params + ' --folder ' + QuoteArg(Folder);
  end;

  Log('Enrolling ' + DeviceName + ' for customer ' + Customer);
  Exec(ExpandConstant('{app}\Agent\{#AgentExe}'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  DeleteFile(PasswordFile);
  Log('  enrollment exit code ' + IntToStr(ResultCode));

  Result := ResultCode = 0;
end;

procedure RegisterThisPc;
var
  I: Integer;
  Folders, Plan: String;
begin
  if HasScriptedEnroll then
  begin
    EnrollAttempted := True;
    EnrollFailed := not Enroll(
      GetParam('SERVER', '{#DefaultServer}'),
      GetParam('USER', 'admin'),
      GetParam('PASSWORD', ''),
      GetParam('CUSTOMER', ''),
      GetParam('NAME', GetComputerNameString),
      GetParam('PLAN', ''),
      GetParam('FOLDERS', ''));
  end
  else if not IsEnrolled and (AccessToken <> '') then
  begin
    Folders := '';
    for I := 0 to FolderList.Items.Count - 1 do
      Folders := Folders + FolderList.Items[I] + ';';

    Plan := '';
    if PlanCombo.ItemIndex > 0 then
      Plan := PlanIds[PlanCombo.ItemIndex];

    EnrollAttempted := True;
    EnrollFailed := not Enroll(
      ServerUrl,
      Trim(SignInPage.Values[1]),
      SignInPage.Values[2],
      CustomerIds[CustomerCombo.ItemIndex],
      Trim(DeviceNameEdit.Text),
      Plan,
      Folders);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := 'Installing the backup service...';
    InstallService;

    WizardForm.StatusLabel.Caption := 'Registering this computer with the backup server...';
    RegisterThisPc;

    if IsEnrolled then
    begin
      WizardForm.StatusLabel.Caption := 'Starting the backup service...';
      RunHidden(ExpandConstant('{sys}\sc.exe'), 'start ' + ServiceName);
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    if EnrollAttempted and EnrollFailed then
      WizardForm.FinishedLabel.Caption :=
        'ErongoIT Backup is installed, but registering this computer failed.' + #13#10#13#10 +
        'Open "Change backup setup" from the Start menu to try again.'
    else if not IsEnrolled then
      WizardForm.FinishedLabel.Caption :=
        'ErongoIT Backup is installed, but this computer is not registered yet, so no backups will run.' + #13#10#13#10 +
        'Open "Change backup setup" from the Start menu to register it.'
    else
      WizardForm.FinishedLabel.Caption :=
        'ErongoIT Backup is installed and this computer is registered.' + #13#10#13#10 +
        'Backups run automatically in the background, even when nobody is logged in.';
  end;
end;

{ ------------------------------------------------------------------ uninstall }

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopServiceAndApps;
    RunHidden(ExpandConstant('{sys}\sc.exe'), 'delete ' + ServiceName);
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if DirExists(DataDir) then
    begin
      if UninstallSilent or
         (MsgBox('Also remove this computer''s backup registration, logs and cache?' + #13#10#13#10 +
                 '(Backups already stored on the server are NOT deleted.)',
                 mbConfirmation, MB_YESNO) = IDYES) then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
