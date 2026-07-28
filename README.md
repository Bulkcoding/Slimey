# ThrowMe

윈도우 바탕화면 위에 떠다니는 반투명 슬라임/공 데스크톱 토이. (C# / WPF / .NET 8, 게임 엔진 없이 순수 구현)

- 드래그·던지기, 관성/마찰, 모니터 모서리 반사(멀티 모니터·DPI 대응)
- 스킨: 젤리 · 당구공 · 몬스터볼/하이퍼볼/마스터볼
- 당구 큐대 모드 + 스핀(끌어치기/밀어치기/사이드)
- 젤리 타격 문구(메이플식 "Hit!") · 전역 잡기 단축키
- GitHub 릴리스 기반 자동 업데이트

## 빌드

```
dotnet build src/ThrowMe/ThrowMe.csproj -c Release
```

## 배포용 단일 exe

```
dotnet publish src/ThrowMe/ThrowMe.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

`bin/Release/net8.0-windows/win-x64/publish/ThrowMe.exe` 한 파일만 전달하면 됩니다(.NET 설치 불필요).

## 자동 업데이트

- 저장소는 비공개(Private)이므로, 클라이언트가 릴리스를 조회하려면
  **ThrowMe 저장소 Contents 읽기 전용**으로 제한된 fine-grained PAT 가 필요합니다.
- 토큰은 `Services/UpdateConfig.cs` 의 `EmbeddedToken` 에 넣거나,
  로컬 `%LOCALAPPDATA%\ThrowMe\update_token.txt` 에 저장합니다(후자가 우선).
- 릴리스 태그는 `v1.2.3` 형식, exe 자산을 첨부하면 됩니다.
