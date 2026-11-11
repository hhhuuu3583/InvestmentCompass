-- 먼저 기존 mock_investment DB를 선택한 후 실행하세요.
-- 이 파일은 조회만 수행합니다. 연결 비밀번호나 키를 넣지 마세요.
SELECT DATABASE() AS selected_database, VERSION() AS mysql_version;
SELECT TABLE_NAME, ENGINE, TABLE_COLLATION
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE() ORDER BY TABLE_NAME;
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND (TABLE_NAME = 'users' OR TABLE_NAME LIKE 'competition%')
ORDER BY TABLE_NAME, ORDINAL_POSITION;
SELECT TABLE_NAME, INDEX_NAME, NON_UNIQUE,
       GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX) AS index_columns
FROM information_schema.STATISTICS
WHERE TABLE_SCHEMA = DATABASE()
GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE ORDER BY TABLE_NAME, INDEX_NAME;
-- 일반 계좌 기능에는 users, accounts, stocks, holdings, trades가 필요합니다.
-- 기존 테이블이 있다면 01의 CREATE IF NOT EXISTS는 구조를 변경하지 않습니다.
