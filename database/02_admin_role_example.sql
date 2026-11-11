-- 먼저 대상 계정만 조회합니다.
SELECT user_id, login_id, nickname, role FROM users ORDER BY user_id;
-- 실제 관리자로 정한 단일 계정의 ID와 login_id를 직접 확인한 후 실행하세요.
-- 아래는 실행되지 않는 예시입니다. 사용자 전체를 Admin으로 변경하지 마세요.
-- UPDATE users SET role = 'Admin' WHERE user_id = 123 AND login_id = 'your-admin-login';
-- 변경 후 재로그인해야 새로운 JWT에 관리자 권한이 담깁니다.
