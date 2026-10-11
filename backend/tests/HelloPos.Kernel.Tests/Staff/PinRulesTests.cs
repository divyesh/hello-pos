using HelloPos.Kernel.Staff;

namespace HelloPos.Kernel.Tests.Staff;

/// <summary>Feature 006 FR-008: POS PINs are 4–6 digits and not trivially guessable.</summary>
public sealed class PinRulesTests
{
    [Theory]
    [InlineData("4826")]
    [InlineData("1357")]
    [InlineData("907153")]
    [InlineData("10294")]
    public void Accepted(string pin) => Assert.Null(PinRules.Validate(pin));

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    [InlineData("١٢٣٤")] // non-ASCII digits
    [InlineData("0000")]
    [InlineData("111111")]
    [InlineData("1234")]
    [InlineData("4321")]
    [InlineData("012345")]
    [InlineData("98765")]
    public void Refused(string pin) => Assert.NotNull(PinRules.Validate(pin));

    [Fact]
    public void Null_IsRefused() => Assert.NotNull(PinRules.Validate(null));
}
