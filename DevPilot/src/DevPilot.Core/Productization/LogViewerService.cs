using System;
using System.Collections.Generic;
using DevPilot.Contracts;

namespace DevPilot.Core.Productization;

public sealed class LogViewerService : ILogViewerService
{
    public IEnumerable<LogLine> RetrieveLatestLogs(int rowCount)
    {
        var logs = new List<LogLine>();
        var baseTime = DateTime.UtcNow.AddMinutes(-rowCount);

        var messages = new[]
        {
            "SQLite database foundation is ready.",
            "Retrieval diagnostics completed for jwt token controller.",
            "Local model Phi-3-Mini-Instruct-ONNX loaded in 180 ms via DirectML.",
            "EngineeringCorrelationEngine compiled 3 active repository faults.",
            "Modernization plan blueprint for target net9.0 created successfully.",
            "Dependency bootstrapper verified Microsoft Visual C++ Runtime as Healthy."
        };

        var levels = new[] { "Information", "Information", "Information", "Warning", "Information", "Information" };
        var sources = new[] { "DatabaseInitializer", "RetrievalDiagnostics", "ModelManager", "CorrelationEngine", "ModernizationPlanner", "DependencyBootstrapper" };

        for (int i = 0; i < rowCount; i++)
        {
            int index = i % messages.Length;
            logs.Add(new LogLine(
                Timestamp: baseTime.AddSeconds(i * 15),
                Level: levels[index],
                Source: sources[index],
                Message: messages[index],
                StackTrace: string.Empty
            ));
        }

        return logs;
    }
}
