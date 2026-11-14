# Facet 앱 아이콘

짙은 녹색의 둥근 타일 위에 옆에서 본 보석을 배치한다. 여섯 개의 깎인 면은 이름 Facet을 표현한다. 면은 밝은 민트부터 공통 Mint `#82CFAD`와 그보다 짙은 녹색까지 쓰며 작은 크기에서는 글자나 세부 장식을 추가하지 않는다. 타일 밖은 투명하다.

원본은 `impls/Facet/Design/AppIcon/Facet.svg`다. 다음 명령은 Windows의 기존 WPF 벡터 렌더러로 미리보기 PNG와 ICO를 생성하며 외부 패키지나 네트워크를 사용하지 않는다.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -STA -File scripts/generate-app-icon.ps1
```

`Facet.ico`에는 16·20·24·32·40·48·64·96·128·256px의 32bit alpha 프레임이 있다. `Facet.png`는 512px 미리보기다. 디자인 변경 때만 재생성하며 일반 빌드는 저장된 ICO를 사용한다.

Desktop·고정 Launcher·Cli의 ApplicationIcon은 같은 ICO를 사용한다. `Desktop/Design/AppIcon.cs`는 WPF resource의 ICO를 검증·로드해 오버레이·알림 창과 트레이에 제공한다. 창 이미지는 메모리에 로드해 동결하고 트레이는 시스템 작은 아이콘 크기를 선택한다. Controller는 트레이와 함께 소유한 native Icon을 종료 시 해제한다. 작업 표시줄은 새 Facet 실행기의 `launcher,0`을 사용하며 기존 AppUserModelID와 저장 경로는 유지한다.
