using SharpPersistence.Enums;

namespace SharpPersistence.Models.Internals;

internal sealed record SqlRenderContext(
    SqlDialect Dialect,
    SqlIdentifierCase IdentifierCase,
    bool DelimitIdentifiers);
