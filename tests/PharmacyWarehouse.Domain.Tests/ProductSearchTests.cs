using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Tests;

public class ProductSearchTests
{
    private static SearchCandidate C(string name, string? extra = null) =>
        new(Guid.NewGuid(), TextNormalizer.SearchKey(name)!, TextNormalizer.SearchKey(extra is null ? name : $"{name} {extra}")!);

    [Fact]
    public void Rank_OrdersPrefixThenAllWordsThenSimilar()
    {
        var similar = C("قرص آزمایشی");
        var allWords = C("شربت آزمون قرص");
        var prefix = C("قرص آزمون");

        var result = ProductSearch.Rank("قرص آزمون", [similar, allWords, prefix], 10);

        Assert.Equal([prefix.Id, allWords.Id], result.Take(2).Select(c => c.Id));
    }

    [Fact]
    public void Classify_ArabicLettersAndZwnj_AreNormalizedBeforeMatching()
    {
        var candidate = C("آموکسی‌سیلین");

        Assert.Equal(SearchMatch.NamePrefix, ProductSearch.Classify(TextNormalizer.SearchKey("آموکسی")!, candidate.NameKey, candidate.SearchText));
        Assert.Equal(SearchMatch.AllWords, ProductSearch.Classify(TextNormalizer.SearchKey("آموکسی سیلین")!, candidate.NameKey, candidate.SearchText));
    }

    [Fact]
    public void Classify_SpellingMistake_IsSimilarOnly()
    {
        var candidate = C("سفالکسین");

        Assert.Equal(SearchMatch.Similar, ProductSearch.Classify(TextNormalizer.SearchKey("سفالکسن")!, candidate.NameKey, candidate.SearchText));
    }

    [Fact]
    public void Classify_ShortWord_MustNotMatchWithAnEdit()
    {
        var candidate = C("ABC");

        Assert.Null(ProductSearch.Classify("ABD", candidate.NameKey, candidate.SearchText));
    }

    [Fact]
    public void Classify_Unrelated_IsNull()
    {
        var candidate = C("سفالکسین");

        Assert.Null(ProductSearch.Classify(TextNormalizer.SearchKey("پماد")!, candidate.NameKey, candidate.SearchText));
    }

    [Fact]
    public void Rank_EmptyQuery_ReturnsNothing()
    {
        Assert.Empty(ProductSearch.Rank("   ", [C("TEST-DRUG-01")], 10));
    }

    [Fact]
    public void Rank_RespectsLimit()
    {
        Assert.Equal(2, ProductSearch.Rank("TEST", [C("TEST-1"), C("TEST-2"), C("TEST-3")], 2).Count);
    }

    [Theory]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abcd", "abdc", 1)] // جابه‌جایی دو حرف کنار هم یک غلط است
    [InlineData("abc", "", 3)]
    public void EditDistance_Counts(string a, string b, int expected)
    {
        Assert.Equal(expected, ProductSearch.EditDistance(a, b));
    }

    [Fact]
    public void WithResolvedId_ReplacesRawGuidOnly()
    {
        var id = Guid.NewGuid();

        var resolved = AuditRedaction.WithResolvedId($"{{\"LocationId\":\"{id}\",\"Name\":\"TEST\"}}", "LocationId", _ => "قفسه‌ی آزمایشی");
        Assert.Contains($"{id}|قفسه‌ی آزمایشی", resolved);

        var already = $"{{\"LocationId\":\"{id}|نام قبلی\"}}";
        Assert.Equal(already, AuditRedaction.WithResolvedId(already, "LocationId", _ => "نباید بیاید"));
    }

    [Theory]
    [InlineData("Lines[2].Expiry", 2, "Expiry")]
    [InlineData("lines[10].BatchVersion", 10, "BatchVersion")]
    public void BulkReceiptErrors_ParsesKey(string key, int line, string field)
    {
        Assert.True(BulkReceiptErrors.TryParse(key, out var parsedLine, out var parsedField));
        Assert.Equal((line, field), (parsedLine, parsedField));
    }

    [Fact]
    public void BulkReceiptErrors_RejectsOtherKeys()
    {
        Assert.False(BulkReceiptErrors.TryParse("Quantity", out _, out _));
    }
}
