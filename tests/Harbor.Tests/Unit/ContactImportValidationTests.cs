using Harbor.Api.Contracts;
using Harbor.Api.Infrastructure;

namespace Harbor.Tests.Unit;

public class ContactImportValidationTests
{
    [Fact]
    public void S07_RecordParameterAttributesAreUsedAndAllFieldErrorsAreReported()
    {
        var result = ContactImportValidation.Validate(new CreateContactRequest(" ", "not-email", new string('x', 201)));
        Assert.Null(result.Contact);
        Assert.Contains(result.Errors, e => e.Field == "Name");
        Assert.Contains(result.Errors, e => e.Field == "Email");
        Assert.Contains(result.Errors, e => e.Field == "ExternalId");
    }

    [Fact]
    public void S07_AttributeCollisionAndSizeFailBeforeCommit()
    {
        var collision = ContactImportValidation.Validate(new CreateContactRequest("Ada", null, null,
            new Dictionary<string, string?> { ["plan"] = "free", [" plan "] = "pro" }));
        Assert.Null(collision.Contact);
        Assert.Contains(collision.Errors, e => e.Message.Contains("collide"));
        var size = ContactImportValidation.Validate(new CreateContactRequest("Ada", null, null,
            Enumerable.Range(0, 6).ToDictionary(i => "key" + i, _ => (string?)new string('x', 2_000))));
        Assert.Null(size.Contact);
        Assert.Contains(size.Errors, e => e.Message.Contains("10,000"));
    }

    [Fact]
    public void S07_NormalizationMatchesOrdinaryContactCreation()
    {
        var result = ContactImportValidation.Validate(new CreateContactRequest(" Ada  Lovelace ", "ADA@example.test", " E-1 ",
            new Dictionary<string, string?> { [" plan "] = " pro ", ["unset"] = null }));
        Assert.Empty(result.Errors);
        Assert.Equal("Ada  Lovelace", result.Contact!.Name);
        Assert.Equal("ADA@example.test", result.Contact.Email);
        Assert.Equal(" E-1 ", result.Contact.ExternalId);
        Assert.Equal("{\"plan\":\" pro \"}", result.Contact.AttributesJson);
    }
}
