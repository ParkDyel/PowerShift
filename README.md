# PowerShift

Windows 전원 관리 구성표를 트레이에서 전환하는 작은 유틸리티입니다.
자동차의 ECO / NORMAL / SPORT 주행 모드처럼, PC의 절전 / 균형 조정 / 고성능을 바꿉니다.

문서: [설계](docs/DESIGN.md) · [변경 이력](CHANGELOG.md) · [배포 절차](docs/RELEASING.md) · [보안 검토](docs/SECURITY_REVIEW.md)

- 트레이에서 **기본값 변경**: 직접 고른 구성표를 기본값으로 저장
- **프로그램별 설정**: 지정한 프로세스가 실행 중일 때 원하는 구성표 적용
- 프로그램이 종료되면 기본값으로 복귀
- 여러 규칙이 동시에 맞으면 목록 위쪽 규칙을 우선 적용
- 자동 전환 일시 중지, 로그인 시 자동 실행, 중복 실행 방지
- 적용 규칙과 이전/변경 모드 로그, 일수로 설정하는 자동 삭제 (기본 3일, 0은 삭제 안 함)
- 외부 패키지, 웹뷰, 네트워크 통신, 관리자 권한 불필요
- 약 46KB의 C# / WinForms 실행 파일 (Windows 기본 .NET Framework 사용)

## 실행

Windows 10/11, Windows PowerShell 5.1, .NET Framework 4.x를 사용합니다.

### 설치 프로그램 (권장)

