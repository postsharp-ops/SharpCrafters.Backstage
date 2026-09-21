using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.ProcessClassification;

public interface IContainerDetector : IBackstageService
{
    bool IsRunningInContainer { get; }
}