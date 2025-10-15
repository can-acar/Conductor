using Conductor.Core;

namespace Conductor.Interfaces;

public interface IResponseMetadataProvider
{
    ResponseMetadata CreateMetadata(object? context = null);
}