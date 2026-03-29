-- =============================================================================
-- App Users — for JWT authentication (web app + mobile app login)
-- Run against MolasCacheDb (SQL Server) before deploying the auth endpoints.
-- =============================================================================

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppUsers')
BEGIN
    CREATE TABLE AppUsers (
        SapUserCode  NVARCHAR(50)  NOT NULL PRIMARY KEY,
        PasswordHash NVARCHAR(256) NOT NULL,
        IsActive     BIT           NOT NULL DEFAULT 1,
        DisplayName  NVARCHAR(100) NULL,
        CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
        LastLoginAt  DATETIME2     NULL
    );
END
GO

-- Seed an initial admin user (password: ChangeMe123! — CHANGE BEFORE PRODUCTION)
-- Hash generated with BCrypt.Net-Next workFactor=10
-- To generate a real hash: BCrypt.Net.BCrypt.HashPassword("YourPassword", workFactor: 10)
-- REPLACE this hash before going live:
IF NOT EXISTS (SELECT 1 FROM AppUsers WHERE SapUserCode = 'admin')
BEGIN
    INSERT INTO AppUsers (SapUserCode, PasswordHash, IsActive, DisplayName)
    VALUES (
        'admin',
        '$2a$10$rI8cZN5P.oJ0SB7Yg4Q6De6Z6RmNqMvAzY0eVxUKBUGkjAr6W4/pW',  -- ChangeMe123!
        1,
        'System Administrator'
    );
END
GO
