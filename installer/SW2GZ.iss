; SW2GZ installer — auto-removes previous install, kills SolidWorks if running,
; deploys + registers v2.0.0 in one click.
; Build: ISCC.exe installer\SW2GZ.iss
#define MyAppName        "SW2GZ"
#define MyAppVersion     "2.0.0"
#define MyAppPublisher   "Aryan Arlikar"
; SW2GZ COM addin GUID (matches HKLM\SOFTWARE\SolidWorks\Addins\{...}).
#define AddinGuid        "{34fad620-2a46-4ba6-9f5f-1dfefde894c7}"

[Setup]
AppId={{#AddinGuid}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SW2GZ
DefaultGroupName=SW2GZ
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=SW2GZ-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
; Close SolidWorks automatically if it's running — DLL is locked while SW holds the addin.
; The [Code] InitializeSetup hook also runs taskkill /F as a hard backstop.
CloseApplications=force
RestartApplications=no

[Files]
Source: "..\SW2GZ\bin\x64\Release\SW2GZ.dll";        DestDir: "{app}"; Flags: ignoreversion
Source: "..\SW2GZ\bin\x64\Release\SW2GZ.dll.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Ship runtime dependency DLLs, but NEVER redistribute:
;   solidworkstools.dll  -> Dassault Systemes' property, already present on every machine that has SolidWorks.
;   xunit* / Moq / Castle.Core / *TestPlatform*  -> test-only deps, not needed at runtime.
Source: "..\SW2GZ\bin\x64\Release\*.dll";            DestDir: "{app}"; Flags: ignoreversion; Excludes: "solidworkstools.dll,xunit*.dll,Moq.dll,Castle.Core.dll,Microsoft.VisualStudio.TestPlatform*.dll"

[InstallDelete]
; Wipe stale binaries from the install dir before laying down the new build.
Type: filesandordirs; Name: "{app}\*"

; RegAsm registration runs in [Code] (CurStepChanged/ssPostInstall) — it must execute
; AFTER solidworkstools.dll is copied next to SW2GZ.dll (see CopyLocalSolidWorksTools).
; RegAsm has to load the assembly to register it, and the assembly references SolidWorksTools.

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: "/u ""{app}\SW2GZ.dll"""; Flags: runhidden runascurrentuser; RunOnceId: "Sw2gzRegasmUnregister"

[UninstallDelete]
; Remove the locally-copied solidworkstools.dll and the (now-empty) install dir.
Type: filesandordirs; Name: "{app}"

[Code]
{ ---------- Detect + silently uninstall any previous SW2GZ install. ---------- }
function GetUninstallerPath: string;
var
  sUnInstPath: string;
  sUnInstallString: string;
begin
  Result := '';
  // Literal Pascal string -- NO ExpandConstant. The #AddinGuid substitution
  // happens at compile time; passing the resulting brace-wrapped GUID through
  // ExpandConstant would make Inno try to parse it as a runtime constant.
  sUnInstPath := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AddinGuid}_is1';
  sUnInstallString := '';
  if not RegQueryStringValue(HKLM, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function StopSolidWorks: Integer;
var
  iResult: Integer;
begin
  Result := 0;
  { Try to close SOLIDWORKS gracefully via taskkill; ignore failures (it may not be running). }
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM SLDWORKS.exe /T /F >NUL 2>NUL', '', SW_HIDE, ewWaitUntilTerminated, iResult);
end;

function InitializeSetup: Boolean;
var
  sUninstaller: string;
  iResult: Integer;
begin
  Result := True;
  { Kill any running SolidWorks (DLL is locked while it loads the addin). }
  StopSolidWorks;

  { Find any prior SW2GZ install registered under our AppId and silently uninstall it. }
  sUninstaller := GetUninstallerPath;
  if sUninstaller <> '' then
  begin
    { Strip surrounding quotes Inno records around the path. }
    sUninstaller := RemoveQuotes(sUninstaller);
    if FileExists(sUninstaller) then
    begin
      Exec(sUninstaller,
           '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCANCEL',
           '', SW_HIDE, ewWaitUntilTerminated, iResult);
      { Give regasm /u a moment to release any handles before we lay down new bits. }
      Sleep(800);
    end;
  end;
end;

{ ---------- Locate the machine's own solidworkstools.dll (Dassault's). ----------
  We never ship this DLL. RegAsm must load SW2GZ.dll to register it, and SW2GZ.dll
  references the SolidWorksTools assembly, so the DLL has to be resolvable. Every
  target machine already has SolidWorks installed, so we source it from there. }
function GetSolidWorksToolsDll: string;
var
  sRegPath: string;
begin
  Result := '';
  if FileExists(ExpandConstant('{commonpf}\SOLIDWORKS Corp\SOLIDWORKS\solidworkstools.dll')) then
    Result := ExpandConstant('{commonpf}\SOLIDWORKS Corp\SOLIDWORKS\solidworkstools.dll')
  else if FileExists(ExpandConstant('{commonpf32}\SOLIDWORKS Corp\SOLIDWORKS\solidworkstools.dll')) then
    Result := ExpandConstant('{commonpf32}\SOLIDWORKS Corp\SOLIDWORKS\solidworkstools.dll')
  else if RegQueryStringValue(HKLM, 'SOFTWARE\SOLIDWORKS\Setup', 'SolidWorks Folder', sRegPath) then
    if FileExists(AddBackslash(sRegPath) + 'solidworkstools.dll') then
      Result := AddBackslash(sRegPath) + 'solidworkstools.dll';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  sApp, sTools: string;
  iResult: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    sApp := ExpandConstant('{app}');
    { Copy the local (already-licensed) solidworkstools.dll next to SW2GZ.dll so RegAsm
      can resolve it. This is a local copy of the user's own file, not redistribution. }
    sTools := GetSolidWorksToolsDll;
    if sTools <> '' then
      FileCopy(sTools, sApp + '\solidworkstools.dll', False);
    { Register the COM add-in. Must run AFTER the copy above. }
    Exec(ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'),
         '/codebase "' + sApp + '\SW2GZ.dll"', '', SW_HIDE, ewWaitUntilTerminated, iResult);
  end;
end;
