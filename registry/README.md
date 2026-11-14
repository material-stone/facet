# Facet 패키지 레지스트리

Facet 앱의 **설정 → 플러그인·스킨 목록**이 읽는 공개 목록입니다. 별도 저장소 없이 이 저장소의 `registry/` 폴더가 레지스트리입니다. 결정 배경은 [ADR 0088](../docs/decisions/0088-packages-and-registry.md)과 [ADR 0097](../docs/decisions/0097-plugin-uninstall-and-in-repo-registry.md)에 있습니다.

- `index.json`: 앱이 `main` 브랜치에서 읽는 목록입니다.
- 패키지 파일: 이 저장소 Releases의 시험판(prerelease) 첨부 파일입니다. 앱은 이 저장소의 릴리스 URL만 받고, SHA-256·종류·ID·버전이 목록과 모두 같을 때만 설치합니다.
- `samples/`: 스킨 예제입니다.

## 흐름

GitHub 로그인 사용자가 [제출 양식](https://github.com/material-stone/facet/issues/new?template=submit.yml)에 zip을 첨부하면 `.github/workflows/package-submission.yml`이 `tools/submission.py`로 검증합니다. 스킨은 바로 `index.json`에 올라가고, 플러그인은 pull request로 대기하며 병합이 승인입니다. 같은 ID는 처음 올린 작성자만 갱신할 수 있습니다.

## 저장소 설정

1. `submission` 라벨을 만듭니다.
2. Settings → Actions → General에서 **Read and write permissions**와 **Allow GitHub Actions to create and approve pull requests**를 켭니다.
3. `main` 보호 규칙에서 리뷰를 요구하면 봇의 스킨 직접 푸시가 막히므로, 봇을 예외로 두거나 일반 푸시 제한만 설정합니다.

검사: `python registry/tools/test_submission.py`

## 업로드 양식

- [스킨 업로드 양식](docs/skin.md)
- [플러그인 업로드 양식](docs/plugin.md)