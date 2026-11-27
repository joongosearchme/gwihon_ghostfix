# GhostFix

온라인 게임 **귀혼**(`game.exe`)의 튕김을 줄이는 Windows 도구.
게임 파일과 Windows 설정을 몇 가지 바꾸고, 튕겼을 때 원인을 찾을 수 있게 기록을 모아 보여 준다.

## 기능

- **LAA (4GB 메모리)**: 32비트 `game.exe`가 2GB 대신 4GB 메모리를 쓰게 한다. 원본은 `game.exe.laa-backup`으로 백업한다.
- **호환성 설정**: 높은 DPI 재정의, 전체 화면 최적화 끄기, 관리자 권한 실행. 선택하면 DEP 예외도 넣을 수 있다.
- **자동 유지 관리 시간 옮기기**: Windows 자동 유지 관리를 새벽 3시에서 낮 14시로 옮기고 절전 해제를 끈다.
- **게임 중 절전 방지와 기록 (선택)**: 로그인할 때 트레이로 자동 시작해서 귀혼이 떠 있는 동안 PC 절전과 자동 유지 관리를 막고, 메모리와 서버 연결 상태를 기록한다. 게임을 끄면 원래대로 되돌린다.
- **튕김 기록**: Windows 이벤트 로그와 오류 보고에서 귀혼 튕김을 찾아, 원인으로 보이는 모듈(그래픽 드라이버, VC++ 런타임, 화면 오버레이 프로그램 등)과 대처법을 보여 준다.
- 적용한 설정은 "원래대로" 버튼으로 모두 되돌릴 수 있다.

## 사용법

1. [Releases](../../releases)에서 `GhostFix.exe`를 받는다.
2. 다운로드 폴더가 아닌 고정 폴더(예: `C:\GhostFix`)에 옮긴 뒤 실행한다. 관리자 권한이 필요하다.
3. `game.exe` 경로를 확인하고, 원하는 항목을 체크한 뒤 "적용"을 누른다.

필요 환경: Windows 10/11, .NET Framework 4.8 (Windows 10 1903 이후에는 기본으로 들어 있다).

## 빌드

GitHub에 푸시하면 자동으로 빌드된다(`.github/workflows/build.yml`).

- **테스트용 exe**: 저장소의 Actions 탭 → 해당 실행 → 아래 Artifacts의 `GhostFix`를 내려받는다(zip 안에 exe).
- **배포**: `v1.1.1` 같은 태그를 푸시하면 그 번호가 exe 버전으로 들어가고 Releases에 `GhostFix.exe`가 올라간다.
- 버전·제품 정보는 `GhostFix.csproj`에 있다.

직접 빌드하려면 .NET SDK(6 이상)를 깔고 `dotnet build -c Release` (결과: `bin/Release/net48/GhostFix.exe`).

## 구성

- `src/Program.cs` — 시작점. 프로세스를 하나만 띄운다(이미 떠 있으면 그 창을 띄움), 관리자 권한 재실행, `--tray`는 창 없이 감시로 시작
- `src/MainForm.cs` — 화면 전체
- `src/Compat.cs`, `src/Pe.cs` — 호환성 설정, LAA 패치
- `src/Maint.cs` — Windows 자동 유지 관리 설정
- `src/Crashes.cs`, `src/Wer.cs` — 튕김 기록·Windows 오류 보고 분석, 원인 모듈(DirectX·VC++·오버레이 프로그램) 안내
- `src/Watch.cs`, `src/SessionLog.cs` — 로그인 자동 시작(작업 스케줄러 `GhostFix`, 가장 높은 권한) 등록, 게임 실행 기록(`session.log`)
- `src/Nearby.cs` — 튕긴 시각 앞뒤의 Windows 이벤트 로그
- `src/TcpInfo.cs` — 게임 접속(TCP) 상태
- `src/PowerInfo.cs` — 절전 설정, 충돌 가능성 있는 프로그램(VPN·백신·Killer 네트워크 등)

## 소스 이력

1.0.0까지는 소스를 따로 보관하지 못해, 작성자가 배포했던 `GhostFix.exe`(1.0.0.0)를 ILSpy로 디컴파일해 이 저장소의 소스로 복원했다.
그래서 지역 변수 이름(`val`, `flag` 등)과 주석이 없는 코드가 남아 있다. 1.1.0부터는 이 저장소에서 개발한다.

## 코드 서명 정책

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

- 배포용 exe는 이 저장소의 GitHub Actions에서 소스로부터 빌드한 것만 서명한다.
- Committers and reviewers: [joongosearchme](https://github.com/joongosearchme)
- Approvers: [joongosearchme](https://github.com/joongosearchme)

## 개인정보

GhostFix는 사용자가 직접 요청하지 않는 한 어떤 정보도 다른 네트워크 시스템으로 보내지 않는다.
기록(`session.log` 등)은 이 PC의 `%LOCALAPPDATA%\GhostFix`에만 저장된다. 네트워크를 쓰는 곳은 사용자가 "오픈채팅 열기" 버튼을 눌렀을 때 브라우저로 카카오 오픈채팅 주소를 여는 것뿐이다.

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

## 라이선스

[MIT](LICENSE)
