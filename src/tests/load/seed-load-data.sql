-- Adds 5,000 generated products (with specs and stock) so catalog queries do real work.
-- The normal dev seed has only 50 products, too few to tell a cached read from an uncached one.
-- Safe to re-run: does nothing if the generated data is already there. Remove with:
--   DELETE FROM Products WHERE Sku LIKE 'LOAD-%'   (specs and inventory cascade)
SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM Products WHERE Sku = 'LOAD-00001')
BEGIN
  PRINT 'load-test products already present';
  RETURN;
END;

DECLARE @count INT = 5000;

SELECT Id, Name, ROW_NUMBER() OVER (ORDER BY Id) - 1 AS rn INTO #cats FROM Categories;
DECLARE @catCount INT = (SELECT COUNT(*) FROM #cats);

;WITH nums AS (
  SELECT TOP (@count) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
  FROM sys.all_objects a CROSS JOIN sys.all_objects b
)
INSERT INTO Products (Sku, Name, Brand, Description, Price, IsActive, CategoryId)
SELECT
  CONCAT('LOAD-', RIGHT(CONCAT('00000', i), 5)),
  CONCAT(CHOOSE(i % 6 + 1, 'Acme', 'Zenith', 'Orbit', 'Nova', 'Pixel', 'Vertex'), ' ', c.Name, ' ', i),
  CHOOSE(i % 6 + 1, 'Acme', 'Zenith', 'Orbit', 'Nova', 'Pixel', 'Vertex'),
  'Generated load-test product',
  CAST(20 + (ABS(CHECKSUM(NEWID())) % 200000) / 100.0 AS DECIMAL(18, 2)),
  1,
  c.Id
FROM nums
JOIN #cats c ON c.rn = i % @catCount;

INSERT INTO ProductSpecs (ProductId, Name, Value)
SELECT p.Id, s.Name, s.Value
FROM Products p
CROSS APPLY (VALUES
  ('socket',   CHOOSE(p.Id % 3 + 1, 'AM5', 'LGA1700', 'LGA1851')),
  ('color',    CHOOSE(p.Id % 4 + 1, 'Black', 'White', 'Silver', 'Red')),
  ('capacity', CHOOSE(p.Id % 5 + 1, '8GB', '16GB', '32GB', '64GB', '1TB'))
) s(Name, Value)
WHERE p.Sku LIKE 'LOAD-%';

INSERT INTO Inventory (ProductId, QuantityOnHand, QuantityReserved)
SELECT p.Id, ABS(CHECKSUM(NEWID())) % 200, 0 FROM Products p WHERE p.Sku LIKE 'LOAD-%';

DROP TABLE #cats;
PRINT CONCAT('added ', @count, ' products');
