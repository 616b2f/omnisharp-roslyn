using System.Collections.Generic;
using bsp4csharp.Protocol;

namespace OmniSharp.Bsp
{
    public record BspWorkspaceInfo(string SolutionFileOrRootPath, IEnumerable<BuildTarget> ProjectTargets);
}

