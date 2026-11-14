# 그림자 배경의 사전 렌더링과 재사용

2026-10-24: 빈 그림자 배경에 적용하던 DropShadowEffect만 미리 렌더링하여 재사용한다. 글자·입력란·달력·날짜 패널의 전경은 WPF 벡터/텍스트 렌더 경로에 유지하고 부모 BitmapCache를 사용하지 않는다.

Desktop/CachedShadow는 Border의 크기·배경·모서리·기존 DropShadowEffect 값과 현재 DPI로 빈 Border만 별도 RenderTargetBitmap에 그린다. 그림자 바깥 여유를 포함한 작은 frozen Pbgra32 atlas의 모서리·가장자리 조각을 같은 로컬 좌표에 그리며 레이아웃과 hit test는 바꾸지 않는다. 기존 검정/0.42/blur24/depth6/direction270 설정을 유지한다. 인스턴스별 현재 atlas 하나와 최대9개의 cropped view만 보유하고 크기·brush/색·모서리·그림자 값·DPI가 바뀌면 다시 만든다. 단색·무테 배경은 모서리와 blur 영향을 받지 않는 직선 부분만 늘리는 nine-slice를 사용한다. 원래 우측/하단의 소수 pixel 위상을 유지하며 불투명 중심은 같은 단색으로 직접 그린다. gradient/테두리는 전체 bitmap 경로를 사용한다. 드래그의 위치·배율 변경과 날짜·노트 내용 변경은 bitmap 재생성을 유발하지 않는다.

적용 위치는 상단 탭·달력·날짜 패널·빈 캐러셀 둘의 비대화형 배경 형제다. UI의 blur 캡처 worker·저장·Core·QuickLaunch는 변경하지 않는다. live effect 비교 경로는 내부 QA용으로만 제공하여 같은 요소에서 원래 모양과 성능을 A/B 검증한다.

검증은 캐시/원래 경로의 그림자 경계·softness·offset 이미지 비교, 전경 글자 영역 pixel 동일성, 크기별 모든 캐러셀 상태·DPI별 bitmap·입력 비간섭, 재사용과 속성 변경 시 재생성, cold bake 및 steady 렌더 비용 측정으로 수행한다. 결과는 software 렌더 시간과 실제 GPU FPS를 구분하여 기록한다.
