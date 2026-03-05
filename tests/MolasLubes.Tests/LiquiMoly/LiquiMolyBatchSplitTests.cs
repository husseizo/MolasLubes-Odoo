namespace MolasLubes.Tests.LiquiMoly;

/// <summary>
/// Unit tests for the batch-processing logic used in LiquiMolyProductScrapeJob.
///
/// The job splits a list of article numbers into fixed-size batches before
/// passing them to the scraper, allowing incremental saves and graceful
/// cancellation between batches.  These tests validate the batch-splitting
/// math in isolation.
/// </summary>
public class LiquiMolyBatchSplitTests
{
    // =========================================================================
    // Helpers (mirrors the job logic without any I/O)
    // =========================================================================

    private static IEnumerable<IReadOnlyList<T>> SplitIntoBatches<T>(
        IReadOnlyList<T> source, int batchSize)
    {
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));

        for (int i = 0; i < source.Count; i += batchSize)
            yield return source.Skip(i).Take(batchSize).ToList();
    }

    private static int BatchCount(int total, int batchSize) =>
        (int)Math.Ceiling((double)total / batchSize);

    // =========================================================================
    // BatchCount
    // =========================================================================

    [Theory]
    [InlineData(100,  50, 2)]
    [InlineData(101,  50, 3)]
    [InlineData(  1,  50, 1)]
    [InlineData( 50,  50, 1)]
    [InlineData(  0,  50, 0)]
    [InlineData(  7,   3, 3)]
    [InlineData(  6,   3, 2)]
    public void BatchCount_ReturnsExpectedValue(int total, int batchSize, int expected)
    {
        Assert.Equal(expected, BatchCount(total, batchSize));
    }

    // =========================================================================
    // SplitIntoBatches — sizes
    // =========================================================================

    [Fact]
    public void SplitIntoBatches_EvenDivision_AllBatchesFullSize()
    {
        var items  = Enumerable.Range(1, 100).ToList();
        var batches = SplitIntoBatches(items, 50).ToList();

        Assert.Equal(2, batches.Count);
        Assert.Equal(50, batches[0].Count);
        Assert.Equal(50, batches[1].Count);
    }

    [Fact]
    public void SplitIntoBatches_UnevenDivision_LastBatchIsSmaller()
    {
        var items   = Enumerable.Range(1, 7).ToList();
        var batches = SplitIntoBatches(items, 3).ToList();

        Assert.Equal(3, batches.Count);
        Assert.Equal(3, batches[0].Count);
        Assert.Equal(3, batches[1].Count);
        _ = Assert.Single(batches[2]);
    }

    [Fact]
    public void SplitIntoBatches_EmptySource_ReturnsNoBatches()
    {
        var batches = SplitIntoBatches(new List<string>(), 50).ToList();

        Assert.Empty(batches);
    }

    [Fact]
    public void SplitIntoBatches_SingleItem_ReturnsSingleBatch()
    {
        var batches = SplitIntoBatches(new List<string> { "1035" }, 50).ToList();

        var single = Assert.Single(batches);
        Assert.Equal("1035", Assert.Single(single));
    }

    [Fact]
    public void SplitIntoBatches_BatchSizeGreaterThanCount_ReturnsSingleBatch()
    {
        var items   = Enumerable.Range(1, 10).Select(i => i.ToString()).ToList();
        var batches = SplitIntoBatches(items, 100).ToList();

        var single = Assert.Single(batches);
        Assert.Equal(10, single.Count);
    }

    // =========================================================================
    // SplitIntoBatches — data integrity
    // =========================================================================

    [Fact]
    public void SplitIntoBatches_AllItemsPresent_NoItemLost()
    {
        var items   = Enumerable.Range(1, 77).Select(i => $"ART-{i}").ToList();
        var batches = SplitIntoBatches(items, 10).ToList();

        var reconstructed = batches.SelectMany(b => b).ToList();

        Assert.Equal(items.Count, reconstructed.Count);
        Assert.Equal(items.OrderBy(x => x), reconstructed.OrderBy(x => x));
    }

    [Fact]
    public void SplitIntoBatches_AllItemsPresent_NoDuplicates()
    {
        var items    = Enumerable.Range(1, 50).Select(i => $"ART-{i}").ToList();
        var batches  = SplitIntoBatches(items, 15).ToList();
        var allItems = batches.SelectMany(b => b).ToList();

        Assert.Equal(allItems.Count, allItems.Distinct().Count());
    }

    // =========================================================================
    // Invalid batchSize guard
    // =========================================================================

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SplitIntoBatches_InvalidBatchSize_Throws(int batchSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SplitIntoBatches(new List<int> { 1, 2, 3 }, batchSize).ToList());
    }
}
