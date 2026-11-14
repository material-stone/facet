# 수동 날씨 지역 자료

설정의 한국 지역 목록은 [GeoNames 한국 자료](https://download.geonames.org/export/dump/KR.zip)의 ADM1/ADM2 대표 좌표를 사용한다. 2026-10-24 내려받은 자료에서 17개 시·도와 228개 시·군·구를 추출해 245개 항목을 만들었다. 세종의 중복 ADM2는 제외했다. 날씨 위치는 선택 지역의 대표 좌표이며 동·도로명·개별 주소를 뜻하지 않는다.

원자료: [GeoNames](https://www.geonames.org/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). 배포 JSON에 출처·라이선스·조회일을 포함한다. 영문 행정구역 코드에 현재 한국어 시·도 명칭을 적용하고, 한국어 대체 이름 중 행정 접미사 시/군/구에 맞는 이름을 선택했다. 없다면 한국어 이름에 행정 접미사를 붙였다. 좌표는 소수 넷째 자리로 반올림했다.

Desktop/DashboardWeather/WeatherRegions만 JSON을 resource로 읽고 항목 수·ID 중복·명칭·한국 좌표 범위를 검증한다. 런타임에 GeoNames로 요청하거나 현재 위치를 전송하지 않는다. Windows 자동 위치의 표시 이름은 별도 행정구역 경계로 판별하며 이 대표 좌표 목록에서 가장 가까운 도시를 골라 붙이지 않는다.

## 자동 위치의 행정구역 이름

`WeatherAdministrativeRegions.json`은 [geoBoundaries KOR ADM1](https://www.geoboundaries.org/api/current/gbOpen/KOR/ADM1/)의 [원본 GeoJSON](https://github.com/wmgeolab/geoBoundaries/raw/9469f09/releaseData/gbOpen/KOR/ADM1/geoBoundaries-KOR-ADM1.geojson)에서 17개 시·도 Polygon/MultiPolygon을 추출했다. 자료가 표현하는 경계 연도는 2021년이며 원자료는 Natural Earth public domain이다. geoBoundaries 데이터베이스는 CC BY 4.0으로 출처를 표시한다. 경계의 위·경도 순서를 그대로 유지하고 소수 여섯째 자리까지 저장했으며, ISO 코드의 명칭을 현재 한국어 시·도 이름으로 정규화했다.

WeatherAdministrativeRegions가 동일 모듈의 이 JSON을 로드해 17개 지역·명칭·좌표 범위·닫힌 링을 검증한다. 바깥 경계 포함, 내부 구멍 제외, 다중 폴리곤을 처리한다. 한국 경계 밖이나 해상 등 포함 지역이 없으면 다른 도시를 추측하지 않는다. Windows 위치의 오차와 경계 자료의 해상도·표현 연도가 실제 행정 경계와의 차이에 영향을 줄 수 있다. 표시 이름 판별은 메모리 안에서만 수행하며 현재 좌표는 외부 역지오코딩 서비스에 보내거나 저장하지 않는다.

출처: [geoBoundaries](https://www.geoboundaries.org/), [Natural Earth 이용 조건](https://www.naturalearthdata.com/about/terms-of-use/), [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). 설정의 자료 출처 안내에도 geoBoundaries를 표시한다.
