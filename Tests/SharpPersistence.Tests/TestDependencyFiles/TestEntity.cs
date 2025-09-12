using System.ComponentModel.DataAnnotations;

namespace SharpPersistence.Tests.TestDependencyFiles;

public class TestEntity
{
    public int Id { get; set; }
    [MaxLength(10000)] public required string Name { get; set; }
    public required int NumericValue { get; set; }
}