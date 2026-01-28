// 프로젝트 관리 JavaScript

async function loadProjects() {
    try {
        const response = await fetch('/api/projects');
        const projects = await response.json();

        const projectsList = document.getElementById('projects-list');

        if (projects.length === 0) {
            projectsList.innerHTML = '<p class="text-muted">프로젝트가 없습니다.</p>';
        } else {
            projectsList.innerHTML = '<ul class="list-group">' +
                projects.map(p => `<li class="list-group-item">
                    <strong>${escapeHtml(p.name)}</strong><br/>
                    <small class="text-muted">${escapeHtml(p.path)}</small><br/>
                    <small>Branch: ${escapeHtml(p.gitBranch) || 'N/A'}</small>
                </li>`).join('') +
                '</ul>';
        }
    } catch (error) {
        console.error('Error loading projects:', error);
        document.getElementById('projects-list').innerHTML =
            '<p class="text-danger">프로젝트 로드 실패</p>';
    }
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 페이지 로드 시 실행
loadProjects();
