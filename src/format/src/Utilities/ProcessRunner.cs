﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Tools.Utilities
{
    public readonly struct ProcessResult
    {
        public Process Process { get; }
        public int ExitCode { get; }
        public ReadOnlyCollection<string> OutputLines { get; }
        public ReadOnlyCollection<string> ErrorLines { get; }

        public ProcessResult(Process process, int exitCode, ReadOnlyCollection<string> outputLines, ReadOnlyCollection<string> errorLines)
        {
            Process = process;
            ExitCode = exitCode;
            OutputLines = outputLines;
            ErrorLines = errorLines;
        }
    }

    public readonly struct ProcessInfo
    {
        public Process Process { get; }
        public ProcessStartInfo StartInfo { get; }
        public Task<ProcessResult> Result { get; }

        public int Id => Process.Id;

        public ProcessInfo(Process process, ProcessStartInfo startInfo, Task<ProcessResult> result)
        {
            Process = process;
            StartInfo = startInfo;
            Result = result;
        }
    }

    public static class ProcessRunner
    {
        public static void OpenFile(string file)
        {
            if (File.Exists(file))
            {
                Process.Start(file);
            }
        }

        public static ProcessInfo CreateProcess(
            string executable,
            string arguments,
            bool lowPriority = false,
            string? workingDirectory = null,
            bool captureOutput = false,
            bool displayWindow = true,
            Dictionary<string, string>? environmentVariables = null,
            Action<Process>? onProcessStartHandler = null,
            CancellationToken cancellationToken = default) =>
            CreateProcess(
                CreateProcessStartInfo(executable, arguments, workingDirectory, captureOutput, displayWindow, environmentVariables),
                lowPriority: lowPriority,
                onProcessStartHandler: onProcessStartHandler,
                cancellationToken: cancellationToken);

        public static ProcessInfo CreateProcess(
            ProcessStartInfo processStartInfo,
            bool lowPriority = false,
            Action<Process>? onProcessStartHandler = null,
            CancellationToken cancellationToken = default)
        {
            var errorLines = new List<string>();
            var outputLines = new List<string>();
            var process = new Process();
            var tcs = new TaskCompletionSource<ProcessResult>();

            process.EnableRaisingEvents = true;
            process.StartInfo = processStartInfo;

            process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        outputLines.Add(e.Data);
                    }
                };

            process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        errorLines.Add(e.Data);
                    }
                };

            process.Exited += (s, e) =>
                {
                    // We must call WaitForExit to make sure we've received all OutputDataReceived/ErrorDataReceived calls
                    // or else we'll be returning a list we're still modifying. For paranoia, we'll start a task here rather
                    // than enter right back into the Process type and start a wait which isn't guaranteed to be safe.
                    Task.Run(() =>
                    {
                        process.WaitForExit();
                        var result = new ProcessResult(
                            process,
                            process.ExitCode,
                            new ReadOnlyCollection<string>(outputLines),
                            new ReadOnlyCollection<string>(errorLines));
                        tcs.TrySetResult(result);
                    });
                };

            _ = cancellationToken.Register(() =>
                {
                    if (tcs.TrySetCanceled())
                    {
                        // If the underlying process is still running, we should kill it
                        if (!process.HasExited)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch (InvalidOperationException)
                            {
                                // Ignore, since the process is already dead
                            }
                        }
                    }
                });

            process.Start();
            onProcessStartHandler?.Invoke(process);

            if (lowPriority)
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }

            if (processStartInfo.RedirectStandardOutput)
            {
                process.BeginOutputReadLine();
            }

            if (processStartInfo.RedirectStandardError)
            {
                process.BeginErrorReadLine();
            }

            return new ProcessInfo(process, processStartInfo, tcs.Task);
        }

        public static ProcessStartInfo CreateProcessStartInfo(
            string executable,
            string arguments,
            string? workingDirectory = null,
            bool captureOutput = false,
            bool displayWindow = true,
            Dictionary<string, string>? environmentVariables = null)
        {
            var processStartInfo = new ProcessStartInfo(executable, arguments);

            if (!string.IsNullOrEmpty(workingDirectory))
            {
                processStartInfo.WorkingDirectory = workingDirectory;
            }

            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                {
                    processStartInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            if (captureOutput)
            {
                processStartInfo.UseShellExecute = false;
                processStartInfo.RedirectStandardOutput = true;
                processStartInfo.RedirectStandardError = true;
            }
            else
            {
                processStartInfo.CreateNoWindow = !displayWindow;
                processStartInfo.UseShellExecute = displayWindow;
            }

            return processStartInfo;
        }
    }
}
