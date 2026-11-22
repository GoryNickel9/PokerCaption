[Setup]
AppId={{B8C5D832-C26D-4646-9FA1-37D55B7CE221}
AppName=PokerCaption
AppVersion={#AppVersion}
AppPublisher=PokerCaption
AppPublisherURL=https://github.com/GoryNickel9/PokerCaption
DefaultDirName={localappdata}\Programs\PokerCaption
DefaultGroupName=PokerCaption
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x86compatible
MinVersion=10.0
SetupIconFile="{#ProgramIcon}"
UninstallDisplayIcon={app}\PokerCaption.exe
Compression=lzma2
SolidCompression=yes
RestartApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\PokerCaption"; Filename: "{app}\PokerCaption.exe"; WorkingDir: "{app}"

[Run]
Filename: "{app}\PokerCaption.exe"; Description: "{cm:LaunchProgram,PokerCaption}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked
