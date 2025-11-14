using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OmniSharp.Eventing;
using OmniSharp.FileSystem;
using OmniSharp.FileWatching;
using OmniSharp.Mef;
using OmniSharp.Models.WorkspaceInformation;
using OmniSharp.Services;
using System.Linq;
using System.Text.Json;
using bsp_client;
using bsp4csharp.Protocol;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Scripting;
using OmniSharp.Helpers;
using System.Reflection;
using OmniSharp.Roslyn.Utilities;

namespace OmniSharp.Bsp
{
    [ExportProjectSystem(ProjectSystemNames.BspProjectSystem), Shared]
    internal class ProjectSystem : IProjectSystem
    {
        private readonly IOmniSharpEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private BuildServer? _server;
        private readonly OmniSharpWorkspace _workspace;
        // private readonly ImmutableDictionary<string, string> _propertyOverrides;
        // private readonly IDotNetCliService _dotNetCli;
        // private readonly MetadataFileReferenceCache _metadataFileReferenceCache;
        // private readonly IEventEmitter _eventEmitter;
        // private readonly IFileSystemWatcher _fileSystemWatcher;
        // private readonly FileSystemHelper _fileSystemHelper;
        // private readonly IAnalyzerAssemblyLoader _assemblyLoader;
        private readonly Lazy<CSharpCompilationOptions> _compilationOptions;
        private string? _solutionFileOrRootPath;

        public string Key { get; } = "Bsp";
        public string Language { get; } = LanguageNames.CSharp;
        public IEnumerable<string> Extensions { get; } = [".cs"];
        public bool EnabledByDefault { get; } = true;

        private BuildServerClient? _bspClient;
        private InitializeBuildResult? _initializeInfo;

        public bool Initialized { get; private set; }

        [ImportingConstructor]
        public ProjectSystem(
            IOmniSharpEnvironment environment,
            OmniSharpWorkspace workspace,
            IEventEmitter eventEmitter,
            IFileSystemWatcher fileSystemWatcher,
            FileSystemHelper fileSystemHelper,
            ILoggerFactory loggerFactory,
            IAnalyzerAssemblyLoader assemblyLoader)
        {
            _workspace = workspace;
            _loggerFactory = loggerFactory;
            _environment = environment;
            _logger = _loggerFactory.CreateLogger<ProjectSystem>();
            _compilationOptions = new Lazy<CSharpCompilationOptions>(CreateCompilationOptions());
        }

        public void Initalize(IConfiguration configuration)
        {
            if (Initialized) return;

            BspConnectionDetails? connectionDetails = FindConnectionDetails();

            if (connectionDetails is null)
            {
                return;
            }

            var buildServerLogger = _loggerFactory.CreateLogger<BuildServer>();
            _server = BuildServerFactory.CreateServer(connectionDetails, buildServerLogger);

            var initParams = new InitializeBuildParams
            {
                DisplayName = "OmnisharpLspClient",
                Version = "1.0.0",
                BspVersion = "2.1.1",
                RootUri = new Uri("file://" + _environment.TargetDirectory),
                Capabilities = new BuildClientCapabilities()
            };
            initParams.Capabilities.LanguageIds.Add("csharp");

            var serverCallbacksLogger = _loggerFactory.CreateLogger<ServerCallbacks>();
            _bspClient = _server.CreateClient(new ServerCallbacks(_environment.LogLevel, serverCallbacksLogger));
            _initializeInfo = _bspClient.BuildInitializeAsync(initParams, CancellationToken.None).GetAwaiter().GetResult();
            _bspClient.BuildInitializedAsync().GetAwaiter().GetResult();

            Initialized = true;
        }

        private BspConnectionDetails? FindConnectionDetails()
        {
            // -- <workspace-dir>/.bsp/
            // -- USER: $XDG_DATA_HOME/bsp/
            // --    if not set use $HOME/.local/share
            // -- SYSTEM: $XDG_DATA_DIRS/bsp/
            // --    if not set use /usr/local/share/:/usr/share/
            //
            // TODO: add OS check and do other stuff for windows
            // if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            // {
            // }
            // else
            // {
            // }
            var localBspDir = Path.Combine(_environment.TargetDirectory, "/.bsp");
            var userBspDir = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ??
                (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HOME")) ? ""
                : Path.Combine(Environment.GetEnvironmentVariable("HOME")!, "/.local/share"));
            var systemBspDir = Environment.GetEnvironmentVariable("XDG_DATA_DIRS")
                ?? "/usr/local/share/:/usr/share/";

            var connectionDetailsFiles = Directory.GetFiles(localBspDir, "*.json");

            var connectionDetails = connectionDetailsFiles
                .Select(static x => JsonSerializer.Deserialize<BspConnectionDetails>(x))
                .Where(static x =>
                    x is not null &&
                    x.Languages is not null &&
                    x.Languages.Contains(LanguageId.Csharp))
                .OrderByDescending(static x => x!.BspVersion)
                .FirstOrDefault();
            return connectionDetails;
        }

