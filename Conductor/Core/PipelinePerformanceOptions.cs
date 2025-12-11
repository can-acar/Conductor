namespace Conductor.Core;

public class PipelinePerformanceOptions
{
	public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
}