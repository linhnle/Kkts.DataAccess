namespace Kkts.DataAccess;

public interface IEntityReadRegistrationValidator<T>
    where T : class
{
    ValueTask ValidateAsync(ResourceSchema<T> schema, CancellationToken cancellation = default);
}
