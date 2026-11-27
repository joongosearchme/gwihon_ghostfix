# gwihon_ghostfix

귀혼 `game.exe`의 LAA·호환성·자동 유지 관리 설정과 튕김 진단을 하는 Windows 도구 **GhostFix**의 소스.

## 출처

원본 소스가 없어 배포된 `GhostFix.exe`(1.0.0.0)를 ILSpy로 디컴파일해 복원했다.
다시 빌드한 exe를 같은 방법으로 풀어 원본과 비교해 동작상 차이가 없음을 확인했다.
클래스·메서드·필드 이름은 원본 그대로이고, 지역 변수 이름(`val`, `flag`, `obj` 등)과 주석은 디컴파일 과정에서 사라졌다.

## 빌드

.NET SDK(6 이상)가 있으면 Windows·리눅스 어디서나 빌드된다. 결과물은 .NET Framework 4.8용이다.

```
dotnet build -c Release
```

결과: `bin/Release/net48/GhostFix.exe`. Visual Studio 2022에서 `GhostFix.csproj`를 열어도 된다.

## 구성

- `src/Program.cs` — 시작점(관리자 권한 재실행, `--tray` 트레이 모드)
- `src/MainForm.cs` — 화면 전체
- `src/Compat.cs`, `src/Pe.cs` — 호환성 설정, LAA 패치
- `src/Maint.cs` — Windows 자동 유지 관리 설정
- `src/Crashes.cs`, `src/Wer.cs` — 튕김 기록·Windows 오류 보고 분석, 원인 모듈(DirectX·VC++·오버레이 프로그램) 안내
- `src/Watch.cs`, `src/SessionLog.cs` — 시작 프로그램 등록·트레이 감시, 게임 실행 기록(`session.log`)
- `src/Nearby.cs` — 튕긴 시각 앞뒤의 Windows 이벤트 로그
- `src/TcpInfo.cs` — 게임 접속(TCP) 상태
- `src/PowerInfo.cs` — 절전 설정, 충돌 가능성 있는 프로그램(VPN·백신·Killer 네트워크 등)
