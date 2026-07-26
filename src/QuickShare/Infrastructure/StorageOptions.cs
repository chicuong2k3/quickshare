namespace QuickShare.Infrastructure;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    public LocalDiskOptions LocalDisk { get; init; } = new();
}

public sealed class LocalDiskOptions
{
    public string RootPath { get; init; } = "./storage/chunks";
}
