using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Eventing;
using OmniSharp.FileSystem;
using OmniSharp.FileWatching;
using OmniSharp.Models.FilesChanged;
using OmniSharp.Models.WorkspaceInformation;
using OmniSharp.Options;
using OmniSharp.Roslyn;
using OmniSharp.Services;
using Xunit;

namespace OmniSharp.Bsp.Tests
{
    public class BspProjectSystemFacts : IDisposable
    {
        private readonly string _tempDir;

        private class NullFileSystemWatcher : IFileSystemWatcher
        {
            public void Watch(string pathOrExtension, FileSystemNotificationCallback callback) { }
            public void WatchDirectories(FileSystemNotificationCallback callback) { }
        }

        public BspProjectSystemFacts()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "OmniSharpBspTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        private (ProjectSystem, OmniSharpWorkspace) CreateProjectSystem(string workspaceDir)
        {
            var env = new OmniSharpEnvironment(workspaceDir);
            var loggerFactory = NullLoggerFactory.Instance;
            var hostServicesAggregator = new HostServicesAggregator(Enumerable.Empty<IHostServicesProvider>(), loggerFactory);
            var fileSystemWatcher = new NullFileSystemWatcher();
            var workspace = new OmniSharpWorkspace(hostServicesAggregator, loggerFactory, fileSystemWatcher);
            var fileSystemHelper = new FileSystemHelper(new OmniSharpOptions(), env);

            var projectSystem = new ProjectSystem(
                env,
                workspace,
                NullEventEmitter.Instance,
                fileSystemWatcher,
                fileSystemHelper,
                loggerFactory);

            return (projectSystem, workspace);
        }

        [Fact]
        public void WhenNoBspDirectory_InitializesGracefullyAndStaysUninitialized()
        {
            var (projectSystem, _) = CreateProjectSystem(_tempDir);
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

            projectSystem.Initalize(config);

            Assert.False(projectSystem.Initialized);
        }

        [Fact]
        public void WhenBspConfigDoesNotSupportCsharp_StaysUninitialized()
        {
            var bspDir = Path.Combine(_tempDir, ".bsp");
            Directory.CreateDirectory(bspDir);

            File.WriteAllText(Path.Combine(bspDir, "scala.json"), """
            {
                "name": "scala-bsp",
                "version": "1.0.0",
                "bspVersion": "2.1.1",
                "languages": ["scala"],
                "argv": ["echo"]
            }
            """);

            var (projectSystem, _) = CreateProjectSystem(_tempDir);
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

            projectSystem.Initalize(config);

            Assert.False(projectSystem.Initialized);
        }

        [Fact]
        public async Task WhenValidBspConfigPresent_LaunchesServerAndInitializes()
        {
            var bspDir = Path.Combine(_tempDir, ".bsp");
            Directory.CreateDirectory(bspDir);

            var mockScript = Path.Combine(_tempDir, "mock_bsp.py");
            var projectCsproj = Path.Combine(_tempDir, "MyApp.csproj");

            // Create a minimal mock BSP server script in python that speaks JSON-RPC over stdio
            File.WriteAllText(mockScript, $$"""
            import sys, json

            def read_msg():
                content_length = None
                while True:
                    line = sys.stdin.readline()
                    if not line:
                        return None
                    line = line.strip()
                    if not line:
                        break
                    if line.lower().startswith('content-length:'):
                        content_length = int(line.split(':')[1].strip())
                if content_length is not None:
                    body = sys.stdin.read(content_length)
                    return json.loads(body)
                return None

            def send_msg(obj):
                body = json.dumps(obj)
                header = f"Content-Length: {len(body)}\r\n\r\n"
                sys.stdout.write(header + body)
                sys.stdout.flush()

            while True:
                req = read_msg()
                if req is None:
                    break
                method = req.get('method')
                msg_id = req.get('id')

                if method == 'build/initialize':
                    send_msg({
                        'jsonrpc': '2.0',
                        'id': msg_id,
                        'result': {
                            'displayName': 'MockBspServer',
                            'version': '1.0.0',
                            'bspVersion': '2.1.1',
                            'capabilities': {
                                'compileProvider': { 'languageIds': ['csharp'] }
                            }
                        }
                    })
                elif method == 'build/initialized':
                    pass
                elif method == 'workspace/buildTargets':
                    send_msg({
                        'jsonrpc': '2.0',
                        'id': msg_id,
                        'result': {
                            'targets': [
                                {
                                    'id': { 'uri': 'file://{{projectCsproj.Replace("\\", "/")}}' },
                                    'displayName': 'MyApp',
                                    'baseDirectory': 'file://{{_tempDir.Replace("\\", "/")}}',
                                    'tags': ['application'],
                                    'languageIds': ['csharp'],
                                    'capabilities': { 'canCompile': True, 'canTest': False, 'canRun': True }
                                }
                            ]
                        }
                    })
                elif method == 'build/shutdown':
                    send_msg({ 'jsonrpc': '2.0', 'id': msg_id, 'result': None })
                elif method == 'build/exit':
                    break
            """);

            var connectionDetailsFile = Path.Combine(bspDir, "csharp.json");
            File.WriteAllText(connectionDetailsFile, $$"""
            {
                "name": "mock-csharp-bsp",
                "version": "1.0.0",
                "bspVersion": "2.1.1",
                "languages": ["csharp"],
                "argv": ["python3", "{{mockScript.Replace("\\", "/")}}"]
            }
            """);

            var (projectSystem, workspace) = CreateProjectSystem(_tempDir);
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

            projectSystem.Initalize(config);

            Assert.True(projectSystem.Initialized);

            // Wait for initialize task to finish
            await projectSystem.WaitForIdleAsync();

            // Test GetWorkspaceModelAsync
            IProjectSystem iProjectSystem = projectSystem;
            var workspaceInfo = await iProjectSystem.GetWorkspaceModelAsync(new WorkspaceInformationRequest());

            Assert.NotNull(workspaceInfo);
            Assert.IsType<BspWorkspaceInfo>(workspaceInfo);
            var bspWorkspaceInfo = (BspWorkspaceInfo)workspaceInfo;
            Assert.Single(bspWorkspaceInfo.ProjectTargets);

            // Verify project was added to OmniSharpWorkspace
            var roslynProjects = workspace.CurrentSolution.Projects.ToList();
            Assert.Single(roslynProjects);
            Assert.Equal("MyApp", roslynProjects[0].Name);

            // Test GetProjectModelAsync
            var projectModel = await iProjectSystem.GetProjectModelAsync(projectCsproj);
            Assert.NotNull(projectModel);
        }
    }
}
