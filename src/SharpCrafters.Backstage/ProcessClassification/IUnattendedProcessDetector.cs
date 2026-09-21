using SharpCrafters.Backstage.Extensibility;

namespace SharpCrafters.Backstage.ProcessClassification;

public interface IUnattendedProcessDetector : IBackstageService
{
    bool IsCurrentProcessUnattended { get; }
}