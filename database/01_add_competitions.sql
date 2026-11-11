-- 기존 일반 투자 DB에 대회 기능을 추가하는 MySQL 8용 스크립트입니다.
-- 실행 전 DB 백업과 00_preflight.sql 확인을 권장합니다.
-- USE mock_investment;  -- 실제 사용하는 DB를 선택하세요.
-- 기존 데이터 삭제나 계정 권한 일괄 변경은 하지 않습니다.
-- DDL은 MySQL에서 암시적으로 커밋될 수 있습니다.

DELIMITER $$
CREATE PROCEDURE mockinvestment_merge_add_role_20261106()
BEGIN
  IF DATABASE() IS NULL THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Select your existing database first';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users') THEN
    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Existing users table is required';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'users' AND COLUMN_NAME = 'role') THEN
    ALTER TABLE users ADD COLUMN role VARCHAR(20) NOT NULL DEFAULT 'User';
  END IF;
END$$
DELIMITER ;
CALL mockinvestment_merge_add_role_20261106();
DROP PROCEDURE mockinvestment_merge_add_role_20261106;

CREATE TABLE IF NOT EXISTS competitions (
  competition_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  title VARCHAR(200) NOT NULL,
  start_date DATETIME(6) NOT NULL,
  end_date DATETIME(6) NOT NULL,
  initial_balance DECIMAL(19,2) NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT '준비',
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  KEY ix_competitions_status (status)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_applications (
  application_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  competition_id INT NOT NULL,
  user_id INT NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT '대기',
  applied_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  processed_at DATETIME(6) NULL,
  UNIQUE KEY uq_competition_application (competition_id, user_id),
  KEY ix_application_user (user_id),
  CONSTRAINT fk_merge_application_competition FOREIGN KEY (competition_id)
    REFERENCES competitions(competition_id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_participants (
  participant_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  competition_id INT NOT NULL,
  user_id INT NOT NULL,
  joined_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_competition_participant (competition_id, user_id),
  KEY ix_participant_user (user_id),
  CONSTRAINT fk_merge_participant_competition FOREIGN KEY (competition_id)
    REFERENCES competitions(competition_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_accounts (
  competition_account_id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  participant_id INT NOT NULL,
  initial_balance DECIMAL(19,2) NOT NULL,
  cash_balance DECIMAL(19,2) NOT NULL,
  created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  UNIQUE KEY uq_competition_account_participant (participant_id),
  CONSTRAINT fk_merge_account_participant FOREIGN KEY (participant_id)
    REFERENCES competition_participants(participant_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_holdings (
  competition_account_id INT NOT NULL,
  stock_id INT NOT NULL,
  quantity INT NOT NULL,
  average_buy_price DECIMAL(19,6) NOT NULL,
  PRIMARY KEY (competition_account_id, stock_id),
  CONSTRAINT fk_merge_holding_account FOREIGN KEY (competition_account_id)
    REFERENCES competition_accounts(competition_account_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_trades (
  competition_trade_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  competition_account_id INT NOT NULL,
  stock_id INT NOT NULL,
  client_order_id CHAR(36) NOT NULL,
  trade_type VARCHAR(4) NOT NULL,
  quantity INT NOT NULL,
  unit_price DECIMAL(19,2) NOT NULL,
  total_amount DECIMAL(19,2) NOT NULL,
  quoted_at DATETIME(6) NOT NULL,
  traded_at DATETIME(6) NOT NULL,
  UNIQUE KEY uq_competition_trade_order (competition_account_id, client_order_id),
  KEY ix_competition_trade_time (competition_account_id, traded_at, competition_trade_id),
  CONSTRAINT fk_merge_trade_account FOREIGN KEY (competition_account_id)
    REFERENCES competition_accounts(competition_account_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS competition_final_rankings (
  competition_id INT NOT NULL,
  participant_id INT NOT NULL,
  user_id INT NOT NULL,
  nickname VARCHAR(100) NOT NULL,
  initial_balance DECIMAL(19,2) NOT NULL,
  cash_balance DECIMAL(19,2) NOT NULL,
  stock_evaluation DECIMAL(19,2) NOT NULL,
  total_asset DECIMAL(19,2) NOT NULL,
  profit_amount DECIMAL(19,2) NOT NULL,
  profit_rate DECIMAL(19,6) NOT NULL,
  ranking INT NOT NULL,
  finalized_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (competition_id, participant_id),
  KEY ix_competition_final_rank (competition_id, ranking),
  CONSTRAINT fk_merge_final_competition FOREIGN KEY (competition_id)
    REFERENCES competitions(competition_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- 기존 users.user_id / stocks.stock_id의 signed/unsigned가 확인되지 않아
-- 기존 테이블 대상 FK는 생성하지 않았습니다. 신규 대회 테이블 간 FK만 포함합니다.
-- 이미 같은 이름의 대회 테이블이 있으면 컬럼·고유키·자료형을 별도 대조하세요.
