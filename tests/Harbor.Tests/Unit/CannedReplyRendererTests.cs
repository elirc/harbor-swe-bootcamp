using Harbor.Domain;

namespace Harbor.Tests.Unit;

public class CannedReplyRendererTests
{
    private static readonly CannedReplyValues Values = new("Ada", null, "Sam", "Billing");

    [Fact]
    public void M26_RenderIsSinglePassAndTokenNamesAreDistinctAndSorted()
    {
        var rendered = CannedReplyRenderer.Render(
            "Hi {{ contact.name }}, {{teammate.name}} is checking {{conversation.subject}}. {{contact.name}}", Values);
        Assert.Equal("Hi Ada, Sam is checking Billing. Ada", rendered.Body);
        Assert.Equal(new[] { "contact.name", "conversation.subject", "teammate.name" }, rendered.UsedTokens);
        var literal = CannedReplyRenderer.Render("Hi {{contact.name}}", Values with { ContactName = "{{teammate.name}}" });
        Assert.Equal("Hi {{teammate.name}}", literal.Body);
        Assert.Equal("", CannedReplyRenderer.Render("{{contact.email}}", Values).Body);
        Assert.Equal("Literal {text}", CannedReplyRenderer.Render("Literal {text}", Values).Body);
    }

    [Theory]
    [InlineData("{{unknown}}")]
    [InlineData("{{Contact.Name}}")]
    [InlineData("{{contact.name")]
    [InlineData("contact.name}}")]
    [InlineData("{{contact.{{name}}}}")]
    public void M26_InvalidDelimitersAndUnknownTokensAreRejected(string template) =>
        Assert.Throws<DomainException>(() => CannedReplyRenderer.Render(template, Values));

    [Fact]
    public void M26_OutputLimitIsCheckedAfterExpansion()
    {
        Assert.Equal(20_000, CannedReplyRenderer.Render(new string('x', 20_000), Values).Body.Length);
        Assert.Throws<DomainException>(() => CannedReplyRenderer.Render(new string('x', 20_001), Values));
        Assert.Throws<DomainException>(() => CannedReplyRenderer.Render(new string('x', 19_999) + "{{contact.name}}", Values));
    }
}