[최신 릴리스](https://github.com/ParkDyel/PowerShift/releases/latest)에서 **PowerShift-Setup-0.4.0.exe**를 내려받아 실행하세요.
설치 마법사에서 폴더와 로그인 시 자동 실행 여부를 선택하면 됩니다. 별도의 PowerShell 명령을 입력할 필요가 없습니다.

- 현재 사용자 계정에 설치하며 관리자 권한을 요구하지 않습니다.
- 기본 설치 위치: `%LOCALAPPDATA%\Programs\PowerShift`
- 시작 메뉴와 Windows 앱 검색에 **PowerShift** 등록
- Windows **설정 → 앱 → 설치된 앱**에서 제거 가능
- 업데이트 시 실행 중인 앱을 정상 종료하고 교체
- 업데이트와 제거 시 기존 설정과 로그 유지

설치 파일은 코드 서명되지 않았습니다. Windows의 SmartScreen 또는 조직/PC의 앱 제어 정책에 따라 실행이 제한될 수 있으며, 설치 프로그램이 이 정책을 변경하지는 않습니다.

### ZIP / 소스에서 실행

저장소를 내려받은 폴더에서 아래 명령으로 바로가기를 만들고 자동 실행을 등록합니다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Setup.ps1 -EnableStartup
```

소스에서 설치하면 EXE를 먼저 빌드합니다. 생성된 **PowerShift.lnk**를 실행합니다. 아이콘이 안 보이면 작업 표시줄의 숨겨진 아이콘 메뉴(`^`)를 여세요.
설치 스크립트는 현재 사용자의 시작 메뉴에도 바로가기를 등록합니다. Windows에서 **PowerShift**를 검색해 실행할 수 있습니다. 새 항목이 표시되기까지 잠시 걸릴 수 있습니다.
아이콘을 클릭하면 모드와 설정 메뉴가 열립니다. 이미 실행 중일 때 바로가기를 다시 열면 설정 화면이 표시됩니다.
트레이 아이콘 색상은 **절전: 노랑 / 균형 조정: 초록 / 고성능: 파랑**입니다. 사용자 구성표는 초록으로 표시합니다.

PowerShell 실행 정책은 해당 프로세스에만 지정하며 사용자/시스템 기본 정책을 바꾸지 않습니다.
인터넷에서 받은 파일이 `RemoteSigned` 정책으로 차단된다면 소스를 검토한 뒤 Windows의 파일 속성에서 내려받은 파일의 차단 상태를 확인하세요.
조직에서 관리하는 실행 제한 정책은 그대로 적용됩니다.

## 기본값과 프로그램별 설정

1. **설정 → 기본 설정**에서 평소 사용할 전원 구성표를 고릅니다.
2. **프로그램별 설정 → 추가**에서 실행 파일을 고르거나 **실행 중에서 선택**을 누릅니다. 실행 중인 프로그램 표의 마지막 열에 전체 경로가 표시됩니다.
3. 실행 중 적용할 전원 구성표를 선택하고 저장합니다.

예: 기본값 **절전**, `game.exe` 실행 중에는 **고성능**, 종료 후에는 **절전**.

규칙 표의 마지막 **전체 경로** 열에서 대상 파일을 확인할 수 있습니다. 파일을 찾아보거나 실행 중인 프로그램을 선택하면 전체 경로를 함께 저장합니다.
경로를 지정한 규칙은 해당 EXE에만 적용합니다. 같은 파일 이름이라도 경로가 다르면 각각 등록할 수 있습니다.
경로를 비워 두면 기존처럼 실행 파일 **이름**으로 일치시킵니다. 기존 규칙은 이 방식으로 유지됩니다.
프로그램 이름과 경로는 대소문자를 구분하지 않습니다. 여러 EXE를 사용하는 앱은 필요한 EXE를 각각 추가하세요.
조회 권한이 없는 프로세스는 실행 중인 프로그램 표에 경로 조회 불가로 표시됩니다. 경로를 읽을 수 없으면 경로 지정 규칙을 적용하지 않습니다.
백그라운드 실행도 포함합니다.
창이 활성화되어 있는지는 판단하지 않습니다. 프로세스가 실제로 종료되어야 복귀합니다.
자동 전환은 2초 간격으로 확인합니다. 활성 규칙이 없으면 기본값을 유지합니다.
규칙 적용 중 트레이에서 기본값을 바꾸면, 규칙 종료 후 새 기본값으로 돌아갑니다.
자동 전환을 끄거나 앱을 정상 종료하면 기본값으로 복원합니다. 강제 종료/프로세스 충돌 시에는 복원을 보장하지 않습니다.

Windows에 등록된 구성표를 사용하며 사용자 구성표도 선택할 수 있습니다. 구성표를 새로 만들거나 세부 전원 값을 수정하지 않습니다.
Windows 설정의 별도 **전원 모드**와 제어판의 **전원 관리 구성표**는 서로 다른 기능입니다. PowerShift는 구성표를 전환합니다.
삭제된 구성표를 가리키는 규칙은 건너뜁니다. 기본 구성표가 삭제되면 현재 구성표를 유지하므로 설정에서 새 기본값을 선택하세요.

## 로그

**설정 → 로그**에서 언제 어떤 규칙으로 전환됐는지 확인할 수 있습니다.
시간, 이전 모드, 변경 모드, 적용 규칙의 순서와 프로세스 이름, 전환 이유, EXE 전체 경로를 기록합니다.
행을 선택하면 이전 규칙과 적용된 규칙까지 아래 상세 영역에 표시됩니다.
프로그램 종료로 기본값에 복귀하는 경우와 자동 전환 해제/기본값 변경으로 전환된 경우도 구분합니다.
적용 규칙만 바뀌고 모드는 같으면 ‘전원 모드 유지’로 기록하며, 상태가 그대로인 반복 확인은 기록하지 않습니다.

**로그 자동 삭제**에 일수를 입력하고 저장하세요. 기본값은 **3일**이며 **0이면 자동 삭제하지 않습니다**.
설정 저장 시, 앱 시작 시, 실행 중 매시간 보관 기간이 지난 로그 항목을 정리합니다.
기간은 기록 시점부터 경과한 일수(24시간) 기준입니다. UI는 최신 500개를 표시하며, 보관 중인 나머지 기록도 로컬 파일에 남습니다.
로그 저장에 실패해도 전원 전환은 계속 동작하며 로그 탭에 오류가 표시됩니다.

## 저장 위치와 제거

- 설정: `%APPDATA%\PowerShift\settings.json` (기본값, 규칙, 우선순위)
- 로그: `%APPDATA%\PowerShift\logs\PowerShift-yyyy-MM-dd.jsonl` (로컬 저장, 화면은 현지 시간으로 표시)
- 자동 실행: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\PowerShift`

설정 파일은 Git에 포함되지 않습니다. 설치 폴더를 이동했다면 이동한 위치에서 `Setup.ps1 -EnableStartup`을 다시 실행하세요.
설치 프로그램으로 설치했다면 Windows **설정 → 앱 → 설치된 앱 → PowerShift → 제거**를 사용하세요. 시작 메뉴와 해당 설치의 자동 실행 항목도 정리합니다. 설정과 로그는 남겨 둡니다.
ZIP 방식은 설정에서 자동 실행을 해제하고 정상 종료한 뒤 앱 폴더와 `%APPDATA%\Microsoft\Windows\Start Menu\Programs\PowerShift.lnk` 바로가기를 삭제합니다. 필요하면 설정 폴더도 삭제할 수 있습니다.

## 개발 및 검증

```powershell
# EXE 빌드 (추가 SDK나 NuGet 패키지 불필요)
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Build.ps1

# 설치 EXE 빌드: 개발 PC에 Inno Setup 7.1.1 필요
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Build-Installer.ps1 -IsccPath "C:\Program Files\Inno Setup 7\ISCC.exe"

# 규칙 우선순위, 복귀, 저장/유효성 검사
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Test.ps1

# 실제 전원 구성표 전환 및 로컬 테스트 프로세스 시작/종료 검증
# 끝나면 원래 활성 구성표를 복원합니다.
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\Test.ps1 -Integration

# 실제 트레이 EXE의 감지 주기, 여러 인스턴스, 복귀 및 정상 종료 검증
# 앱을 종료한 상태에서 실행하며 기존 설정과 활성 구성표를 복원합니다.
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\tests\SmokeTest.ps1

# 설치·실행 중 업그레이드·제거·설정/로그 보존 검증
# 개발/검증용 새 폴더를 지정하세요. 기존 설치가 있는 계정에서는 실행을 거부합니다.
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\tests\InstallerTest.ps1 -InstallerPath .\dist\PowerShift-Setup-0.4.0.exe -InstallDirectory "$env:TEMP\PowerShift-Installer-Test"
```

`bin\PowerShift.exe`가 기본 실행 방식입니다. PowerShell 호스트가 상주하지 않습니다.
서명되지 않은 EXE의 실행이 차단되는 환경에서는 `Setup.ps1 -ScriptHost`로 같은 C# 소스를 Windows PowerShell에서 실행하는 바로가기를 만들 수 있습니다.
PowerShell 방식은 실행 시 소스를 컴파일하며 EXE 방식보다 호스트 메모리를 더 사용합니다. 조직의 애플리케이션 제어 정책은 우선 적용됩니다.

개발 중 실행한 앱은 `bin\PowerShift.exe --exit`로 정상 종료할 수 있습니다.
GitHub Actions에서도 Windows 빌드, 규칙/로그 검사, 설치·업데이트·제거를 검증합니다.

Windows 공식 API: [PowerEnumerate](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powerenumerate),
[PowerSetActiveScheme](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersetactivescheme).
