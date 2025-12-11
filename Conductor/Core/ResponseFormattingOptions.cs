namespace Conductor.Core;

public class ResponseFormattingOptions
{
	public bool WrapAllResponses { get; set; } = true;
	public bool IncludeTimestamp { get; set; } = true;
	public bool IncludeCorrelationId { get; set; } = true;
	public bool IncludeRequestId { get; set; } = true;
	public bool IncludeUserId { get; set; } = false;
	public string SuccessMessage { get; set; } = "Success";
	public string DefaultErrorMessage { get; set; } = "An error occurred";
	public List<string> ExcludedPaths { get; set; } = new() { "/health", "/metrics", "/swagger" };
	public List<string> ExcludedContentTypes { get; set; } = new() { "text/html", "image/*", "application/octet-stream" };
	public bool LogExceptions { get; set; } = true;
	public bool IncludeStackTrace { get; set; } = false;
	public Dictionary<string, object> GlobalMetadata { get; set; } = new();
}