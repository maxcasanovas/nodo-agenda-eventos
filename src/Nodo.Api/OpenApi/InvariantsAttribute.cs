namespace Nodo.Api.OpenApi;

/// <summary>
/// Declares which invariants (INV-xx) an endpoint can violate. The OpenAPI transformers turn it into the endpoint's
/// description and into the example of its 422 response, so Scalar shows the real code the caller would get.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class InvariantsAttribute(params string[] ids) : Attribute
{
    public IReadOnlyList<string> Ids { get; } = ids;
}
