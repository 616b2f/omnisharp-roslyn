namespace OmniSharp.MSBuild.Logging
{
    internal class ErrorMessages
    {
        internal const string ReferenceAssembliesNotFoundUnix = "This project targets .NET version that requires reference assemblies that are not installed (e.g. .NET Framework). The most common solution is to make sure Mono is fully updated on your machine (https://mono-project.com/download/) and that you are running the .NET Framework build of OmniSharp (e.g. 'omnisharp.useModernNet': false in C# Extension for VS Code).";
    }
}