        public Task WaitForIdleAsync() { return Task.CompletedTask; }

        async Task<object?> IProjectSystem.GetWorkspaceModelAsync(WorkspaceInformationRequest request)
        {
            if (!Initialized)
            {
                _logger.LogError("Not initialized yet");
                return Task.FromResult<object?>(null);
            }

            var workspaceResult = await _bspClient!.WorkspaceBuildTargetsAsync(CancellationToken.None);
            var slnTarget = workspaceResult.Targets
                .Where(static x => x.LanguageIds.Contains(LanguageId.Csharp))
                .OrderByDescending(static x => Path.GetExtension(x.Id.Uri.AbsolutePath) == "sln")
                .FirstOrDefault();

            if (slnTarget == null)
            {
                _solutionFileOrRootPath = _environment.TargetDirectory;
            } else {
                _solutionFileOrRootPath = slnTarget.BaseDirectory?.AbsolutePath;
            }

            if (_solutionFileOrRootPath is null)
            {
                _logger.LogError("No solution file and no root dir found");
                return Task.FromResult<object?>(null);
            }

            var allProjects = workspaceResult.Targets
                .Where(static x => Path.GetExtension(x.Id.Uri.AbsolutePath) == "csproj");


            // TODO: implement editorconfig handling
            foreach(var project in allProjects)
            {
                var projectId = ProjectId.CreateNewId(Guid.NewGuid().ToString());

                var projectInfo = ProjectInfo.Create(
                    id: projectId,
                    version: VersionStamp.Create(),
                    name: project.DisplayName!,
                    filePath: project.Id.Uri.AbsolutePath,
                    assemblyName: $"{project.DisplayName}.dll",
                    language: LanguageNames.CSharp,
                    compilationOptions: _compilationOptions.Value,
                    parseOptions: new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Script),
                    metadataReferences: DefaultMetadataReferenceHelper
                        .GetDefaultMetadataReferenceLocations()
                        .Select(loc => MetadataReference.CreateFromFile(loc)),
                    // TODO: projectReferences?
                    isSubmission: true);
                    // hostObjectType: hostObjectType);
                    // .WithAnalyzerConfigDocuments(analyzerConfigDocuments);

                _workspace.AddProject(projectInfo);

            }
            return new BspWorkspaceInfo(_solutionFileOrRootPath, allProjects);
        }

        async Task<object?> IProjectSystem.GetProjectModelAsync(string filePath)
        {
            if (!Initialized)
            {
                _logger.LogError("Not initialized yet");
                return Task.FromResult<object?>(null);
            }

            var workspaceResult = await _bspClient!.WorkspaceBuildTargetsAsync(CancellationToken.None);

            var project = workspaceResult.Targets
                .SingleOrDefault(x =>
                    Path.GetExtension(x.Id.Uri.AbsolutePath) == "csproj" &&
                    string.Equals(x.Id.Uri.AbsolutePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (project is null)
            {
                _logger.LogDebug($"Could not locate project for '{filePath}'");
                return Task.FromResult<object?>(null);
            }

            return project;
        }

        private static CSharpCompilationOptions CreateCompilationOptions()
        {
            var compilationOptions = new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    allowUnsafe: true,
                    metadataReferenceResolver: new CachingScriptMetadataResolver(),
                    sourceReferenceResolver: ScriptSourceResolver.Default,
                    assemblyIdentityComparer: DesktopAssemblyIdentityComparer.Default).
                    WithSpecificDiagnosticOptions(CompilationOptionsHelper.GetDefaultSuppressedDiagnosticOptions());

            var topLevelBinderFlagsProperty = typeof(CSharpCompilationOptions).GetProperty("TopLevelBinderFlags", BindingFlags.Instance | BindingFlags.NonPublic);
            var binderFlagsType = typeof(CSharpCompilationOptions).GetTypeInfo().Assembly.GetType("Microsoft.CodeAnalysis.CSharp.BinderFlags");

            var ignoreCorLibraryDuplicatedTypesMember = binderFlagsType?.GetField("IgnoreCorLibraryDuplicatedTypes", BindingFlags.Static | BindingFlags.Public);
            var ignoreCorLibraryDuplicatedTypesValue = ignoreCorLibraryDuplicatedTypesMember?.GetValue(null);
            if (ignoreCorLibraryDuplicatedTypesValue != null)
            {
                topLevelBinderFlagsProperty?.SetValue(compilationOptions, ignoreCorLibraryDuplicatedTypesValue);
            }

            return compilationOptions;
        }
    }

}
