SELECT 
    l.SourceItemCode,
    l.TargetItemCode,
    l.ArticleNumber,
    l.SuggestedQty,
    ISNULL(l.ApprovedQty, l.SuggestedQty) AS EffectiveQty,
    l.ExecutionStatus,
    l.ExecutionMessage,
    r.ErrorMessage AS HeaderError
FROM CacheLiquiMolyReplenishmentRequests r
JOIN CacheLiquiMolyReplenishmentRequestLines l ON l.RequestId = r.Id
WHERE r.RequestRef = 'RPL-20260429-000001'
ORDER BY l.ExecutionStatus, l.Id;
