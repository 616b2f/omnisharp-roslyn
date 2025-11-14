using System.Collections.Generic;
using System.Threading.Tasks;
using bsp_client;
using bsp4csharp.Protocol;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

public class ServerCallbacks(LogLevel LogLevel, ILogger<ServerCallbacks> Logger) : IServerCallbacks
{
    private readonly List<Diagnostic> _diagnosticsCollection = [];
    public IReadOnlyCollection<Diagnostic> Diagnostics => _diagnosticsCollection.AsReadOnly();

    private readonly List<object> _taskNotificationsCollection = [];
    public IReadOnlyCollection<object> TaskNotifications => _taskNotificationsCollection.AsReadOnly();

    private readonly List<LogMessageParams> _logMessagesCollection = [];
    public IReadOnlyCollection<LogMessageParams> LogMessages => _logMessagesCollection.AsReadOnly();

    [JsonRpcMethod(Methods.BuildPublishDiagnostics, UseSingleObjectParameterDeserialization = true)]
    public Task BuildPublishDiagnosticsRecievedAsync(PublishDiagnosticsParams publishDiagnosticsParams)
    {
        _diagnosticsCollection.AddRange(publishDiagnosticsParams.Diagnostics);
        if (LogLevel >= LogLevel.Information)
        {
            foreach (var diagnostic in publishDiagnosticsParams.Diagnostics)
            {
                Logger.LogInformation(diagnostic.Message);
            }
        }
        return Task.CompletedTask;
    }

    [JsonRpcMethod(Methods.BuildLogMessage, UseSingleObjectParameterDeserialization = true)]
    public void BuildLogMessage(LogMessageParams logMessageParams)
    {
        _logMessagesCollection.Add(logMessageParams);
        if (LogLevel >= LogLevel.Information)
        {
            Logger.LogInformation(logMessageParams.Message);
        }
    }

    [JsonRpcMethod(Methods.BuildTaskStart, UseSingleObjectParameterDeserialization = true)]
    public Task BuildTaskStartAsync(TaskStartParams taskStartParams)
    {
        if (LogLevel >= LogLevel.Information)
        {
            var message = string.Format("TaskStart[{0}]: {1}", taskStartParams.TaskId.Id, taskStartParams.Message);
            Logger.LogInformation(message);
        }
        _taskNotificationsCollection.Add(taskStartParams);
        return Task.CompletedTask;
    }

    [JsonRpcMethod(Methods.BuildTaskProgress, UseSingleObjectParameterDeserialization = true)]
    public Task BuildTaskProgressAsync(TaskProgressParams taskProgressParams)
    {
        if (LogLevel >= LogLevel.Information)
        {
            var message = string.Format("TaskProgress[{0}]: {1}", taskProgressParams.TaskId.Id, taskProgressParams.Message);
            Logger.LogInformation(message);
        }
        _taskNotificationsCollection.Add(taskProgressParams);
        return Task.CompletedTask;
    }

    [JsonRpcMethod(Methods.BuildTaskFinish, UseSingleObjectParameterDeserialization = true)]
    public Task BuildTaskFinishAsync(TaskFinishParams taskFinishParams)
    {
        if (LogLevel >= LogLevel.Information)
        {
            var message = string.Format("TaskFinish[{0}]: {1}", taskFinishParams.TaskId.Id, taskFinishParams.Message);
            Logger.LogInformation(message);
        }
        _taskNotificationsCollection.Add(taskFinishParams);
        return Task.CompletedTask;
    }

    // public const string BuildShowMessage = "build/showMessage";
    // public const string BuildTargetDidChange = "buildTarget/didChange";
    // public const string RunPrintStdout = "run/printStdout";
    // public const string RunPrintStderr = "run/printStderr";
}
