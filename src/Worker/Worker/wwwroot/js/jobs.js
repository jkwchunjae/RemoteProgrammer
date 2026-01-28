// 작업 목록 관리 JavaScript

// 탭 전환 기능
function initTabs() {
    const tabButtons = document.querySelectorAll('.tab-button');

    tabButtons.forEach(button => {
        button.addEventListener('click', () => {
            const tabId = button.getAttribute('data-tab');

            // 모든 탭 버튼과 컨텐츠에서 active 제거
            document.querySelectorAll('.tab-button').forEach(btn => btn.classList.remove('active'));
            document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));

            // 선택된 탭 활성화
            button.classList.add('active');
            document.getElementById(`tab-${tabId}`).classList.add('active');
        });
    });
}

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

async function loadProjects() {
    try {
        const response = await fetch('/api/projects');
        const projects = await response.json();

        const projectSelect = document.getElementById('project-select');

        if (projects.length === 0) {
            projectSelect.innerHTML = '<option value="">프로젝트가 없습니다</option>';
        } else {
            projectSelect.innerHTML = '<option value="">프로젝트 선택...</option>' +
                projects.map(p => `<option value="${escapeHtml(p.name)}">${escapeHtml(p.name)}</option>`).join('');
        }
    } catch (error) {
        console.error('Error loading projects:', error);
    }
}

async function loadActiveJobs() {
    try {
        const response = await fetch('/api/jobs');
        const jobs = await response.json();

        const activeJobs = document.getElementById('active-jobs');

        if (jobs.length === 0) {
            activeJobs.innerHTML = '<p class="text-muted">진행 중인 작업이 없습니다.</p>';
        } else {
            activeJobs.innerHTML = '<ul class="list-group">' +
                jobs.map(j => `<li class="list-group-item clickable" onclick="showJobDetail('${j.id}')">
                    <strong>${escapeHtml(j.projectName)}</strong> -
                    <span class="badge bg-${getStatusColor(j.status)}">${j.status}</span><br/>
                    <small>${escapeHtml(j.description.substring(0, 100))}${j.description.length > 100 ? '...' : ''}</small>
                </li>`).join('') +
                '</ul>';
        }
    } catch (error) {
        console.error('Error loading active jobs:', error);
    }
}

async function loadJobHistory() {
    try {
        const response = await fetch('/api/jobs/history?limit=10');
        const jobs = await response.json();

        const jobHistory = document.getElementById('job-history');

        if (jobs.length === 0) {
            jobHistory.innerHTML = '<p class="text-muted">작업 이력이 없습니다.</p>';
        } else {
            jobHistory.innerHTML = '<ul class="list-group">' +
                jobs.map(j => `<li class="list-group-item clickable" onclick="showJobDetail('${j.id}', true)">
                    <strong>${escapeHtml(j.projectName)}</strong> -
                    <span class="badge bg-${getStatusColor(j.status)}">${j.status}</span><br/>
                    <small>${escapeHtml(j.description.substring(0, 100))}${j.description.length > 100 ? '...' : ''}</small><br/>
                    <small class="text-muted">${new Date(j.createdAt).toLocaleString('ko-KR')}</small>
                </li>`).join('') +
                '</ul>';
        }
    } catch (error) {
        console.error('Error loading job history:', error);
    }
}

// 폼 제출 핸들러
document.getElementById('create-job-form').addEventListener('submit', async (e) => {
    e.preventDefault();

    const projectName = document.getElementById('project-select').value;
    const description = document.getElementById('description').value;

    if (!projectName || !description) {
        alert('프로젝트와 작업 설명을 모두 입력해주세요.');
        return;
    }

    try {
        const response = await fetch('/api/jobs', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ projectName, description })
        });

        if (response.ok) {
            alert('작업이 생성되었습니다.');
            document.getElementById('description').value = '';
            loadActiveJobs();
        } else {
            const error = await response.text();
            alert('작업 생성 실패: ' + error);
        }
    } catch (error) {
        console.error('Error creating job:', error);
        alert('작업 생성 중 오류가 발생했습니다.');
    }
});

// 초기 로드
initTabs();
loadProjects();
loadActiveJobs();
loadJobHistory();

// 자동 새로고침
setInterval(() => {
    loadActiveJobs();
    loadJobHistory();
}, 5000);
