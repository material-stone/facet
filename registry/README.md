# Facet 레지스트리

Facet 앱이 **스킨 받기**와 **플러그인 받기**에서 보여 주는 공개 목록입니다.

- 👉 **[스킨 목록](skins/README.md)**
- 👉 **[플러그인 목록](plugins/README.md)**

## 구성

| 경로 | 내용 |
| --- | --- |
| `index.json` | 앱이 `main` 브랜치에서 읽는 목록. 종류(`skin`/`plugin`), ID, 버전, 파일 URL, SHA-256이 들어 있습니다. |
| `skins/<id>/` | 스킨 원본. 패키지는 이 폴더를 zip으로 묶은 것입니다. |
| `skins/README.md`, `plugins/README.md` | 사용자용 목록 |
| `skins/UPLOAD.md`, `plugins/UPLOAD.md` | 만들고 올리는 방법 |
| `tools/` | 제출 검증 스크립트 |

패키지 파일은 이 저장소 Releases의 시험판(prerelease) 첨부 파일입니다(태그 `skin-<id>-<버전>`, `plugin-<id>-<버전>`). 앱은 이 저장소의 릴리스 URL만 받고, SHA-256·종류·ID·버전이 목록과 모두 같을 때만 설치합니다. 결정 배경은 [ADR 0088](../docs/decisions/0088-packages-and-registry.md), [0097](../docs/decisions/0097-plugin-uninstall-and-in-repo-registry.md), [0100](../docs/decisions/0100-light-dark-and-skin-packages.md)에 있습니다.

## 제출 흐름

GitHub 로그인 사용자가 [제출 양식](https://github.com/material-stone/facet/issues/new?template=submit.yml)에 zip을 첨부하면 `.github/workflows/package-submission.yml`이 `tools/submission.py`로 검증합니다. 색과 이미지만 쓰는 스킨은 바로 공개되고, 플러그인과 3D 월드 페이지가 있는 스킨은 pull request로 대기하며 병합이 승인입니다. 같은 ID는 처음 올린 사람만 갱신할 수 있습니다.

### 저장소 설정 (관리자)

1. `submission` 라벨을 만듭니다.
2. 조직 설정(Organization → Settings → Actions → General)에서 **Allow GitHub Actions to create and approve pull requests**를 켭니다. 조직에서 꺼 두면 저장소 설정에서는 회색으로 바꿀 수 없습니다.
3. `main` 보호 규칙에서 리뷰를 요구하면 봇의 스킨 직접 푸시가 막히므로, 봇을 예외로 두거나 일반 푸시 제한만 설정합니다.

검사: `python registry/tools/test_submission.py`