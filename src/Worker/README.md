# Worker - Remote Programmer 개발 컴퓨터

## 개요

Worker는 Remote Programmer 시스템의 개발 컴퓨터 컴포넌트입니다. 중계 서버로부터 작업을 받아 Claude Code를 활용하여 자동으로 개발 작업을 수행합니다.

## 주요 기능

1. **WebSocket 통신**: 중계 서버와 WebSocket으로 연결하여 작업 요청 수신
2. **프로젝트 관리**: 여러 Git 프로젝트 관리 및 상태 추적
3. **작업 관리**: 작업 상태 저장 (JobStatus) 및 이력 관리 (JobHistory)
4. **Claude Code 실행**: Bash 스크립트로 Claude Code를 실행하여 자동 개발
5. **로컬 웹 인터페이스**: 테스트 및 모니터링을 위한 웹 UI

## 프로젝트 구조

```
Worker/
├── Models/              # 데이터 모델
│   ├── Project.cs      # 프로젝트 정보
│   ├── Job.cs          # 작업 정보
│   └── WorkerMessage.cs # WebSocket 메시지
├── Services/            # 핵심 서비스
│   ├── ProjectManager.cs       # 프로젝트 관리
│   ├── JobManager.cs           # 작업 상태 관리
│   ├── ClaudeCodeExecutor.cs   # Claude Code 실행
│   └── WebSocketService.cs     # 중계 서버 통신
├── Controllers/         # API 컨트롤러
│   ├── JobsController.cs
│   └── ProjectsController.cs
└── Pages/              # Razor Pages (웹 UI)
    └── Index.cshtml
```

## 설정

### appsettings.json

```json
{
  "WorkspacePath": "/workspace",
  "RelayServerUrl": "ws://localhost:5000/worker"
}
```

- `WorkspacePath`: 프로젝트들이 있는 워크스페이스 경로
- `RelayServerUrl`: 중계 서버 WebSocket URL (비어있으면 WebSocket 서비스 비활성화)

### 워크스페이스 구조

```
/workspace/
├── JobStatus/          # 현재 실행 중인 작업 정보
├── JobHistory/         # 과거 실행한 작업 이력
├── RemoteProgrammer/   # 이 프로젝트
├── Project1/           # 관리할 프로젝트 1
└── Project2/           # 관리할 프로젝트 2
```

## 실행 방법

### 1. 개발 환경에서 실행

```bash
cd src/Worker/Worker
dotnet run
```

웹 브라우저에서 `http://localhost:5000` 접속

### 2. 프로덕션 빌드

```bash
cd src/Worker/Worker
dotnet publish -c Release -o ./publish
cd publish
./Worker
```

## API 엔드포인트

### Projects API

- `GET /api/projects` - 모든 프로젝트 목록
- `GET /api/projects/{name}` - 특정 프로젝트 정보

### Jobs API

- `GET /api/jobs` - 진행 중인 작업 목록
- `GET /api/jobs/{id}` - 특정 작업 정보
- `GET /api/jobs/history?limit=50` - 작업 이력
- `POST /api/jobs` - 새 작업 생성

#### 작업 생성 예시

```bash
curl -X POST http://localhost:5000/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "projectName": "Project1",
    "description": "Add a new feature to handle user authentication"
  }'
```

## WebSocket 메시지 프로토콜

### Worker → Server

#### WorkerStatus (연결 시 및 상태 변경 시)
```json
{
  "type": "WorkerStatus",
  "data": {
    "projects": ["Project1", "Project2"],
    "runningJobs": [
      {
        "jobId": "abc-123",
        "projectName": "Project1",
        "status": "Running"
      }
    ]
  }
}
```

#### JobResponse (작업 완료 시)
```json
{
  "type": "JobResponse",
  "data": {
    "jobId": "abc-123",
    "status": "Completed",
    "result": "작업 완료 결과...",
    "errorMessage": null
  }
}
```

#### UserInputRequest (사용자 입력 필요 시)
```json
{
  "type": "UserInputRequest",
  "data": {
    "jobId": "abc-123",
    "question": "어떤 인증 방식을 사용하시겠습니까?"
  }
}
```

### Server → Worker

#### JobRequest (작업 요청)
```json
{
  "type": "JobRequest",
  "data": {
    "jobId": "abc-123",
    "projectName": "Project1",
    "description": "Add user authentication feature"
  }
}
```

#### UserInputResponse (사용자 입력 응답)
```json
{
  "type": "UserInputResponse",
  "data": {
    "jobId": "abc-123",
    "answer": "JWT 방식을 사용합니다"
  }
}
```

#### StatusRequest (상태 요청)
```json
{
  "type": "StatusRequest",
  "data": null
}
```

## Claude Code 실행 방식

작업이 요청되면 다음과 같은 프로세스로 실행됩니다:

1. 작업 정보를 기반으로 Bash 스크립트 생성
2. 스크립트에서 프로젝트 경로로 이동
3. `echo "작업설명" | claude --no-confirm` 명령으로 Claude Code 실행
4. 실행 결과를 실시간으로 로그에 기록
5. 완료 후 결과를 중계 서버로 전송

### 생성되는 스크립트 예시

```bash
#!/bin/bash

# Job: abc-123
# Project: Project1
# Description: Add user authentication feature

cd "/workspace/Project1"

# Claude Code 실행
echo "Add user authentication feature" | claude --no-confirm

exit $?
```

## 로컬 웹 인터페이스

웹 UI를 통해 다음 작업을 수행할 수 있습니다:

1. **프로젝트 목록 확인**: 현재 관리 중인 모든 프로젝트 보기
2. **진행 중인 작업 모니터링**: 실시간으로 작업 상태 확인
3. **새 작업 생성**: 웹에서 직접 작업 요청
4. **작업 이력 조회**: 과거 실행한 작업들의 결과 확인

페이지는 5초마다 자동으로 새로고침되어 최신 상태를 표시합니다.

## 주의사항

1. **Claude CLI 필요**: Claude Code CLI가 설치되어 있어야 합니다
2. **Git 저장소**: 관리할 프로젝트는 모두 Git 저장소여야 합니다
3. **경로 권한**: WorkspacePath에 읽기/쓰기 권한이 있어야 합니다
4. **자동 실행**: `--no-confirm` 옵션으로 Claude가 자동으로 실행됩니다

## 개발 시 참고사항

- ASP.NET Core Web 프로젝트로 만들어졌지만 Console Application처럼 동작
- WebSocket 서비스는 `RelayServerUrl`이 설정되어 있을 때만 활성화
- 로컬 테스트는 웹 인터페이스를 통해 가능
- 모든 작업은 비동기로 실행되며, 여러 작업을 동시에 처리 가능
