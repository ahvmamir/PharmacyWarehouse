using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PharmacyWarehouse.Api.Tests;

// IsUniqueViolation فقط نقض یکتایی و کلید اصلی را می‌گیرد، نه هر خطای ۱۹ (مثل NOT NULL یا کلید خارجی).
public class UniqueViolationTests
{
    [Theory]
    [InlineData(2067, true)]  // SQLITE_CONSTRAINT_UNIQUE
    [InlineData(1555, true)]  // SQLITE_CONSTRAINT_PRIMARYKEY
    [InlineData(1299, false)] // NOT NULL
    [InlineData(787, false)]  // FOREIGN KEY
    [InlineData(275, false)]  // CHECK
    public void OnlyUniqueAndPrimaryKey_AreUniqueViolations(int extendedCode, bool expected)
    {
        var inner = new SqliteException("test", 19, extendedCode);

        Assert.Equal(expected, ApiProblems.IsUniqueViolation(new DbUpdateException("test", inner)));
    }

    [Fact]
    public void NonSqliteInner_IsNotUniqueViolation() =>
        Assert.False(ApiProblems.IsUniqueViolation(new DbUpdateException("test", new InvalidOperationException())));
}
