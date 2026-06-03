namespace SharpPersistence.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HideFromDbContextAssemblyScanAttribute : Attribute;