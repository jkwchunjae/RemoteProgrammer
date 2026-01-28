// 작업 상세 정보 관리 JavaScript

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function getStatusColor(status) {
    switch (status) {
        case 'Completed': return 'success';
        case 'Failed': return 'danger';
        case 'Running': return 'primary';
        case 'Pending': return 'secondary';
        case 'WaitingForUserInput': return 'warning';
        default: return 'secondary';
    }
}

async function showJobDetail(jobId, isHistory = false) {
    const content = document.getElementById('job-detail-content');

    content.innerHTML = '<div class="job-detail-placeholder"><p class="text-muted">로딩 중...</p></div>';

    try {
        const endpoint = isHistory ? `/api/jobs/history?limit=100` : `/api/jobs/${jobId}`;
        const response = await fetch(endpoint);

        let job;
        if (isHistory) {
            const jobs = await response.json();
            job = jobs.find(j => j.id === jobId);
        } else {
            job = await response.json();
        }

        if (!job) {
            content.innerHTML = '<div class="job-detail-placeholder"><p class="text-danger">작업을 찾을 수 없습니다.</p></div>';
            return;
        }

        content.innerHTML = `
            <div class="detail-section">
                <h4>기본 정보</h4>
                <div class="detail-content">
작업 ID: ${escapeHtml(job.id)}
프로젝트: ${escapeHtml(job.projectName)}
브랜치 (큰작업): ${job.bigTaskName ? escapeHtml(job.bigTaskName) : 'N/A'}
프로젝트 경로: ${escapeHtml(job.projectPath)}
${job.worktreePath ? `작업 경로 (worktree): ${escapeHtml(job.worktreePath)}` : ''}
상태: <span class="badge bg-${getStatusColor(job.status)}">${job.status}</span>
생성 시간: ${job.createdAt ? new Date(job.createdAt).toLocaleString('ko-KR') : 'N/A'}
시작 시간: ${job.startedAt ? new Date(job.startedAt).toLocaleString('ko-KR') : 'N/A'}
완료 시간: ${job.completedAt ? new Date(job.completedAt).toLocaleString('ko-KR') : 'N/A'}
                </div>
            </div>

            <div class="detail-section">
                <h4>작업 설명</h4>
                <div class="detail-content">${escapeHtml(job.description)}</div>
            </div>

            ${job.result ? `
            <div class="detail-section">
                <h4>작업 결과</h4>
                <div class="detail-content">${escapeHtml(job.result)}</div>
            </div>
            ` : ''}

            ${job.errorMessage ? `
            <div class="detail-section">
                <h4>에러 메시지</h4>
                <div class="detail-content text-danger">${escapeHtml(job.errorMessage)}</div>
            </div>
            ` : ''}

            ${job.logs && job.logs.length > 0 ? `
            <div class="detail-section">
                <h4>로그 (${job.logs.length}건)</h4>
                <div class="detail-content">
${job.logs.map(log => `<div class="log-entry">${escapeHtml(log)}</div>`).join('')}
                </div>
            </div>
            ` : ''}
        `;
    } catch (error) {
        console.error('Error loading job detail:', error);
        content.innerHTML = '<div class="job-detail-placeholder"><p class="text-danger">작업 정보를 불러오는데 실패했습니다.</p></div>';
    }
}

function closeJobDetail() {
    const content = document.getElementById('job-detail-content');
    content.innerHTML = '<div class="job-detail-placeholder"><p>작업을 선택하면 상세 정보가 표시됩니다</p></div>';
}
