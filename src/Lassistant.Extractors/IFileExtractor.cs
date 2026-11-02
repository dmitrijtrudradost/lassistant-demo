using Lassistant.Core.Models;

namespace Lassistant.Extractors;

public interface IFileExtractor
{
    bool CanHandle(string filePath);
    ClientData Extract(string filePath);
}
