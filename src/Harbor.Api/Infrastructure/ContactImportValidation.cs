using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Harbor.Api.Contracts;
using Harbor.Infrastructure;

namespace Harbor.Api.Infrastructure;

public record ImportFieldError(string Field, string Message);
public record ValidatedImportContact(string Name, string? Email, string? ExternalId, string AttributesJson);
public record ImportValidation(ValidatedImportContact? Contact, IReadOnlyList<ImportFieldError> Errors);

public static class ContactImportValidation
{
    public static ImportValidation Validate(CreateContactRequest? input)
    {
        if (input is null) return new(null, [new("row", "A contact object is required.")]);
        var results = new List<ValidationResult>();
        // MVC reads positional-record validation from constructor parameters.
        // TryValidateObject alone would silently miss those parameter attributes.
        foreach (var parameter in typeof(CreateContactRequest).GetConstructors().Single().GetParameters())
        {
            var value = typeof(CreateContactRequest).GetProperty(parameter.Name!)!.GetValue(input);
            Validator.TryValidateValue(value, new ValidationContext(input) { MemberName = parameter.Name }, results,
                parameter.GetCustomAttributes<ValidationAttribute>());
        }
        var errors = results.SelectMany(r => r.MemberNames.DefaultIfEmpty("row")
            .Select(field => new ImportFieldError(field, r.ErrorMessage ?? "Invalid value."))).ToList();
        string attributes = "{}";
        if (input.Attributes is { } map)
        {
            var normalized = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                var key = pair.Key.Trim();
                if (key.Length is < 1 or > 100) errors.Add(new("Attributes", "Attribute keys must contain 1-100 characters after trimming."));
                if (pair.Value?.Length > 2_000) errors.Add(new("Attributes", "Attribute values must not exceed 2,000 characters."));
                if (!normalized.TryAdd(key, pair.Value)) errors.Add(new("Attributes", "Attribute keys collide after trimming."));
            }
            attributes = ContactAttributes.Write(normalized);
            if (attributes.Length > 10_000) errors.Add(new("Attributes", "Serialized attributes exceed 10,000 characters."));
        }
        if (errors.Count > 0) return new(null, errors.Distinct().ToList());
        return new(new ValidatedImportContact(input.Name.Trim(), input.Email, input.ExternalId, attributes), []);
    }
}
