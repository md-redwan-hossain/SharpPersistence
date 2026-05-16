using SharpPersistence.Abstractions;

namespace SharpPersistence.Tests.TestDependencyFiles;

public class TestDto : IJsonDeserializable
{
    public required string Name { get; set; }
}