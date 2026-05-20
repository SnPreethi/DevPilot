using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevPilot.Contracts;
using Microsoft.Extensions.Logging;

namespace DevPilot.Core.Modernization;

public sealed class ModernizationEngine : IModernizationEngine
{
    private readonly IModernizationPlanner _planner;
    private readonly ILogger<ModernizationEngine> _logger;
    private readonly ConcurrentDictionary<string, ModernizationPlan> _plans = new();
    private readonly string _plansPath;
    private readonly object _fileLock = new();

    public ModernizationEngine(IModernizationPlanner planner, ILogger<ModernizationEngine> logger)
    {
        _planner = planner;
        _logger = logger;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _plansPath = Path.Combine(appData, "DevPilot", "modernization-plans.json");
        
        LoadPlansFromDisk();
    }

    // Secondary constructor for testing
    public ModernizationEngine(IModernizationPlanner planner, ILogger<ModernizationEngine> logger, string plansPath)
    {
        _planner = planner;
        _logger = logger;
        _plansPath = plansPath;
        
        LoadPlansFromDisk();
    }

    private void LoadPlansFromDisk()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(_plansPath))
                {
                    var json = File.ReadAllText(_plansPath);
                    var plansList = JsonSerializer.Deserialize<List<ModernizationPlan>>(json);
                    if (plansList != null)
                    {
                        foreach (var plan in plansList)
                        {
                            _plans[plan.PlanId] = plan;
                        }
                        _logger.LogInformation("Loaded {Count} modernization plans from disk persistent cache.", plansList.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load modernization plans from disk.");
            }
        }
    }

    private void SavePlansToDisk()
    {
        lock (_fileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(_plansPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var plansList = _plans.Values.ToList();
                var json = JsonSerializer.Serialize(plansList, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_plansPath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist modernization plans to disk.");
            }
        }
    }

    public async Task<ModernizationPlan> GenerateAndRegisterPlanAsync(
        string repositoryId,
        ModernizationType type,
        string targetPayload,
        CancellationToken cancellationToken = default)
    {
        var plan = await _planner.GeneratePlanAsync(repositoryId, type, targetPayload, cancellationToken).ConfigureAwait(false);
        _plans[plan.PlanId] = plan;
        SavePlansToDisk();
        return plan;
    }

    public Task<ModernizationPlan> ApprovePlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Approving modernization workflow plan {PlanId}.", planId);

        if (!_plans.TryGetValue(planId, out var plan))
        {
            throw new ArgumentException($"Plan {planId} not found.");
        }

        var approved = plan with { Status = ModernizationPlanStatus.Approved };
        _plans[planId] = approved;
        SavePlansToDisk();
        return Task.FromResult(approved);
    }

    public Task<ModernizationPlan> ExecuteStepAsync(string planId, string stepId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing step {StepId} of modernization plan {PlanId}.", stepId, planId);

        if (!_plans.TryGetValue(planId, out var plan))
        {
            throw new ArgumentException($"Plan {planId} not found.");
        }

        if (plan.Status != ModernizationPlanStatus.Approved && plan.Status != ModernizationPlanStatus.Executing)
        {
            throw new InvalidOperationException("Workflow plan must be approved before executing steps.");
        }

        var step = plan.Steps.FirstOrDefault(s => s.StepId == stepId);
        if (step == null)
        {
            throw new ArgumentException($"Step {stepId} not found in plan {planId}.");
        }

        if (step.RequiresApproval && plan.Status != ModernizationPlanStatus.Approved)
        {
            throw new InvalidOperationException("Step requires explicit user approval gate.");
        }

        // Run the step
        var updatedSteps = plan.Steps.Select(s => s.StepId == stepId ? s with { Completed = true } : s).ToList();
        
        bool allCompleted = updatedSteps.All(s => s.Completed);
        var nextStatus = allCompleted ? ModernizationPlanStatus.Completed : ModernizationPlanStatus.Executing;

        var updatedPlan = plan with { Status = nextStatus, Steps = updatedSteps };
        _plans[planId] = updatedPlan;
        SavePlansToDisk();

        return Task.FromResult(updatedPlan);
    }

    public Task<ModernizationPlan> RollbackPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rolling back modernization plan {PlanId}.", planId);

        if (!_plans.TryGetValue(planId, out var plan))
        {
            throw new ArgumentException($"Plan {planId} not found.");
        }

        var rolledBackSteps = plan.Steps.Select(s => s with { Completed = false }).ToList();
        var rolledBack = plan with { Status = ModernizationPlanStatus.RolledBack, Steps = rolledBackSteps };
        _plans[planId] = rolledBack;
        SavePlansToDisk();
        return Task.FromResult(rolledBack);
    }

    public Task<ModernizationPlan?> GetPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        _plans.TryGetValue(planId, out var plan);
        return Task.FromResult(plan);
    }
}
