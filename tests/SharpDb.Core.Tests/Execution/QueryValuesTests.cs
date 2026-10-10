using SharpDb.Core.Execution;

namespace SharpDb.Core.Tests.Execution;

/// <summary>
/// How a value read out of a result set is written into the payload a client receives.
/// </summary>
/// <remarks>
/// The assertion is the shape the reader of a payload sees, not the serializer that produces it:
/// numbers stay numbers, a null stays a null, a blob becomes base64 and a date becomes ISO 8601.
/// <para>
/// These cases cannot all be reached through a real SQLite database, and that is why they are
/// asserted here. Measured on Microsoft.Data.Sqlite 10.0.12: a column declared <c>BOOLEAN</c>
/// reads back as <c>long</c>, one declared <c>DECIMAL</c> as <c>double</c>, and one declared
/// <c>DATETIME</c> as the <c>string</c> the value was stored as — so a boolean, a decimal and a
/// date never arrive as themselves from that engine, and only a unit test can pin the mapping.
/// </para>
/// </remarks>
public class QueryValuesTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(0L)]
    [InlineData(-42L)]
    public void A_whole_number_stays_a_number(long value)
    {
        Assert.Equal<object?>(value, QueryValues.ForJson(value));
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(-0.25)]
    public void A_fractional_number_stays_a_number(double value)
    {
        Assert.Equal<object?>(value, QueryValues.ForJson(value));
    }

    [Fact]
    public void A_decimal_stays_a_number()
    {
        // A decimal must not be turned into a string: the reader of the payload needs to be able to
        // do arithmetic with it without parsing.
        Assert.Equal<object?>(12.34m, QueryValues.ForJson(12.34m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_boolean_stays_a_boolean(bool value)
    {
        Assert.Equal<object?>(value, QueryValues.ForJson(value));
    }

    [Fact]
    public void A_string_stays_a_string()
    {
        Assert.Equal<object?>("Alice", QueryValues.ForJson("Alice"));
    }

    [Fact]
    public void A_null_stays_a_null()
    {
        // A null must be JSON null rather than the string "null", so a reader can tell an absent
        // value from the four letters.
        Assert.Null(QueryValues.ForJson(null));
    }

    [Fact]
    public void The_database_null_stays_a_null()
    {
        // A reader reports SQL NULL as DBNull.Value; the payload must not carry the sentinel.
        Assert.Null(QueryValues.ForJson(DBNull.Value));
    }

    [Fact]
    public void A_blob_becomes_base64()
    {
        // Base64 is what makes a reader recognise the value as bytes rather than as text.
        Assert.Equal<object?>("AQI=", QueryValues.ForJson(new byte[] { 1, 2 }));
    }

    [Fact]
    public void An_empty_blob_becomes_an_empty_string()
    {
        Assert.Equal<object?>(string.Empty, QueryValues.ForJson(Array.Empty<byte>()));
    }

    [Fact]
    public void A_date_becomes_iso_8601()
    {
        var value = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        Assert.Equal<object?>("2024-01-02T03:04:05.0000000Z", QueryValues.ForJson(value));
    }

    [Fact]
    public void A_date_with_an_offset_becomes_iso_8601()
    {
        var value = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(8));

        Assert.Equal<object?>("2024-01-02T03:04:05.0000000+08:00", QueryValues.ForJson(value));
    }

    [Fact]
    public void A_value_of_an_unexpected_type_becomes_text()
    {
        // The payload has to stay writable whatever a Driver hands back, so a type this mapping
        // does not name is written as its text rather than left to fail at serialization time.
        var value = Guid.Parse("11111111-2222-3333-4444-555555555555");

        Assert.Equal<object?>("11111111-2222-3333-4444-555555555555", QueryValues.ForJson(value));
    }
}
