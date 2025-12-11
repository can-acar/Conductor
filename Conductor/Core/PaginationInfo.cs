namespace Conductor.Core;

public class PaginationInfo
{
	public int Page { get; set; }
	public int PageSize { get; set; }
	public long TotalCount { get; set; }
	public int TotalPages { get; set; }
	public bool HasNextPage { get; set; }
	public bool HasPreviousPage { get; set; }
}