CREATE DATABASE MolasCacheDb;
GO
USE MolasCacheDb;
GO

-- =============================
-- PRODUCTS
-- =============================
CREATE TABLE CacheProducts (
    ItemCode NVARCHAR(50) PRIMARY KEY,
    ItemName NVARCHAR(200) NOT NULL,
    OnHand DECIMAL(18,2) NOT NULL,
    RowVersion ROWVERSION
);

-- =============================
-- CUSTOMERS
-- =============================
CREATE TABLE CacheCustomers (
    CardCode NVARCHAR(20) PRIMARY KEY,
    CardName NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(50),
    Email NVARCHAR(100),
    OdooCustomerId NVARCHAR(50),
    BillToCity NVARCHAR(100),
    ShipToCity NVARCHAR(100),
    LastSapSyncAt DATETIME2 NOT NULL
);

-- =============================
-- SALES ORDERS
-- =============================
CREATE TABLE CacheSalesOrders (
    SapDocEntry INT PRIMARY KEY,
    SapDocNum INT NOT NULL,
    CustomerCode NVARCHAR(20) NOT NULL,
    DocStatus NVARCHAR(20) NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    LastUpdatedAt DATETIME2 NULL
);

CREATE TABLE CacheSalesOrderLines (
    Id INT IDENTITY PRIMARY KEY,
    SapDocEntry INT NOT NULL,
    ItemCode NVARCHAR(50) NOT NULL,
    Quantity DECIMAL(18,2) NOT NULL,
    FOREIGN KEY (SapDocEntry) REFERENCES CacheSalesOrders(SapDocEntry)
);

-- =============================
-- INVOICES
-- =============================
CREATE TABLE CacheInvoices (
    SapDocEntry INT PRIMARY KEY,
    SapDocNum INT NOT NULL,
    CardCode NVARCHAR(20) NOT NULL,
    DocDate DATETIME2 NOT NULL,
    DocTotal DECIMAL(18,2) NOT NULL,
    VatSum DECIMAL(18,2) NOT NULL,
    CachedAt DATETIME2 NOT NULL
);

-- =============================
-- PAYMENTS
-- =============================
CREATE TABLE CachePayments (
    Id INT IDENTITY PRIMARY KEY,
    SapDocEntry INT NOT NULL,
    CardCode NVARCHAR(20) NOT NULL,
    TotalPaid DECIMAL(18,2) NOT NULL,
    PaidAt DATETIME2 NOT NULL
);